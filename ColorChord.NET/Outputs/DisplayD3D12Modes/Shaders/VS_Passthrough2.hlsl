struct VertexPos
{
    float2 Position : POSITION;
};

struct VertexShaderOutput
{
    float4 Position : SV_Position;
};

VertexShaderOutput Main(VertexPos IN)
{
    VertexShaderOutput OUT;
    OUT.Position = float4(IN.Position, 0.0, 0.0);
    return OUT;
}