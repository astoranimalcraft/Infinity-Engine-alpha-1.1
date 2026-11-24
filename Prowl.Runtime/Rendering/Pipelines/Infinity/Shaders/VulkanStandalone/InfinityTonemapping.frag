#version 450
layout(location = 0) in vec2 vUV;
layout(location = 0) out vec4 outColor;
layout(set = 0, binding = 0) uniform sampler2D sourceTexture;
layout(set = 0, binding = 1, std140) uniform TonemappingSettings
{
    float exposureEV;
    int mode;       // 0 None, 1 ACES, 2 AgX approximation, 3 Reinhard, 4 Uncharted2
    int encodeSRGB;
    float whitePoint;
} settings;

vec3 ACES(vec3 x)
{
    return clamp((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14), 0.0, 1.0);
}
vec3 AgXApprox(vec3 x)
{
    vec3 logColor = log2(max(x, vec3(1e-6)));
    vec3 t = clamp((logColor + 10.0) / 16.0, 0.0, 1.0);
    return t * t * (3.0 - 2.0 * t);
}
vec3 LinearToSRGB(vec3 x)
{
    x = max(x, vec3(0.0));
    return mix(12.92 * x, 1.055 * pow(x, vec3(1.0 / 2.4)) - 0.055, step(vec3(0.0031308), x));
}

void main()
{
    vec4 source = texture(sourceTexture, vUV);
    vec3 color = max(source.rgb, vec3(0.0)) * exp2(settings.exposureEV);
    if (settings.mode == 1) color = ACES(color);
    else if (settings.mode == 2) color = AgXApprox(color);
    else if (settings.mode == 3) color = color / (1.0 + color);
    else if (settings.mode == 4) color = color / (color + vec3(max(settings.whitePoint, 1e-3)));
    if (settings.encodeSRGB != 0) color = LinearToSRGB(color);
    outColor = vec4(clamp(color, 0.0, 1.0), source.a);
}
