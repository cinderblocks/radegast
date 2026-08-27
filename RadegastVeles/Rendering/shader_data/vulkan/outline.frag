#version 310 es
precision mediump float;

// Flat highlight color for the selection-outline pass -- a warm amber, matching SL's own
// build-highlight tint. No uniforms: same fixed-color choice wireframe.frag makes.

layout(location = 0) out vec4 fragColor;

void main()
{
    fragColor = vec4(1.0, 0.75, 0.15, 1.0);
}
