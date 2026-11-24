#version 450

const float PI = 3.14159265358979323846;
const int MAX_LOCAL_LIGHTS = 256;

struct InfinityLight
{
    // xyz = world position, w = range
    vec4 positionRange;
    // xyz = world forward direction, w = 0 point / 1 spot
    vec4 directionType;
    // rgb = linear light colour, a = intensity
    vec4 colorIntensity;
    // x = inner cone cosine, y = outer cone cosine
    vec4 spotAngles;
};

layout(set = 0, binding = 0, std140) uniform InfinitySceneData
{
    mat4 viewProjection;
    vec4 cameraPosition;
    vec4 directionalDirectionIntensity; // xyz points from surface toward the sun, w = intensity
    vec4 directionalColor;             // rgb = linear sun colour
    vec4 ambientColor;                  // rgb = ambient radiance approximation
    vec4 environmentInfo;              // x = diffuse IBL, y = specular IBL, z = max prefiltered mip
    uvec4 lightCounts;                  // x = local light count
} scene;

layout(set = 0, binding = 1, std430) readonly buffer InfinityLightBuffer
{
    InfinityLight items[];
} localLights;

layout(set = 0, binding = 2) uniform samplerCube uIrradianceMap;
layout(set = 0, binding = 3) uniform samplerCube uPrefilteredEnvironment;
layout(set = 0, binding = 4) uniform sampler2D uBrdfLut;

layout(set = 1, binding = 0, std140) uniform InfinityMaterialData
{
    vec4 baseColorFactor;
    vec4 emissiveFactor; // rgb = emissive colour, a = emissive intensity
    vec4 surfaceFactors; // x = metallic, y = roughness, z = AO strength, w = normal strength
} materialData;

layout(set = 1, binding = 1) uniform sampler2D uAlbedoMap;
layout(set = 1, binding = 2) uniform sampler2D uNormalMap;
layout(set = 1, binding = 3) uniform sampler2D uSurfaceMap; // R = AO, G = roughness, B = metallic
layout(set = 1, binding = 4) uniform sampler2D uEmissionMap;

layout(location = 0) in vec3 vWorldPosition;
layout(location = 1) in vec3 vWorldNormal;
layout(location = 2) in vec2 vUV;
layout(location = 3) in vec4 vWorldTangent;
layout(location = 0) out vec4 outColor;

float DistributionGGX(vec3 N, vec3 H, float roughness)
{
    float a = roughness * roughness;
    float a2 = a * a;
    float NdotH = max(dot(N, H), 0.0);
    float d = NdotH * NdotH * (a2 - 1.0) + 1.0;
    return a2 / max(PI * d * d, 1e-6);
}

float GeometrySchlickGGX(float NdotV, float roughness)
{
    float r = roughness + 1.0;
    float k = (r * r) * 0.125;
    return NdotV / max(NdotV * (1.0 - k) + k, 1e-6);
}

float GeometrySmith(vec3 N, vec3 V, vec3 L, float roughness)
{
    return GeometrySchlickGGX(max(dot(N, V), 0.0), roughness)
         * GeometrySchlickGGX(max(dot(N, L), 0.0), roughness);
}

