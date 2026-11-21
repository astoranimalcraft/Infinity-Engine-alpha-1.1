#version 450
layout(location = 0) in vec2 vUV;
layout(location = 0) out vec4 outColor;
layout(set = 0, binding = 0) uniform sampler2D sourceTexture;
layout(set = 0, binding = 1, std140) uniform BloomBlurSettings
{
    vec2 texelSize;
    vec2 direction;
} settings;

void main()
{
    vec2 stepUV = settings.texelSize * settings.direction;
    vec3 color = texture(sourceTexture, vUV).rgb * 0.227027;
    color += texture(sourceTexture, vUV + stepUV * 1.384615).rgb * 0.316216;
    color += texture(sourceTexture, vUV - stepUV * 1.384615).rgb * 0.316216;
    color += texture(sourceTexture, vUV + stepUV * 3.230769).rgb * 0.070270;
    color += texture(sourceTexture, vUV - stepUV * 3.230769).rgb * 0.070270;
    outColor = vec4(color, 1.0);
}
