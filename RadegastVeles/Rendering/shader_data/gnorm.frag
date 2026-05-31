#version 300 es
precision highp float;

in  vec3 vNormal;
in  vec3 vViewPos;
in  vec2 vTexCoord;

// We only need the view-space normal packed into RGB16F / RGB8 colour attachment.
// Normals are in [-1,1] — pack to [0,1] for RGBA8 storage.
out vec4 fragNormal;

void main()
{
    vec3 n = normalize(gl_FrontFacing ? vNormal : -vNormal);
    fragNormal = vec4(n * 0.5 + 0.5, 1.0);
}
