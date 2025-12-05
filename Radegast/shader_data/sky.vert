#version 120

// Sky dome vertex shader
// Simple pass-through shader with vertex colors

attribute vec3 aPosition;
attribute vec4 aColor;

varying vec4 vColor;
varying vec3 vDir; // direction from dome center to vertex, normalized

uniform mat4 uMVP;

void main()
{
    // Pass color to fragment shader
    vColor = aColor;
    
    // Provide normalized direction for fragment computations (cloud UVs, scattering)
    vDir = normalize(aPosition);
    
    // Transform position
    gl_Position = uMVP * vec4(aPosition, 1.0);
}
