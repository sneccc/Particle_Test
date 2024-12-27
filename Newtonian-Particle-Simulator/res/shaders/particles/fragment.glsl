#version 430 core
layout(location = 0) out vec4 FragColor;

uniform sampler2DArray particleTextures;

in InOutVars
{
    vec2 TexCoord;
    vec3 Color;
    float TextureLayer;
    bool IsSelected;
} inData;

void main()
{
    vec4 texColor = texture(particleTextures, vec3(inData.TexCoord, inData.TextureLayer));
    
    // Softer alpha cutoff
    if(texColor.a < 0.1)
        discard;
    
    vec3 finalColor = texColor.rgb * inData.Color;
    
    // Make selection effect more obvious
    if(inData.IsSelected) {
        finalColor = vec3(1.0, 0.0, 0.0);  // Pure red
        texColor.a = 1.0;  // Full opacity
    }
    
    // Add glow effect for selected particles
    if(inData.IsSelected) {
        float glowStrength = 1.5;  // Increase brightness for selected particles
        finalColor *= glowStrength;
    }
    
    FragColor = vec4(finalColor, texColor.a);
}