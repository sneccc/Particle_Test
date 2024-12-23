#version 430 core
#define EPSILON 0.001
const float DRAG_COEF = log(0.998) * 176.0;
const int ATLAS_SIZE = 4096;
const int TEXTURE_SIZE = 256;
const int TEXTURES_PER_ROW = ATLAS_SIZE / TEXTURE_SIZE;
const int TEXTURES_PER_ATLAS = (ATLAS_SIZE / TEXTURE_SIZE) * (ATLAS_SIZE / TEXTURE_SIZE);

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
layout(location = 7) uniform int numTextures;

out InOutVars
{
    vec2 TexCoord;
    vec3 Color;
    float TextureLayer;
} outData;

vec3 PackedVec3ToVec3(PackedVector3 vec);
PackedVector3 Vec3ToPackedVec3(vec3 vec);

void getAtlasUV(int textureIndex, out vec2 uvMin, out vec2 uvMax, out float layer)
{
    // Ensure texture index is within bounds
    textureIndex = textureIndex % numTextures;
    
    // Calculate which atlas layer and position within the layer
    int atlasLayer = textureIndex / TEXTURES_PER_ATLAS;
    int localIndex = textureIndex % TEXTURES_PER_ATLAS;
    
    float texSize = float(TEXTURE_SIZE) / float(ATLAS_SIZE);
    float atlasX = float(localIndex % TEXTURES_PER_ROW) * texSize;
    float atlasY = float(localIndex / TEXTURES_PER_ROW) * texSize;
    
    uvMin = vec2(atlasX, atlasY);
    uvMax = vec2(atlasX + texSize, atlasY + texSize);
    layer = float(atlasLayer);
}

void main()
{
    // Calculate which particle and which vertex of the quad we're processing
    int particleIndex = gl_VertexID >> 2;
    int cornerIndex = gl_VertexID & 3;
    
    // Quad corners
    vec2 corners[4] = vec2[4](
        vec2(-1.0, -1.0),
        vec2( 1.0, -1.0),
        vec2(-1.0,  1.0),
        vec2( 1.0,  1.0)
    );
    
    vec2 corner = corners[cornerIndex] * particleSize;

    // Calculate texture coordinates from atlas
    vec2 uvMin, uvMax;
    float layer;
    getAtlasUV(particleIndex, uvMin, uvMax, layer);
    
    // Map corner index to atlas UV coordinates
    vec2 texCoords[4] = vec2[4](
        vec2(uvMin.x, uvMax.y),
        vec2(uvMax.x, uvMax.y),
        vec2(uvMin.x, uvMin.y),
        vec2(uvMax.x, uvMin.y)
    );
    
    outData.TexCoord = texCoords[cornerIndex];
    outData.TextureLayer = layer;

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

    // Use neutral white color to preserve original image colors
    outData.Color = vec3(1.0);

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

uint hash(uint x)
{
    x += (x << 10u);
    x ^= (x >>  6u);
    x += (x <<  3u);
    x ^= (x >> 11u);
    x += (x << 15u);
    return x;
}