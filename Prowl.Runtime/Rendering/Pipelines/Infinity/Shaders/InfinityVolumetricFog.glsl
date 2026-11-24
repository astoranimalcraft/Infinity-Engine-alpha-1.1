// Volumetric height fog and single-scattering helpers.
#ifndef INFINITY_VOLUMETRIC_FOG_GLSL
#define INFINITY_VOLUMETRIC_FOG_GLSL

float InfinityHeightFogDensity(float worldHeight, float fogBaseHeight, float falloff, float baseDensity)
{
    return max(baseDensity, 0.0) * exp(-max(worldHeight - fogBaseHeight, 0.0) * max(falloff, 0.0));
}

float InfinityHenyeyGreenstein(float cosTheta, float anisotropy)
{
    float g = clamp(anisotropy, -0.9, 0.9);
    float g2 = g * g;
    float denominator = max(1.0 + g2 - 2.0 * g * cosTheta, 1e-4);
    return (1.0 - g2) / (4.0 * 3.14159265359 * denominator * sqrt(denominator));
}

// March a view ray through height fog. Returns RGB in-scattering and alpha opacity.
vec4 InfinityMarchHeightFog(vec3 rayOrigin, vec3 rayDirection, float maxDistance,
                            float fogBaseHeight, float falloff, float baseDensity,
                            vec3 sunDirection, vec3 scatteringColor, float anisotropy,
                            int requestedSteps)
{
    int steps = clamp(requestedSteps, 8, 96);
    float stepLength = maxDistance / float(steps);
    float transmittance = 1.0;
    vec3 scattering = vec3(0.0);
    float phase = InfinityHenyeyGreenstein(dot(normalize(rayDirection), normalize(sunDirection)), anisotropy);

    for (int i = 0; i < steps; ++i)
    {
        vec3 p = rayOrigin + rayDirection * ((float(i) + 0.5) * stepLength);
        float density = InfinityHeightFogDensity(p.y, fogBaseHeight, falloff, baseDensity);
        float opticalDepth = density * stepLength;
        float attenuation = exp(-opticalDepth);
        float absorbed = 1.0 - attenuation;
        scattering += transmittance * absorbed * scatteringColor * phase;
        transmittance *= attenuation;
        if (transmittance < 0.01) break;
    }
    return vec4(scattering, 1.0 - transmittance);
}

#endif
