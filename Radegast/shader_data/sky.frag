#version 120

// Sky dome fragment shader implementing a simple Rayleigh + Mie scattering approximation

varying vec4 vColor;
varying vec3 vDir;

const float PI = 3.14159265358979323846;

uniform float atmosphereStrength; // 0.0 to 1.0, controls atmospheric haze
uniform vec3 sunDirection; // Normalized sun direction in world space
uniform vec4 sunColor; // Color of the sun
uniform float sunInfluence; // How much the sun affects the sky color

// Additional scattering parameters (optional)
uniform float rayleighScale; // multiplier for Rayleigh scattering
uniform float mieScale;      // multiplier for Mie scattering
uniform float mieG;          // anisotropy for Mie (0..0.99)
uniform float sunIntensity; // intensity multiplier for sunlight
uniform float exposure;      // simple exposure/tone control

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

// safe normalize helper
vec3 safeNormalize(vec3 v)
{
    float len = length(v);
    if (len <= 1e-6) return vec3(0.0, 0.0, 1.0);
    return v / len;
}

void main()
{
    // base vertex color (precomputed gradient + baked sun influence)
    vec3 base = vColor.rgb;

    // Normalized direction to this point on the dome
    vec3 dir = safeNormalize(vDir);

    // Use the z component as altitude indicator (0 at horizon, 1 at zenith for hemisphere)
    float altitude = clamp(dir.z, 0.0, 1.0);

    // Surface normal approximation (pointing outward from dome center)
    vec3 normal = dir;

    // Compute angle between sun and vertex direction
    vec3 sd = safeNormalize(sunDirection);
    float mu = clamp(dot(normal, sd), -1.0, 1.0);

    // Rayleigh scattering: wavelength dependent ~ 1/lambda^4
    vec3 lambda = vec3(680e-9, 550e-9, 440e-9);
    vec3 invWavelength4 = vec3(pow(lambda.x, -4.0), pow(lambda.y, -4.0), pow(lambda.z, -4.0));

    // Default scales if uniforms not provided
    float rScale = (rayleighScale > 0.0) ? rayleighScale : 0.0025;
    float mScale = (mieScale > 0.0) ? mieScale : 0.0010;
    float g = clamp(mieG, -0.99, 0.99);
    float sunI = (sunIntensity > 0.0) ? sunIntensity : 20.0;
    float expExposure = (exposure > 0.0) ? exposure : 1.0;

    // Rayleigh phase function (approx)
    float cosTheta = clamp(dot(dir, sd), -1.0, 1.0);
    float rayleighPhase = (3.0 / (16.0 * PI)) * (1.0 + cosTheta * cosTheta);

    // Mie phase function (Henyey-Greenstein)
    float denom = 1.0 + g * g - 2.0 * g * cosTheta;
    float miePhase = (1.0 - g * g) / (4.0 * PI * pow(denom, 1.5));

    // Optical depth approximation using altitude
    float optical = exp(- (1.0 - altitude) * atmosphereStrength * 4.0);

    // Compute scattering contributions
    vec3 rayleigh = rayleighPhase * invWavelength4 * rScale * sunI * optical;
    vec3 mie = miePhase * mScale * sunI * optical * vec3(1.0);

    // Combine scattering with base color
    vec3 scattered = rayleigh + mie * sunInfluence;

    // Blend base color with scattering
    float scatterMix = clamp(atmosphereStrength * 0.7 + (1.0 - altitude) * 0.3, 0.0, 1.0);
    vec3 color = mix(base, base + scattered, scatterMix);

    // Add a soft sun core highlight
    float sunDot = clamp(dot(dir, sd), 0.0, 1.0);
    float sunCore = smoothstep(0.98, 1.0, sunDot);
    color += sunCore * sunColor.rgb * sunI * 0.6;

    // Cloud layers sampling - use planar projection from XY of direction
    // Scale the XY by the inverse of Z to project onto a plane at z=1
    // This creates a planar UV mapping that tiles across the dome
    float projScale = 1.0 / max(0.1, altitude + 0.1); // avoid division by zero near horizon
    vec2 baseUV = dir.xy * projScale * 0.5 + 0.5; // map to 0..1 range
    
    // Apply per-layer scale and offset (offset animates rotation)
    float scale0 = (cloud0Scale > 0.0) ? cloud0Scale : 1.0;
    float scale1 = (cloud1Scale > 0.0) ? cloud1Scale : 1.8;
    float scale2 = (cloud2Scale > 0.0) ? cloud2Scale : 2.6;
    
    vec2 uv0 = baseUV * scale0 + vec2(cloud0Offset * 0.1, cloud0Offset * 0.1);
    vec2 uv1 = baseUV * scale1 + vec2(cloud1Offset * 0.1, -cloud1Offset * 0.05);
    vec2 uv2 = baseUV * scale2 + vec2(-cloud2Offset * 0.08, cloud2Offset * 0.08);

    // Wrap UVs for tiling
    uv0 = fract(uv0);
    uv1 = fract(uv1);
    uv2 = fract(uv2);

    // Sample cloud textures
    vec4 c0 = texture2D(cloud0, uv0);
    vec4 c1 = texture2D(cloud1, uv1);
    vec4 c2 = texture2D(cloud2, uv2);

    // Get alpha values with defaults
    float alpha0 = (cloud0Alpha > 0.0) ? cloud0Alpha : 0.6;
    float alpha1 = (cloud1Alpha > 0.0) ? cloud1Alpha : 0.48;
    float alpha2 = (cloud2Alpha > 0.0) ? cloud2Alpha : 0.36;

    // Fade clouds near horizon (clouds more visible near zenith)
    float cloudFade = smoothstep(0.05, 0.4, altitude);

    // Combine clouds - use the texture's brightness (assume grayscale cloud texture)
    float cloudAmount0 = c0.r * alpha0 * cloudFade;
    float cloudAmount1 = c1.r * alpha1 * cloudFade;
    float cloudAmount2 = c2.r * alpha2 * cloudFade;

    // Blend clouds onto sky color (white clouds)
    vec3 cloudColor = vec3(1.0, 1.0, 1.0);
    color = mix(color, cloudColor, cloudAmount0 * 0.7);
    color = mix(color, cloudColor, cloudAmount1 * 0.5);
    color = mix(color, cloudColor, cloudAmount2 * 0.3);

    // Apply exposure / simple tone mapping
    color = 1.0 - exp(-color * expExposure);

    // Ensure valid range
    color = clamp(color, 0.0, 1.0);

    gl_FragColor = vec4(color, 1.0);
}
