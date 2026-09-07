#version 310 es
precision mediump float;

// Never actually visible -- VkOcclusionQueryPipeline's ColorWriteMask is 0 (see that class's own
// doc comment), so whatever this writes is masked off by the fixed-function stage regardless. A
// real fragment shader is still required by the pipeline (VkOutlinePipeline/VkWireframePipeline
// never omit one either); this exists only to satisfy that requirement as cheaply as possible.

layout(location = 0) out vec4 fragColor;

void main()
{
    fragColor = vec4(0.0);
}
