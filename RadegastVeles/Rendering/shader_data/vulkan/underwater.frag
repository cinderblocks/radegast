#version 310 es
precision mediump float;

// Vulkan-only, no GL equivalent: underwater post-process pass, composited on top of the
// already-rendered frame when the camera is below the water surface (see VkViewportControl's
// `underwater` gate). Paired with quad.vert (same full-screen-triangle template the SSAO/blur
// passes use). Reuses the water pipeline's own already-loaded dudv/normal map textures (see
// VkViewportControl's _waterNormalTex/_waterDudvTex) for the distortion/caustic samples --
// same textures, different purpose, no new texture assets needed.

layout(location = 0) in vec2 vTexCoord;

layout(set = 0, binding = 0) uniform sampler2D uSceneColor;
layout(set = 0, binding = 1) uniform sampler2D uNormalMap;
layout(set = 0, binding = 2) uniform sampler2D uDudvMap;

layout(push_constant) uniform PerDraw
{
    vec4 uFogColorIntensity; // rgb = water fog color, a = depth-based tint intensity [0,1]
    float uTime;
} pc;
#define uFogColorIntensity pc.uFogColorIntensity
#define uTime pc.uTime

layout(location = 0) out vec4 fragColor;

void main()
{
    // Scrolled dudv sample for a subtle refraction wobble -- the same scrolling-UV trick
    // water.frag itself uses for its own surface distortion, applied here to the
    // already-rendered scene color instead of a reflection/refraction texture.
    vec2 scrollA = vTexCoord * 3.0 + vec2(uTime * 0.03, uTime * 0.02);
    vec2 scrollB = vTexCoord * 3.0 + vec2(-uTime * 0.02, uTime * 0.025);
    vec2 dudvA = texture(uDudvMap, scrollA).rg * 2.0 - 1.0;
    vec2 dudvB = texture(uDudvMap, scrollB).rg * 2.0 - 1.0;
    vec2 distortion = (dudvA + dudvB) * 0.008; // small screen-space offset, in UV units

    vec2 sampleUv = clamp(vTexCoord + distortion, vec2(0.001), vec2(0.999));
    vec3 sceneColor = texture(uSceneColor, sampleUv).rgb;

    // Soft caustic highlight from the normal map's own scrolled samples: a cheap, common trick
    // that brightens where two differently-scrolled normal samples agree (their dot product is
    // high), producing a shifting dappled-light look without a real light-simulation pass or any
    // new geometry.
    vec2 causticScrollA = vTexCoord * 6.0 + vec2(uTime * 0.05, -uTime * 0.04);
    vec2 causticScrollB = vTexCoord * 6.0 + vec2(-uTime * 0.045, uTime * 0.035);
    vec3 normA = texture(uNormalMap, causticScrollA).rgb * 2.0 - 1.0;
    vec3 normB = texture(uNormalMap, causticScrollB).rgb * 2.0 - 1.0;
    float caustic = pow(clamp(dot(normalize(normA), normalize(normB)), 0.0, 1.0), 4.0);

    vec3 tinted = mix(sceneColor, uFogColorIntensity.rgb, uFogColorIntensity.a);
    tinted += caustic * 0.06 * (1.0 - uFogColorIntensity.a); // caustics fade out with depth too

    fragColor = vec4(tinted, 1.0);
}
