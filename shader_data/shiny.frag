#version 120

uniform sampler2D colorMap;
uniform vec3 lightDir; // normalized
uniform vec4 ambientColor;
uniform vec4 diffuseColor;
uniform vec4 specularColor;

varying vec3 vNormal;
varying vec2 vTexCoord;

void main()
{
    vec4 tex = texture2D(colorMap, vTexCoord);
    vec3 n = normalize(vNormal);
    float lambert = max(dot(n, normalize(lightDir)), 0.0);
    vec4 color = ambientColor * tex + diffuseColor * lambert * tex;
    // simple specular (using view direction approximated)
    vec3 viewDir = normalize(-vec3(gl_ModelViewMatrix * gl_Vertex));
    vec3 halfVec = normalize(normalize(lightDir) + viewDir);
    float spec = pow(max(dot(n, halfVec), 0.0), 16.0);
    color += specularColor * spec;
    gl_FragColor = color;
}
