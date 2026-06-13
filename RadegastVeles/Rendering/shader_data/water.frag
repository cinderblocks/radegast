#version 300 es
precision highp float;

in vec3 vWorldPos;
in vec2 vDudvUV;
in vec2 vNormUV;
in vec4 vClipPos;
in vec3 vToCam;

uniform sampler2D uReflectionTex;  // unit 0 - reflected scene
uniform sampler2D uNormalMap;      // unit 1 - water surface normals
uniform sampler2D uDudvMap;        // unit 2 - distortion UV map
uniform vec4      uWaterColor;     // deep-water tint (rgba)
uniform vec3      uLightDir;       // world-space unit vector pointing toward sun
uniform int       uHasReflection;  // 1 = reflection FBO ready

out vec4 fragColor;

const float kDistortion = 0.012;
const float kShininess  = 96.0;

void main()
{
    // DUDV distortion: remap [0,1] -> [-1,1] then scale
    vec2 dudv    = texture(uDudvMap, vNormUV).rg * 2.0 - 1.0;
    vec2 distOff = dudv * kDistortion;

    // Surface normal from normal map (with DUDV offset)
    vec3 rawNorm = texture(uNormalMap, vDudvUV + distOff).rgb;
    vec3 mapNorm = normalize(rawNorm * 2.0 - 1.0);
    // Blend toward geometric up-normal (Z-up world) for subtle ripple
    mapNorm      = normalize(mix(vec3(0.0, 0.0, 1.0), mapNorm, 0.5));

    vec3 viewDir = normalize(vToCam);

    // Schlick Fresnel (F0 = 0.04 for water)
    float cosV   = max(dot(mapNorm, viewDir), 0.0);
    float fresnel = 0.04 + 0.96 * pow(1.0 - cosV, 5.0);

    // Reflection texture lookup: project clip coords to screen UV, flip Y for
    // the mirrored camera, then apply distortion
    vec4 reflColor = uWaterColor;
    if (uHasReflection != 0)
    {
        vec2 ndc    = vClipPos.xy / vClipPos.w;
        vec2 reflUV = ndc * 0.5 + 0.5;
        reflUV.y    = 1.0 - reflUV.y;   // Y-flip: reflection camera is Z-mirrored
        reflUV     += distOff * 0.4;
        reflUV      = clamp(reflUV, 0.001, 0.999);
        reflColor   = texture(uReflectionTex, reflUV);
    }

    // Blinn-Phong specular highlight (sun glint on water)
    vec3  L    = normalize(uLightDir);
    vec3  H    = normalize(L + viewDir);
    float spec = pow(max(dot(mapNorm, H), 0.0), kShininess) * 0.5;

    // Composite: deep water tint at normal incidence, reflection at grazing
    vec3 col   = mix(uWaterColor.rgb, reflColor.rgb, fresnel);
    col       += vec3(spec);

    float alpha = mix(uWaterColor.a * 0.75, 0.95, fresnel);
    fragColor   = vec4(col, alpha);
}
