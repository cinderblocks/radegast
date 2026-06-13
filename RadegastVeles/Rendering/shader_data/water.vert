#version 300 es
precision highp float;

// Full-screen triangle — no vertex buffer needed.
// The fragment shader casts a ray and intersects it with the water plane,
// so the water surface analytically extends to the true horizon.

out vec2 vNdc;

void main()
{
    float x = float((gl_VertexID & 1) << 2) - 1.0;
    float y = float((gl_VertexID & 2) << 1) - 1.0;
    vNdc        = vec2(x, y);
    gl_Position = vec4(x, y, 1.0, 1.0);  // depth 1 for early-out via depth test
}
