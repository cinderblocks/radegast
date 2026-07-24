// ── Shared shadow-mapping helpers ────────────────────────────────────────────
// Included (via ShaderLoader's #include preprocessing) by prim.frag. Two
// independent shadow sources, both sampled with hardware depth-compare
// (GL_COMPARE_REF_TO_TEXTURE, i.e. sampler2DShadow/samplerCubeShadow give a
// filtered 0..1 "lit fraction" directly from texture(), no manual comparison):
//   - one directional (sun/moon) shadow map, 3x3 PCF-filtered.
//   - up to kMaxShadowedPointLights local-light shadow cubemaps.
//
// NOTE: do not put a #version directive in this file — it is textually inlined
// below prim.frag's own #version line.

// GLSL ES 3.00 gives sampler2D/samplerCube a default (lowp) precision in
// fragment shaders, but NOT the shadow-sampler types — prim.frag's blanket
// `precision mediump float;` doesn't cover them either. Without an explicit
// declaration here, strict compilers (ANGLE included) reject the uniform
// declarations below with a "No precision specified" error.
precision mediump sampler2DShadow;
precision mediump samplerCubeShadow;

uniform int              uShadowsOn;
uniform mat4              uLightVp;      // world -> directional-light clip space
uniform sampler2DShadow   uShadowMap;

const int kMaxShadowedPointLights = 2;
uniform int               uPointShadowCount;
uniform vec3              uPointShadowPos[kMaxShadowedPointLights];
uniform float              uPointShadowFar[kMaxShadowedPointLights]; // far plane == that light's Radius
uniform samplerCubeShadow uPointShadowMap0;
uniform samplerCubeShadow uPointShadowMap1;

// Fixed world-space normal-offset amounts (metres) — deliberately NOT derived from
// GlViewportControl's ShadowRadius/texel size: they were originally scaled by texel
// world size (radius/resolution), so doubling ShadowRadius from 48m to 96m (to fix
// clouds bleeding into the ground — see GlViewportControl's ShadowRadius comment)
// silently doubled the offset too, from ~0.07-0.21m to ~0.14-0.42m — enough to push
// the test point past the real occluder for anything but large casters, which is
// why shadows stopped being visible at all after that fix. A fixed, small,
// radius-independent offset avoids this class of bug recurring if the radius
// changes again.
const float kShadowOffsetBase   = 0.02; // near-perpendicular surfaces
const float kShadowOffsetGrazing = 0.06; // additional offset at grazing angles

