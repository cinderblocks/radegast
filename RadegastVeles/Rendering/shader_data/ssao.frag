#version 300 es
precision highp float;

in vec2 vTexCoord;

uniform sampler2D uDepthTex;     // scene depth (GL_DEPTH_COMPONENT24)
uniform sampler2D uNormalTex;    // packed view-space normal (RGB8)
uniform sampler2D uNoiseTex;     // 4x4 tiling random rotation vectors (RG8)

// 64-sample hemisphere kernel in view space, pre-distributed with acceleration
// bias toward the origin. Passed as an array of vec3.
uniform vec3      uKernel[64];
uniform int       uKernelSize;   // actual samples used (default 32)
uniform vec2      uNoiseScale;   // screenW/4, screenH/4
uniform mat4      uProj;
uniform vec2      uScreenSize;   // physical pixel dimensions

// Radius and bias match SL viewer llDrawPoolSimple SSAO pass defaults.
uniform float     uRadius;       // world-space hemisphere radius (default 0.5)
uniform float     uBias;         // normal-direction bias to avoid self-occlusion
uniform float     uStrength;     // multiplier on final occlusion factor

out vec4 fragColor;

// Reconstruct view-space position from depth buffer sample.
vec3 viewPosFromDepth(vec2 uv, float depth)
{
    // Convert depth from [0,1] to NDC [-1,1] then unproject.
    float ndcZ = depth * 2.0 - 1.0;
    float ndcX = uv.x  * 2.0 - 1.0;
    float ndcY = uv.y  * 2.0 - 1.0;
    // Invert the perspective divide using the projection matrix diagonal.
    // proj[0][0] = 2*near/(right-left) = cot(fov/2)/aspect
    // proj[1][1] = 2*near/(top-bottom) = cot(fov/2)
    float projA = uProj[2][2];  // -(far+near)/(far-near)
    float projB = uProj[3][2];  // -2*far*near/(far-near)
    float viewZ = projB / (ndcZ + projA);
    // viewZ is negative in OpenGL (camera looks down -Z)
    float viewX = -viewZ * ndcX / uProj[0][0];
    float viewY = -viewZ * ndcY / uProj[1][1];
    return vec3(viewX, viewY, viewZ);
}

void main()
{
    float depth = texture(uDepthTex, vTexCoord).r;

    // Skip background (depth at far plane ≈ 1.0) — occlusion is 1.0 (no shadow).
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
