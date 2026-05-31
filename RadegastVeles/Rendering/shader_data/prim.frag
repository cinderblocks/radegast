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
uniform float     uAlphaCutoff;

// Legacy TE shiny factor: 0.0=none, 0.24=low, 0.64=medium, 0.96=high
uniform float     uShiny;
// 1 if the TE face has a legacy bump code (Bumpiness != None)
uniform int       uHasBump;
// Alpha mode: 0=none(opaque), 1=blend, 2=mask(cutoff), 3=emissive
uniform int       uAlphaMode;

// -- LLMaterial uniforms ------------------------------------------------------
uniform int       uHasMaterial;

uniform sampler2D uNormalMap;
uniform int       uHasNormalMap;
uniform vec4      uNormalUvST;   // xy = scale, zw = offset
uniform float     uNormalUvRot;

uniform sampler2D uSpecularMap;
uniform int       uHasSpecularMap;
uniform vec4      uSpecUvST;     // xy = scale, zw = offset
uniform float     uSpecUvRot;

uniform vec4      uSpecColor;    // linear RGBA specular tint
uniform float     uSpecExp;      // [0,1] -> shininess = max(1, exp*128)
uniform float     uEnvIntensity; // [0,1] Fresnel reflection intensity

// -- PBR uniforms -------------------------------------------------------------
uniform int       uIsPBR;

uniform sampler2D uMetallicRoughnessMap;
uniform int       uHasMRMap;
uniform vec4      uMRUvST;
uniform float     uMRUvRot;

uniform sampler2D uEmissiveMap;
uniform int       uHasEmissiveMap;
uniform vec4      uEmissiveUvST;
uniform float     uEmissiveUvRot;

uniform vec4      uBaseColorFactor;
uniform float     uMetallicFactor;
uniform float     uRoughnessFactor;
uniform vec3      uEmissiveFactor;

uniform vec4      uPbrNormalUvST;
uniform float     uPbrNormalUvRot;

uniform vec4      uBaseColorUvST;
uniform float     uBaseColorUvRot;

// SSAO
uniform sampler2D uSsaoMap;
uniform int       uHasSsao;
uniform vec2      uScreenSize;

// Key light - warm, upper-right of camera (view space, pre-normalised)
const vec3  kKeyDir    = vec3(0.4472, 0.7155, 0.5366);  // normalize(0.5, 0.8, 0.6)
const vec3  kKeyColor  = vec3(1.00, 0.96, 0.90);
const float kKey       = 0.70;

// Fill light - cool, lower-left of camera (view space, pre-normalised)
const vec3  kFillDir   = vec3(-0.8893, -0.2540, 0.3810); // normalize(-0.7,-0.2, 0.3)
const vec3  kFillColor = vec3(0.76, 0.88, 1.00);
const float kFill      = 0.28;

// Rim / back light - behind the subject (negative view-space Z = away from camera),
// slight upward tilt. Creates silhouette-edge definition on avatars and prims.
// Pure diffuse - no specular component.
const vec3  kRimDir    = vec3(0.0, 0.0995, -0.9950);    // normalize(0, 0.1, -1)
const vec3  kRimColor  = vec3(0.82, 0.88, 1.00);
const float kRim       = 0.18;

const float kAmbient   = 0.25;
const float PI         = 3.14159265359;

// ACES filmic tone-mapping approximation (Narkowicz 2015).
// Maps HDR linear colour to [0,1] before gamma correction, preventing hard
// clipping on bright specular highlights. Used by the SL deferred renderer.
vec3 acesFilmic(vec3 x)
{
    const float a = 2.51;
    const float b = 0.03;
    const float c = 2.43;
    const float d = 0.59;
    const float e = 0.14;
    return clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0.0, 1.0);
}

out vec4 fragColor;

vec2 transformUv(vec2 uv, vec4 st, float rot)
{
    vec2 scaled = (uv - 0.5) * st.xy + 0.5;
    float c = cos(rot);
    float s = sin(rot);
    vec2 centered = scaled - 0.5;
    vec2 rotated = vec2(centered.x * c - centered.y * s,
                        centered.x * s + centered.y * c);
    return rotated + 0.5 + st.zw;
}

// ── PBR helpers (GGX / Cook-Torrance) ────────────────────────────────────────

float distributionGGX(float NdotH, float roughness)
{
    float a  = roughness * roughness;
    float a2 = a * a;
    float d  = NdotH * NdotH * (a2 - 1.0) + 1.0;
    return a2 / (PI * d * d + 1e-7);
}

