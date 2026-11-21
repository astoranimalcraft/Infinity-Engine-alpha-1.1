#version 450

layout(location = 0) in vec2 vUV;
layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 0) uniform sampler2D uSource;
layout(set = 0, binding = 1, std140) uniform TonemappingSettings
{
    float exposureEV;
    int mode;       // 0 none, 1 ACES, 2 Reinhard, 3 Uncharted2
    float padding0;
    int encodeSRGB; // set to 0 when the swapchain attachment performs sRGB encoding
} settings;

vec3 ACES(vec3 x)
{
    return clamp((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14), 0.0, 1.0);
}

vec3 Reinhard(vec3 x)
{
    return x / (1.0 + x);
}

vec3 Uncharted2Curve(vec3 x)
{
    const float A = 0.15;
    const float B = 0.50;
    const float C = 0.10;
    const float D = 0.20;
    const float E = 0.02;
    const float F = 0.30;
    return ((x * (A * x + C * B) + D * E) / (x * (A * x + B) + D * F)) - E / F;
}

vec3 Uncharted2(vec3 x)
{
    return Uncharted2Curve(x) / Uncharted2Curve(vec3(11.2));
}

vec3 LinearToSRGB(vec3 c)
{
    c = max(c, vec3(0.0));
    vec3 low = c * 12.92;
    vec3 high = 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055;
    return mix(high, low, lessThanEqual(c, vec3(0.0031308)));
}

void main()
{
    vec4 source = texture(uSource, vUV);
    vec3 color = max(source.rgb, vec3(0.0)) * exp2(settings.exposureEV);

    if (settings.mode == 1)
        color = ACES(color);
    else if (settings.mode == 2)
        color = Reinhard(color);
    else if (settings.mode == 3)
        color = Uncharted2(color);

    color = clamp(color, 0.0, 1.0);
    if (settings.encodeSRGB != 0)
        color = LinearToSRGB(color);

    outColor = vec4(color, source.a);
}
