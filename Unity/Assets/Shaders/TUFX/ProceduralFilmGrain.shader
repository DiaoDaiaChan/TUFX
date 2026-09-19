Shader "Hidden/TUFX/GrainBaker"
{
    HLSLINCLUDE

        #pragma exclude_renderers d3d11_9x
        #pragma target 3.0
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"

        float _Phase;
        float3 _NoiseParameters; // x: rndOffsetX, y: rndOffsetY, z: scale

        // Dave Hoskins high-precision hash without sine
        float ProceduralHash(float2 p, float seed)
        {
            float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973) + seed);
            p3 += dot(p3, p3.yzx + 33.33);
            return frac((p3.x + p3.y) * p3.z) * 2.0 - 1.0;
        }

        // 5-tap discrete Laplacian high-pass filter: generates uniform blue noise
        float BlueNoise(float2 p, float seed)
        {
            float c = ProceduralHash(p, seed);
            float l = ProceduralHash(p + float2(-1.0,  0.0), seed);
            float r = ProceduralHash(p + float2( 1.0,  0.0), seed);
            float t = ProceduralHash(p + float2( 0.0,  1.0), seed);
            float b = ProceduralHash(p + float2( 0.0, -1.0), seed);

            // Laplacian kernel suppresses low-frequency clumping, creating organic film grain
            float highPass = c - 0.25 * (l + r + t + b);
            return clamp(highPass * 1.5, -1.0, 1.0);
        }

        // Pass 0: Monochrome Silver Halide Grain
        float4 FragBW(VaryingsDefault i) : SV_Target
        {
            float2 p = floor(i.texcoordStereo * float2(128.0, 128.0));
            float seed = frac(_Phase * 0.61803398875);
            float grain = BlueNoise(p, seed);
            return float4(grain.xxx, 1.0);
        }

        // Pass 1: 3-Channel Organic Color Film Grain
        float4 FragColored(VaryingsDefault i) : SV_Target
        {
            float2 p = floor(i.texcoordStereo * float2(128.0, 128.0));
            float seed = frac(_Phase * 0.61803398875);

            float gR = BlueNoise(p, seed + 0.1337);
            float gG = BlueNoise(p, seed + 0.4242);
            float gB = BlueNoise(p, seed + 0.7777);

            return float4(gR, gG, gB, 1.0);
        }

    ENDHLSL

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // 0: Monochrome
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragBW
            ENDHLSL
        }

        // 1: Colored
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragColored
            ENDHLSL
        }
    }
}
