#version 450
layout(location = 0) in vec2 vUV;
layout(location = 0) out vec4 outColor;
layout(set = 0, binding = 0) uniform sampler2D sourceTexture;
layout(set = 0, binding = 1, std140) uniform BloomExtractSettings
{
    float threshold;
    float softKnee;
    float maxBrightness;
    float antiFlicker;
} settings;

vec3 Prefilter(vec3 color)
{
    color = clamp(color, vec3(0.0), vec3(max(settings.maxBrightness, 0.0)));
    float brightness = max(color.r, max(color.g, color.b));
    float knee = max(settings.threshold * settings.softKnee, 1e-5);
    float soft = clamp(brightness - settings.threshold + knee, 0.0, 2.0 * knee);
    soft = soft * soft / (4.0 * knee);
    float contribution = max(soft, brightness - settings.threshold);
    return color * contribution / max(brightness, 1e-5);
}

void main()
{
    ivec2 size = textureSize(sourceTexture, 0);
    vec2 texel = 1.0 / vec2(size);
    vec3 sum = vec3(0.0);
    float totalWeight = 0.0;
    const vec2 offsets[4] = vec2[](
        vec2(-1.0, -1.0), vec2(1.0, -1.0),
        vec2(-1.0, 1.0), vec2(1.0, 1.0));
    for (int i = 0; i < 4; ++i)
    {
        vec3 s = Prefilter(texture(sourceTexture, vUV + offsets[i] * texel).rgb);
        float w = settings.antiFlicker > 0.5 ? 1.0 / (1.0 + dot(s, vec3(0.2126, 0.7152, 0.0722))) : 1.0;
        sum += s * w;
        totalWeight += w;
    }
    outColor = vec4(sum / max(totalWeight, 1e-5), 1.0);
}
