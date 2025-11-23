#version 120

uniform sampler2D colorMap;
uniform vec3 lightDir; // normalized
uniform vec4 ambientColor;
uniform vec4 diffuseColor;
uniform vec4 specularColor;

varying vec3 vNormal;
varying vec2 vTexCoord;
varying vec3 vPos;

void main()
{
    vec4 tex = texture2D(colorMap, vTexCoord);
    vec3 n = normalize(vNormal);
    vec3 ld = normalize(lightDir);
    float lambert = max(dot(n, ld), 0.0);
    vec4 color = ambientColor * tex + diffuseColor * lambert * tex;
    // simple specular
    vec3 viewDir = normalize(-vPos);
    vec3 halfVec = normalize(ld + viewDir);
    float spec = pow(max(dot(n, halfVec), 0.0), 16.0);
    color += specularColor * spec;
    gl_FragColor = color;
}
