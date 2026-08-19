#version 310 es
precision highp float;

// Vulkan variant of ../gnorm.frag -- paired with the EXISTING vulkan/prim.vert (not a new
// vertex shader), reusing its vNormal/vViewPos/vTexCoord outputs directly. Locations below
// match prim.vert's layout(location=N) declarations exactly (3/4/5). No uniforms/samplers
// of its own: this fragment shader is pure varying-in/color-out, same as the GL original.
// Note prim.vert still requires set 0 (PerFrame UBO, for vWorldPos) even though nothing in
// THIS fragment shader reads it -- the G-buffer pipeline's layout must include set 0 to
// satisfy prim.vert's own descriptor requirement, reusing VkPrimPipeline.PerFrameLayout
// directly (same object, not a duplicate), same pattern as the sky pipeline's set 0.

layout(location = 3) in vec3 vNormal;
layout(location = 4) in vec3 vViewPos;
layout(location = 5) in vec2 vTexCoord;

// We only need the view-space normal packed into RGBA8 colour attachment.
// Normals are in [-1,1] -- pack to [0,1] for RGBA8 storage.
layout(location = 0) out vec4 fragNormal;

void main()
{
    vec3 n = normalize(gl_FrontFacing ? vNormal : -vNormal);
    fragNormal = vec4(n * 0.5 + 0.5, 1.0);
}
