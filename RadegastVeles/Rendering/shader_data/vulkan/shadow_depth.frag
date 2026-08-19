#version 310 es
precision mediump float;

// Vulkan variant of ../shadow_depth.frag. Byte-identical to the GL original -- an empty
// depth-only shadow-caster pass needs no descriptor/layout changes at all, paired with the
// existing vulkan/prim.vert (like vulkan/gnorm.frag reuses it for the SSAO G-buffer pass).
// gl_Position (computed by prim.vert from the per-instance baked MVP) is all that matters here;
// the FBO this renders into has no colour attachment, so the GPU's fixed depth-test/write stage
// fills the depth texture from gl_Position alone.
void main()
{
}
