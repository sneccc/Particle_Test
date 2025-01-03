#version 430 core
#define EPSILON 0.001
const float DRAG_COEF = log(0.998) * 176.0;

struct PackedVector3
{
    float X, Y, Z;
};

struct Particle
{
    PackedVector3 Position;
    PackedVector3 Velocity;
};

layout(std430, binding = 0) restrict buffer ParticlesSSBO
{
    Particle Particles[];
} particleSSBO;

layout(location = 0) uniform float dT;
layout(location = 1) uniform vec3 pointOfMass;
layout(location = 2) uniform float isActive;
layout(location = 3) uniform float isRunning;
layout(location = 4) uniform mat4 projViewMatrix;
layout(location = 5) uniform float particleSize;
layout(location = 6) uniform vec3 cameraPos;

out InOutVars
{
    vec2 TexCoord;
    float TextureLayer;
    vec3 Color;
} outData;

vec3 PackedVec3ToVec3(PackedVector3 vec);
PackedVector3 Vec3ToPackedVec3(vec3 vec);

void main()
{
    // Calculate which particle and which vertex of the quad we're processing
    int particleIndex = gl_VertexID >> 2;
    int cornerIndex = gl_VertexID & 3;
    
    // Quad corners and texture coordinates
    vec2 corners[4] = vec2[4](
        vec2(-1.0, -1.0),
        vec2( 1.0, -1.0),
        vec2(-1.0,  1.0),
        vec2( 1.0,  1.0)
    );
    vec2 texCoords[4] = vec2[4](
        vec2(0.0, 1.0),
        vec2(1.0, 1.0),
        vec2(0.0, 0.0),
        vec2(1.0, 0.0)
    );
    
    vec2 corner = corners[cornerIndex];
    outData.TexCoord = texCoords[cornerIndex];
    outData.TextureLayer = float(particleIndex % 4);

    // Get particle data
    PackedVector3 packedPosition = particleSSBO.Particles[particleIndex].Position;
    PackedVector3 packedVelocity = particleSSBO.Particles[particleIndex].Velocity;
    vec3 position = PackedVec3ToVec3(packedPosition);
    vec3 velocity = PackedVec3ToVec3(packedVelocity);

    // Physics calculations
    vec3 toMass = pointOfMass - position;
    float m1 = 1.0;
    float m2 = 176.0;
    float G  = 1.0;
    float m1_m2 = m1 * m2;
    float rSquared = max(dot(toMass, toMass), EPSILON * EPSILON);
    vec3 force = toMass * (G * ((m1_m2) / rSquared));
    vec3 acceleration = (force * isRunning * isActive) / m1;

    velocity *= mix(1.0, exp(DRAG_COEF * dT), isRunning);
    position += (dT * velocity + 0.5 * acceleration * dT * dT) * isRunning;
    velocity += acceleration * dT;

    particleSSBO.Particles[particleIndex].Position = Vec3ToPackedVec3(position);
    particleSSBO.Particles[particleIndex].Velocity = Vec3ToPackedVec3(velocity);

    // Corrected Billboard calculation
    vec3 toCamera = normalize(cameraPos - position);
    vec3 worldUp = vec3(0.0, 1.0, 0.0);
    vec3 right = normalize(cross(worldUp, toCamera));
    vec3 up = cross(toCamera, right);
    
    vec3 finalPosition = position + (right * corner.x + up * corner.y) * particleSize;

    // Fixed bright base color independent of speed
    vec3 baseColor = vec3(1.0, 0.9, 0.7); // Bright warm white base
    
    // Add subtle velocity influence
    float speed = length(velocity);
    float speedInfluence = min(speed * 0.01, 0.3); // Limited speed influence
    
    // Warm tint for highlights
    vec3 warmColor = vec3(1.0, 0.8, 0.4);
    
    // Combine colors: start with bright base, add warm highlights
    outData.Color = mix(baseColor, warmColor, speedInfluence);

    gl_Position = projViewMatrix * vec4(finalPosition, 1.0);
}

vec3 PackedVec3ToVec3(PackedVector3 vec)
{
    return vec3(vec.X, vec.Y, vec.Z);
}

PackedVector3 Vec3ToPackedVec3(vec3 vec)
{
    return PackedVector3(vec.x, vec.y, vec.z);
}