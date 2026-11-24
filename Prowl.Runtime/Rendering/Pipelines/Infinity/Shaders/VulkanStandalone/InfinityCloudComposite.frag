#version 450
layout(location = 0) in vec2 vUV;
layout(location = 0) out vec4 outColor;
layout(set = 0, binding = 0) uniform sampler2D sceneColorTexture;
layout(set = 0, binding = 1) uniform sampler2D cloudTexture;
layout(set = 0, binding = 2, std140) uniform CloudCompositeSettings
{
    float intensity;
    float skyBlend;
    float padding0;
    float padding1;
} settings;

void main()
{
    vec4 scene = texture(sceneColorTexture, vUV);
    vec4 cloud = texture(cloudTexture, vUV);
    float alpha = clamp(cloud.a * settings.intensity, 0.0, 1.0);
    vec3 result = scene.rgb * (1.0 - alpha) + cloud.rgb * settings.intensity;
    outColor = vec4(result, scene.a);
}
