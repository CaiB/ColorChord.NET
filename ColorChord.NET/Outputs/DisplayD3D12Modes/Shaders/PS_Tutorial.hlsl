struct PixelShaderInput
{
	float4 Color    : COLOR;
};

float4 Main( PixelShaderInput IN ) : SV_Target
{
    return IN.Color;
}