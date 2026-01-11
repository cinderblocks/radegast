#version 120

// Sky dome fragment shader - simplified and balanced

varying vec4 vColor;
varying vec3 vDir;

uniform float atmosphereStrength; // 0.0 to 1.0, controls atmospheric haze
uniform vec3 sunDirection; // Normalized sun direction in world space
uniform vec4 sunColor; // Color of the sun
uniform float sunInfluence; // How much the sun affects the sky color

// Cloud samplers (up to 3 layers)
uniform sampler2D cloud0;
uniform sampler2D cloud1;
uniform sampler2D cloud2;
uniform float cloud0Scale;
uniform float cloud1Scale;
uniform float cloud2Scale;
uniform float cloud0Alpha;
uniform float cloud1Alpha;
uniform float cloud2Alpha;
uniform float cloud0Offset;
uniform float cloud1Offset;
uniform float cloud2Offset;

vec3 safeNormalize(vec3 v)
{
    float len = length(v);
    if (len <= 1e-6) return vec3(0.0, 0.0, 1.0);
    return v / len;
}

void main()
{
    // Start with the base vertex color (already has gradient and sun baked in)
    vec3 color = vColor.rgb;
    
    // Normalized direction to this point on the dome
    vec3 dir = safeNormalize(vDir);
    
    // Use the z component as altitude indicator (0 at horizon, 1 at zenith)
    float altitude = clamp(dir.z, 0.0, 1.0);
    
    // Add subtle atmospheric haze near horizon if enabled
    if (atmosphereStrength > 0.0)
    {
        // Create a subtle whitening near the horizon
        float horizonFade = 1.0 - altitude;
        float hazeFactor = horizonFade * horizonFade * atmosphereStrength * 0.3;
        vec3 hazeColor = vec3(0.9, 0.95, 1.0); // Slight blue-white
        color = mix(color, hazeColor, hazeFactor);
    }
    
    // Add sun glow
    vec3 sd = safeNormalize(sunDirection);
    float sunDot = max(0.0, dot(dir, sd));
    
    // Sun halo (already in vertex colors, but can enhance)
    float sunHalo = pow(sunDot, 8.0) * sunInfluence;
    color += sunColor.rgb * sunHalo * 0.3;
    
    // Bright sun core
    float sunCore = smoothstep(0.995, 1.0, sunDot);
    color += sunColor.rgb * sunCore * 2.0;
    
    // Cloud layers sampling - only render above horizon
    if (altitude > 0.05)
    {
        // Use a simple cylindrical projection that wraps around the dome
        // This avoids the stretching/striping near the horizon
        
        // Calculate angle around the dome (0 to 2*PI)
        float angle = atan(dir.y, dir.x);
        
        // Use altitude for the V coordinate, but with less distortion
        // Map altitude (0 to 1) to a range that tiles nicely
        float v = altitude * 2.0; // Scale altitude for better tiling
        
        // For U coordinate, normalize angle to 0..1
        float u = (angle + 3.14159265) / (2.0 * 3.14159265);
        
        vec2 cloudUV = vec2(u, v);
        
        // Apply per-layer scale and animated rotation offset
        float scale0 = (cloud0Scale > 0.0) ? cloud0Scale : 1.0;
        float scale1 = (cloud1Scale > 0.0) ? cloud1Scale : 1.3;
        float scale2 = (cloud2Scale > 0.0) ? cloud2Scale : 1.6;
        
        // Apply scale and animated offset (offset acts as a scroll/rotation)
        vec2 uv0 = cloudUV * scale0 + vec2(cloud0Offset * 0.001, 0.0);
        vec2 uv1 = cloudUV * scale1 + vec2(cloud1Offset * 0.001, 0.0);
        vec2 uv2 = cloudUV * scale2 + vec2(cloud2Offset * 0.001, 0.0);
        
        // Wrap UVs for seamless tiling
        uv0 = fract(uv0);
        uv1 = fract(uv1);
        uv2 = fract(uv2);
        
        // Sample cloud textures (use alpha channel for cloud density)
        float c0 = texture2D(cloud0, uv0).a;
        float c1 = texture2D(cloud1, uv1).a;
        float c2 = texture2D(cloud2, uv2).a;
        
        // Fade clouds near horizon for smooth transition
        float cloudFade = smoothstep(0.05, 0.25, altitude);
        
        // Apply per-layer alpha and fade
        float alpha0 = (cloud0Alpha > 0.0) ? cloud0Alpha : 0.65;
        float alpha1 = (cloud1Alpha > 0.0) ? cloud1Alpha : 0.50;
        float alpha2 = (cloud2Alpha > 0.0) ? cloud2Alpha : 0.35;
        
        c0 *= alpha0 * cloudFade;
        c1 *= alpha1 * cloudFade;
        c2 *= alpha2 * cloudFade;
        
        // Combine cloud layers with additive blending for soft, fluffy appearance
        float totalCloud = c0 + c1 * 0.8 + c2 * 0.6;
        totalCloud = clamp(totalCloud, 0.0, 1.0);
        
        // Clouds are white/slightly warm tinted
        vec3 cloudColor = vec3(1.0, 0.98, 0.95);
        
        // Blend clouds onto sky
        color = mix(color, cloudColor, totalCloud * 0.85);
    }
    
    // Ensure valid range
    color = clamp(color, 0.0, 1.0);
    
    gl_FragColor = vec4(color, 1.0);
}
