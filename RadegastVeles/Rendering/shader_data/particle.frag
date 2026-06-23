#version 300 es
precision mediump float;

in  vec2 vTexCoord;
in  vec4 vColor;

uniform sampler2D uAlbedo;
uniform int       uHasTexture;
uniform float     uGlow;

out vec4 fragColor;

void main()
{
    vec4 texColor = (uHasTexture != 0) ? texture(uAlbedo, vTexCoord) : vec4(1.0);
    vec4 col = texColor * vColor;
    if (col.a < 0.004) discard;

    // Additive glow contribution: blend toward a brighter version by glow factor.
    vec3 glowCol = clamp(col.rgb * (1.0 + uGlow * 2.0), 0.0, 1.0);
    fragColor = vec4(mix(col.rgb, glowCol, uGlow), col.a);
}
