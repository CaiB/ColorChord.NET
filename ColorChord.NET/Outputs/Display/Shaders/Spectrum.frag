#version 330 core
#extension GL_EXT_gpu_shader4 : enable

in vec2 TexCoord;

uniform int BinCount;
uniform int BinsPerOctave;
uniform sampler2D TextureRawBins;
uniform usampler2D TexturePeakBits, TextureWidebandBits;
uniform float ScaleFactor;
uniform float Exponent;
uniform uint FeatureBits; // PeakHighlight >> 3, HideWideband >> 2, EnableColour >> 1, StartInCenter >> 0

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
    int SectionHere = int(floor(TexCoord.x * BinCount));
    float HeightHere = pow(texture(TextureRawBins, vec2((SectionHere + 0.5) / BinCount, 0.25)).r, Exponent) * ScaleFactor;
    //float ChangeHere = texture(TextureRawBins, vec2((SectionHere + 0.5) / BinCount, 0.75)).r * 10.0;
    uint PeakHere = (texture(TexturePeakBits, vec2(((SectionHere / 8) + 0.5) / textureSize(TexturePeakBits, 0).x, 0.5)).r >> (SectionHere % 8)) & 1u;
    uint PeakHereMasked = PeakHere & ((FeatureBits >> 3) & 1U);
    uint WidebandHere = (texture(TextureWidebandBits, vec2(((SectionHere / 8) + 0.5) / textureSize(TextureWidebandBits, 0).x, 0.5)).r >> (SectionHere % 8)) & 1u;
    uint WidebandHereMasked = WidebandHere & ((FeatureBits >> 2) & 1U);

    vec3 Colour = (((FeatureBits >> 1) & 1U) == 1U)
        ? AngleToRGB(mod(float(SectionHere) / BinsPerOctave, 1.0), 1.0 - (0.8 * WidebandHereMasked), min(1.0, 0.1 + (HeightHere * 3.0)) - (0.9 * WidebandHereMasked))
        : vec3(min(1.0, HeightHere * 3.0));
    
    bool PixelIsInBar = ((FeatureBits & 1U) == 1U)
        ? HeightHere >= abs(TexCoord.y)
        : HeightHere >= 1.0 - abs(TexCoord.y);

    FragColor = vec4(PixelIsInBar ? Colour : (vec3(0.1) * PeakHereMasked * Colour), 1.0);
}