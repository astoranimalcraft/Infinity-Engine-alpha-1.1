// Physically-based single-scattering atmosphere helpers.
// Inputs are in world units; choose consistent planet/atmosphere radii and scale heights.
#ifndef INFINITY_ATMOSPHERE_GLSL
#define INFINITY_ATMOSPHERE_GLSL

const float INFINITY_ATMO_PI = 3.14159265358979323846;

float InfinityRayleighPhase(float cosTheta)
{
    return 3.0 / (16.0 * INFINITY_ATMO_PI) * (1.0 + cosTheta * cosTheta);
}

float InfinityMiePhase(float cosTheta, float g)
{
    float g2 = g * g;
    float denominator = max(1.0 + g2 - 2.0 * g * cosTheta, 1e-4);
    return (1.0 - g2) / (4.0 * INFINITY_ATMO_PI * denominator * sqrt(denominator));
}

bool InfinityRaySphere(vec3 origin, vec3 direction, float radius, out vec2 hit)
{
    float b = dot(origin, direction);
    float c = dot(origin, origin) - radius * radius;
    float h = b * b - c;
    if (h < 0.0) { hit = vec2(0.0); return false; }
    h = sqrt(h);
    hit = vec2(-b - h, -b + h);
    return hit.y > 0.0;
}

// Returns atmospheric in-scatter for one view ray. Caller handles ground intersection and exposure.
vec3 InfinityIntegrateAtmosphere(vec3 origin, vec3 direction, vec3 sunDirection,
                                 float planetRadius, float atmosphereRadius,
                                 float rayleighHeight, float mieHeight,
                                 vec3 betaRayleigh, vec3 betaMie,
                                 float mieAnisotropy, int sampleCount)
{
    vec2 atmosphereHit;
    if (!InfinityRaySphere(origin, direction, atmosphereRadius, atmosphereHit)) return vec3(0.0);

    float startDistance = max(atmosphereHit.x, 0.0);
    float endDistance = atmosphereHit.y;
    vec2 groundHit;
    if (InfinityRaySphere(origin, direction, planetRadius, groundHit) && groundHit.x > startDistance)
        endDistance = min(endDistance, groundHit.x);

    int steps = clamp(sampleCount, 4, 64);
    float stepLength = (endDistance - startDistance) / float(steps);
    float opticalRayleigh = 0.0;
    float opticalMie = 0.0;
    vec3 accumulated = vec3(0.0);
    float cosTheta = dot(direction, sunDirection);
    float phaseR = InfinityRayleighPhase(cosTheta);
    float phaseM = InfinityMiePhase(cosTheta, mieAnisotropy);

    for (int i = 0; i < steps; ++i)
    {
        float distanceAlongRay = startDistance + (float(i) + 0.5) * stepLength;
        vec3 samplePosition = origin + direction * distanceAlongRay;
        float altitude = max(length(samplePosition) - planetRadius, 0.0);
        float localR = exp(-altitude / max(rayleighHeight, 1e-3));
        float localM = exp(-altitude / max(mieHeight, 1e-3));
        opticalRayleigh += localR * stepLength;
        opticalMie += localM * stepLength;

        vec2 sunHit;
        float sunDepthR = 0.0;
        float sunDepthM = 0.0;
        if (InfinityRaySphere(samplePosition, normalize(sunDirection), atmosphereRadius, sunHit))
        {
            float sunStep = sunHit.y / 8.0;
            for (int j = 0; j < 8; ++j)
            {
                vec3 p = samplePosition + normalize(sunDirection) * ((float(j) + 0.5) * sunStep);
                float h = max(length(p) - planetRadius, 0.0);
                sunDepthR += exp(-h / max(rayleighHeight, 1e-3)) * sunStep;
                sunDepthM += exp(-h / max(mieHeight, 1e-3)) * sunStep;
            }
        }

        vec3 transmittance = exp(-(betaRayleigh * (opticalRayleigh + sunDepthR)
                                 + betaMie * (opticalMie + sunDepthM)));
        accumulated += transmittance * (betaRayleigh * localR * phaseR + betaMie * localM * phaseM) * stepLength;
    }
    return accumulated;
}

#endif
