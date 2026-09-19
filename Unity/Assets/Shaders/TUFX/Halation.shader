Shader "Hidden/TUFX/Halation"
{
    HLSLINCLUDE
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"

        TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
        TEXTURE2D_SAMPLER2D(_HalationTightTex, sampler_HalationTightTex);
        TEXTURE2D_SAMPLER2D(_HalationWideTex, sampler_HalationWideTex);
        float4 _MainTex_TexelSize;
        float _Threshold;
        float _Intensity;
        float _Radius;
        float4 _ColorTint; // CineStill warm orange-red: float4(1.0, 0.28, 0.10, 1.0)

        // Pass 0: Soft-Knee Threshold Extraction & Red Emulsion Tinting
        float4 FragExtract(VaryingsDefault i) : SV_Target
        {
            float4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            float luma = dot(color.rgb, float3(0.2126, 0.7152, 0.0722));

            // Soft-knee threshold to prevent harsh cutoffs
            float val = max(0.0, luma - _Threshold);
            float factor = saturate(val / max(0.1, _Threshold * 0.6));

            // Red layer penetration in film base
            float3 halationSeed = color.rgb * _ColorTint.rgb * factor;
            return float4(halationSeed, 1.0);
        }

        // Pass 1: Horizontal Gaussian Diffusion (9-tap normalized)
        float4 FragBlurH(VaryingsDefault i) : SV_Target
        {
            static const float weights[5] = { 0.2270270, 0.1945946, 0.1216216, 0.0540541, 0.0162162 };
            float stepX = _MainTex_TexelSize.x * _Radius;

            float3 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord).rgb * weights[0];

            [unroll]
            for (int t = 1; t <= 4; t++)
            {
                float w = weights[t];
                float offset = float(t) * stepX;
                float3 s1 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord + float2(offset, 0.0)).rgb;
                float3 s2 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord - float2(offset, 0.0)).rgb;
                col += (s1 + s2) * w;
            }

            return float4(col, 1.0);
        }

        // Pass 2: Vertical Gaussian Diffusion (9-tap normalized)
        float4 FragBlurV(VaryingsDefault i) : SV_Target
        {
            static const float weights[5] = { 0.2270270, 0.1945946, 0.1216216, 0.0540541, 0.0162162 };
            float stepY = _MainTex_TexelSize.y * _Radius;

            float3 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord).rgb * weights[0];

            [unroll]
            for (int t = 1; t <= 4; t++)
            {
                float w = weights[t];
                float offset = float(t) * stepY;
                float3 s1 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord + float2(0.0, offset)).rgb;
                float3 s2 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord - float2(0.0, offset)).rgb;
                col += (s1 + s2) * w;
            }

            return float4(col, 1.0);
        }

        // Pass 3: Dual-Scale Halation Composite onto Scene
        float4 FragComposite(VaryingsDefault i) : SV_Target
        {
            float4 scene = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            float3 tightGlow = SAMPLE_TEXTURE2D(_HalationTightTex, sampler_HalationTightTex, i.texcoord).rgb;
            float3 wideBleed = SAMPLE_TEXTURE2D(_HalationWideTex, sampler_HalationWideTex, i.texcoord).rgb;

            // CineStill 800T characteristic: sharp red boundary glow + broad warm halo
            float3 halation = (tightGlow * 0.5 + wideBleed * 0.75) * (_Intensity * _ColorTint.rgb);
            return float4(scene.rgb + halation, scene.a);
        }
    ENDHLSL

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // 0: Extract
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragExtract
            ENDHLSL
        }

        // 1: Blur H
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragBlurH
            ENDHLSL
        }

        // 2: Blur V
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragBlurV
            ENDHLSL
        }

        // 3: Composite
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragComposite
            ENDHLSL
        }
    }
}
