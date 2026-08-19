#version 310 es

// Vulkan variant of ../sky.vert. Full-screen triangle -- no vertex buffer, an empty
// VkPipelineVertexInputStateCreateInfo, drawn with vkCmdDraw(cmd, 3, 1, 0, 0). gl_VertexIndex
// replaces GL's gl_VertexID per the Vulkan GLSL built-in mapping (GL_KHR_vulkan_glsl) -- verified
// via dotnet-shaderc.

layout(location = 0) out vec2 vNdc;

void main()
{
    float x = float((gl_VertexIndex & 1) << 2) - 1.0;
    float y = float((gl_VertexIndex & 2) << 1) - 1.0;
    vNdc        = vec2(x, y);
    gl_Position = vec4(x, y, 1.0, 1.0);  // z=w=1 -> NDC depth 1 (far plane)
}
