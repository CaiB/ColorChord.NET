#include "INC_Common.hlsl"

struct RadialPolesConfig
{
    uint Width;
    uint Height;
    uint PoleCount;
    float Advance;
    float ScaleFactor;
    float Exponent;
    float CenterBlank;
    
    uint FeatureBits; // unused
};

ConstantBuffer<RadialPolesConfig> Config : register(b0);

ByteAddressBuffer BinValues : register(t1);

struct PixelShaderInput
{
    float4 Position : SV_Position;
};

float4 Main(PixelShaderInput inVal) : SV_Target
{
    float2 Pos = ((inVal.Position.xy - float2(0.5, 0.5)) / float2(Config.Width * 0.5, Config.Height * 0.5)) - float2(1.0, 1.0); // transforms X,Y to (-1~1),(-1~1)
    float Angle = fmod(((atan2(-Pos.y, -Pos.x) + PI) * REC_HALF_PI) + Config.Advance, 1.0);
    float Radius = distance(0, Pos) * 0.7;

    int SectionHere = (int)floor(Angle * Config.PoleCount);
    float BinValue = asfloat(BinValues.Load(SectionHere * 4));
    float HeightHere = pow(BinValue, Config.Exponent) * Config.ScaleFactor;

    float3 Colour = AngleToRGB((float)SectionHere / Config.PoleCount, 1.0, 1.0);
    return float4(Colour * (1.0 - step(HeightHere + Config.CenterBlank, Radius)) * step(Config.CenterBlank, Radius), 1.0);
}