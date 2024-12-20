#version 430 core
layout(location = 0) out vec4 FragColor;

uniform sampler2D particleTexture;

in InOutVars
{
    vec2 TexCoord;
    vec3 Color;
} inData;

void main()
{
    vec4 texColor = texture(particleTexture, inData.TexCoord);
    
    // Sharp cutoff for transparency
    if(texColor.a < 0.5)
        discard;
    
    // Force full opacity for non-discarded pixels
    FragColor = vec4(inData.Color * texColor.rgb, 1.0);
}