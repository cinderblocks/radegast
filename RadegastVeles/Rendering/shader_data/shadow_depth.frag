#version 300 es
precision mediump float;

// Depth-only shadow-caster pass. Paired with the unmodified prim.vert (like
// gnorm.frag reuses it for the SSAO G-buffer pass) — gl_Position is all that
// matters here, so every varying prim.vert writes is simply ignored.
//
// The FBO this renders into has no colour attachment, so nothing needs to be
// written; the GPU's fixed depth-test/write stage fills the depth texture from
// gl_Position alone. An empty main() is intentional, not a placeholder.
void main()
{
}
