// Procedural volumetric cloud density and ray-march helpers.
// Supply weather/shape/detail textures or replace the noise functions with the engine's 3D noise volumes.
#ifndef INFINITY_VOLUMETRIC_CLOUDS_GLSL
#define INFINITY_VOLUMETRIC_CLOUDS_GLSL

float InfinityCloudHash(vec3 p)
{
    p = fract(p * 0.3183099 + vec3(0.1));
    p *= 17.0;
    return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float InfinityCloudNoise(vec3 p)
{
    vec3 i = floor(p);
    vec3 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float n000 = InfinityCloudHash(i + vec3(0,0,0));
    float n100 = InfinityCloudHash(i + vec3(1,0,0));
    float n010 = InfinityCloudHash(i + vec3(0,1,0));
    float n110 = InfinityCloudHash(i + vec3(1,1,0));
    float n001 = InfinityCloudHash(i + vec3(0,0,1));
    float n101 = InfinityCloudHash(i + vec3(1,0,1));
    float n011 = InfinityCloudHash(i + vec3(0,1,1));
    float n111 = InfinityCloudHash(i + vec3(1,1,1));
    float z0 = mix(mix(n000,n100,f.x), mix(n010,n110,f.x), f.y);
    float z1 = mix(mix(n001,n101,f.x), mix(n011,n111,f.x), f.y);
    return mix(z0, z1, f.z);
}

float InfinityCloudFBM(vec3 p)
{
    float value = 0.0;
    float amplitude = 0.5;
    mat3 octave = mat3(1.6, 1.2, 0.0, -1.2, 1.6, 0.0, 0.0, 0.0, 1.4);
    for (int i = 0; i < 5; ++i)
    {
        value += InfinityCloudNoise(p) * amplitude;
        p = octave * p + 13.1;
        amplitude *= 0.5;
    }
    return value;
}

float InfinityCloudDensity(vec3 worldPosition, float cloudBase, float cloudTop,
                           float coverage, float densityMultiplier, vec3 windOffset)
{
    float height01 = clamp((worldPosition.y - cloudBase) / max(cloudTop - cloudBase, 1.0), 0.0, 1.0);
    float verticalProfile = smoothstep(0.0, 0.12, height01) * (1.0 - smoothstep(0.68, 1.0, height01));
    vec3 p = (worldPosition + windOffset) * 0.0015;
    float shape = InfinityCloudFBM(p * 2.0);
    float erosion = InfinityCloudFBM(p * 9.0 + 17.0) * 0.22;
    float weathered = shape - erosion;
    float threshold = mix(0.82, 0.32, clamp(coverage, 0.0, 1.0));
    return clamp((weathered - threshold) * densityMultiplier * 3.0, 0.0, 1.0) * verticalProfile;
}

// Approximate Beer-Powder cloud march. Caller supplies the world-space ray and lighting colour.
vec4 InfinityMarchClouds(vec3 rayOrigin, vec3 rayDirection, vec3 sunDirection,
                         float maxDistance, float cloudBase, float cloudTop,
                         float coverage, float densityMultiplier, vec3 windOffset,
                         vec3 sunColor, int requestedSteps)
{
    int steps = clamp(requestedSteps, 8, 96);
    float stepLength = maxDistance / float(steps);
    float transmittance = 1.0;
    vec3 radiance = vec3(0.0);
    for (int i = 0; i < steps; ++i)
    {
        float t = (float(i) + 0.5) * stepLength;
        vec3 p = rayOrigin + rayDirection * t;
        if (p.y < cloudBase || p.y > cloudTop) continue;
        float density = InfinityCloudDensity(p, cloudBase, cloudTop, coverage, densityMultiplier, windOffset);
        if (density <= 1e-4) continue;
        float sunPhase = 0.35 + 0.65 * pow(max(dot(rayDirection, sunDirection), 0.0), 8.0);
        float attenuation = exp(-density * stepLength * 0.0018);
        float powder = 1.0 - exp(-density * stepLength * 0.0035);
        radiance += transmittance * sunColor * sunPhase * powder;
        transmittance *= attenuation;
        if (transmittance < 0.01) break;
    }
    return vec4(radiance, 1.0 - transmittance);
}

#endif
