#version 430 core

uniform sampler2DArray particleTextures;

in InOutVars
{
    vec2 TexCoord;
    float TextureLayer;
    vec3 Color;
} inData;

out vec4 FragColor;

void main()
{
    vec4 texColor = texture(particleTextures, vec3(inData.TexCoord, inData.TextureLayer));
    FragColor = vec4(texColor.rgb * inData.Color, texColor.a);
}