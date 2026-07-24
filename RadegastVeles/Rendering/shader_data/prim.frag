#version 300 es
precision mediump float;

in  vec3 vNormal;
in  vec3 vViewPos;
in  vec2 vTexCoord;
in  vec3 vObjPos;     // object-space position — terrain triplanar path only
in  vec3 vObjNormal;  // object-space normal   — terrain triplanar path only
in  vec3 vWorldPos;   // world-space position  — shadow-map projection only

// Per-face / per-instance data forwarded from prim.vert.
// For non-instanced draws these mirror the per-face uniforms; for instanced draws
// they carry per-instance values without any extra fragment-shader branching.
flat in vec4 vInstColor;
flat in vec4 vInstMisc;   // x=fullbright01, y=glow, z=shiny, w=alphaCutoff
flat in int  vInstAlphaMode;

uniform sampler2D uAlbedo;
uniform int       uHasTexture;
// 1 if the TE face has a legacy bump code (Bumpiness != None)
uniform int       uHasBump;
// 1 for the single terrain face (SceneTerrainBuilder). Repurposes uAlbedo/uNormalMap/
// uSpecularMap/uMetallicRoughnessMap/uEmissiveMap to carry four raw detail textures +
// a baked layer-select map instead of their usual meaning — see terrainAlbedo() below.
uniform int       uIsTerrain;

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

// Sun light (view-space direction and colour driven by EEP/Windlight environment)
uniform vec3 uSunDir;       // view-space unit vector toward sun
uniform vec3 uSunColor;     // sun/moon colour (EEP sun_moon_color)
uniform vec3 uAmbientColor; // sky ambient colour (EEP sky_ambient)
const float kKey       = 0.70;

// Shared atmosphere model — only atmHazeColor() is used here, as the distance-
// haze target colour. Sharing the formula with sky.frag/water.frag keeps hazed
// geometry converging on the exact colour the sky shows at the horizon.
#include "atmosphere.glsl"

// Per-metre atmospheric haze density. 0 disables (studio viewers, toggle off).
uniform float uFogDensity;

// Shared shadow-mapping helpers (sampleDirShadow/samplePointShadow) — see
// shadow.glsl. Declares its own uShadowsOn/uLightVp/uShadowMap/uPointShadow*
// uniforms.
#include "shadow.glsl"

// Local point lights (SL "Light" prims) — nearest few to the camera, collected
// by SceneLightStreamer and selected per-frame in GlViewportControl. Colour is
// already premultiplied by Intensity (see LocalLight.Color); Radius/Falloff
// drive the smooth-cutoff attenuation curve below, mirroring the LightData
// params an SL builder actually sets in the edit UI.
const int kMaxLocalLights = 4;
uniform int   uPointLightCount;
uniform vec3  uPointLightPos[kMaxLocalLights];
uniform vec3  uPointLightColor[kMaxLocalLights];
uniform float uPointLightRadius[kMaxLocalLights];
uniform float uPointLightFalloff[kMaxLocalLights];

// Distance haze (aerial perspective). Applied to every lit and fullbright path —
// atmosphere doesn't care what it's in front of. Operates on display-space
// colour (post-tonemap/gamma), matching how the WL colour params are treated
// throughout this renderer.
vec3 applyFog(vec3 col)
{
    if (uFogDensity <= 0.0) return col;
    float f = 1.0 - exp(-length(vViewPos) * uFogDensity);
    return mix(col, atmHazeColor(), f);
}

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

in vec4 vTangent;  // view-space tangent xyz + handedness w (0 = no tangent data)

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

// ── Terrain triplanar path ────────────────────────────────────────────────────
// Fixes severe texture stretching on steep terrain: SculptMesh's UVs (vTexCoord) are a
// simple top-down (x,y)-grid projection with no dependence on height, so a near-vertical
// slope has many vertices sharing nearly the same UV — the old single pre-baked splat
// texture got smeared across the whole vertical extent. Sampling from world/object-space
// position along three axes and blending by how much the surface normal faces each axis
// sidesteps that entirely: steep faces draw mostly from the side projections, which don't
// stretch, while flat ground keeps using (effectively) the old top-down look.

vec3 triplanarSample(sampler2D tex, vec3 p, vec3 blend)
{
    vec3 cx = texture(tex, p.yz).rgb;
    vec3 cy = texture(tex, p.xz).rgb;
    vec3 cz = texture(tex, p.xy).rgb;
    return cx * blend.x + cy * blend.y + cz * blend.z;
}

