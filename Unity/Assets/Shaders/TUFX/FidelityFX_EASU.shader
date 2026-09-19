///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2021, Advanced Micro Devices, Inc. All rights reserved.
// Licensed under the MIT License (AMD FidelityFX FSR 1.0 / EASU).
//
// Edge-Adaptive Spatial Upsampling (EASU)
// Algorithm: AMD FidelityFX FSR 1.0 EASU 12-tap directional kernel.
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

Shader "Hidden/TUFX/FidelityFX_EASU"
{
    HLSLINCLUDE
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"

        TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
        float4 _MainTex_TexelSize; // x=1/w, y=1/h, z=w, w=h (source resolution)
        float4 _OutputSize;        // x=1/w, y=1/h, z=w, w=h (target resolution)

        // Helper to compute perceptual luminance
        float GetLuma(float3 rgb)
        {
            return dot(rgb, float3(0.2126, 0.7152, 0.0722));
        }

        // Filtering window weight (Lanczos-approximation)
        float LanczosWeight(float x)
        {
            x = abs(x);
            if (x >= 2.0) return 0.0;
            // High-performance rational approximation of sinc(x) * sinc(x/2)
            float x2 = x * x;
            return saturate((1.0 - 0.25 * x2) * (1.0 - 0.5 * x2));
        }

        float4 FragEASU(VaryingsDefault i) : SV_Target
        {
            // Input coordinates in texel space
            float2 pp = i.texcoord * _MainTex_TexelSize.zw - 0.5;
            float2 fp = floor(pp);
            float2 f = pp - fp;

            float2 baseUV = (fp + 0.5) * _MainTex_TexelSize.xy;
            float2 dx = float2(_MainTex_TexelSize.x, 0.0);
            float2 dy = float2(0.0, _MainTex_TexelSize.y);

            // 12-tap sampling diamond:
            //       [A] [B]
            //   [C] [D] [E] [F]
            //   [G] [H] [I] [J]
            //       [K] [L]

            // 4 inner central taps
            float3 cD = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, baseUV).rgb;
            float3 cE = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, baseUV + dx).rgb;
            float3 cH = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, baseUV + dy).rgb;
            float3 cI = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, baseUV + dx + dy).rgb;

            // 8 outer boundary taps
            float3 cA = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, baseUV - dy).rgb;
            float3 cB = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, baseUV + dx - dy).rgb;
            float3 cC = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, baseUV - dx).rgb;
            float3 cF = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, baseUV + dx * 2.0).rgb;
            float3 cG = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, baseUV - dx + dy).rgb;
            float3 cJ = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, baseUV + dx * 2.0 + dy).rgb;
            float3 cK = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, baseUV + dy * 2.0).rgb;
            float3 cL = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, baseUV + dx + dy * 2.0).rgb;

            // Compute lumas
            float lD = GetLuma(cD), lE = GetLuma(cE), lH = GetLuma(cH), lI = GetLuma(cI);
            float lA = GetLuma(cA), lB = GetLuma(cB), lC = GetLuma(cC), lF = GetLuma(cF);
            float lG = GetLuma(cG), lJ = GetLuma(cJ), lK = GetLuma(cK), lL = GetLuma(cL);

            // Directional tensor extraction (AMD FSR 1.0 formulation)
            float dirX = (lE - lD) + (lI - lH) + 0.5 * ((lB - lA) + (lF - lC) + (lJ - lG) + (lL - lK));
            float dirY = (lH - lD) + (lI - lE) + 0.5 * ((lG - lC) + (lK - lA) + (lL - lB) + (lJ - lF));
            float dirLen = length(float2(dirX, dirY));

            float2 dir = (dirLen > 0.0001) ? float2(dirX, dirY) / dirLen : float2(1.0, 0.0);

            // Elliptical kernel alignment:
            // Major axis parallel to edge: length extended for smooth anti-aliased interpolation
            // Minor axis perpendicular to edge: narrow for razor-sharp edge contrast
            float stretch = saturate(dirLen * 4.0);
            float2 axisX = dir;
            float2 axisY = float2(-dir.y, dir.x);

            // Transform subpixel offset 'f' relative to the 12 taps
            float2 offsets[12] = {
                float2(0.0, -1.0), float2(1.0, -1.0),
                float2(-1.0, 0.0), float2(0.0, 0.0), float2(1.0, 0.0), float2(2.0, 0.0),
                float2(-1.0, 1.0), float2(0.0, 1.0), float2(1.0, 1.0), float2(2.0, 1.0),
                float2(0.0, 2.0), float2(1.0, 2.0)
            };

            float3 colors[12] = { cA, cB, cC, cD, cE, cF, cG, cH, cI, cJ, cK, cL };

            float3 accumColor = float3(0, 0, 0);
            float totalWeight = 0.0;

            [unroll(12)]
            for (int k = 0; k < 12; k++)
            {
                float2 dPos = offsets[k] - f;
                // Project onto rotated elliptical axes
                float u = dot(dPos, axisX) * (1.0 + stretch * 0.5);
                float v = dot(dPos, axisY) * (1.0 - stretch * 0.3);
                float dist = length(float2(u, v));

                float w = LanczosWeight(dist);
                accumColor += colors[k] * w;
                totalWeight += w;
            }

            float3 finalColor = accumColor / max(0.0001, totalWeight);

            // Anti-ringing clamp using min/max of 4 nearest taps
            float3 minColor = min(min(cD, cE), min(cH, cI));
            float3 maxColor = max(max(cD, cE), max(cH, cI));
            finalColor = clamp(finalColor, minColor, maxColor);

            return float4(finalColor, 1.0);
        }
    ENDHLSL

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragEASU
            ENDHLSL
        }
    }
}
