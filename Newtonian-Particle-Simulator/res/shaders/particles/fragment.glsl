#version 430 core
layout(location = 0) out vec4 FragColor;

uniform sampler2DArray particleTextures;

in InOutVars
{
    vec2 TexCoord;
    vec3 Color;
    float TextureLayer;
} inData;

void main()
{
    vec4 texColor = texture(particleTextures, vec3(inData.TexCoord, inData.TextureLayer));
    
    // Sharp cutoff for transparency
    if(texColor.a < 0.5)
        discard;
    
    FragColor = vec4(texColor.rgb * inData.Color, 1.0);
}