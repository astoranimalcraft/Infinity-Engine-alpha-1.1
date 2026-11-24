#version 450

layout(location = 0) in vec3 vWorldPosition;
layout(location = 1) in vec3 vWorldNormal;
layout(location = 2) in vec2 vUV;
layout(location = 3) in vec4 vWorldTangent;
layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 0, std140) uniform InfinitySceneData
{
    mat4 viewProjection;
    mat4 model;
    mat4 normalMatrix;
    vec4 cameraPosition;
    vec4 sunDirectionIntensity;
    vec4 sunColor;
} scene;

struct InfinityLocalLight
{
    vec4 positionRadius;
    vec4 colorIntensity;
    vec4 directionType;
};
layout(set = 0, binding = 1, std430) readonly buffer InfinityLightBuffer
{
    uint lightCount;
    InfinityLocalLight lights[];
};

layout(set = 1, binding = 0, std140) uniform InfinityMaterialData
{
    vec4 baseColorFactor;
    vec4 emissiveFactor;
    float metallicFactor;
    float roughnessFactor;
    float normalScale;
    float occlusionStrength;
    float alphaCutoff;
    int alphaMode;
    vec2 materialPadding;
} material;

layout(set = 1, binding = 1) uniform sampler2D baseColorMap;
layout(set = 1, binding = 2) uniform sampler2D normalMap;
layout(set = 1, binding = 3) uniform sampler2D metallicRoughnessMap;
layout(set = 1, binding = 4) uniform sampler2D occlusionMap;
layout(set = 1, binding = 5) uniform sampler2D emissiveMap;
layout(set = 2, binding = 0) uniform samplerCube irradianceMap;
layout(set = 2, binding = 1) uniform samplerCube prefilteredEnvironment;
layout(set = 2, binding = 2) uniform sampler2D brdfLut;

const float PI = 3.14159265359;

vec3 FresnelSchlick(float cosTheta, vec3 f0)
{
    return f0 + (1.0 - f0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}

float DistributionGGX(vec3 n, vec3 h, float roughness)
{
    float a = max(roughness * roughness, 0.0025);
    float a2 = a * a;
    float nDotH = max(dot(n, h), 0.0);
    float d = nDotH * nDotH * (a2 - 1.0) + 1.0;
    return a2 / max(PI * d * d, 1e-5);
}

float GeometrySchlickGGX(float nDotV, float roughness)
{
    float r = roughness + 1.0;
    float k = (r * r) / 8.0;
    return nDotV / max(nDotV * (1.0 - k) + k, 1e-5);
}

float GeometrySmith(vec3 n, vec3 v, vec3 l, float roughness)
{
    return GeometrySchlickGGX(max(dot(n, v), 0.0), roughness)
         * GeometrySchlickGGX(max(dot(n, l), 0.0), roughness);
}

vec3 EvaluateLight(vec3 albedo, float metallic, float roughness, vec3 n, vec3 v,
                   vec3 l, vec3 radiance)
{
    vec3 h = normalize(v + l);
    float nDotL = max(dot(n, l), 0.0);
    float nDotV = max(dot(n, v), 0.0);
    if (nDotL <= 0.0 || nDotV <= 0.0) return vec3(0.0);

    vec3 f0 = mix(vec3(0.04), albedo, metallic);
    vec3 f = FresnelSchlick(max(dot(h, v), 0.0), f0);
    float d = DistributionGGX(n, h, roughness);
    float g = GeometrySmith(n, v, l, roughness);
    vec3 specular = (d * g * f) / max(4.0 * nDotV * nDotL, 1e-4);
    vec3 diffuseWeight = (1.0 - f) * (1.0 - metallic);
    return (diffuseWeight * albedo / PI + specular) * radiance * nDotL;
}

void main()
{
    vec4 baseSample = texture(baseColorMap, vUV) * material.baseColorFactor;
    if (material.alphaMode == 1 && baseSample.a < material.alphaCutoff) discard;

    vec3 albedo = pow(max(baseSample.rgb, vec3(0.0)), vec3(2.2));
    vec4 mr = texture(metallicRoughnessMap, vUV);
    float metallic = clamp(material.metallicFactor * mr.b, 0.0, 1.0);
    float roughness = clamp(material.roughnessFactor * mr.g, 0.045, 1.0);
    float ao = mix(1.0, texture(occlusionMap, vUV).r, clamp(material.occlusionStrength, 0.0, 1.0));

    vec3 n = normalize(vWorldNormal);
    vec3 t = normalize(vWorldTangent.xyz - n * dot(n, vWorldTangent.xyz));
    vec3 b = normalize(cross(n, t)) * vWorldTangent.w;
    vec3 mappedNormal = texture(normalMap, vUV).xyz * 2.0 - 1.0;
    mappedNormal.xy *= material.normalScale;
    n = normalize(mat3(t, b, n) * mappedNormal);

    vec3 v = normalize(scene.cameraPosition.xyz - vWorldPosition);
    vec3 sunL = normalize(-scene.sunDirectionIntensity.xyz);
    vec3 direct = EvaluateLight(albedo, metallic, roughness, n, v, sunL,
                                scene.sunColor.rgb * scene.sunDirectionIntensity.w);

    for (uint i = 0u; i < min(lightCount, 64u); ++i)
    {
        vec3 toLight = lights[i].positionRadius.xyz - vWorldPosition;
        float distanceToLight = length(toLight);
        float radius = max(lights[i].positionRadius.w, 0.01);
        vec3 l = toLight / max(distanceToLight, 1e-4);
        float attenuation = pow(clamp(1.0 - distanceToLight / radius, 0.0, 1.0), 2.0);
        direct += EvaluateLight(albedo, metallic, roughness, n, v, l,
                                lights[i].colorIntensity.rgb * lights[i].colorIntensity.w * attenuation);
    }

    vec3 f0 = mix(vec3(0.04), albedo, metallic);
    vec3 reflection = reflect(-v, n);
    float maxLod = 8.0;
    vec3 irradiance = texture(irradianceMap, n).rgb;
    vec3 prefiltered = textureLod(prefilteredEnvironment, reflection, roughness * maxLod).rgb;
    vec2 brdf = texture(brdfLut, vec2(max(dot(n, v), 0.0), roughness)).rg;
    vec3 f = FresnelSchlick(max(dot(n, v), 0.0), f0);
    vec3 diffuseIBL = irradiance * albedo * (1.0 - metallic);
    vec3 specularIBL = prefiltered * (f * brdf.x + brdf.y);
    vec3 ambient = (diffuseIBL + specularIBL) * ao;
    vec3 emissive = pow(max(texture(emissiveMap, vUV).rgb, vec3(0.0)), vec3(2.2)) * material.emissiveFactor.rgb;

    vec3 hdr = direct + ambient + emissive;
    outColor = vec4(hdr, baseSample.a);
}
