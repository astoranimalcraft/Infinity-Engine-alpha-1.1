#version 450

layout(location = 0) in vec2 vUV;
layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 0, std140) uniform InfinityAtmosphereSettings
{
    mat4 inverseViewProjection;
    vec4 cameraPosition;
    vec4 sunDirection;
    vec4 sunColor;
    vec4 rayleighCoefficient;
    vec4 mieCoefficient;
    vec4 planetAndAtmosphereRadius; // x planet radius, y atmosphere radius, z rayleigh height, w mie height
    vec4 scatteringSettings;        // x mie g, y exposure, z sample count, w ground radius fade
} settings;

const float PI = 3.14159265359;

float RayleighPhase(float c) { return 3.0 / (16.0 * PI) * (1.0 + c * c); }
float MiePhase(float c, float g)
{
    float g2 = g * g;
    float d = max(1.0 + g2 - 2.0 * g * c, 1e-4);
    return (1.0 - g2) / (4.0 * PI * d * sqrt(d));
}

bool RaySphere(vec3 ro, vec3 rd, float radius, out vec2 hit)
{
    float b = dot(ro, rd);
    float c = dot(ro, ro) - radius * radius;
    float h = b * b - c;
    if (h < 0.0) { hit = vec2(0.0); return false; }
    h = sqrt(h);
    hit = vec2(-b - h, -b + h);
    return hit.y > 0.0;
}

vec3 IntegrateAtmosphere(vec3 ro, vec3 rd)
{
    float planetRadius = settings.planetAndAtmosphereRadius.x;
    float atmosphereRadius = settings.planetAndAtmosphereRadius.y;
    vec2 atmoHit;
    if (!RaySphere(ro, rd, atmosphereRadius, atmoHit)) return vec3(0.0);
    float startT = max(atmoHit.x, 0.0);
    float endT = atmoHit.y;
    vec2 groundHit;
    if (RaySphere(ro, rd, planetRadius, groundHit) && groundHit.x > startT)
        endT = min(endT, groundHit.x);

    int steps = clamp(int(settings.scatteringSettings.z), 8, 64);
    float stepLength = (endT - startT) / float(steps);
    float opticalR = 0.0;
    float opticalM = 0.0;
    vec3 sum = vec3(0.0);
    vec3 sunDir = normalize(settings.sunDirection.xyz);
    float cosTheta = dot(rd, sunDir);
    float phaseR = RayleighPhase(cosTheta);
    float phaseM = MiePhase(cosTheta, settings.scatteringSettings.x);
    vec3 betaR = settings.rayleighCoefficient.rgb;
    vec3 betaM = settings.mieCoefficient.rgb;

    for (int i = 0; i < steps; ++i)
    {
        float t = startT + (float(i) + 0.5) * stepLength;
        vec3 p = ro + rd * t;
        float altitude = max(length(p) - planetRadius, 0.0);
        float localR = exp(-altitude / max(settings.planetAndAtmosphereRadius.z, 1.0));
        float localM = exp(-altitude / max(settings.planetAndAtmosphereRadius.w, 1.0));
        opticalR += localR * stepLength;
        opticalM += localM * stepLength;

        vec2 sunHit;
        float sunR = 0.0;
        float sunM = 0.0;
        if (RaySphere(p, sunDir, atmosphereRadius, sunHit))
        {
            float sunStep = sunHit.y / 8.0;
            for (int j = 0; j < 8; ++j)
            {
                vec3 q = p + sunDir * ((float(j) + 0.5) * sunStep);
                float h = max(length(q) - planetRadius, 0.0);
                sunR += exp(-h / max(settings.planetAndAtmosphereRadius.z, 1.0)) * sunStep;
                sunM += exp(-h / max(settings.planetAndAtmosphereRadius.w, 1.0)) * sunStep;
            }
        }
        vec3 transmittance = exp(-(betaR * (opticalR + sunR) + betaM * (opticalM + sunM)));
        sum += transmittance * (betaR * localR * phaseR + betaM * localM * phaseM) * stepLength;
    }
    return sum * settings.sunColor.rgb;
}

void main()
{
    vec4 farPoint = settings.inverseViewProjection * vec4(vUV * 2.0 - 1.0, 0.0, 1.0);
    vec3 worldPoint = farPoint.xyz / max(abs(farPoint.w), 1e-6);
    vec3 direction = normalize(worldPoint - settings.cameraPosition.xyz);
    vec3 sky = IntegrateAtmosphere(settings.cameraPosition.xyz, direction);
    vec3 sunDirection = normalize(settings.sunDirection.xyz);
    float sunDisk = smoothstep(cos(0.0095), cos(0.00465), dot(direction, sunDirection));
    sky += settings.sunColor.rgb * sunDisk * 18.0;
    sky = vec3(1.0) - exp(-sky * max(settings.scatteringSettings.y, 0.0));
    outColor = vec4(sky, 1.0);
}
