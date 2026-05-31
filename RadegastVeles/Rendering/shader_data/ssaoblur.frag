#version 300 es
precision mediump float;

in vec2 vTexCoord;

uniform sampler2D uSsaoTex;
uniform vec2      uTexelSize;   // 1.0 / screenSize

out vec4 fragColor;

// 4x4 box blur — fast, removes high-frequency SSAO noise.
void main()
{
    float result = 0.0;
    for (int x = -2; x <= 1; x++)
    {
        for (int y = -2; y <= 1; y++)
        {
            vec2 offset = vec2(float(x), float(y)) * uTexelSize;
            result += texture(uSsaoTex, vTexCoord + offset).r;
        }
    }
    result /= 16.0;
    fragColor = vec4(vec3(result), 1.0);
}
