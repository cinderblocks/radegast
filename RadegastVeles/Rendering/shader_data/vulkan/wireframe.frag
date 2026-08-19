#version 310 es
precision mediump float;

// Vulkan variant of ../wireframe.frag. No uniforms in the original -- only the #version
// bump was needed.

layout(location = 0) out vec4 fragColor;

void main()
{
    fragColor = vec4(0.20, 0.20, 0.20, 1.0);
}
