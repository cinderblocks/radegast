#version 310 es

// Vulkan variant of ../water.vert. Byte-identical logic to the GL original -- full-screen
// triangle generated entirely from the vertex index, same family as vulkan/sky.vert/quad.vert
// (see plan Section 7/8c-2's gl_VertexID -> gl_VertexIndex note). No uniforms, no vertex buffer.

layout(location = 0) out vec2 vNdc;

void main()
{
    float x = float((gl_VertexIndex & 1) << 2) - 1.0;
    float y = float((gl_VertexIndex & 2) << 1) - 1.0;
    vNdc        = vec2(x, y);
    gl_Position = vec4(x, y, 1.0, 1.0);  // depth 1 for early-out via depth test
}
