#version 450

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec2 inUV;
layout(location = 3) in vec4 inTangent;

layout(set = 0, binding = 0, std140) uniform InfinitySceneData
{
    mat4 viewProjection;
    mat4 model;
    mat4 normalMatrix;
    vec4 cameraPosition;
    vec4 sunDirectionIntensity;
    vec4 sunColor;
} scene;

layout(location = 0) out vec3 vWorldPosition;
layout(location = 1) out vec3 vWorldNormal;
layout(location = 2) out vec2 vUV;
layout(location = 3) out vec4 vWorldTangent;

void main()
{
    vec4 world = scene.model * vec4(inPosition, 1.0);
    vWorldPosition = world.xyz;
    vWorldNormal = normalize(mat3(scene.normalMatrix) * inNormal);
    vWorldTangent = vec4(normalize(mat3(scene.model) * inTangent.xyz), inTangent.w);
    vUV = inUV;
    gl_Position = scene.viewProjection * world;
}
