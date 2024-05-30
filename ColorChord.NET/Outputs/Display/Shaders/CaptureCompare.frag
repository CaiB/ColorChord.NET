#version 330 core

in vec2 TexCoord;
out vec4 FragColor;

uniform sampler2D TextureUnitALive, TextureUnitBLive, TextureUnitACapture, TextureUnitBCapture;

uniform float HorizontalSplit;

uniform float AdvanceALive, AdvanceBLive, AdvanceACapture, AdvanceBCapture;

/*vec3 HSVToRGB(vec3 c)
{
    vec4 K = vec4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    vec3 p = abs(fract(c.xxx + K.xyz) * 6.0 - K.www);
    return c.z * mix(K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y);
}

vec3 AngleToRGB(float angle, float val)
{
    float Hue;
    Hue = (1.0 - step(4.0 / 12.0, angle)) * ((1.0 / 3.0) - angle) * 0.5; // Yellow -> Red
    Hue += (step(4.0 / 12.0, angle) - step(8.0 / 12.0, angle)) * (1 - (angle - (1.0 / 3.0))); // Red -> Blue
    Hue += step(8.0 / 12.0, angle) * ((2.0 / 3.0) - (1.5 * (angle - (2.0 / 3.0)))); // Blue -> Yellow
    return HSVToRGB(vec3(Hue, 1.0, val));
}*/

void main()
{
    vec4 TextureColour;
    if (TexCoord.x < HorizontalSplit) // Capture
    {
        if (TexCoord.y < 0.5) // A
        {
            TextureColour = texture(TextureUnitACapture, (vec2(1.0 - TexCoord.y, TexCoord.x) - vec2(0.0, -AdvanceACapture / 2.0)) * vec2(1.0 / HorizontalSplit, 2.0));
        }
        else // B
        {
            TextureColour = texture(TextureUnitBCapture, (vec2(0.5 - TexCoord.y, TexCoord.x) - vec2(0.0, -AdvanceBCapture / 2.0)) * vec2(1.0 / HorizontalSplit, 2.0));
        }
    }
    else // Live
    {
        if (TexCoord.y < 0.5) // A
        {
            TextureColour = texture(TextureUnitALive, (vec2(1.0 - TexCoord.y, TexCoord.x) - vec2(0.0, HorizontalSplit - (AdvanceALive / 2.0))) * vec2(2.0, 1.0 / (1.0 - HorizontalSplit)));
        }
        else // B
        {
            TextureColour = texture(TextureUnitBLive, (TexCoord - vec2(HorizontalSplit, 0.5)) * vec2(1.0 / (1.0 - HorizontalSplit), 2.0));
        }
    }
    
    FragColor = vec4(TextureColour.rgb, 1.0);

}