vec3 FresnelSchlick(float cosTheta, vec3 F0)
{
    return F0 + (1.0 - F0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}

vec3 FresnelSchlickRoughness(float cosTheta, vec3 F0, float roughness)
{
    return F0 + (max(vec3(1.0 - roughness), F0) - F0)
         * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}

vec3 EvaluateBRDF(vec3 albedo, float metallic, float roughness,
                  vec3 N, vec3 V, vec3 L, vec3 radiance)
{
    float NdotL = max(dot(N, L), 0.0);
    if (NdotL <= 0.0)
        return vec3(0.0);

    vec3 H = normalize(V + L);
    float NdotV = max(dot(N, V), 0.0);

    vec3 F0 = mix(vec3(0.04), albedo, metallic);
    vec3 F = FresnelSchlick(max(dot(H, V), 0.0), F0);
    float D = DistributionGGX(N, H, roughness);
    float G = GeometrySmith(N, V, L, roughness);

    vec3 specular = (D * G * F)
        / max(4.0 * NdotV * NdotL, 1e-4);

    vec3 diffuseWeight = (1.0 - F) * (1.0 - metallic);
    vec3 diffuse = diffuseWeight * albedo / PI;

    return (diffuse + specular) * radiance * NdotL;
}

vec3 GetWorldNormal()
{
    vec3 N = normalize(vWorldNormal);
    vec3 T = normalize(vWorldTangent.xyz - dot(vWorldTangent.xyz, N) * N);
    vec3 B = normalize(cross(N, T)) * vWorldTangent.w;

    vec3 tangentNormal = texture(uNormalMap, vUV).xyz * 2.0 - 1.0;
    tangentNormal.xy *= materialData.surfaceFactors.w;
    return normalize(mat3(T, B, N) * tangentNormal);
}

void main()
{
    vec4 albedoTexel = texture(uAlbedoMap, vUV) * materialData.baseColorFactor;
    vec3 albedo = max(albedoTexel.rgb, vec3(0.0));

    vec3 surface = texture(uSurfaceMap, vUV).rgb;
    float aoTexture = clamp(surface.r, 0.0, 1.0);
    float roughness = clamp(surface.g * materialData.surfaceFactors.y, 0.045, 1.0);
    float metallic = clamp(surface.b * materialData.surfaceFactors.x, 0.0, 1.0);
    float ao = mix(1.0, aoTexture, clamp(materialData.surfaceFactors.z, 0.0, 1.0));

    vec3 N = GetWorldNormal();
    vec3 V = normalize(scene.cameraPosition.xyz - vWorldPosition);
    vec3 F0 = mix(vec3(0.04), albedo, metallic);

    // Directional sunlight.
    vec3 Lo = vec3(0.0);
    vec3 sunL = normalize(scene.directionalDirectionIntensity.xyz);
    vec3 sunRadiance = scene.directionalColor.rgb
                     * max(scene.directionalDirectionIntensity.w, 0.0);
    Lo += EvaluateBRDF(albedo, metallic, roughness, N, V, sunL, sunRadiance);

    // Point and spot lights. The CPU must cap lightCounts.x to MAX_LOCAL_LIGHTS.
    uint count = min(scene.lightCounts.x, uint(MAX_LOCAL_LIGHTS));
    for (uint i = 0u; i < count; ++i)
    {
        InfinityLight light = localLights.items[i];
        vec3 toLight = light.positionRange.xyz - vWorldPosition;
        float distanceToLight = length(toLight);
        float range = max(light.positionRange.w, 0.001);

        if (distanceToLight >= range || distanceToLight <= 0.0001)
            continue;

        vec3 L = toLight / distanceToLight;
        float normalizedDistance = distanceToLight / range;
        float rangeFade = clamp(1.0 - normalizedDistance * normalizedDistance, 0.0, 1.0);
        float attenuation = rangeFade * rangeFade / max(distanceToLight * distanceToLight, 0.01);

        if (light.directionType.w > 0.5)
        {
            vec3 spotForward = normalize(light.directionType.xyz);
            float coneCos = dot(spotForward, -L);
            float cone = smoothstep(light.spotAngles.y, light.spotAngles.x, coneCos);
            attenuation *= cone;
        }

        vec3 radiance = light.colorIntensity.rgb
                      * max(light.colorIntensity.a, 0.0)
                      * attenuation;
        Lo += EvaluateBRDF(albedo, metallic, roughness, N, V, L, radiance);
    }

    // Image-based lighting. The maps are generated from the active sky/environment by Infinity's IBL pass.
    float NdotV = max(dot(N, V), 0.0);
    vec3 F = FresnelSchlickRoughness(NdotV, F0, roughness);
    vec3 kD = (1.0 - F) * (1.0 - metallic);

    vec3 irradiance = texture(uIrradianceMap, N).rgb;
    vec3 diffuseIBL = irradiance * albedo * kD;

    vec3 R = reflect(-V, N);
    float maxMip = max(scene.environmentInfo.z, 0.0);
    vec3 prefiltered = textureLod(uPrefilteredEnvironment, R, roughness * maxMip).rgb;
    vec2 brdf = texture(uBrdfLut, vec2(NdotV, roughness)).rg;
    vec3 specularIBL = prefiltered * (F * brdf.x + brdf.y);

    vec3 ambient = scene.ambientColor.rgb * albedo * (1.0 - metallic);
    vec3 indirect = (diffuseIBL * scene.environmentInfo.x
                   + specularIBL * scene.environmentInfo.y
                   + ambient) * ao;

    vec3 emission = texture(uEmissionMap, vUV).rgb
                  * materialData.emissiveFactor.rgb
                  * max(materialData.emissiveFactor.a, 0.0);

    // Keep this output linear HDR. Bloom and tonemapping happen in later passes.
    outColor = vec4(Lo + indirect + emission, albedoTexel.a);
}
