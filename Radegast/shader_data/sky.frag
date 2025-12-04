#version 120

// Sky dome fragment shader implementing a simple Rayleigh + Mie scattering approximation

varying vec4 vColor;

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

    // Approximate direction and normal for the dome using the up vector.
    // The original shader used gl_FrontColor which is not available in this profile.
    // A more accurate approach would provide a vertex direction as a varying from the vertex shader.
    vec3 dir = vec3(0.0, 0.0, 1.0);

    // Use the blue channel of the vertex color as a simple altitude indicator
    float altitude = clamp(vColor.b, 0.0, 1.0);

    // Surface normal (upwards for a dome approximation)
    vec3 normal = vec3(0.0, 0.0, 1.0);

    // Compute angle between sun and vertex direction
    vec3 sd = safeNormalize(sunDirection);
    float mu = clamp(dot(normal, sd), -1.0, 1.0);

    // Rayleigh scattering: wavelength dependent ~ 1/lambda^4
    // Typical wavelengths (nm -> meters): R=680nm, G=550nm, B=440nm
    vec3 lambda = vec3(680e-9, 550e-9, 440e-9);
    vec3 invWavelength4 = vec3(pow(lambda.x, -4.0), pow(lambda.y, -4.0), pow(lambda.z, -4.0));

    // Default scales if uniforms not provided (these will be multiplied by provided uniform values)
    float rScale = (rayleighScale > 0.0) ? rayleighScale : 0.0025;
    float mScale = (mieScale > 0.0) ? mieScale : 0.0010;
    float g = clamp(mieG, -0.99, 0.99);
    float sunI = (sunIntensity > 0.0) ? sunIntensity : 20.0;
    float expExposure = (exposure > 0.0) ? exposure : 1.0;

    // Rayleigh phase function (approx)
    float cosTheta = clamp(dot(normal, sd), -1.0, 1.0);
    float rayleighPhase = (3.0 / (16.0 * PI)) * (1.0 + cosTheta * cosTheta);

    // Mie phase function (Henyey-Greenstein)
    float denom = 1.0 + g * g - 2.0 * g * cosTheta;
    float miePhase = (1.0 - g * g) / (4.0 * PI * pow(denom, 1.5));

    // Optical depth / attenuation approximation using altitude (simple)
    // altitude ~ 0 at horizon, 1 at zenith; use exponential falloff to simulate density
    float optical = exp(- (1.0 - altitude) * atmosphereStrength * 4.0);

    // Compute scattering contributions
    vec3 rayleigh = rayleighPhase * invWavelength4 * rScale * sunI * optical;
    vec3 mie = miePhase * mScale * sunI * optical * vec3(1.0);

    // Combine scattering with base color
    vec3 scattered = rayleigh + mie * sunInfluence;

    // Blend base color with scattering with altitude-based fade
    float scatterMix = clamp(atmosphereStrength * 0.7 + (1.0 - altitude) * 0.3, 0.0, 1.0);
    vec3 color = mix(base, base + scattered, scatterMix);

    // Add a soft sun core highlight based on cos between normal and sun direction
    float sunDot = clamp(dot(normalize(sd), normal), 0.0, 1.0);
    float sunCore = smoothstep(0.98, 1.0, sunDot);
    color += sunCore * sunColor.rgb * sunI * 0.6;

    // Apply exposure / simple tone mapping
    color = 1.0 - exp(-color * expExposure);

    // Ensure valid range
    color = clamp(color, 0.0, 1.0);

    gl_FragColor = vec4(color, 1.0);
}
