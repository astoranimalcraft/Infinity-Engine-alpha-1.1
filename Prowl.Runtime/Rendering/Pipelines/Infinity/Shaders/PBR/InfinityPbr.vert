#version 450

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec2 inUV;
layout(location = 3) in vec4 inTangent;

layout(set = 0, binding = 0, std140) uniform InfinitySceneData
{
    mat4 viewProjection;
    vec4 cameraPosition;
    vec4 directionalDirectionIntensity;
    vec4 directionalColor;
    vec4 ambientColor;
    vec4 environmentInfo; // x: irradiance intensity, y: specular intensity, z: max prefiltered mip
    uvec4 lightCounts;     // x: number of local lights
} scene;

layout(push_constant, std430) uniform InfinityObjectData
{
    mat4 model;
    mat4 normalMatrix;
} objectData;

layout(location = 0) out vec3 vWorldPosition;
layout(location = 1) out vec3 vWorldNormal;
layout(location = 2) out vec2 vUV;
layout(location = 3) out vec4 vWorldTangent;

void main()
{
    vec4 worldPosition = objectData.model * vec4(inPosition, 1.0);
    mat3 normalTransform = mat3(objectData.normalMatrix);

    vec3 N = normalize(normalTransform * inNormal);
    vec3 T = mat3(objectData.model) * inTangent.xyz;

    if (dot(T, T) < 1e-8)
    {
        vec3 axis = abs(N.y) < 0.999
            ? vec3(0.0, 1.0, 0.0)
            : vec3(1.0, 0.0, 0.0);
        T = cross(axis, N);
    }

    T = normalize(T - dot(T, N) * N);
    float handedness = abs(inTangent.w) < 1e-5 ? 1.0 : sign(inTangent.w);

    vWorldPosition = worldPosition.xyz;
    vWorldNormal = N;
    vWorldTangent = vec4(T, handedness);
    vUV = inUV;

    gl_Position = scene.viewProjection * worldPosition;
}
