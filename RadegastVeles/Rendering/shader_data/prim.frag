#version 300 es
precision mediump float;

in  vec3 vNormal;
in  vec3 vViewPos;
in  vec2 vTexCoord;

uniform sampler2D uAlbedo;
uniform int       uHasTexture;
uniform vec4      uColor;
uniform float     uGlow;
uniform int       uFullbright;

// Key light — warm, upper-right of camera (view space, pre-normalised)
const vec3  kKeyDir    = vec3(0.4472, 0.7155, 0.5366);  // normalize(0.5, 0.8, 0.6)
const vec3  kKeyColor  = vec3(1.00, 0.96, 0.90);
const float kKey       = 0.70;

// Fill light — cool, lower-left of camera (view space, pre-normalised)
const vec3  kFillDir   = vec3(-0.8893, -0.2540, 0.3810); // normalize(-0.7,-0.2, 0.3)
const vec3  kFillColor = vec3(0.76, 0.88, 1.00);
const float kFill      = 0.28;

const float kAmbient   = 0.20;
const float kSpec      = 0.22;
const float kShininess = 28.0;

out vec4 fragColor;

void main()
{
    vec4 base = (uHasTexture != 0)
        ? texture(uAlbedo, vTexCoord) * uColor
        : uColor;

    if (base.a < 0.004) discard;

    if (uFullbright != 0)
    {
        fragColor = base;
        return;
    }

    // Linearise sRGB input for physically-plausible lighting.
    vec3 albedo = pow(base.rgb, vec3(2.2));

    vec3 n = normalize(vNormal);
    vec3 v = normalize(-vViewPos);

    // Key light: half-lambert diffuse + Blinn-Phong specular
    float keyDiff = dot(n, kKeyDir) * 0.5 + 0.5;
    vec3  keyH    = normalize(kKeyDir + v);
    float keySpec = pow(max(dot(n, keyH), 0.0), kShininess) * kSpec;

    // Fill light: clamped lambert, no specular
    float fillDiff = max(dot(n, kFillDir), 0.0);

    vec3 col = albedo * (kAmbient
             + kKeyColor  * kKey  * keyDiff
             + kFillColor * kFill * fillDiff);
    col     += kKeyColor * keySpec;
    col      = mix(col, albedo, clamp(uGlow, 0.0, 1.0));

    // Convert back to sRGB for display.
    col = pow(col, vec3(1.0 / 2.2));

    fragColor = vec4(col, base.a);
}

