#version 300 es

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aTexCoord;

// Per-instance attributes (VertexAttribDivisor=1) used when uInstanced is true.
// mat4 occupies 4 consecutive attribute locations (one per column).
layout(location = 3)  in mat4  aInstMvp;       // locations 3–6
layout(location = 7)  in mat4  aInstMv;        // locations 7–10
layout(location = 11) in vec4  aInstColor;     // location 11
layout(location = 12) in vec4  aInstMisc;      // location 12: fullbright01, glow, shiny, alphaCutoff
layout(location = 13) in float aInstAlphaMode; // location 13
layout(location = 14) in vec4  aTangent;       // object-space tangent xyz + handedness w

uniform mat4 uMvp;
uniform mat4 uModelView;
uniform mat3 uNormalMat;
uniform bool uInstanced;

// Per-face uniforms — read in the non-instanced path to populate varyings.
uniform vec4  uColor;
uniform int   uFullbright;
uniform float uGlow;
uniform float uShiny;
uniform float uAlphaCutoff;
uniform int   uAlphaMode;

// Per-face/per-instance data forwarded to the fragment shader as flat varyings
// so prim.frag works identically for both draw paths.
flat out vec4 vInstColor;
flat out vec4 vInstMisc;   // x=fullbright01, y=glow, z=shiny, w=alphaCutoff
flat out int  vInstAlphaMode;

out vec3 vNormal;
out vec3 vViewPos;
out vec2 vTexCoord;
out vec4 vTangent;  // view-space tangent xyz + handedness w

void main()
{
    if (uInstanced)
    {
        gl_Position = aInstMvp * vec4(aPosition, 1.0);
        vViewPos    = vec3(aInstMv * vec4(aPosition, 1.0));
        // Normal matrix = (MV^-1)^T; computed from the per-instance MV in the vertex shader.
        vNormal        = transpose(inverse(mat3(aInstMv))) * aNormal;
        vInstColor     = aInstColor;
        vInstMisc      = aInstMisc;
        vInstAlphaMode = int(round(aInstAlphaMode));
        vTangent = vec4(normalize(mat3(aInstMv) * aTangent.xyz), aTangent.w);
    }
    else
    {
        gl_Position = uMvp * vec4(aPosition, 1.0);
        vViewPos    = vec3(uModelView * vec4(aPosition, 1.0));
        vNormal     = uNormalMat * aNormal;
        // Mirror per-face uniforms into varyings so prim.frag reads a single source.
        vInstColor     = uColor;
        vInstMisc      = vec4(float(uFullbright), uGlow, uShiny, uAlphaCutoff);
        vInstAlphaMode = uAlphaMode;
        vTangent = vec4(normalize(mat3(uModelView) * aTangent.xyz), aTangent.w);
    }
    vTexCoord = aTexCoord;
}