vec3 terrainAlbedo()
{
    // Layer-select map (baked by TerrainSplat.BuildLayersAsync), sampled at the mesh's
    // existing UV — bilinear-filtered by the GPU, giving the same cross-cell smoothing
    // the old CPU composite achieved by hand-blending neighbouring heightmap cells.
    float layer = texture(uEmissiveMap, vTexCoord).r * 3.0;

    vec3 n = normalize(vObjNormal);
    vec3 blend = pow(abs(n), vec3(4.0));
    blend /= (blend.x + blend.y + blend.z + 1e-5);

    // ~32 m per detail-tile repeat, matching the tiling density the old CPU splat used
    // (256-texel detail tiles repeating 8× across the 256 m region).
    const float kTileScale = 1.0 / 32.0;
    vec3 p = vObjPos * kTileScale;

    // Anti-tiling: a very-low-frequency (~260 m repeat) pass over the first detail
    // texture drives a subtle domain warp of the sample position plus a macro
    // brightness modulation below. Either alone still shows the 32 m repeat grid
    // at middle distance; together the repetition stops being findable. Warp
    // amplitude is deliberately small — at 0.35 it produced obvious marbled
    // swirls across the whole ground.
    vec3 vary = triplanarSample(uAlbedo, p * 0.123, blend);
    p += (vary - 0.5) * 0.10;

    vec3 c0 = triplanarSample(uAlbedo,              p, blend);
    vec3 c1 = triplanarSample(uNormalMap,            p, blend);
    vec3 c2 = triplanarSample(uSpecularMap,          p, blend);
    vec3 c3 = triplanarSample(uMetallicRoughnessMap, p, blend);

    // Smooth "tent" weight per layer index — always all four samples (branch-free, GPU-
    // friendly, cheap next to a single terrain draw call), naturally reducing to a
    // two-layer blend since at most two tents overlap at any given layer value.
    float w0 = clamp(1.0 - abs(layer - 0.0), 0.0, 1.0);
    float w1 = clamp(1.0 - abs(layer - 1.0), 0.0, 1.0);
    float w2 = clamp(1.0 - abs(layer - 2.0), 0.0, 1.0);
    float w3 = clamp(1.0 - abs(layer - 3.0), 0.0, 1.0);
    float wSum = w0 + w1 + w2 + w3;
    vec3 albedo = (c0 * w0 + c1 * w1 + c2 * w2 + c3 * w3) / max(wSum, 0.0001);

    // Macro variation: large-scale brightness undulation (second half of the
    // anti-tiling scheme above) — reads as natural ground patchiness.
    return albedo * (0.90 + 0.20 * vary.r);
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

// Local point lights, Cook-Torrance (mirrors the sun's key-light block in
// pbrLighting below, just summed over up to kMaxLocalLights local sources).
vec3 pointLightsPBR(vec3 albedo, float metallic, float roughness, vec3 F0,
                     vec3 n, vec3 v, float NdotV)
{
    vec3 Lo = vec3(0.0);
    for (int i = 0; i < kMaxLocalLights; i++)
    {
        if (i >= uPointLightCount) break;

        vec3  toLight = uPointLightPos[i] - vWorldPos;
        float dist    = length(toLight);
        vec3  L       = toLight / max(dist, 0.001);

        // Smooth-cutoff falloff driven by the light's own Radius/Falloff (from
        // Primitive.LightData): 1 at the source, 0 at Radius, curve shaped by
        // Falloff. Squaring softens the cutoff so it doesn't read as a hard edge.
        float atten = clamp(1.0 - pow(dist / max(uPointLightRadius[i], 0.001),
                                       max(uPointLightFalloff[i], 0.001)), 0.0, 1.0);
        atten *= atten;
        if (atten <= 0.0) continue;

        float shadow = samplePointShadow(i, vWorldPos);
        vec3  radiance = uPointLightColor[i] * atten * shadow;

        vec3  H     = normalize(L + v);
        float NdotL = max(dot(n, L), 0.0);
        float NdotH = max(dot(n, H), 0.0);
        float HdotV = max(dot(H, v), 0.0);

        float D = distributionGGX(NdotH, roughness);
        float G = geometrySmith(NdotV, NdotL, roughness);
        vec3  F = fresnelSchlick(HdotV, F0);

        vec3 specular = (D * G * F) / (4.0 * NdotV * NdotL + 0.001);
        vec3 kD = (vec3(1.0) - F) * (1.0 - metallic);

        Lo += (kD * albedo / PI + specular) * radiance * NdotL;
    }
    return Lo;
}

// ── PBR lighting path ────────────────────────────────────────────────────────

void pbrLighting(vec3 albedo, float metallic, float roughness, float occlusion,
                 vec3 emissive, vec3 n, vec3 v, float alpha, out vec4 result)
{
    vec3 F0 = mix(vec3(0.04), albedo, metallic);
    roughness = max(roughness, 0.04); // avoid div-by-zero

    float NdotV = max(dot(n, v), 0.001);

    vec3 Lo = vec3(0.0);
    float sunShadow = 1.0;

    // Key light (sun)
    {
        vec3  L     = uSunDir;
        vec3  H     = normalize(L + v);
        float NdotL = max(dot(n, L), 0.0);
        float NdotH = max(dot(n, H), 0.0);
        float HdotV = max(dot(H, v), 0.0);

        float D = distributionGGX(NdotH, roughness);
        float G = geometrySmith(NdotV, NdotL, roughness);
        vec3  F = fresnelSchlick(HdotV, F0);

        vec3 specular = (D * G * F) / (4.0 * NdotV * NdotL + 0.001);
        vec3 kD = (vec3(1.0) - F) * (1.0 - metallic);

        // Shadow attenuates the direct/key term fully, and (below, applied to
        // `ambient`) dims indirect light partially — see the matching comment on
        // ambientShadow in the legacy Blinn-Phong path for why pure sun-only
        // attenuation isn't enough to read as a visible shadow in every environment.
        sunShadow = sampleDirShadow(vWorldPos, n, NdotL);

        Lo += (kD * albedo / PI + specular) * uSunColor * kKey * NdotL * sunShadow;
    }

    // Local point lights (SL "Light" prims).
    Lo += pointLightsPBR(albedo, metallic, roughness, F0, n, v, NdotV);

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
    float ambientShadow = mix(0.35, 1.0, sunShadow);
    vec3 ambient     = uAmbientColor * (kD_amb * albedo + F_amb * specScale) * occlusion * ssao * ambientShadow;

    vec3 col = ambient + Lo + emissive;

    // ACES filmic tonemap: compresses HDR highlights to [0,1] before gamma,
    // preventing hard clipping on smooth metallic specular spikes.
    col = acesFilmic(col);
    col = pow(col, vec3(1.0 / 2.2));

    result = vec4(col, alpha);
}

// Local point lights, legacy Lambert + Blinn-Phong (mirrors the sun key-light
// term in main() below). Returns a fully-weighted colour (albedo/specTint
// already applied) ready to add directly into `col`.
vec3 pointLightsLegacy(vec3 albedo, vec3 n, vec3 v, vec3 specTint, float shininess)
{
    vec3 result = vec3(0.0);
    for (int i = 0; i < kMaxLocalLights; i++)
    {
        if (i >= uPointLightCount) break;

        vec3  toLight = uPointLightPos[i] - vWorldPos;
        float dist    = length(toLight);
        vec3  L       = toLight / max(dist, 0.001);

        float atten = clamp(1.0 - pow(dist / max(uPointLightRadius[i], 0.001),
                                       max(uPointLightFalloff[i], 0.001)), 0.0, 1.0);
        atten *= atten;
        if (atten <= 0.0) continue;

        float shadow  = samplePointShadow(i, vWorldPos);
        vec3  contrib = uPointLightColor[i] * atten * shadow;

        float diff = max(dot(n, L), 0.0);
        vec3  h    = normalize(L + v);
        float spec = pow(max(dot(n, h), 0.0), shininess);

        result += albedo * contrib * diff + specTint * contrib * spec;
    }
    return result;
}

void main()
{
    // Unpack per-face / per-instance data from varyings written by prim.vert.
    vec4  faceColor    = vInstColor;
    bool  fullbright   = vInstMisc.x > 0.5;
    float glow         = vInstMisc.y;
    float shiny        = vInstMisc.z;
    float alphaCutoff  = vInstMisc.w;
    int   alphaMode    = vInstAlphaMode;

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
        if (alphaMode != 0)
        {
            float cutoff = (alphaMode == 2) ? alphaCutoff : 0.004;
            if (base.a < cutoff) discard;
        }

        if (fullbright)
        {
            vec3 glowCol = clamp(base.rgb * (1.0 + glow), 0.0, 1.0);
            fragColor = vec4(applyFog(glowCol), (alphaMode == 1) ? base.a : 1.0);
            return;
        }

        vec3 albedo = pow(base.rgb, vec3(2.2));

        vec3 n = normalize(gl_FrontFacing ? vNormal : -vNormal);
        vec3 v = normalize(-vViewPos);

        // Per-vertex tangent basis. Fall back to screen-space derivatives only when no
        // tangent data was supplied (w == 0, e.g. placeholder meshes).
        vec3 T, B;
        bool hasTBN = abs(vTangent.w) > 0.1;
        if (hasTBN)
        {
            T = normalize(vTangent.xyz);
            B = vTangent.w * cross(n, T);
        }
        else
        {
            vec3 posDx = dFdx(vViewPos);
            vec3 posDy = dFdy(vViewPos);
            vec2 texDx = dFdx(vTexCoord);
            vec2 texDy = dFdy(vTexCoord);
            float det = texDx.x * texDy.y - texDy.x * texDx.y;
            if (abs(det) > 1e-5) {
                T = normalize( texDy.y * posDx - texDx.y * posDy);
                B = normalize(-texDy.x * posDx + texDx.x * posDy);
            } else {
                T = vec3(1.0, 0.0, 0.0);
                B = vec3(0.0, 1.0, 0.0);
            }
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
        emissive += albedo * glow;

        vec4 result;
        pbrLighting(albedo, metallic, roughness, occlusion, emissive, n, v,
                    (alphaMode == 1) ? base.a : 1.0, result);
        fragColor = vec4(applyFog(result.rgb), result.a);
        return;
    }

    // ── Legacy Blinn-Phong path ──────────────────────────────────────────
    vec4 base = (uIsTerrain != 0)
        ? vec4(terrainAlbedo(), 1.0)
        : (uHasTexture != 0)
            ? texture(uAlbedo, vTexCoord) * faceColor
            : faceColor;

    // AlphaMode 0 (None/opaque): never discard — baked textures store compositing
    // data in alpha, not GL transparency.  Mode 1 (blend) and 2 (mask) use the
    // texture alpha; for everything else a near-zero sentinel drops degenerate
    // fully-transparent fragments from bad content.
    if (alphaMode != 0)
    {
        float cutoff = (alphaMode == 2) ? alphaCutoff : 0.004;
        if (base.a < cutoff) discard;
    }

    if (fullbright)
    {
        // Glow adds extra brightness on top of the fully-lit base colour,
        // approximating the emission the face would contribute to a bloom pass.
        vec3 glowCol = clamp(base.rgb * (1.0 + glow), 0.0, 1.0);
        fragColor = vec4(applyFog(glowCol), (alphaMode == 1) ? base.a : 1.0);
        return;
    }

    // Linearise sRGB input for physically-plausible lighting.
    vec3 albedo = pow(base.rgb, vec3(2.2));

    vec3 n = normalize(gl_FrontFacing ? vNormal : -vNormal);
    vec3 v = normalize(-vViewPos);

    // Per-vertex tangent basis. Fall back to screen-space derivatives for meshes
    // without tangent data (w == 0, e.g. placeholder avatars).
    vec3 T, B;
    bool hasTBN = abs(vTangent.w) > 0.1;
    if (hasTBN)
    {
        T = normalize(vTangent.xyz);
        B = vTangent.w * cross(n, T);
    }
    else
    {
        vec3 posDx = dFdx(vViewPos);
        vec3 posDy = dFdy(vViewPos);
        vec2 texDx = dFdx(vTexCoord);
        vec2 texDy = dFdy(vTexCoord);
        float det = texDx.x * texDy.y - texDy.x * texDx.y;
        if (abs(det) > 1e-5) {
            T = normalize( texDy.y * posDx - texDx.y * posDy);
            B = normalize(-texDy.x * posDx + texDx.x * posDy);
        } else {
            T = vec3(1.0, 0.0, 0.0);
            B = vec3(0.0, 1.0, 0.0);
        }
    }

    // Material normal map: perturb normal using tangent-space sample.
    // Terrain repurposes uNormalMap/uHasNormalMap to carry a raw detail texture (not a
    // real normal map) — excluded here so it isn't misread as tangent-space normal data.
    if (uHasNormalMap != 0 && hasTBN && uIsTerrain == 0)
    {
        vec2 nmUv = transformUv(vTexCoord, uNormalUvST, uNormalUvRot);
        vec3 nmSample = texture(uNormalMap, nmUv).rgb * 2.0 - 1.0;
        n = normalize(T * nmSample.x + B * nmSample.y + n * nmSample.z);
    }
    // Legacy bump: perturb the surface normal using luminance gradient. Terrain
    // (which carries no tangent data — T/B come from the derivative fallback)
    // opts in too: the splat detail's luminance doubles as a cheap height field,
    // giving ground relief that catches the sun instead of shading dead-flat.
    else if ((uHasBump != 0 && hasTBN) || uIsTerrain != 0)
    {
        float lum  = dot(albedo, vec3(0.299, 0.587, 0.114));
        float amt  = uIsTerrain != 0 ? 0.7 : 0.5;
        n = normalize(n + (dFdx(lum) * T + dFdy(lum) * B) * amt);
    }

    // Resolve specular parameters: material path or legacy TE shiny.
    // Terrain sets HasMaterial=false (SceneTerrainBuilder) so this already reads false,
    // but the explicit uIsTerrain check guards uSpecularMap (terrain's detail2) from ever
    // being read as a real specular tint even if that ever changes.
    float specStrength;
    float shininess;
    vec3  specTint;
    if (uHasMaterial != 0 && uIsTerrain == 0)
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
        specStrength = mix(0.06, 1.4,   shiny);
        shininess    = mix(16.0, 128.0, shiny);
        specTint     = vec3(specStrength);
    }

    // Key light (sun): standard Lambert diffuse + Blinn-Phong specular.
    // SL C++ viewer uses max(dot(N,L),0) — standard Lambert — not half-Lambert.
    // Half-Lambert (range [0.5,1.0]) washes out all shadow definition; standard
    // Lambert (range [0,1]) gives proper light/shadow contrast matching SL.
    float keyDiff = max(dot(n, uSunDir), 0.0);
    vec3  keyH    = normalize(uSunDir + v);
    float keySpec = pow(max(dot(n, keyH), 0.0), shininess);

    // Shadow only attenuates the direct/key term — fill, rim and ambient stay lit.
    float sunShadow = sampleDirShadow(vWorldPos, n, keyDiff);

    // Fill light: clamped lambert, no specular
    float fillDiff = max(dot(n, kFillDir), 0.0);

    // Rim light: pure diffuse, lights silhouette edges facing away from camera.
    float rimDiff = max(dot(n, kRimDir), 0.0);

    float ssao = (uHasSsao != 0)
        ? texture(uSsaoMap, gl_FragCoord.xy / uScreenSize).r
        : 1.0;
    // Shadow also lightly dims ambient (never below 55%), on top of fully removing
    // the key/sun term above. A "pure" shadow map should only attenuate direct
    // light, but WL/EEP regions vary enormously in how much of the lighting budget
    // is ambient vs. direct sun (verified in-world: one environment had ambient
    // brighter than the sun itself, green channel exactly zero) — attenuating only
    // the sun term made shadows real but visually imperceptible whenever ambient
    // dominates. This keeps shadowed areas well short of pure black (physically
    // reasonable — real shadows still receive bounce/sky light) while making the
    // effect legible across environments instead of only in sun-dominant ones.
    float ambientShadow = mix(0.35, 1.0, sunShadow);
    vec3 col = albedo * (uAmbientColor * ssao * ambientShadow
             + uSunColor   * kKey  * keyDiff * sunShadow
             + kFillColor  * kFill * fillDiff
             + kRimColor   * kRim  * rimDiff);
    col     += uSunColor * specTint * keySpec * sunShadow;

    // Local point lights (SL "Light" prims).
    col += pointLightsLegacy(albedo, n, v, specTint, shininess);

    // Fresnel environment reflection (material path).
    if (uHasMaterial != 0 && uEnvIntensity > 0.0)
    {
        float fresnel = pow(1.0 - max(dot(n, v), 0.0), 4.0);
        col += specTint * uEnvIntensity * fresnel;
    }

    // Glow is additive self-illumination (linear space), approximating the
    // brightness that a bloom post-process would add around glowing faces.
    col += albedo * glow;

    // Convert back to sRGB for display.
    col = pow(col, vec3(1.0 / 2.2));

    // Emissive alpha mode (3): alpha drives additive emission rather than
    // transparency. The final fragment is fully opaque, mirroring the SL
    // viewer's DIFFUSE_ALPHA_MODE_EMISSIVE behaviour.
    if (alphaMode == 3)
    {
        col += base.rgb * base.a;
        fragColor = vec4(applyFog(clamp(col, 0.0, 1.0)), 1.0);
        return;
    }

    fragColor = vec4(applyFog(col), (alphaMode == 1) ? base.a : 1.0);
}

