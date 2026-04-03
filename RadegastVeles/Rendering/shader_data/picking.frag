#version 300 es
// highp required for exact float-to-byte round-trips when encoding prim/face IDs.
precision highp float;

uniform vec4 uPickColor;

out vec4 fragColor;

void main()
{
    fragColor = uPickColor;
}
