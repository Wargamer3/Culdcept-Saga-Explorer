float4x4 WorldViewProjection;
float RawOpacity;
int AlphaPass;
Texture2D ModelTexture;
sampler2D ModelSampler = sampler_state
{
    Texture = <ModelTexture>;
};

struct VertexInput
{
    float4 Position : POSITION0;
    float4 Color : COLOR0;
    float2 UV : TEXCOORD0;
};

struct VertexOutput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float2 UV : TEXCOORD0;
};

VertexOutput MainVS(VertexInput input)
{
    VertexOutput output;
    output.Position = mul(input.Position, WorldViewProjection);
    output.Color = input.Color;
    output.Color.a *= RawOpacity;
    output.UV = input.UV;
    return output;
}

float4 MainPS(VertexOutput input) : COLOR
{
    float4 texel = tex2D(ModelSampler, input.UV);
    float alpha = saturate(texel.a * input.Color.a);
    if (alpha <= 0 || (AlphaPass == 0 && alpha < 1) || (AlphaPass == 1 && alpha >= 1))
        discard;
    // Explorer textures are premultiplied; recover straight RGB before final premultiplication.
    float3 rgb = texel.a > 0 ? texel.rgb / texel.a : 0;
    return float4(rgb * input.Color.rgb * alpha, alpha);
}

technique RseRawOpacity
{
    pass P0
    {
        VertexShader = compile vs_4_0_level_9_1 MainVS();
        PixelShader = compile ps_4_0_level_9_1 MainPS();
    }
}
