Shader "Hidden/TUFX/SubsurfaceScattering"
{
    HLSLINCLUDE
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"

        TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
        TEXTURE2D_SAMPLER2D(_CameraDepthTexture, sampler_CameraDepthTexture);
        TEXTURE2D(_SSSSIntermediate);

        float4 _MainTex_TexelSize;
        float _ScatterRadius;
        float _Intensity;
        float _DepthThreshold;
        float _MaxDistance;
        float4 _SubsurfaceColor;

        // Jimenez 6-tap separable subsurface kernel weights
        static const float kernelOffsets[6] = { 0.0, 0.05, 0.12, 0.26, 0.58, 1.0 };
        static const float3 kernelWeights[6] = {
            float3(0.24, 0.24, 0.24),
            float3(0.32, 0.17, 0.10),
            float3(0.20, 0.18, 0.15),
            float3(0.12, 0.16, 0.20),
            float3(0.08, 0.13, 0.25),
            float3(0.04, 0.12, 0.06)
        };

        // Pass 0: Horizontal Diffusion Pass (Distance-Gated)
        float4 FragSSSSHorizontal(VaryingsDefault i) : SV_Target
        {
            float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord);
            #if UNITY_REVERSED_Z
                if (rawDepth <= 0.00005) return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            #else
                if (rawDepth >= 0.99995) return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            #endif

            float centerDepth = LinearEyeDepth(rawDepth);
            // Strictly gate SSSS to close-up organic range (Kerbals / close surface details).
            // Landscape, terrain, and distant launchpad remain 100% untouched and sharp!
            if (centerDepth <= 0.05 || centerDepth > _MaxDistance)
            {
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            }

            float4 centerCol = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            float scale = (_ScatterRadius / max(0.5, centerDepth)) * _MainTex_TexelSize.x * 1.5;

            float3 totalColor = centerCol.rgb * kernelWeights[0];
            float3 totalWeight = kernelWeights[0];

            [unroll]
            for (int k = 1; k < 6; k++)
            {
                float offset = kernelOffsets[k] * scale;
                float2 uvL = i.texcoord + float2(-offset, 0.0);
                float2 uvR = i.texcoord + float2( offset, 0.0);

                float depthL = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, uvL));
                float depthR = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, uvR));

                float wL = exp(-abs(depthL - centerDepth) / max(0.02, _DepthThreshold));
                float wR = exp(-abs(depthR - centerDepth) / max(0.02, _DepthThreshold));

                float3 tapColL = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvL).rgb;
                float3 tapColR = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvR).rgb;

                totalColor += tapColL * (kernelWeights[k] * wL) + tapColR * (kernelWeights[k] * wR);
                totalWeight += kernelWeights[k] * (wL + wR);
            }

            float3 blurred = totalColor / max(float3(0.0001, 0.0001, 0.0001), totalWeight);
            return float4(blurred, centerCol.a);
        }

        // Pass 1: Vertical Diffusion & Composite Pass (Distance-Gated)
        float4 FragSSSSVertical(VaryingsDefault i) : SV_Target
        {
            float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord);
            #if UNITY_REVERSED_Z
                if (rawDepth <= 0.00005) return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            #else
                if (rawDepth >= 0.99995) return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            #endif

            float centerDepth = LinearEyeDepth(rawDepth);
            if (centerDepth <= 0.05 || centerDepth > _MaxDistance)
            {
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            }

            float4 originalCol = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            float4 centerCol = SAMPLE_TEXTURE2D(_SSSSIntermediate, sampler_MainTex, i.texcoord);
            float scale = (_ScatterRadius / max(0.5, centerDepth)) * _MainTex_TexelSize.y * 1.5;

            float3 totalColor = centerCol.rgb * kernelWeights[0];
            float3 totalWeight = kernelWeights[0];

            [unroll]
            for (int k = 1; k < 6; k++)
            {
                float offset = kernelOffsets[k] * scale;
                float2 uvT = i.texcoord + float2(0.0,  offset);
                float2 uvB = i.texcoord + float2(0.0, -offset);

                float depthT = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, uvT));
                float depthB = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, uvB));

                float wT = exp(-abs(depthT - centerDepth) / max(0.02, _DepthThreshold));
                float wB = exp(-abs(depthB - centerDepth) / max(0.02, _DepthThreshold));

                float3 tapColT = SAMPLE_TEXTURE2D(_SSSSIntermediate, sampler_MainTex, uvT).rgb;
                float3 tapColB = SAMPLE_TEXTURE2D(_SSSSIntermediate, sampler_MainTex, uvB).rgb;

                totalColor += tapColT * (kernelWeights[k] * wT) + tapColB * (kernelWeights[k] * wB);
                totalWeight += kernelWeights[k] * (wT + wB);
            }

            float3 blurred = totalColor / max(float3(0.0001, 0.0001, 0.0001), totalWeight);
            float3 sssColor = blurred * _SubsurfaceColor.rgb;

            // Distance smooth fadeout to 0 when approaching _MaxDistance
            float distFade = saturate((_MaxDistance - centerDepth) / max(1.0, _MaxDistance * 0.25));
            float3 result = lerp(originalCol.rgb, sssColor, _Intensity * distFade);
            return float4(result, originalCol.a);
        }
    ENDHLSL

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // 0: Horizontal Blur
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragSSSSHorizontal
            ENDHLSL
        }

        // 1: Vertical Blur & Composite
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragSSSSVertical
            ENDHLSL
        }
    }
}
