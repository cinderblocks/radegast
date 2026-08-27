#version 310 es

// Inverted-hull selection-outline pass (see VkOutlinePipeline.cs): pushes each vertex out
// along its own normal by a small fixed object-space offset before applying the same
// per-draw MVP wireframe.vert uses. Combined with VkOutlinePipeline's CullMode.Front, only
// the expanded shell's back faces survive -- visible as a thin rim right at the silhouette,
// since everywhere else the expanded shell is behind the real (un-expanded) surface drawn
// in the main pass.

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;

layout(push_constant) uniform PerDraw
{
    mat4 uMvp;
} draw;

const float OUTLINE_OFFSET = 0.02;

void main()
{
    gl_Position = draw.uMvp * vec4(aPosition + aNormal * OUTLINE_OFFSET, 1.0);
}
