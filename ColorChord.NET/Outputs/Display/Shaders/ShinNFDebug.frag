#version 330 core

#define BINS_PER_OCATVE 24

in vec2 TexCoord;

uniform int BinCount;
uniform sampler2D Texture;
uniform float ScaleFactor;

out vec4 FragColor;

vec3 HSVToRGB(vec3 c)
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
}

void main()
{
    int SectionHere = int(floor(TexCoord.x * BinCount));
    float HeightHere = pow(texture(Texture, vec2((SectionHere + 0.5) / BinCount, 0.5)).r, 3.0) * ScaleFactor;
    vec3 Colour = AngleToRGB(float(SectionHere) / BINS_PER_OCATVE, 1);
    FragColor = vec4(step(abs(TexCoord.y), HeightHere) * Colour, 1.0);
}