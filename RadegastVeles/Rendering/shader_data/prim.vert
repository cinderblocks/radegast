#version 300 es

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aTexCoord;

uniform mat4 uMvp;
uniform mat4 uModelView;
uniform mat3 uNormalMat;

out vec3 vNormal;
out vec3 vViewPos;
out vec2 vTexCoord;

void main()
{
    gl_Position = uMvp * vec4(aPosition, 1.0);
    vViewPos    = vec3(uModelView * vec4(aPosition, 1.0));
    vNormal     = uNormalMat * aNormal;
    vTexCoord   = aTexCoord;
}
