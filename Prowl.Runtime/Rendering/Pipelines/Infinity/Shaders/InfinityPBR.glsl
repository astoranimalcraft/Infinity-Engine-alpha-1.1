// Infinity PBR include library (GLSL 450 compatible).
// Include this in the material fragment shader after declaring material/light inputs.
#ifndef INFINITY_PBR_GLSL
#define INFINITY_PBR_GLSL

const float INFINITY_PI = 3.14159265358979323846;

vec3 InfinityFresnelSchlick(float cosTheta, vec3 F0)
{
    return F0 + (1.0 - F0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}

vec3 InfinityFresnelSchlickRoughness(float cosTheta, vec3 F0, float roughness)
{
    return F0 + (max(vec3(1.0 - roughness), F0) - F0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}

float InfinityDistributionGGX(vec3 N, vec3 H, float roughness)
{
    float a = max(roughness * roughness, 0.0025);
    float a2 = a * a;
    float NdotH = max(dot(N, H), 0.0);
    float NdotH2 = NdotH * NdotH;
    float denominator = NdotH2 * (a2 - 1.0) + 1.0;
    return a2 / max(INFINITY_PI * denominator * denominator, 1e-6);
}

float InfinityGeometrySchlickGGX(float NdotV, float roughness)
{
    float r = roughness + 1.0;
    float k = (r * r) / 8.0;
    return NdotV / max(NdotV * (1.0 - k) + k, 1e-6);
}

float InfinityGeometrySmith(vec3 N, vec3 V, vec3 L, float roughness)
{
    return InfinityGeometrySchlickGGX(max(dot(N, V), 0.0), roughness)
         * InfinityGeometrySchlickGGX(max(dot(N, L), 0.0), roughness);
}

// Cook-Torrance direct BRDF. radiance is light colour multiplied by its attenuation.
vec3 InfinityEvaluateDirectBRDF(vec3 albedo, float metallic, float roughness,
                                vec3 N, vec3 V, vec3 L, vec3 radiance)
{
    N = normalize(N); V = normalize(V); L = normalize(L);
    vec3 H = normalize(V + L);
    float NdotL = max(dot(N, L), 0.0);
    float NdotV = max(dot(N, V), 0.0);
    if (NdotL <= 0.0 || NdotV <= 0.0) return vec3(0.0);

    metallic = clamp(metallic, 0.0, 1.0);
    roughness = clamp(roughness, 0.045, 1.0);
    vec3 F0 = mix(vec3(0.04), albedo, metallic);
    vec3 F = InfinityFresnelSchlick(max(dot(H, V), 0.0), F0);
    float D = InfinityDistributionGGX(N, H, roughness);
    float G = InfinityGeometrySmith(N, V, L, roughness);
    vec3 specular = (D * G * F) / max(4.0 * NdotV * NdotL, 1e-5);
    vec3 kD = (vec3(1.0) - F) * (1.0 - metallic);
    return (kD * albedo / INFINITY_PI + specular) * radiance * NdotL;
}

vec3 InfinityEvaluateIBLDiffuse(vec3 albedo, float metallic, vec3 N, vec3 V,
                                vec3 irradiance, float ao)
{
    vec3 F0 = mix(vec3(0.04), albedo, clamp(metallic, 0.0, 1.0));
    vec3 F = InfinityFresnelSchlickRoughness(max(dot(N, V), 0.0), F0, 1.0);
    vec3 kD = (vec3(1.0) - F) * (1.0 - metallic);
    return kD * irradiance * albedo * clamp(ao, 0.0, 1.0);
}

#endif
