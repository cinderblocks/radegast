// Vulkan variant of ../shadow.glsl. Declarations use set/binding descriptors instead of loose
// uniforms; #define aliasing keeps function bodies below reading uShadowsOn/uLightVp/etc as bare
// names, resolving to descriptor members.
//
// NOTE: do not put a #version directive in this file -- it is textually inlined below the
// including shader's own #version line, same as the GL original.

precision mediump sampler2DShadow;
precision mediump samplerCubeShadow;

const int kMaxShadowedPointLights = 2;

// Bare `int`/`mat4`/`vec3`/`float` uniforms are illegal outside a block for SPIR-V, so these are
// folded into frame.PerFrame's shadow fields (set 0, binding 0, declared in the including shader,
// e.g. vulkan/prim.frag) rather than a separate block, since shadow state is per-frame data like
// the lighting fields already there. Samplers stay as individual set/binding declarations --
// opaque types can't live inside a uniform block at all, in GL or Vulkan.
#define uShadowsOn        frame.uShadowsOn
#define uLightVp          frame.uLightVp
#define uPointShadowCount frame.uPointShadowCount
#define uPointShadowPos   frame.uPointShadowPos
#define uPointShadowFar   frame.uPointShadowFar

layout(set = 1, binding = 1) uniform sampler2DShadow   uShadowMap;
layout(set = 1, binding = 2) uniform samplerCubeShadow uPointShadowMap0;
layout(set = 1, binding = 3) uniform samplerCubeShadow uPointShadowMap1;

// Fixed world-space normal-offset amounts (metres), deliberately NOT derived from the shadow
// radius/texel size: scaling the offset by texel world size makes it drift whenever the shadow
// radius changes, which can silently push the test point past the real occluder. A fixed, small,
// radius-independent offset avoids that class of bug.
const float kShadowOffsetBase   = 0.02; // near-perpendicular surfaces
const float kShadowOffsetGrazing = 0.06; // additional offset at grazing angles

// Directional shadow: 3x3 PCF tap (each tap is itself hardware-bilinear-filtered
// by the compare sampler, so this reads as a soft ~4x4-ish penumbra edge).
//
// Bias is applied as a normal offset (push the test point off the surface along its normal, in
// world space, before projecting into light space) rather than biasing the depth-compare value
// directly. A depth-space bias scaled by N.L flickers on terrain, whose normal is bump-perturbed
// per-texel rather than the smooth geometric normal the shadow map was actually rendered from.
// A world-space normal offset only needs the normal's general direction, so it tolerates that.
float sampleDirShadow(vec3 worldPos, vec3 normal, float NdotL)
{
    if (uShadowsOn == 0) return 1.0;

    float slopeScale = clamp(1.0 - NdotL, 0.0, 1.0);
    vec3 offsetPos = worldPos + normal * (kShadowOffsetBase + kShadowOffsetGrazing * slopeScale);

    vec4 clip = uLightVp * vec4(offsetPos, 1.0);
    vec3 proj = clip.xyz / clip.w;
    // X/Y get the ordinary clip[-1,1] -> UV[0,1] remap; Z does NOT. GL's rasterizer applies its
    // own glDepthRangef(0,1) hardware remap when writing the shadow depth texture, so GL's shader
    // matches that by also scaling Z here. Vulkan has no such remap -- the shadow depth pass
    // (shadow_depth.frag) writes native, unscaled clip.z/clip.w, so the compare value here must
    // match that directly. Kept as two statements (not one vector op) so this split can't be
    // accidentally collapsed back together.
    proj.xy = proj.xy * 0.5 + 0.5;

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

    // Fade toward unshadowed over the outer half of the shadow volume, not just the outer 15%:
    // the volume re-centers on the camera every frame, so its boundary is often within the
    // visible frame at ordinary draw distances. A narrow fade reads as a soft-edged patch of
    // darkening drifting as the camera moves; spreading the falloff over half the radius tapers
    // shadow strength gradually instead, closer to how cascaded shadow maps read.
    float edge = max(abs(proj.x - 0.5), abs(proj.y - 0.5)) * 2.0; // 0 at center -> 1 at edge
    float fade = smoothstep(0.5, 1.0, edge);
    return mix(shadow, 1.0, fade);
}

// Point-light cubemap shadow for one already-selected shadow-casting light. All 6 cube faces
// share one perspective projection (fixed 90 deg FOV, square aspect), so rather than picking one
// of 6 view-proj matrices in the fragment shader, reconstruct the same non-linear compare depth
// analytically from the dominant axis of the light->fragment vector -- for a symmetric
// perspective projection that axis is the eye-space Z of whichever face would have been selected.
// Standard scalar cubemap-shadow trick; avoids a geometry shader (unavailable in GLSL ES 3.00)
// and per-face matrices in the fragment shader. Must mirror the depth pass's own projection
// exactly (kPointShadowNear, point-shadow far == light radius).
float samplePointShadowCube(samplerCubeShadow map, vec3 lightPos, float farPlane, vec3 worldPos)
{
    vec3 toFrag = worldPos - lightPos;

    const float kPointShadowNear = 0.1;
    float zEye = max(max(abs(toFrag.x), abs(toFrag.y)), abs(toFrag.z));
    float ndcZ = farPlane / (farPlane - kPointShadowNear)
               - (farPlane * kPointShadowNear) / ((farPlane - kPointShadowNear) * zEye);
    // Point-light shadows are not yet enabled (uPointShadowCount stays 0, so this function is
    // unreachable dead code), and this ndcZ formula still carries the same depth-range issue
    // sampleDirShadow's own comment above documents, unfixed. Unlike the X/Y-vs-Z split there,
    // this formula needs re-deriving against the actual perspective-projection matrix entries
    // before applying a fix -- do that re-derivation, and apply it, when point-light shadows are
    // implemented.
    float compareZ = ndcZ * 0.5 + 0.5; // same GL-style remap sampleDirShadow used before its fix

    return texture(map, vec4(toFrag, compareZ));
}

// Dispatches to the shadow cubemap for point light `i` (0 or 1). GLSL ES 3.00 cannot index an
// array of samplers by a non-constant, hence the explicit branch. Deliberately independent of
// uShadowsOn (the directional map's readiness flag): uPointShadowCount already collapses to 0
// whenever shadows are off or this light isn't a shadow caster, so gating on both would wrongly
// suppress point shadows if the directional pass alone ever failed to be ready on a given frame.
float samplePointShadow(int i, vec3 worldPos)
{
    if (i >= uPointShadowCount) return 1.0;
    if (i == 0) return samplePointShadowCube(uPointShadowMap0, uPointShadowPos[0], uPointShadowFar[0], worldPos);
    if (i == 1) return samplePointShadowCube(uPointShadowMap1, uPointShadowPos[1], uPointShadowFar[1], worldPos);
    return 1.0;
}
