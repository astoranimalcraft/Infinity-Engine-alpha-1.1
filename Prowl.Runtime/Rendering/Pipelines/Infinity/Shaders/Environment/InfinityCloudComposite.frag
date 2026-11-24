#version 450

layout(location = 0) in vec2 vUV;
layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 0) uniform sampler2D uSky;
layout(set = 0, binding = 1) uniform sampler2D uCloudLayer;

void main()
{
    vec3 sky = texture(uSky, vUV).rgb;
    vec4 clouds = texture(uCloudLayer, vUV);
    vec3 result = mix(sky, clouds.rgb, clamp(clouds.a, 0.0, 1.0));
    outColor = vec4(result, 1.0);
}
