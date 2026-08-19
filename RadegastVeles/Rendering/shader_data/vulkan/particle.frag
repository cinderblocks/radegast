#version 310 es
precision mediump float;

// Vulkan variant of ../particle.frag (plan Section 8c-4b). Same #define-aliasing convention as
// prim.frag/skin.comp: main() below is byte-identical to the GL original, only declarations
// changed. uAlbedo moves to set 0 binding 0 (fragment-only); uHasTexture/uGlow share the SAME
// push-constant block declared in particle.vert (must match byte-for-byte -- both stages bind
// the same pipeline layout's single push-constant range).
//
// uGlow is ported EXACTLY as GL has it: the GL code hardcodes
// `_particleShader.Set("uGlow", 0f)` unconditionally -- ParticleVertex.Glow is read from the
// simulator but never actually reaches the shader on the GL side either. This is a pre-existing
// GL gap, not something introduced or silently "fixed" by this port -- the uniform stays wired
// exactly as dead as it already is.

layout(location = 0) in vec2 vTexCoord;
layout(location = 1) in vec4 vColor;

layout(set = 0, binding = 0) uniform sampler2D uAlbedo;

layout(push_constant) uniform PushConstants {
    mat4  uMvp;
    int   uHasTexture;
    float uGlow;
} pc;
#define uHasTexture pc.uHasTexture
#define uGlow pc.uGlow

layout(location = 0) out vec4 fragColor;

void main()
{
    vec4 texColor = (uHasTexture != 0) ? texture(uAlbedo, vTexCoord) : vec4(1.0);
    vec4 col = texColor * vColor;
    if (col.a < 0.004) discard;

    // Additive glow contribution: blend toward a brighter version by glow factor.
    vec3 glowCol = clamp(col.rgb * (1.0 + uGlow * 2.0), 0.0, 1.0);
    fragColor = vec4(mix(col.rgb, glowCol, uGlow), col.a);
}
