#version 450

const float PI = 3.14159265358979323846;

layout(location = 0) in vec2 vUV;
layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 0, std140) uniform AtmosphereData
{
    mat4 inverseViewProjection;
    vec4 cameraPosition;
    vec4 planetCenterRadius;  // xyz = center, w = planet radius
    vec4 atmosphereParameters; // x = atmosphere height, y = rayleigh scale height, z = mie scale height, w = mie g
    vec4 rayleighCoefficient; // rgb = beta Rayleigh
    vec4 mieCoefficient;      // rgb = beta Mie
    vec4 sunDirectionIntensity; // xyz = direction toward sun, w = intensity
    vec4 sunColor;
} atmosphere;

vec2 RaySphere(vec3 origin, vec3 direction, vec3 center, float radius)
{
    vec3 oc = origin - center;
    float b = dot(oc, direction);
    float c = dot(oc, oc) - radius * radius;
    float h = b * b - c;
    if (h < 0.0)
        return vec2(-1.0);
    h = sqrt(h);
    return vec2(-b - h, -b + h);
}

float RayleighPhase(float mu)
{
    return 3.0 * (1.0 + mu * mu) / (16.0 * PI);
}

float MiePhase(float mu, float g)
{
    float g2 = g * g;
    float denominator = pow(max(1.0 + g2 - 2.0 * g * mu, 1e-3), 1.5);
    return (1.0 - g2) / (4.0 * PI * denominator);
}

void main()
{
    vec2 ndc = vUV * 2.0 - 1.0;
    vec4 farPoint = atmosphere.inverseViewProjection * vec4(ndc, 1.0, 1.0);
    farPoint.xyz /= max(abs(farPoint.w), 1e-6);

    vec3 origin = atmosphere.cameraPosition.xyz;
    vec3 direction = normalize(farPoint.xyz - origin);
    vec3 center = atmosphere.planetCenterRadius.xyz;
    float planetRadius = max(atmosphere.planetCenterRadius.w, 1.0);
    float atmosphereRadius = planetRadius + max(atmosphere.atmosphereParameters.x, 1.0);

    vec2 atmosphereHit = RaySphere(origin, direction, center, atmosphereRadius);
    if (atmosphereHit.y <= 0.0)
    {
        outColor = vec4(0.0, 0.0, 0.0, 1.0);
        return;
    }

    float tStart = max(atmosphereHit.x, 0.0);
    float tEnd = atmosphereHit.y;
    vec2 planetHit = RaySphere(origin, direction, center, planetRadius);
    if (planetHit.x > 0.0)
        tEnd = min(tEnd, planetHit.x);

    if (tEnd <= tStart)
    {
        outColor = vec4(0.0, 0.0, 0.0, 1.0);
        return;
    }

    vec3 sunDir = normalize(atmosphere.sunDirectionIntensity.xyz);
    float mu = dot(direction, sunDir);
    float rayleighScale = max(atmosphere.atmosphereParameters.y, 1.0);
    float mieScale = max(atmosphere.atmosphereParameters.z, 1.0);
    float g = clamp(atmosphere.atmosphereParameters.w, -0.95, 0.95);

    const int SAMPLE_COUNT = 24;
    float stepLength = (tEnd - tStart) / float(SAMPLE_COUNT);
    float opticalRayleigh = 0.0;
    float opticalMie = 0.0;
    vec3 rayleighAccum = vec3(0.0);
    vec3 mieAccum = vec3(0.0);

    for (int i = 0; i < SAMPLE_COUNT; ++i)
    {
        float t = tStart + (float(i) + 0.5) * stepLength;
        vec3 samplePosition = origin + direction * t;
        float altitude = max(length(samplePosition - center) - planetRadius, 0.0);

        float densityR = exp(-altitude / rayleighScale);
        float densityM = exp(-altitude / mieScale);
        opticalRayleigh += densityR * stepLength;
        opticalMie += densityM * stepLength;

        // Approximate sun-path optical depth using local altitude. A dedicated transmittance LUT
        // replaces this approximation in the full atmosphere precompute path.
        float sunOpticalR = densityR * rayleighScale * 1.25;
        float sunOpticalM = densityM * mieScale * 1.25;
        vec3 extinction = atmosphere.rayleighCoefficient.rgb * (opticalRayleigh + sunOpticalR)
                        + atmosphere.mieCoefficient.rgb * (opticalMie + sunOpticalM);
        vec3 transmittance = exp(-extinction);

        rayleighAccum += transmittance * densityR * stepLength;
        mieAccum += transmittance * densityM * stepLength;
    }

    vec3 betaR = atmosphere.rayleighCoefficient.rgb;
    vec3 betaM = atmosphere.mieCoefficient.rgb;
    vec3 sunRadiance = atmosphere.sunColor.rgb * max(atmosphere.sunDirectionIntensity.w, 0.0);

    vec3 scattering = sunRadiance * (
        betaR * rayleighAccum * RayleighPhase(mu)
        + betaM * mieAccum * MiePhase(mu, g)
    );

    vec3 transmittanceView = exp(-betaR * opticalRayleigh - betaM * opticalMie);
    float sunDisk = smoothstep(cos(radians(0.54)), cos(radians(0.27)), mu);
    vec3 sun = sunRadiance * sunDisk * transmittanceView;

    outColor = vec4(max(scattering + sun, vec3(0.0)), 1.0);
}
