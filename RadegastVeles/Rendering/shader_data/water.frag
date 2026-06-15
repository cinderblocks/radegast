#version 300 es
precision highp float;

in vec2 vNdc;

uniform mat4      uViewProj;       // view * proj — unused after refactor (kept for compat)
uniform mat4      uReflViewProj;   // reflected-camera view * proj — for reflection UV lookup
uniform mat4      uInvViewProj;    // inverse(view * proj) — ray reconstruction
uniform vec3      uEyePos;         // camera world position (Z-up)
uniform float     uWaterHeight;    // world Z of the water plane
uniform float     uTime;           // animation clock (seconds, monotonically increasing)

uniform sampler2D uReflectionTex;  // unit 0 - reflected scene FBO
uniform sampler2D uNormalMap;      // unit 1 - water surface normals
uniform sampler2D uDudvMap;        // unit 2 - distortion UV map
uniform vec4      uWaterColor;     // deep-water tint (rgba)
uniform vec3      uLightDir;       // world-space unit vector toward sun
uniform int       uHasReflection;  // 1 = reflection FBO available

out vec4 fragColor;

const float kDistortion   = 0.012;
const float kShininess    = 96.0;
const float kMaxReflDist  = 400.0; // world-units beyond which reflection FBO is not sampled

// UV constants identical to the old water.vert
const float kWaterUV  = 35.0 / 256.0;
const float kNormUV   = 8.75 / 256.0;
const float kFlow     = 0.0025;
const float kNormFlow = 0.000625;

// Reconstruct world-space view direction from NDC using the inverse VP matrix.
vec3 viewRay(vec2 ndc)
{
    vec4 near = uInvViewProj * vec4(ndc, -1.0, 1.0);
    vec4 far  = uInvViewProj * vec4(ndc,  1.0, 1.0);
    near /= near.w;
    far  /= far.w;
    return normalize(far.xyz - near.xyz);
}

void main()
{
    vec3 ray = viewRay(vNdc);

    // Ray must be going downward (Z-up world) to hit the water plane.
    // Use a small epsilon so the horizon edge itself still renders.
    if (ray.z >= -0.0001) discard;

    // Ray-plane intersection: eye + t*ray at z = uWaterHeight
    float t = (uWaterHeight - uEyePos.z) / ray.z;
    if (t <= 0.0) discard;   // intersection behind camera

    vec3 hitPos  = uEyePos + ray * t;
    vec2 worldXY = hitPos.xy;

    // ── Water UV (world-anchored so the pattern is seamless as camera moves) ──
    vec2 dudvUV = worldXY * kWaterUV + vec2(0.0, -uTime * kFlow);
    vec2 normUV = worldXY * kNormUV  + vec2(0.0,  uTime * kNormFlow);

    // ── DUDV distortion → surface normal ─────────────────────────────────────
    vec2 dudv    = texture(uDudvMap, normUV).rg * 2.0 - 1.0;
    vec2 distOff = dudv * kDistortion;

    vec3 rawNorm = texture(uNormalMap, dudvUV + distOff).rgb;
    vec3 mapNorm = normalize(rawNorm * 2.0 - 1.0);
    // Blend toward geometric up-normal for subtle ripple at distance
    mapNorm      = normalize(mix(vec3(0.0, 0.0, 1.0), mapNorm, 0.5));

    // View direction: from surface toward camera
    vec3 viewDir = -ray;

    // ── Schlick Fresnel (F0 = 0.04 for water) ────────────────────────────────
    float cosV    = max(dot(mapNorm, viewDir), 0.0);
    float fresnel = 0.04 + 0.96 * pow(1.0 - cosV, 5.0);

    // ── Reflection ────────────────────────────────────────────────────────────
    // Project the water surface hit point through the reflected camera's VP to get
    // the correct texture coordinates into the reflection FBO.
    // Beyond kMaxReflDist the hit is so far from the camera that the 512×512 FBO
    // cannot resolve it correctly (the reflected VP maps the point outside the FBO,
    // and the clamp produces arbitrary "terrain" samples).  Use water color instead.
    vec4 reflColor = uWaterColor;
    if (uHasReflection != 0 && t < kMaxReflDist)
    {
        vec4 reflClip = uReflViewProj * vec4(hitPos, 1.0);
        vec2 reflUV   = reflClip.xy / reflClip.w * 0.5 + 0.5;
        reflUV       += distOff * 0.4;
        reflUV        = clamp(reflUV, 0.001, 0.999);
        reflColor     = texture(uReflectionTex, reflUV);
    }

    // ── Blinn-Phong specular (sun glint) ─────────────────────────────────────
    vec3  L    = normalize(uLightDir);
    vec3  H    = normalize(L + viewDir);
    float spec = pow(max(dot(mapNorm, H), 0.0), kShininess) * 0.5;

    // ── Composite ─────────────────────────────────────────────────────────────
    vec3 col  = mix(uWaterColor.rgb, reflColor.rgb, fresnel);
    col      += vec3(spec);
    float alpha = mix(uWaterColor.a * 0.75, 0.95, fresnel);

    fragColor = vec4(col, alpha);

    // Write the depth of the actual water surface so the water:
    //   • renders over underwater terrain (terrain depth > water depth → Less passes)
    //   • is culled by above-water terrain (terrain depth < water depth → Less fails)
    //   • appears at the horizon where no geometry fills the pixel (depth buffer = 1.0)
    // Disabling early-z is acceptable for a single full-screen pass per frame.
    vec4 clipHit  = uViewProj * vec4(hitPos, 1.0);
    gl_FragDepth  = clamp(clipHit.z / clipHit.w * 0.5 + 0.5, 0.0, 1.0);
}
