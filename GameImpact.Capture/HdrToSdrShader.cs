namespace GameImpact.Capture
{
    /// <summary>HDR→SDR Compute Shader（Hable/Uncharted2 Tone Mapping + sRGB Gamma）</summary>
    public static class HdrToSdrShader
    {
        /// <summary>HLSL Compute Shader 源码</summary>
        public const string ShaderSource = @"
// Hable/Uncharted2 Tone Mapping 参数
static const float A = 0.15;  // Shoulder Strength
static const float B = 0.50;  // Linear Strength
static const float C = 0.10;  // Linear Angle
static const float D = 0.20;  // Toe Strength
static const float E = 0.02;  // Toe Numerator
static const float F = 0.30;  // Toe Denominator
static const float W = 11.2;  // White Point

float HableFunc(float x)
{
    return ((x * (A * x + C * B) + D * E) / (x * (A * x + B) + D * F)) - E / F;
}

float3 HableTonemap(float3 col)
{
    float whiteScale = 1.0 / HableFunc(W);
    return float3(
        HableFunc(col.r) * whiteScale,
        HableFunc(col.g) * whiteScale,
        HableFunc(col.b) * whiteScale
    );
}

float LinearToSrgb(float val)
{
    return (val <= 0.0031308) ? (val * 12.92) : (1.055 * pow(val, 1.0 / 2.4) - 0.055);
}

float3 LinearToSrgb3(float3 val)
{
    return float3(LinearToSrgb(val.r), LinearToSrgb(val.g), LinearToSrgb(val.b));
}

Texture2D<float4> InputTexture : register(t0);
RWTexture2D<float4> OutputTexture : register(u0);

[numthreads(8, 8, 1)]
void CSMain(uint3 dtid : SV_DispatchThreadID)
{
    float4 hdr = InputTexture.Load(int3(dtid.xy, 0));
    hdr.rgb = max(hdr.rgb, 0.0);
    
    float3 sdr = LinearToSrgb3(HableTonemap(hdr.rgb));
    OutputTexture[dtid.xy] = float4(saturate(sdr), 1.0);
}
";
    }
}
