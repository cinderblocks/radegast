#version 300 es
precision highp float;

// Unit-square input position [-1,1]x[-1,1]; expanded in world XY by uHalfSize.
layout(location = 0) in vec2 aPos;

uniform mat4  uViewProj;       // view * proj (no separate model matrix)
uniform float uHalfSize;       // half-extent of water quad in world units
uniform vec2  uCenterXY;       // world XY centre of the quad (~= camera XY)
uniform float uWaterHeight;    // world Z of water surface
uniform float uTime;           // animation clock (seconds, monotonically increasing)
uniform vec3  uCameraPos;      // world-space eye position

out vec3 vWorldPos;
out vec2 vDudvUV;   // DUDV / first normalmap UV (larger scale, faster flow)
out vec2 vNormUV;   // second normalmap UV       (smaller scale, slower flow)
out vec4 vClipPos;
out vec3 vToCam;

void main()
{
    vec2  worldXY  = aPos * uHalfSize + uCenterXY;
    vec3  worldPos = vec3(worldXY, uWaterHeight);

    vWorldPos = worldPos;

    // UV tiling constants ported from Legacy RenderWater
    // waterUV=35 over a 256m tile -> 35/256 per world metre
    const float kWaterUV  = 35.0 / 256.0;
    const float kNormUV   = 8.75 / 256.0;   // 0.25x waterUV
    const float kFlow     = 0.0025;
    const float kNormFlow = 0.000625;        // 0.25x kFlow

    vDudvUV   = worldXY * kWaterUV  + vec2(0.0, -uTime * kFlow);
    vNormUV   = worldXY * kNormUV   + vec2(0.0,  uTime * kNormFlow);

    vToCam    = uCameraPos - worldPos;

    vClipPos    = uViewProj * vec4(worldPos, 1.0);
    gl_Position = vClipPos;
}
