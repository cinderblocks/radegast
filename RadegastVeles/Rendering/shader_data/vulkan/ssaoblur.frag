#version 310 es
precision mediump float;

// Vulkan variant of ../ssaoblur.frag. Byte-identical blur body; only the uniform declarations
// changed. uTexelSize is small enough (8 bytes) to be a push constant instead of a UBO --
// avoids allocating a whole descriptor set binding for two floats.

layout(location = 0) in vec2 vTexCoord;

layout(set = 0, binding = 0) uniform sampler2D uSsaoTex;

layout(push_constant) uniform PerDraw
{
    vec2 uTexelSize; // 1.0 / screenSize
} pc;
#define uTexelSize pc.uTexelSize

layout(location = 0) out vec4 fragColor;

// 4x4 box blur -- fast, removes high-frequency SSAO noise.
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
