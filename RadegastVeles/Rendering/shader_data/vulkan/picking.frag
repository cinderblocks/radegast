#version 310 es
// highp required for exact float-to-byte round-trips when encoding prim/face IDs.
precision highp float;

// Vulkan variant of ../picking.frag. Pairs with vulkan/wireframe.vert (plan Section 5's
// pipeline table, row 3) -- same push-constant block layout, so both stages of this
// pipeline share one push-constant range with wireframe's vertex stage effectively only
// using the leading mat4 and this stage only using the trailing vec4.

layout(push_constant) uniform PerDraw
{
    mat4 uMvp;
    vec4 uPickColor;
} draw;

layout(location = 0) out vec4 fragColor;

void main()
{
    fragColor = draw.uPickColor;
}
