#version 310 es

// Vulkan variant of ../wireframe.vert. Reused as-is by picking.frag's pipeline too (see
// plan Section 5's pipeline table, row 3), same as the GL original.

layout(location = 0) in vec3 aPosition;

layout(push_constant) uniform PerDraw
{
    mat4 uMvp;
} draw;

void main()
{
    gl_Position = draw.uMvp * vec4(aPosition, 1.0);
}