float geometrySchlickGGX(float NdotV, float roughness)
{
    float r = roughness + 1.0;
    float k = (r * r) / 8.0;
    return NdotV / (NdotV * (1.0 - k) + k);
}

float geometrySmith(float NdotV, float NdotL, float roughness)
{
    return geometrySchlickGGX(NdotV, roughness) * geometrySchlickGGX(NdotL, roughness);
}

vec3 fresnelSchlick(float cosTheta, vec3 F0)
{
    return F0 + (1.0 - F0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}

// ── PBR lighting path ────────────────────────────────────────────────────────

void pbrLighting(vec3 albedo, float metallic, float roughness, float occlusion,
                 vec3 emissive, vec3 n, vec3 v, float alpha, out vec4 result)
{
    vec3 F0 = mix(vec3(0.04), albedo, metallic);
    roughness = max(roughness, 0.04); // avoid div-by-zero

    float NdotV = max(dot(n, v), 0.001);

    vec3 Lo = vec3(0.0);

    // Key light
    {
        vec3  L     = kKeyDir;
        vec3  H     = normalize(L + v);
        float NdotL = max(dot(n, L), 0.0);
        float NdotH = max(dot(n, H), 0.0);
        float HdotV = max(dot(H, v), 0.0);

        float D = distributionGGX(NdotH, roughness);
        float G = geometrySmith(NdotV, NdotL, roughness);
        vec3  F = fresnelSchlick(HdotV, F0);

        vec3 specular = (D * G * F) / (4.0 * NdotV * NdotL + 0.001);
        vec3 kD = (vec3(1.0) - F) * (1.0 - metallic);

        Lo += (kD * albedo / PI + specular) * kKeyColor * kKey * NdotL;
    }

    // Fill light
    {
        vec3  L     = kFillDir;
        vec3  H     = normalize(L + v);
        float NdotL = max(dot(n, L), 0.0);
        float NdotH = max(dot(n, H), 0.0);
        float HdotV = max(dot(H, v), 0.0);

        float D = distributionGGX(NdotH, roughness);
        float G = geometrySmith(NdotV, NdotL, roughness);
        vec3  F = fresnelSchlick(HdotV, F0);

        vec3 specular = (D * G * F) / (4.0 * NdotV * NdotL + 0.001);
        vec3 kD = (vec3(1.0) - F) * (1.0 - metallic);

        Lo += (kD * albedo / PI + specular) * kFillColor * kFill * NdotL;
    }

    // Rim light: pure diffuse, no specular. Illuminates silhouette edges by
    // lighting surfaces that face away from the camera (negative view-space Z normal).
    {
        float NdotL = max(dot(n, kRimDir), 0.0);
        Lo += (1.0 - metallic) * albedo / PI * kRimColor * kRim * NdotL;
    }

    // Indirect ambient: split into diffuse and specular terms.
    // Mirrors the split-sum IBL decomposition used in the SL PBR viewer:
    //   indirect_diffuse  = (1 - F) * (1 - metallic) * albedo
    //   indirect_specular = F * specScale   where specScale ≈ integral of GGX over a
    //                                        uniform hemisphere at this roughness.
    float ssao = (uHasSsao != 0)
        ? texture(uSsaoMap, gl_FragCoord.xy / uScreenSize).r
        : 1.0;
    vec3  F_amb      = fresnelSchlick(NdotV, F0);
    vec3  kD_amb     = (vec3(1.0) - F_amb) * (1.0 - metallic);
    // Specular ambient response: smooth surfaces reflect more of the environment.
    float specScale  = mix(0.04, 0.50, 1.0 - roughness * roughness);
    vec3 ambient     = kAmbient * (kD_amb * albedo + F_amb * specScale) * occlusion * ssao;

    vec3 col = ambient + Lo + emissive;

    // ACES filmic tonemap: compresses HDR highlights to [0,1] before gamma,
    // preventing hard clipping on smooth metallic specular spikes.
    col = acesFilmic(col);
    col = pow(col, vec3(1.0 / 2.2));

    result = vec4(col, alpha);
}

void main()
{
    // ── PBR path ─────────────────────────────────────────────────────────
    if (uIsPBR != 0)
    {
        vec2 bcUv = transformUv(vTexCoord, uBaseColorUvST, uBaseColorUvRot);
        vec4 base = (uHasTexture != 0)
            ? texture(uAlbedo, bcUv) * uBaseColorFactor
            : uBaseColorFactor;

        // AlphaMode 0 (None/opaque): never discard — baked textures store compositing
        // data in alpha, not GL transparency.  Mode 1 (blend) and 2 (mask) use the
        // texture alpha; for everything else keep a near-zero sentinel for degenerate
        // fully-transparent fragments produced by bad content.
        if (uAlphaMode != 0)
        {
            float cutoff = (uAlphaMode == 2) ? uAlphaCutoff : 0.004;
            if (base.a < cutoff) discard;
        }

        if (uFullbright != 0)
        {
            vec3 glowCol = clamp(base.rgb * (1.0 + uGlow), 0.0, 1.0);
            fragColor = vec4(glowCol, (uAlphaMode == 1) ? base.a : 1.0);
            return;
        }

        vec3 albedo = pow(base.rgb, vec3(2.2));

        vec3 n = normalize(gl_FrontFacing ? vNormal : -vNormal);
        vec3 v = normalize(-vViewPos);

        // TBN from screen-space derivatives
        vec3 posDx = dFdx(vViewPos);
        vec3 posDy = dFdy(vViewPos);
        vec2 texDx = dFdx(vTexCoord);
        vec2 texDy = dFdy(vTexCoord);
        float det = texDx.x * texDy.y - texDy.x * texDx.y;
        bool hasTBN = abs(det) > 1e-5;
        vec3 T, B;
        if (hasTBN)
        {
            T = normalize( texDy.y * posDx - texDx.y * posDy);
            B = normalize(-texDy.x * posDx + texDx.x * posDy);
        }
        else
        {
            T = vec3(1.0, 0.0, 0.0);
            B = vec3(0.0, 1.0, 0.0);
        }

        if (uHasNormalMap != 0 && hasTBN)
        {
            vec2 nmUv = transformUv(vTexCoord, uPbrNormalUvST, uPbrNormalUvRot);
            vec3 nmSample = texture(uNormalMap, nmUv).rgb * 2.0 - 1.0;
            n = normalize(T * nmSample.x + B * nmSample.y + n * nmSample.z);
        }

        // Sample metallic-roughness (ORM: R=occlusion, G=roughness, B=metallic)
        float metallic  = uMetallicFactor;
        float roughness = uRoughnessFactor;
        float occlusion = 1.0;
        if (uHasMRMap != 0)
        {
            vec2 mrUv = transformUv(vTexCoord, uMRUvST, uMRUvRot);
            vec4 mrSample = texture(uMetallicRoughnessMap, mrUv);
            occlusion  = mrSample.r;
            roughness *= mrSample.g;
            metallic  *= mrSample.b;
        }

        // Emissive
        vec3 emissive = uEmissiveFactor;
        if (uHasEmissiveMap != 0)
        {
            vec2 emUv = transformUv(vTexCoord, uEmissiveUvST, uEmissiveUvRot);
            emissive *= pow(texture(uEmissiveMap, emUv).rgb, vec3(2.2));
        }

        // Add glow as extra emission
        emissive += albedo * uGlow;

        vec4 result;
        pbrLighting(albedo, metallic, roughness, occlusion, emissive, n, v,
                    (uAlphaMode == 1) ? base.a : 1.0, result);
        fragColor = result;
        return;
    }

    // ── Legacy Blinn-Phong path ──────────────────────────────────────────
    vec4 base = (uHasTexture != 0)
        ? texture(uAlbedo, vTexCoord) * uColor
        : uColor;

    // AlphaMode 0 (None/opaque): never discard — baked textures store compositing
    // data in alpha, not GL transparency.  Mode 1 (blend) and 2 (mask) use the
    // texture alpha; for everything else a near-zero sentinel drops degenerate
    // fully-transparent fragments from bad content.
    if (uAlphaMode != 0)
    {
        float cutoff = (uAlphaMode == 2) ? uAlphaCutoff : 0.004;
        if (base.a < cutoff) discard;
    }

    if (uFullbright != 0)
    {
        // Glow adds extra brightness on top of the fully-lit base colour,
        // approximating the emission the face would contribute to a bloom pass.
        vec3 glowCol = clamp(base.rgb * (1.0 + uGlow), 0.0, 1.0);
        fragColor = vec4(glowCol, (uAlphaMode == 1) ? base.a : 1.0);
        return;
    }

    // Linearise sRGB input for physically-plausible lighting.
    vec3 albedo = pow(base.rgb, vec3(2.2));

    vec3 n = normalize(gl_FrontFacing ? vNormal : -vNormal);
    vec3 v = normalize(-vViewPos);

    // Build TBN from screen-space derivatives (used by both bump and normal map).
    vec3 posDx = dFdx(vViewPos);
    vec3 posDy = dFdy(vViewPos);
    vec2 texDx = dFdx(vTexCoord);
    vec2 texDy = dFdy(vTexCoord);
    float det = texDx.x * texDy.y - texDy.x * texDx.y;
    bool hasTBN = abs(det) > 1e-5;
    vec3 T, B;
    if (hasTBN)
    {
        T = normalize( texDy.y * posDx - texDx.y * posDy);
        B = normalize(-texDy.x * posDx + texDx.x * posDy);
    }
    else
    {
        T = vec3(1.0, 0.0, 0.0);
        B = vec3(0.0, 1.0, 0.0);
    }

    // Material normal map: perturb normal using tangent-space sample.
    if (uHasNormalMap != 0 && hasTBN)
    {
        vec2 nmUv = transformUv(vTexCoord, uNormalUvST, uNormalUvRot);
        vec3 nmSample = texture(uNormalMap, nmUv).rgb * 2.0 - 1.0;
        n = normalize(T * nmSample.x + B * nmSample.y + n * nmSample.z);
    }
    // Legacy bump: perturb the surface normal using luminance gradient.
    else if (uHasBump != 0 && hasTBN)
    {
        float lum = dot(albedo, vec3(0.299, 0.587, 0.114));
        n = normalize(n + (dFdx(lum) * T + dFdy(lum) * B) * 0.5);
    }

    // Resolve specular parameters: material path or legacy TE shiny.
    float specStrength;
    float shininess;
    vec3  specTint;
    if (uHasMaterial != 0)
    {
        shininess    = max(1.0, uSpecExp * 255.0);
        specStrength = 1.0;
        vec3 matSpec = uSpecColor.rgb;
        if (uHasSpecularMap != 0)
        {
            vec2 spUv = transformUv(vTexCoord, uSpecUvST, uSpecUvRot);
            matSpec *= texture(uSpecularMap, spUv).rgb;
        }
        specTint = matSpec;
    }
    else
    {
        specStrength = mix(0.06, 1.4,   uShiny);
        shininess    = mix(16.0, 128.0, uShiny);
        specTint     = vec3(specStrength);
    }

    // Key light: standard Lambert diffuse + Blinn-Phong specular.
    // SL C++ viewer uses max(dot(N,L),0) — standard Lambert — not half-Lambert.
    // Half-Lambert (range [0.5,1.0]) washes out all shadow definition; standard
    // Lambert (range [0,1]) gives proper light/shadow contrast matching SL.
    float keyDiff = max(dot(n, kKeyDir), 0.0);
    vec3  keyH    = normalize(kKeyDir + v);
    float keySpec = pow(max(dot(n, keyH), 0.0), shininess);

    // Fill light: clamped lambert, no specular
    float fillDiff = max(dot(n, kFillDir), 0.0);

    // Rim light: pure diffuse, lights silhouette edges facing away from camera.
    float rimDiff = max(dot(n, kRimDir), 0.0);

    float ssao = (uHasSsao != 0)
        ? texture(uSsaoMap, gl_FragCoord.xy / uScreenSize).r
        : 1.0;
    vec3 col = albedo * (kAmbient * ssao
             + kKeyColor  * kKey  * keyDiff
             + kFillColor * kFill * fillDiff
             + kRimColor  * kRim  * rimDiff);
    col     += kKeyColor * specTint * keySpec;

    // Fresnel environment reflection (material path).
    if (uHasMaterial != 0 && uEnvIntensity > 0.0)
    {
        float fresnel = pow(1.0 - max(dot(n, v), 0.0), 4.0);
        col += specTint * uEnvIntensity * fresnel;
    }

    // Glow is additive self-illumination (linear space), approximating the
    // brightness that a bloom post-process would add around glowing faces.
    col += albedo * uGlow;

    // Convert back to sRGB for display.
    col = pow(col, vec3(1.0 / 2.2));

    // Emissive alpha mode (3): alpha drives additive emission rather than
    // transparency. The final fragment is fully opaque, mirroring the SL
    // viewer's DIFFUSE_ALPHA_MODE_EMISSIVE behaviour.
    if (uAlphaMode == 3)
    {
        col += base.rgb * base.a;
        fragColor = vec4(clamp(col, 0.0, 1.0), 1.0);
        return;
    }

    fragColor = vec4(col, (uAlphaMode == 1) ? base.a : 1.0);
}

