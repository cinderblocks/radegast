#version 120

// Attributes for vertex data
attribute vec3 aPosition;
attribute vec3 aNormal;
attribute vec2 aTexCoord;

// Uniforms for transformations
uniform mat4 uMVP;
uniform mat4 uModelView;
uniform mat3 uNormalMatrix;

// Varying variables that will be passed to the fragment shader
varying vec3 vNormal;
varying vec2 vTexCoord;
varying vec3 vPos;

void main()
{
    // Transform normal to eye space
    vNormal = normalize(uNormalMatrix * aNormal);
    vTexCoord = aTexCoord;
    vPos = vec3(uModelView * vec4(aPosition, 1.0));
    gl_Position = uMVP * vec4(aPosition, 1.0);
}
