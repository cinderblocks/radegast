#version 120

uniform sampler2D colorMap;
uniform vec3 lightDir; // normalized light direction in eye space
uniform vec4 ambientColor;
uniform vec4 diffuseColor;
uniform vec4 specularColor;
uniform int hasTexture; // 0 = no texture, 1 = has texture

varying vec3 vNormal;
varying vec2 vTexCoord;
varying vec3 vPos;

vec3 safeNormalize(vec3 v, vec3 defaultDir)
{
    if (isnan(v.x) || isnan(v.y) || isnan(v.z)) return defaultDir;
    float len = length(v);
    if (len <= 1e-6) return defaultDir;
    return v / len;
}

void main()
{
    vec4 tex = hasTexture > 0 ? texture2D(colorMap, vTexCoord) : vec4(1.0);

    vec3 n = safeNormalize(vNormal, vec3(0.0, 0.0, 1.0));
    vec3 ld = safeNormalize(lightDir, vec3(0.0, 0.0, 1.0));

    // Very soft wrap-around lighting for flattering avatar shading
    float nDotL = dot(n, ld);
    float lambert = nDotL * 0.25 + 0.75; // Very soft: -1 maps to 0.5, +1 maps to 1.0
    
    // Strong ambient lighting for avatars
    vec4 ambientContribution = ambientColor * tex * 2.0;
    vec4 diffuseContribution = diffuseColor * tex * lambert;
    
    // Heavy ambient bias for soft, flattering lighting
    vec4 color = mix(ambientContribution, diffuseContribution, 0.5);
    
    // Very subtle specular for slight highlights
    vec3 viewDir = safeNormalize(-vPos, vec3(0.0, 0.0, 1.0));
    vec3 halfVec = safeNormalize(ld + viewDir, ld);
    float spec = pow(max(dot(n, halfVec), 0.0), 20.0);
    color += specularColor * spec * 0.15;
    
    // Guarantee minimum brightness
    vec3 minBright = ambientColor.rgb * tex.rgb * 0.3;
    color.rgb = max(color.rgb, minBright);

    if (isnan(color.r) || isnan(color.g) || isnan(color.b) || isnan(color.a))
    {
        color = vec4(0.5 * ambientColor.rgb, tex.a);
    }
    color = clamp(color, 0.0, 1.0);

    gl_FragColor = color;
}
