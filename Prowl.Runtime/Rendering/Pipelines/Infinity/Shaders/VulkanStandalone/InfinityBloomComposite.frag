#version 450
layout(location = 0) in vec2 vUV;
layout(location = 0) out vec4 outColor;
layout(set = 0, binding = 0) uniform sampler2D sceneTexture;
layout(set = 0, binding = 1) uniform sampler2D bloomTexture;
layout(set = 0, binding = 2, std140) uniform BloomCompositeSettings
{
    float intensity;
    float dirtIntensity;
    float padding0;
    float padding1;
} settings;

void main()
{
    vec4 scene = texture(sceneTexture, vUV);
    vec3 bloom = texture(bloomTexture, vUV).rgb;
    outColor = vec4(scene.rgb + bloom * max(settings.intensity, 0.0), scene.a);
}
