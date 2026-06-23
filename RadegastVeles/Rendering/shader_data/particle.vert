#version 300 es

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec2 aTexCoord;
layout(location = 2) in vec4 aColor;

uniform mat4 uMvp;

out vec2 vTexCoord;
out vec4 vColor;

void main()
{
    gl_Position = uMvp * vec4(aPosition, 1.0);
    vTexCoord   = aTexCoord;
    vColor      = aColor;
}
