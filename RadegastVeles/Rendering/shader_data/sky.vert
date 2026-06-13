#version 300 es

// Full-screen triangle — no vertex buffer needed, bind _quadVao and draw 3 vertices.
// Passes the NDC position to the fragment shader for view-ray reconstruction.

out vec2 vNdc;

void main()
{
    float x = float((gl_VertexID & 1) << 2) - 1.0;
    float y = float((gl_VertexID & 2) << 1) - 1.0;
    vNdc        = vec2(x, y);
    gl_Position = vec4(x, y, 1.0, 1.0);  // z=w=1 → NDC depth 1 (far plane)
}