// Directional shadow: 3x3 PCF tap (each tap is itself hardware-bilinear-filtered
// by the compare sampler, so this reads as a soft ~4x4-ish penumbra edge).
//
// Bias is applied as a NORMAL OFFSET (push the test point off the surface along
// its normal, in world space, before projecting into light space) rather than
// biasing the depth-compare value directly. A depth-space bias scaled by N.L
// (the more common textbook approach — this file's original implementation)
// fluctuates with whatever per-fragment normal computes N.L; terrain's normal is
// bump-perturbed per-texel (a texture-driven wobble, not the smooth geometric
// normal the shadow map itself was actually rendered from), which made that
// depth-space bias flicker pixel-to-pixel and read as a shimmering moire pattern
// across the ground. A world-space normal offset is far more tolerant of that —
// it only needs the normal's general direction, not a precise per-fragment value.
float sampleDirShadow(vec3 worldPos, vec3 normal, float NdotL)
{
    if (uShadowsOn == 0) return 1.0;

    float slopeScale = clamp(1.0 - NdotL, 0.0, 1.0);
    vec3 offsetPos = worldPos + normal * (kShadowOffsetBase + kShadowOffsetGrazing * slopeScale);

    vec4 clip = uLightVp * vec4(offsetPos, 1.0);
    vec3 proj = clip.xyz / clip.w;
    proj = proj * 0.5 + 0.5; // clip [-1,1] -> texture/depth [0,1] (matches the GPU's own
                              // fixed glDepthRangef(0,1) remap of gl_Position during the
                              // depth pass, so this always matches what got written there)

    // Outside the map (or past the far plane) -> fully lit. Avoids both a hard
    // clamp artefact at the map's edge and needing a border colour (GL ES 3.0
    // core has no CLAMP_TO_BORDER).
    if (proj.x < 0.0 || proj.x > 1.0 || proj.y < 0.0 || proj.y > 1.0 || proj.z > 1.0)
        return 1.0;

    // Small constant depth bias as a secondary safety net against float precision
    // (the normal offset above does the real acne-prevention work).
    float compareZ = proj.z - 0.0005;

    vec2 texel = 1.0 / vec2(textureSize(uShadowMap, 0));
    float sum = 0.0;
    for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++)
            sum += texture(uShadowMap, vec3(proj.xy + vec2(float(x), float(y)) * texel, compareZ));
    float shadow = sum / 9.0;

    // Fade toward unshadowed over the outer half of the shadow volume, not just the
    // outer 15%: the volume re-centres on the camera every frame, so its boundary is
    // very often within the visible frame at ordinary draw distances. A narrow fade
    // reads as a distinct, soft-edged circular patch of darkening drifting around as
    // the camera moves ("clouds bleeding across the ground"); spreading the falloff
    // over half the volume's radius makes shadow strength taper away gradually with
    // distance instead, closer to how real cascaded shadow maps read.
    float edge = max(abs(proj.x - 0.5), abs(proj.y - 0.5)) * 2.0; // 0 at center -> 1 at edge
    float fade = smoothstep(0.5, 1.0, edge);
    return mix(shadow, 1.0, fade);
}

// Point-light cubemap shadow for one already-selected shadow-casting light.
// All 6 cube faces share one perspective projection (fixed 90 deg FOV, square
// aspect), so rather than picking one of 6 view-proj matrices in the fragment
// shader we reconstruct the same non-linear compare depth analytically from the
// dominant axis of the light->fragment vector — for a symmetric perspective
// projection that axis IS the eye-space Z of whichever face would have been
// selected to render this direction. Standard scalar cubemap-shadow trick;
// avoids a geometry shader (unavailable in GLSL ES 3.00) and per-face matrices
// in the fragment shader. Must mirror the depth pass's own projection exactly
// (see kPointShadowNear / GlViewportControl's point-shadow far == light radius).
float samplePointShadowCube(samplerCubeShadow map, vec3 lightPos, float farPlane, vec3 worldPos)
{
    vec3 toFrag = worldPos - lightPos;

    const float kPointShadowNear = 0.1;
    float zEye = max(max(abs(toFrag.x), abs(toFrag.y)), abs(toFrag.z));
    float ndcZ = farPlane / (farPlane - kPointShadowNear)
               - (farPlane * kPointShadowNear) / ((farPlane - kPointShadowNear) * zEye);
    float compareZ = ndcZ * 0.5 + 0.5; // same glDepthRangef(0,1) remap as sampleDirShadow

    return texture(map, vec4(toFrag, compareZ));
}

// Dispatches to the shadow cubemap for point light `i` (0 or 1). GLSL ES 3.00
// cannot index an array of samplers by a non-constant, hence the explicit branch.
// Deliberately independent of uShadowsOn (the directional map's readiness flag):
// uPointShadowCount already collapses to 0 whenever shadows are off or this
// particular light isn't a shadow caster (see GlViewportControl.DrawFaces), so
// gating on both would wrongly suppress point shadows if the directional pass
// alone ever failed to be ready on a given frame.
float samplePointShadow(int i, vec3 worldPos)
{
    if (i >= uPointShadowCount) return 1.0;
    if (i == 0) return samplePointShadowCube(uPointShadowMap0, uPointShadowPos[0], uPointShadowFar[0], worldPos);
    if (i == 1) return samplePointShadowCube(uPointShadowMap1, uPointShadowPos[1], uPointShadowFar[1], worldPos);
    return 1.0;
}
