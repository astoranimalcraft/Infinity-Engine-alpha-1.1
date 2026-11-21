#version 450

layout(location = 0) in vec2 vUV;
layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 0) uniform sampler2D uScene;
layout(set = 0, binding = 1) uniform sampler2D uBloom;
layout(set = 0, binding = 2, std140) uniform BloomCompositeSettings
{
    float intensity;
    float padding0;
    float padding1;
    float padding2;
} settings;

void main()
{
    vec4 sceneColor = texture(uScene, vUV);
    vec3 bloomColor = texture(uBloom, vUV).rgb;
    outColor = vec4(sceneColor.rgb + bloomColor * max(settings.intensity, 0.0), sceneColor.a);
}
