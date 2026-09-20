///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2021, Advanced Micro Devices, Inc. All rights reserved.
// Licensed under the MIT License (AMD FidelityFX FSR 1.0 / RCAS).
//
// Contrast Adaptive Sharpening (CAS / RCAS)
// Algorithm: AMD FidelityFX Robust Contrast-Adaptive Sharpening, from FidelityFX FSR 1.0.
//   Reference implementation: https://github.com/GPUOpen-Effects/FidelityFX-FSR
//   License: MIT
// This is an independent HLSL implementation of the RCAS weighting kernel, adapted for the Unity
// PostProcessing Stack and TUFX. It is not a verbatim copy of the AMD source.
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

Shader "Hidden/TUFX/ContrastAdaptiveSharpening"
{
    HLSLINCLUDE
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"

        TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
        float4 _MainTex_TexelSize;
        float _Sharpness; // 0.0 (off) to 1.0 (max standard AMD CAS)
        float _DualScale; // 0.0 (off) or 1.0 (Dual-Scale Super-Sampling active)
        float _Overdrive; // 0.0 to 1.5 (additional overdrive boost)

        // AMD FidelityFX CAS (Contrast Adaptive Sharpening) Implementation with Dual-Scale Super-Sampling
        float4 Frag(VaryingsDefault i) : SV_Target
        {
            float2 uv = i.texcoord;

            // Fetch 3x3 cross neighborhood (Inner / Micro Scale)
            float3 a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-_MainTex_TexelSize.x, -_MainTex_TexelSize.y)).rgb;
            float3 b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0.0, -_MainTex_TexelSize.y)).rgb;
            float3 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(_MainTex_TexelSize.x, -_MainTex_TexelSize.y)).rgb;
            float3 d = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-_MainTex_TexelSize.x, 0.0)).rgb;
            float3 e = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb;
            float3 f = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(_MainTex_TexelSize.x, 0.0)).rgb;
            float3 g = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-_MainTex_TexelSize.x, _MainTex_TexelSize.y)).rgb;
            float3 h = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0.0, _MainTex_TexelSize.y)).rgb;
            float3 k = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(_MainTex_TexelSize.x, _MainTex_TexelSize.y)).rgb;

            // Min and Max of cross-filter
            float3 mnR = min(min(min(d, e), min(f, b)), h);
            float3 mxR = max(max(max(d, e), max(f, b)), h);

            // Include diagonal taps for wider range
            float3 mnR2 = min(min(min(mnR, a), min(c, g)), k);
            float3 mxR2 = max(max(max(mxR, a), max(c, g)), k);
            mnR = mnR + mnR2;
            mxR = mxR + mxR2;

            // Smooth minimum distance to limit
            float3 ampR = saturate(min(mnR, 2.0 - mxR) / max(mxR, 1e-4));
            
            // Shaping amount (Standard AMD CAS)
            float3 wR = sqrt(ampR) * (-0.125 * saturate(_Sharpness));

            // Safe denominator to prevent division by zero
            float3 denom = max(float3(0.05, 0.05, 0.05), 1.0 + 4.0 * wR);

            // Filter weight sum (Standard AMD CAS result)
            float3 result = (b * wR + d * wR + f * wR + h * wR + e) / denom;

            // Dual-Scale Super-Sampling & Overdrive Branch
            if (_DualScale > 0.5 && _Overdrive > 0.001)
            {
                float2 wideStep = _MainTex_TexelSize.xy * 1.75;

                // Outer structural taps for macro geometric features (trusses, seams, panel frames)
                float3 ob = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0.0, -wideStep.y)).rgb;
                float3 od = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-wideStep.x, 0.0)).rgb;
                float3 of = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2( wideStep.x, 0.0)).rgb;
                float3 oh = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0.0,  wideStep.y)).rgb;

                float3 outerCross = (ob + od + of + oh) * 0.25;
                float3 macroDiff = e - outerCross;

                // Edge-preserving contrast gating: avoid boosting noise/sky/fog
                float localLumaDiff = abs(dot(macroDiff, float3(0.2126, 0.7152, 0.0722)));
                float edgeGate = saturate(localLumaDiff * 6.0);

                // Add macro structural boost
                float3 boosted = result + macroDiff * (_Overdrive * 0.45 * edgeGate);

                // Anti-ringing clamp: keep within local 3x3 min/max envelope to avoid black/white halo artifacts
                result = clamp(boosted, mnR2 * 0.96, mxR2 * 1.04);
            }

            return float4(saturate(result), 1.0);
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
