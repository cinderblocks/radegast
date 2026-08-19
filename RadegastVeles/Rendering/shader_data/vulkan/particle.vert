#version 310 es

// Vulkan variant of ../particle.vert (plan Section 8c-4b). No license header at the top of this
// file deliberately -- established convention, same reasoning as every other vulkan/ file.
//
// uMvp becomes a push-constant field (Vulkan has no loose uniforms), shared with particle.frag's
// uHasTexture/uGlow in ONE push-constant block/range covering both stages (matching wireframe.
// vert/picking.frag's established "one range, disjoint per-stage active byte spans" pattern) --
// simpler than two separate ranges for a block this small. Every in/out gains an explicit
// layout(location=); aPosition/aTexCoord/aColor already had them in the GL original (GlParticle
// Buffer's own vertex layout requires explicit locations even on GL ES), only the `out` varyings
// needed adding.
layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec2 aTexCoord;
layout(location = 2) in vec4 aColor;

layout(push_constant) uniform PushConstants {
    mat4  uMvp;
    int   uHasTexture;
    float uGlow;
} pc;

layout(location = 0) out vec2 vTexCoord;
layout(location = 1) out vec4 vColor;

void main()
{
    gl_Position = pc.uMvp * vec4(aPosition, 1.0);
    vTexCoord   = aTexCoord;
    vColor      = aColor;
}
