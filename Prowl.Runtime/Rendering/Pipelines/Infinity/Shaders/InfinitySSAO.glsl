// Screen-space ambient occlusion helper for a compute or fullscreen shader.
// depthTexture must contain linear view-space depth; normalTexture must contain view-space normals.
#ifndef INFINITY_SSAO_GLSL
#define INFINITY_SSAO_GLSL

float InfinityComputeSSAO(sampler2D depthTexture, sampler2D normalTexture,
                          vec2 uv, vec2 texelSize, float radius, float bias,
                          int sampleCount)
{
    float centerDepth = texture(depthTexture, uv).r;
    vec3 centerNormal = normalize(texture(normalTexture, uv).xyz * 2.0 - 1.0);
    float occlusion = 0.0;
    int count = clamp(sampleCount, 4, 32);
    const float goldenAngle = 2.39996323;

    for (int i = 0; i < count; ++i)
    {
        float fi = (float(i) + 0.5) / float(count);
        float angle = float(i) * goldenAngle;
        float radial = sqrt(fi) * radius;
        vec2 offset = vec2(cos(angle), sin(angle)) * radial * texelSize;
        float sampleDepth = texture(depthTexture, uv + offset).r;
        vec3 sampleNormal = normalize(texture(normalTexture, uv + offset).xyz * 2.0 - 1.0);
        float depthDelta = centerDepth - sampleDepth;
        float rangeWeight = smoothstep(0.0, 1.0, radius / max(abs(depthDelta), 1e-3));
        float normalWeight = smoothstep(-0.2, 0.8, dot(centerNormal, sampleNormal));
        occlusion += step(bias, depthDelta) * rangeWeight * (0.65 + 0.35 * normalWeight);
    }
    return clamp(1.0 - occlusion / float(count), 0.0, 1.0);
}

#endif
