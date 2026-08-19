#version 310 es

// Vulkan variant of ../prim.vert. Kept as a separate file rather than a shared source with
// #ifdef branches: the GL original stays untouched and in active use for the dual-backend
// period, and this file is the Vulkan-only port of it. Differences from the GL original,
// all driven by SPIR-V requirements found empirically via the dotnet-shaderc smoke test:
//   - #version 300 es -> 310 es (SPIR-V's minimum ES version)
//   - every in/out/attribute gets an explicit layout(location=N)
//   - ViewProj/View/ViewInv move to a per-frame UBO (set 0).
//
// Unlike the GL original, this file has NO non-instanced code path and NO push constants --
// every draw goes through the instanced attribute set (aInstMvp/aInstMv/aInstColor/
// aInstMisc/aInstAlphaMode), including single-object draws, which the CPU side issues as
// VkInstanceDrawer.DrawInstanced(..., instanceCount: 1). Reason: Vulkan pipeline
// vertex-input state is fixed per pipeline -- a runtime branch between "read push
// constants" and "read instance buffer" (what the GL original does) can't resolve to one
// pipeline, since every draw through a pipeline must have all its declared vertex-input
// bindings bound regardless of which branch would've executed. Collapsing to
// always-instanced keeps the pipeline variant count at 2 (Opaque/Alpha) instead of 4
// (Opaque/Alpha x Instanced/NonInstanced). Cost, accepted deliberately: this file's control
// flow no longer matches the GL original line-for-line (prim.frag is unaffected, it never
// read push constants). Future prim.vert bugs need this note in mind.

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aTexCoord;

// Per-instance attributes (unchanged from GL: still one vertex-input binding at
// VK_VERTEX_INPUT_RATE_INSTANCE, replacing VertexAttribDivisor).
layout(location = 3)  in mat4  aInstMvp;       // locations 3-6
layout(location = 7)  in mat4  aInstMv;        // locations 7-10
layout(location = 11) in vec4  aInstColor;
layout(location = 12) in vec4  aInstMisc;      // fullbright01, glow, shiny, alphaCutoff
layout(location = 13) in float aInstAlphaMode;
layout(location = 14) in vec4  aTangent;       // object-space tangent xyz + handedness w

// Set 0: per-frame data, bound once per pass, SHARED with prim.frag --
// both stages bind the same descriptor set, so this struct must match prim.frag's exactly
// even though this stage only reads the first three fields (uView/uProj/uViewInv). See
// prim.frag for what the rest are (lighting/atmosphere/point-light/shadow state).
layout(set = 0, binding = 0) uniform PerFrame
{
    mat4 uView;
    mat4 uProj;
    mat4 uViewInv;

    vec3  uSunDir;
    vec3  uSunColor;
    vec3  uAmbientColor;
    float uFogDensity;

    vec3  uBlueHorizon;
    vec3  uBlueDensity;
    float uHazeHorizon;
    float uHazeDensity;
    vec3  uSunlightColor;
    vec3  uAmbient;
    vec3  uSunDirection;
    float uSunGlowFocus;
    float uSunGlowSize;

    int   uPointLightCount;
    vec3  uPointLightPos[4];
    vec3  uPointLightColor[4];
    float uPointLightRadius[4];
    float uPointLightFalloff[4];

    int   uShadowsOn;
    mat4  uLightVp;
    int   uPointShadowCount;
    vec3  uPointShadowPos[2];
    float uPointShadowFar[2];

    int  uHasSsao;
    vec2 uScreenSize;
} frame;

layout(location = 0) flat out vec4 vInstColor;
layout(location = 1) flat out vec4 vInstMisc;
layout(location = 2) flat out int  vInstAlphaMode;
layout(location = 3) out vec3 vNormal;
layout(location = 4) out vec3 vViewPos;
layout(location = 5) out vec2 vTexCoord;
layout(location = 6) out vec4 vTangent;
layout(location = 7) out vec3 vObjPos;
layout(location = 8) out vec3 vObjNormal;
layout(location = 9) out vec3 vWorldPos;

void main()
{
    gl_Position = aInstMvp * vec4(aPosition, 1.0);
    vViewPos    = vec3(aInstMv * vec4(aPosition, 1.0));
    vNormal     = transpose(inverse(mat3(aInstMv))) * aNormal;
    vInstColor     = aInstColor;
    vInstMisc      = aInstMisc;
    vInstAlphaMode = int(round(aInstAlphaMode));
    vTangent = vec4(normalize(mat3(aInstMv) * aTangent.xyz), aTangent.w);

    vObjPos    = aPosition;
    vObjNormal = aNormal;
    vTexCoord  = aTexCoord;
    vWorldPos  = vec3(frame.uViewInv * vec4(vViewPos, 1.0));
}
