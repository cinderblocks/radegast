#version 120

uniform sampler2D colorMap;
uniform vec3 lightDir; // normalized
uniform vec4 ambientColor;
uniform vec4 diffuseColor;
uniform vec4 specularColor;
uniform vec4 materialColor; // Per-face RGBA color
uniform int hasTexture; // 0 = no texture, 1 = has texture
uniform float glow;
uniform float glowStrength;
uniform float shininessExp; // specular exponent
uniform float specularStrength; // specular multiplier
uniform float gamma; // gamma correction value (>0 to enable)

varying vec3 vNormal;
varying vec2 vTexCoord;
varying vec3 vPos;

// Helper to safely normalize a vector and provide a default if length is zero or NaN
vec3 safeNormalize(vec3 v, vec3 defaultDir)
{
    // Protect against NaNs: NaN is not equal to itself
    if (v.x != v.x || v.y != v.y || v.z != v.z) return defaultDir;
    float len = length(v);
    if (len <= 1e-6) return defaultDir;
    return v / len;
}

void main()
{
    // Get base color from texture if available, otherwise use white
    vec4 tex = hasTexture > 0 ? texture2D(colorMap, vTexCoord) : vec4(1.0);
    
    // Apply material color (teFace.RGBA)
    tex *= materialColor;
    
    // Safely normalize normal and light direction
    vec3 n = safeNormalize(vNormal, vec3(0.0, 0.0, 1.0));
    vec3 ld = safeNormalize(lightDir, vec3(0.0, 0.0, 1.0));
    
    // Very soft wrap-around lighting to eliminate harsh shadows
    // Map dot product from -1..1 to 0.5..1.0 range - much softer
    float nDotL = dot(n, ld);
    float lambert = nDotL * 0.25 + 0.75; // Very soft wrap: -1 maps to 0.5, +1 maps to 1.0
    
    // Boost ambient significantly to fill in shadows
    vec4 ambientContribution = ambientColor * tex * 2.0;
    vec4 diffuseContribution = diffuseColor * tex * lambert;
    
    // Use additive combination to preserve brightness on shiny materials
    vec4 color = ambientContribution + diffuseContribution;
    
    // Specular
    vec3 viewDir = safeNormalize(-vPos, vec3(0.0, 0.0, 1.0));
    vec3 halfVec = safeNormalize(ld + viewDir, ld);
    float spec = pow(max(dot(n, halfVec), 0.0), max(1.0, shininessExp));
    // Apply specular strength and specular color
    color += specularColor * spec * specularStrength;

    // Ensure minimum brightness to prevent completely black surfaces
    vec3 minBright = ambientColor.rgb * tex.rgb * 0.3;
    color.rgb = max(color.rgb, minBright);

    // Additive glow component (uses texture color as tint)
    if (glow > 0.0)
    {
        // Multiply glow by a strength factor to allow stronger effect
        color.rgb += tex.rgb * glow * glowStrength;
    }

    // Clamp and guard against NaNs before writing to the framebuffer
    // Use self-comparison to detect NaNs since 'isnan' may not be available in GLSL 1.20
    if (color.r != color.r || color.g != color.g || color.b != color.b || color.a != color.a)
    {
        color = vec4(0.5 * ambientColor.rgb, tex.a);
    }
    color = clamp(color, 0.0, 1.0);

    // Apply gamma correction if gamma > 0. Gamma encodes linear color to sRGB-like space
    if (gamma > 0.0)
    {
        float invGamma = 1.0 / max(0.001, gamma);
        color.rgb = pow(color.rgb, vec3(invGamma));
    }

    gl_FragColor = color;
}
