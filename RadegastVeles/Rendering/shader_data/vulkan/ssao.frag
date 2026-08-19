#version 310 es
precision highp float;

// Vulkan variant of ../ssao.frag. Function bodies are the same as the GL original except for the
// NDC depth-range handling in viewPosFromDepth, called out at its own site.
//
// Set 0 is SSAO-pass-only data (kernel + params + all 3 input textures) -- not shared with any
// other pipeline, since quad.vert (this pass's vertex shader) has no uniforms of its own and
// nothing else needs a 64-sample kernel. See VkSsaoUbo.cs for the C#-side mirror and its own
// std140-offset note.
layout(set = 0, binding = 0) uniform SsaoParams
{
    vec3  uKernel[64];   // 64-sample hemisphere kernel in view space (only uKernelSize used)
    mat4  uProj;
    vec2  uNoiseScale;   // screenW/4, screenH/4
    vec2  uScreenSize;   // physical pixel dimensions
    float uRadius;       // world-space hemisphere radius (default 0.5)
    float uBias;         // normal-direction bias to avoid self-occlusion
    float uStrength;     // multiplier on final occlusion factor
    int   uKernelSize;   // actual samples used (default 32)
} params;
layout(set = 0, binding = 1) uniform sampler2D uDepthTex;  // scene depth
layout(set = 0, binding = 2) uniform sampler2D uNormalTex; // packed view-space normal (RGBA8)
layout(set = 0, binding = 3) uniform sampler2D uNoiseTex;  // 4x4 tiling random rotation vectors (RG8)

#define uKernel     params.uKernel
#define uProj       params.uProj
#define uNoiseScale params.uNoiseScale
#define uScreenSize params.uScreenSize
#define uRadius     params.uRadius
#define uBias       params.uBias
#define uStrength   params.uStrength
#define uKernelSize params.uKernelSize

layout(location = 0) in  vec2 vTexCoord;
layout(location = 0) out vec4 fragColor;

// Reconstruct view-space position from depth buffer sample.
vec3 viewPosFromDepth(vec2 uv, float depth)
{
    // Vulkan's rasterizer writes NDC z natively in [0,1] (near->0, far->1) with no further
    // hardware remap, unlike GL, which writes NDC z in [-1,1] and then applies a separate
    // depth-range remap (z_win = 0.5*z_ndc + 0.5) to produce the [0,1] depth-buffer value this
    // shader samples. GL's `ndcZ = depth*2.0-1.0` line inverts that remap; Vulkan has no remap to
    // invert, so `depth` sampled from the depth buffer already is ndcZ.
    float ndcZ = depth;
    float ndcX = uv.x  * 2.0 - 1.0;
    float ndcY = uv.y  * 2.0 - 1.0;
    // Invert the perspective divide using the projection matrix diagonal.
    // proj[0][0] = 2*near/(right-left) = cot(fov/2)/aspect
    // proj[1][1] = 2*near/(top-bottom) = cot(fov/2)
    float projA = uProj[2][2];  // -(far+near)/(far-near)
    float projB = uProj[3][2];  // -2*far*near/(far-near)
    // Negated relative to GL's `viewZ = projB / (ndcZ + projA)`: GL's unnegated formula returns
    // +near at the near plane, but Camera3D.GetViewMatrix's CreateLookAt places visible geometry
    // at negative view-space z (camera looks down -Z). The negation matches that convention.
    float viewZ = -projB / (ndcZ + projA);
    // viewZ is negative for visible geometry (camera looks down -Z).
    float viewX = -viewZ * ndcX / uProj[0][0];
    float viewY = -viewZ * ndcY / uProj[1][1];
    return vec3(viewX, viewY, viewZ);
}

void main()
{
    float depth = texture(uDepthTex, vTexCoord).r;

    // Skip background (depth at far plane ~ 1.0) -- occlusion is 1.0 (no shadow).
    if (depth >= 0.9999)
    {
        fragColor = vec4(1.0);
        return;
    }

    vec3 fragPos = viewPosFromDepth(vTexCoord, depth);

    // Unpack view-space normal.
    vec3 normal = normalize(texture(uNormalTex, vTexCoord).rgb * 2.0 - 1.0);

    // Random per-fragment rotation vector from the tiling noise texture.
    vec2 noiseUv = vTexCoord * uNoiseScale;
    vec3 randVec = vec3(texture(uNoiseTex, noiseUv).rg * 2.0 - 1.0, 0.0);

    // Gram-Schmidt: build an orthonormal TBN to orient the sample kernel
    // along the surface normal with a random tangent rotation.
    vec3 tangent   = normalize(randVec - normal * dot(randVec, normal));
    vec3 bitangent = cross(normal, tangent);
    mat3 tbn       = mat3(tangent, bitangent, normal);

    float occlusion = 0.0;
    int   samples   = clamp(uKernelSize, 1, 64);

    for (int i = 0; i < samples; i++)
    {
        // Orient hemisphere sample to surface normal.
        vec3 samplePos = tbn * uKernel[i];
        samplePos = fragPos + samplePos * uRadius;

        // Project sample position to texture UV.
        vec4 offset = uProj * vec4(samplePos, 1.0);
        offset.xyz /= offset.w;          // NDC [-1,1]
        offset.xyz  = offset.xyz * 0.5 + 0.5; // [0,1]

        // Clamp to valid texture range to avoid edge artefacts.
        if (offset.x < 0.0 || offset.x > 1.0 ||
            offset.y < 0.0 || offset.y > 1.0) continue;

        float sampleDepth = texture(uDepthTex, offset.xy).r;
        vec3  sampleView  = viewPosFromDepth(offset.xy, sampleDepth);

        // Range check: only occlude if sample is within the hemisphere radius.
        float rangeCheck = smoothstep(0.0, 1.0, uRadius / abs(fragPos.z - sampleView.z + 0.001));

        // A sample occludes if it is closer to the camera (more negative z in GL view space)
        // than the surface by at least uBias. This matches the SL viewer convention:
        // camera looks down -Z, so sampleView.z <= samplePos.z means "in front of".
        occlusion += (sampleView.z <= samplePos.z - uBias ? 1.0 : 0.0) * rangeCheck;
    }

    occlusion = 1.0 - (occlusion / float(samples)) * uStrength;
    fragColor = vec4(vec3(clamp(occlusion, 0.0, 1.0)), 1.0);
}
