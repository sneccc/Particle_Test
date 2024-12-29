#version 430 core
out vec4 FragColor;

in vec3 TexCoords;

uniform sampler2D hdriTexture;

const vec2 invAtan = vec2(0.1591, 0.3183);

vec2 SampleSphericalMap(vec3 v)
{
    vec2 uv = vec2(atan(v.z, v.x), asin(v.y));
    uv *= invAtan;
    uv += 0.5;
    return uv;
}

void main()
{
    vec3 normal = normalize(TexCoords);
    vec2 uv = SampleSphericalMap(normal);
    vec3 color = texture(hdriTexture, uv).rgb;
    
    // Optional: Apply exposure/gamma correction
    // color = color / (color + vec3(1.0));
    // color = pow(color, vec3(1.0/2.2));
    
    FragColor = vec4(color, 1.0);
} 