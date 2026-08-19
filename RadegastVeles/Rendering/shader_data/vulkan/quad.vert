#version 310 es

// Vulkan variant of ../quad.vert. Full-screen triangle -- no vertex buffer, an empty
// VkPipelineVertexInputStateCreateInfo, drawn with vkCmdDraw(cmd, 3, 1, 0, 0). gl_VertexIndex
// replaces GL's gl_VertexID (see vulkan/sky.vert's identical note -- same GL_KHR_vulkan_glsl
// built-in mapping, verified via dotnet-shaderc). Used by both the SSAO and SSAO-blur passes.

layout(location = 0) out vec2 vTexCoord;

void main()
{
    // Vertices at (-1,-1), (3,-1), (-1,3) cover the entire clip space.
    // Derived UV is in [0,1]x[0,1] over the visible [0,2]x[0,2] sub-range.
    float x = float((gl_VertexIndex & 1) << 2) - 1.0;
    float y = float((gl_VertexIndex & 2) << 1) - 1.0;
    vTexCoord = vec2(x, y) * 0.5 + 0.5;
    gl_Position = vec4(x, y, 0.0, 1.0);
}
