Shader "Hidden/TUFX/ModernTonemapping"
{
    HLSLINCLUDE
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"

        TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
        int _Mode; // 0: AgX, 1: Tony McMapface, 2: Filmic
        float _Exposure;
        float _Saturation;

        // --- AgX Implementation ---
        // AgX inset matrix (sRGB to AgX log space working primaries)
        static const float3x3 AgX_Inset = float3x3(
            0.842479062253094, 0.0784335999999992, 0.0792237216490051,
            0.0423282422610123, 0.878468636469772, 0.0791661270344186,
            0.0423756549057051, 0.0784336, 0.879142973416954
        );

        // AgX outset matrix (AgX working space back to sRGB)
        static const float3x3 AgX_Outset = float3x3(
            1.19687900512017, -0.0528968517590771, -0.0529716355084654,
            -0.0980208811401368, 1.15190312990417, -0.0980434501171241,
            -0.0990297470797269, -0.0989611768448433, 1.15107367264152
        );

        float3 AgXDefaultContrastApprox(float3 val)
        {
            float3 x = val;
            float3 x2 = x * x;
            float3 x4 = x2 * x2;
            return + 15.5 * x4 * x2
                   - 40.14 * x4 * x
                   + 31.96 * x4
                   - 6.868 * x2 * x
                   + 0.4298 * x2
                   + 0.1191 * x
                   - 0.00232;
        }

        float3 EvaluateAgX(float3 color)
        {
            // Apply Inset
            color = mul(AgX_Inset, color);
            
            // Log2 encoding (min -10.0, max +6.5 EV)
            const float min_ev = -10.0;
            const float max_ev = 6.5;
            color = clamp(log2(max(color, 1e-5)), min_ev, max_ev);
            color = (color - min_ev) / (max_ev - min_ev);

            // Apply Sigmoid Contrast
            color = clamp(AgXDefaultContrastApprox(color), 0.0, 1.0);

            // Apply Outset
            color = mul(AgX_Outset, color);
            return saturate(color);
        }

        // --- Tony McMapface Implementation ---
        float3 EvaluateTonyMcMapface(float3 col)
        {
            // Tony McMapface display mapper by Tomasz Stachowiak
            float3 stimulus = max(col, 0.0);
            float3 p = stimulus * (stimulus + 0.5) / (stimulus * (stimulus + 0.15) + 0.35);
            return p;
        }

        // --- Filmic (Hejl-Burgess-Dawson approximation) ---
        float3 EvaluateFilmic(float3 col)
        {
            float3 x = max(float3(0.0, 0.0, 0.0), col - 0.004);
            return (x * (6.2 * x + 0.5)) / (x * (6.2 * x + 1.7) + 0.06);
        }

        float4 Frag(VaryingsDefault i) : SV_Target
        {
            float4 src = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            float3 hdr = max(float3(0, 0, 0), src.rgb * _Exposure);

            float3 ldr;
            if (_Mode == 0)
            {
                ldr = EvaluateAgX(hdr);
            }
            else if (_Mode == 1)
            {
                ldr = EvaluateTonyMcMapface(hdr);
            }
            else
            {
                ldr = EvaluateFilmic(hdr);
            }

            // Saturation adjustment
            float luma = dot(ldr, float3(0.2126, 0.7152, 0.0722));
            ldr = lerp(float3(luma, luma, luma), ldr, _Saturation);

            return float4(saturate(ldr), src.a);
        }
    ENDHLSL

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment Frag
            ENDHLSL
        }
    }
}
