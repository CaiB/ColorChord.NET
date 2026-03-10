#include "INC_Common.hlsl"

struct SpectrumConfig
{
    uint BinCount;
    uint BinsPerOctave;
    float ScaleFactor;
    float Exponent;
    uint FeatureBits;
    uint Width;
    uint Height;
};

ConstantBuffer<SpectrumConfig> Config : register(b0);

ByteAddressBuffer BinValues : register(t1);
//ConstantBuffer<uint> PeakBits : register(b2);
//ConstantBuffer<uint> WidebandBits : register(b3);

struct PixelShaderInput
{
    float4 Position : SV_Position;
};

float4 Main(PixelShaderInput IN) : SV_Target
{
    float2 Pos = ((IN.Position.xy - float2(0.5, 0.5)) / float2(Config.Width, Config.Height * 0.5)) - float2(0.0, 1.0); // transforms X,Y to (0~1),(-1~1)
    int SectionHere = (int)floor(Pos.x * (Config.BinCount - 1));
    float BinValue = asfloat(BinValues.Load(SectionHere * 4));
    float HeightHere = pow(BinValue, Config.Exponent) * Config.ScaleFactor;
    
    ////return float4(HeightHere > abs(Pos.y) ? float4(1, 0, 0, 1) : float4(0, 1, 0, 1));
    //uint PeakHere = PeakBits[SectionHere / 8] >> (SectionHere % 8) & 1U;
    //
    //
    //float HeightHere = pow(texture(TextureRawBins, float2((SectionHere + 0.5) / Config.BinCount, 0.25F)).r, Config.Exponent) * Config.ScaleFactor;
    ////float ChangeHere = texture(TextureRawBins, vec2((SectionHere + 0.5) / BinCount, 0.75)).r * 10.0;
    //uint PeakHere = (texture(TexturePeakBits, float2(((SectionHere / 8) + 0.5) / textureSize(TexturePeakBits, 0).x, 0.5)).r >> (SectionHere % 8)) & 1u;
    //uint PeakHereMasked = PeakHere & ((Config.FeatureBits >> 3) & 1U);
    //uint WidebandHere = (texture(TextureWidebandBits, float2(((SectionHere / 8) + 0.5) / textureSize(TextureWidebandBits, 0).x, 0.5)).r >> (SectionHere % 8)) & 1u;
    //uint WidebandHereMasked = WidebandHere & ((Config.FeatureBits >> 2) & 1U);
    
    uint PeakHereMasked = 0;
    uint WidebandHereMasked = 0;
    
    float HeightHereCapped = min(1.0, HeightHere * 3.0);
    //
    float3 Colour = (((Config.FeatureBits >> 1) & 1U) == 1U)
        ? AngleToRGB(fmod((float)SectionHere / Config.BinsPerOctave, 1.0), 1.0 - (0.8 * WidebandHereMasked), min(1.0, 0.1 + (HeightHere * 3.0)) - (0.9 * WidebandHereMasked))
        : float3(HeightHereCapped, HeightHereCapped, HeightHereCapped);
    
    bool PixelIsInBar = ((Config.FeatureBits & 1U) == 1U)
        ? HeightHere >= abs(Pos.y)
        : HeightHere >= 1.0 - abs(Pos.y);
    
    return float4(PixelIsInBar ? Colour : (float3(0.1, 0.1, 0.1) * PeakHereMasked * Colour), 1.0);
}