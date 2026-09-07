#version 310 es

// Occlusion-query proxy-box vertex shader. Transforms the unit-cube proxy mesh (see
// VkViewportControl.BuildOcclusionProxyMesh) by a per-object MVP already baked to encode the
// object's expanded world-space AABB -- identical push-constant shape to wireframe.vert/
// outline.vert, no descriptor sets needed.

layout(location = 0) in vec3 aPosition;

layout(push_constant) uniform PerDraw
{
    mat4 uMvp;
} draw;

void main()
{
    gl_Position = draw.uMvp * vec4(aPosition, 1.0);
}
