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
    
    // Softer alpha cutoff
    if(texColor.a < 0.1)
        discard;
    
    // Preserve more of the original image color, just add a slight warm tint
    //vec3 warmTint = vec3(1.0, 0.95, 0.9);
    vec3 finalColor = texColor.rgb * inData.Color;
    
    // Optional: Add a bit of gamma correction to brighten mid-tones
    finalColor = pow(finalColor, vec3(1.0));
    
    FragColor = vec4(finalColor, texColor.a);
}