#version 450

layout(location = 0) in vec2 vUV;
layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 0) uniform sampler2D uSource;
layout(set = 0, binding = 1, std140) uniform BloomExtractSettings
{
    float threshold;
    float softKnee;
    float maxBrightness;
    float padding;
} settings;

void main()
{
    vec3 color = max(texture(uSource, vUV).rgb, vec3(0.0));
    color = min(color, vec3(max(settings.maxBrightness, 0.0)));

    float brightness = max(max(color.r, color.g), color.b);
    float knee = max(settings.threshold * settings.softKnee, 1e-4);
    float soft = clamp(brightness - settings.threshold + knee, 0.0, 2.0 * knee);
    soft = soft * soft / (4.0 * knee + 1e-4);

    float contribution = max(brightness - settings.threshold, soft);
    contribution /= max(brightness, 1e-4);

    outColor = vec4(color * clamp(contribution, 0.0, 1.0), 1.0);
}
