#version 120

// Minimal vertex shader using compatibility built-ins to ease integration
varying vec3 vNormal;
varying vec2 vTexCoord;

void main()
{
    // Transform normal to eye space
    vNormal = normalize(gl_NormalMatrix * gl_Normal);
    vTexCoord = gl_MultiTexCoord0.st;
    gl_Position = gl_ModelViewProjectionMatrix * gl_Vertex;
}
