///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2018, Intel Corporation
// Licensed under the Apache License, Version 2.0 (the "License");
//
// Conservative Morphological Anti-Aliasing 2.0 (CMAA2)
// Author: Filip Strugar (filip.strugar@intel.com)
// Adapted for Unity PostProcessing Stack & TUFX
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

Shader "Hidden/TUFX/CMAA2"
{
    HLSLINCLUDE
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"

        TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
        float4 _MainTex_TexelSize;
        float _EdgeThreshold;      // Default ~0.08
        float _ExtraSharpness;     // Default ~0.5

        // Rec. 601 Luma calculation for edge detection
        float RGBToLuma(float3 color)
        {
            return dot(sqrt(saturate(color)), float3(0.299, 0.587, 0.114));
        }

        // Pass 0: Conservative Morphological Anti-Aliasing (CMAA2)
        float4 FragCMAA2(VaryingsDefault i) : SV_Target
        {
            float2 uv = i.texcoord;
            float2 texel = _MainTex_TexelSize.xy;

            float4 centerColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
            float cLuma = RGBToLuma(centerColor.rgb);

            // 4 direct cross neighbors
            float3 cL = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-texel.x, 0.0)).rgb;
            float3 cR = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2( texel.x, 0.0)).rgb;
            float3 cT = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0.0, -texel.y)).rgb;
            float3 cB = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0.0,  texel.y)).rgb;

            float lLuma = RGBToLuma(cL);
            float rLuma = RGBToLuma(cR);
            float tLuma = RGBToLuma(cT);
            float bLuma = RGBToLuma(cB);

            // Compute edge differences in 4 directions
            float4 edgeDiff = abs(float4(rLuma, bLuma, lLuma, tLuma) - cLuma);

            // Conservative edge thresholding: reject flat or gentle gradients
            float maxDiff = max(max(edgeDiff.x, edgeDiff.y), max(edgeDiff.z, edgeDiff.w));
            if (maxDiff < _EdgeThreshold)
            {
                return centerColor; // No edge, return unchanged center color immediately
            }

            // Local contrast adaptation: threshold scales with local maximum contrast
            float localMaxLuma = max(cLuma, max(max(lLuma, rLuma), max(tLuma, bLuma)));
            float localMinLuma = min(cLuma, min(min(lLuma, rLuma), min(tLuma, bLuma)));
            float localContrast = localMaxLuma - localMinLuma;
            if (localContrast < _EdgeThreshold * 1.5)
            {
                return centerColor;
            }

            // Binary edge flags (Right, Bottom, Left, Top)
            float edgeCutoff = _EdgeThreshold * 0.8;
            bool4 edges = edgeDiff > edgeCutoff;

            int edgeCount = (edges.x ? 1 : 0) + (edges.y ? 1 : 0) + (edges.z ? 1 : 0) + (edges.w ? 1 : 0);

            // Conservative line and shape handling:
            // CMAA2 avoids blurring isolated single edges or lines without corners/steps
            if (edgeCount == 0 || edgeCount == 4)
            {
                return centerColor;
            }

            float4 blendWeights = float4(0.0, 0.0, 0.0, 0.0);
            float blurCoeff = 0.5 * _ExtraSharpness;

            // L-shape / corner detection (2 adjacent edges)
            if (edgeCount == 2)
            {
                // Corner between Right & Bottom
                if (edges.x && edges.y) blendWeights = float4(blurCoeff * 0.5, blurCoeff * 0.5, 0.0, 0.0);
                // Corner between Bottom & Left
                else if (edges.y && edges.z) blendWeights = float4(0.0, blurCoeff * 0.5, blurCoeff * 0.5, 0.0);
                // Corner between Left & Top
                else if (edges.z && edges.w) blendWeights = float4(0.0, 0.0, blurCoeff * 0.5, blurCoeff * 0.5);
                // Corner between Top & Right
                else if (edges.w && edges.x) blendWeights = float4(blurCoeff * 0.5, 0.0, 0.0, blurCoeff * 0.5);
            }
            // Straight step line / T-junction (1 or 3 edges)
            else if (edgeCount == 1)
            {
                // Check 2 steps further along the orthogonal direction for step reconstruction
                if (edges.x) // Horizontal step
                {
                    float3 cT2 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0.0, -texel.y * 2.0)).rgb;
                    float3 cB2 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0.0,  texel.y * 2.0)).rgb;
                    float t2Luma = RGBToLuma(cT2);
                    float b2Luma = RGBToLuma(cB2);
                    if (abs(tLuma - t2Luma) > edgeCutoff) blendWeights.x = blurCoeff * 0.35;
                    else if (abs(bLuma - b2Luma) > edgeCutoff) blendWeights.x = blurCoeff * 0.35;
                }
                else if (edges.z)
                {
                    blendWeights.z = blurCoeff * 0.35;
                }
                else if (edges.y) // Vertical step
                {
                    blendWeights.y = blurCoeff * 0.35;
                }
                else if (edges.w)
                {
                    blendWeights.w = blurCoeff * 0.35;
                }
            }
            else if (edgeCount == 3)
            {
                // T-junction: blend towards the non-edge direction
                if (!edges.x) blendWeights = float4(0.0, blurCoeff * 0.25, blurCoeff * 0.25, blurCoeff * 0.25);
                else if (!edges.y) blendWeights = float4(blurCoeff * 0.25, 0.0, blurCoeff * 0.25, blurCoeff * 0.25);
                else if (!edges.z) blendWeights = float4(blurCoeff * 0.25, blurCoeff * 0.25, 0.0, blurCoeff * 0.25);
                else if (!edges.w) blendWeights = float4(blurCoeff * 0.25, blurCoeff * 0.25, blurCoeff * 0.25, 0.0);
            }

            float totalBlend = blendWeights.x + blendWeights.y + blendWeights.z + blendWeights.w;
            if (totalBlend <= 0.001)
            {
                return centerColor;
            }

            // Perform conservative color blending
            float3 blendedColor = centerColor.rgb * (1.0 - totalBlend) +
                                  cR * blendWeights.x +
                                  cB * blendWeights.y +
                                  cL * blendWeights.z +
                                  cT * blendWeights.w;

            return float4(blendedColor, centerColor.a);
        }
    ENDHLSL

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragCMAA2
            ENDHLSL
        }
    }
}
