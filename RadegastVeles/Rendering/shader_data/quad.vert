#version 300 es

// Full-screen triangle using gl_VertexID — no vertex buffer needed.
// Draw with GL.DrawArrays(Triangles, 0, 3) with an empty VAO bound.
out vec2 vTexCoord;

void main()
{
    // Vertices at (-1,-1), (3,-1), (-1,3) cover the entire clip space.
    // Derived UV is in [0,1]x[0,1] over the visible [0,2]x[0,2] sub-range.
    float x = float((gl_VertexID & 1) << 2) - 1.0;
    float y = float((gl_VertexID & 2) << 1) - 1.0;
    vTexCoord = vec2(x, y) * 0.5 + 0.5;
    gl_Position = vec4(x, y, 0.0, 1.0);
}
