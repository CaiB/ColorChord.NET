#version 330 core
#extension GL_EXT_gpu_shader4 : enable

in vec2 TexCoord;

uniform int BinCount;
uniform float BinFreqSize;
uniform sampler2D TextureRawBins;
uniform float ScaleFactor;
uniform float Exponent;

out vec4 FragColor;

vec3 HSVToRGB(vec3 c)
{
    vec4 K = vec4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    vec3 p = abs(fract(c.xxx + K.xyz) * 6.0 - K.www);
    return c.z * mix(K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y);
}

vec3 AngleToRGB(float angle, float sat, float val)
{
    float Hue;
    Hue = (1.0 - step(4.0 / 12.0, angle)) * ((1.0 / 3.0) - angle) * 0.5; // Yellow -> Red
    Hue += (step(4.0 / 12.0, angle) - step(8.0 / 12.0, angle)) * (1 - (angle - (1.0 / 3.0))); // Red -> Blue
    Hue += step(8.0 / 12.0, angle) * ((2.0 / 3.0) - (1.5 * (angle - (2.0 / 3.0)))); // Blue -> Yellow
    return HSVToRGB(vec3(Hue, sat, val));
}

void main()
{
    //int SectionHere = int(floor(TexCoord.x * BinCount));
    float FreqHere = pow(2.0, (log2(55.0) + (TexCoord.x * (log2(3520.0) - log2(55.0)))));
    int SectionHere = int(round(FreqHere * BinFreqSize));

    float HeightHere = pow(texture(TextureRawBins, vec2((SectionHere + 0.5) / BinCount, 0.5)).r, 1.0) * ScaleFactor;
    //float ChangeHere = texture(TextureRawBins, vec2((SectionHere + 0.5) / BinCount, 0.75)).r * 10.0;
    //uint PeakHere = (texture(TexturePeakBits, vec2(((SectionHere / 8) + 0.5) / textureSize(TexturePeakBits, 0).x, 0.5)).r >> (SectionHere % 8)) & 1u;
    //uint WidebandHere = (texture(TextureWidebandBits, vec2(((SectionHere / 8) + 0.5) / textureSize(TextureWidebandBits, 0).x, 0.5)).r >> (SectionHere % 8)) & 1u;
    
    //vec3 Colour = AngleToRGB(mod(float(SectionHere) / BinsPerOctave, 1.0), 1.0 - (0.8 * WidebandHere), min(1.0, 0.1 + (HeightHere * 3.0)) - (0.9 * WidebandHere));
    vec3 Colour = vec3(0.8);

    float IsBar = step(abs(TexCoord.y), HeightHere);
    
    //FragColor = vec4((IsBar * Colour) + ((1.0 - IsBar) * vec3(0.1) * PeakHere * Colour), 1.0);
    FragColor = vec4((IsBar * Colour), 1.0);
    
}