// Applies a 256-entry display gamma ramp, like the one the game sets and Xenia applies at present.
// Compiled to GammaRamp.mgfxo (embedded resource) with:
//   mgfxc GammaRamp.fx GammaRamp.mgfxo /Profile:DirectX_11

Texture2D SpriteTexture;
sampler2D SpriteTextureSampler = sampler_state
{
    Texture = <SpriteTexture>;
};

Texture2D RampTexture;
sampler2D RampSampler = sampler_state
{
    Texture = <RampTexture>;
};

struct VertexShaderOutput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float2 TextureCoordinates : TEXCOORD0;
};

float Lookup(float value)
{
    return tex2D(RampSampler, float2(value * (255.0 / 256.0) + (0.5 / 256.0), 0.5)).r;
}

float4 MainPS(VertexShaderOutput input) : COLOR
{
    float4 color = tex2D(SpriteTextureSampler, input.TextureCoordinates);
    return float4(Lookup(color.r), Lookup(color.g), Lookup(color.b), 1);
}

technique GammaRamp
{
    pass P0
    {
        PixelShader = compile ps_4_0_level_9_1 MainPS();
    }
};
