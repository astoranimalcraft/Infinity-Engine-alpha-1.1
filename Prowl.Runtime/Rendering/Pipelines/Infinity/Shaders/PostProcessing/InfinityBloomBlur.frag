#version 450

layout(location = 0) in vec2 vUV;
layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 0) uniform sampler2D uSource;
layout(set = 0, binding = 1, std140) uniform BloomBlurSettings
{
    vec2 texelSize;
    vec2 direction; // (1,0) horizontal, (0,1) vertical
} settings;

void main()
{
    vec2 d = settings.texelSize * settings.direction;
    vec3 color = texture(uSource, vUV).rgb * 0.227027;
    color += texture(uSource, vUV + d * 1.384615).rgb * 0.1945946;
    color += texture(uSource, vUV - d * 1.384615).rgb * 0.1945946;
    color += texture(uSource, vUV + d * 3.230769).rgb * 0.1216216;
    color += texture(uSource, vUV - d * 3.230769).rgb * 0.1216216;
    outColor = vec4(color, 1.0);
}
