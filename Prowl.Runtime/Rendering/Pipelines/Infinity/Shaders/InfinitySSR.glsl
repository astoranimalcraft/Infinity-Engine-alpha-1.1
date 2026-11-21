// Screen-space reflection ray-march helper.
// Caller passes view-space positions/normals and a projection matrix. Depth must be linear view-space depth.
#ifndef INFINITY_SSR_GLSL
#define INFINITY_SSR_GLSL

bool InfinityTraceSSR(sampler2D depthTexture, mat4 projection, vec3 originVS, vec3 directionVS,
                      vec2 screenSize, float maxDistance, float thickness,
                      int requestedSteps, out vec2 hitUV, out float confidence)
{
    int steps = clamp(requestedSteps, 8, 128);
    float stepDistance = maxDistance / float(steps);
    confidence = 0.0;
    hitUV = vec2(0.0);

    for (int i = 1; i <= steps; ++i)
    {
        float t = float(i) * stepDistance;
        vec3 p = originVS + directionVS * t;
        vec4 clip = projection * vec4(p, 1.0);
        if (clip.w <= 1e-5) return false;
        vec3 ndc = clip.xyz / clip.w;
        vec2 uv = ndc.xy * 0.5 + 0.5;
        if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0)))) return false;

        float sceneDepth = texture(depthTexture, uv).r;
        float rayDepth = -p.z;
        float delta = rayDepth - sceneDepth;
        if (delta >= 0.0 && delta <= thickness)
        {
            hitUV = uv;
            float edgeFade = min(min(uv.x, 1.0 - uv.x), min(uv.y, 1.0 - uv.y)) * 12.0;
            float distanceFade = 1.0 - float(i) / float(steps);
            confidence = clamp(edgeFade, 0.0, 1.0) * distanceFade;
            return confidence > 0.01;
        }
    }
    return false;
}

vec3 InfinityCompositeSSR(vec3 sceneColor, vec3 reflectedColor, float confidence, float fresnel)
{
    float weight = clamp(confidence, 0.0, 1.0) * clamp(fresnel, 0.0, 1.0);
    return mix(sceneColor, reflectedColor, weight);
}

#endif
