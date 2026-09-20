///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2018, Intel Corporation
// Licensed under the Apache License, Version 2.0 (the "License");
//
// Conservative Morphological Anti-Aliasing, version: 2.3 (CMAA2)
// Author(s): Filip Strugar (filip.strugar@intel.com)
// Adapted for Unity PostProcessing Stack & TUFX
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

Shader "Hidden/TUFX/CMAA2"
{
    HLSLINCLUDE
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"

        TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
        TEXTURE2D_SAMPLER2D(_EdgesTex, sampler_EdgesTex);

        float4 _MainTex_TexelSize;
        float _EdgeThreshold;                 // Default ~0.07 (0.02 - 0.25)
        float _ExtraSharpness;                // 1.0 = On, 0.0 = Off
        float _LocalContrastAdaptationAmount; // 0.10 - 0.15
        float _SimpleShapeBlurinessAmount;    // 0.07 - 0.10
        float _MaxLineLength;                 // 16 - 86, default 64
        float _DebugMode;                     // 0 = Normal, 1 = Show Edges, 2 = Show Blend Weights

        // Intel CMAA2 Rec. 601 Luma conversion for gamma-compressed components
        float RGBToLumaForEdges(float3 linearRGB)
        {
            return dot(sqrt(saturate(linearRGB)), float3(0.299, 0.587, 0.114));
        }

        // ===================================================================================
        // Pass 0: Edge Detection with Local Contrast Adaptation
        // ===================================================================================
        float4 FragEdgeDetect(VaryingsDefault i) : SV_Target
        {
            float2 uv = i.texcoord;
            float2 texel = _MainTex_TexelSize.xy;

            // Sample 3x3 neighborhood luma values
            float3 cM1M1 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-texel.x, -texel.y)).rgb;
            float3 c00M1 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(     0.0, -texel.y)).rgb;
            float3 cP1M1 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2( texel.x, -texel.y)).rgb;

            float3 cM100 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-texel.x,      0.0)).rgb;
            float3 c0000 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv                             ).rgb;
            float3 cP100 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2( texel.x,      0.0)).rgb;

            float3 cM1P1 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-texel.x,  texel.y)).rgb;
            float3 c00P1 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(     0.0,  texel.y)).rgb;
            float3 cP1P1 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2( texel.x,  texel.y)).rgb;

            float lM1M1 = RGBToLumaForEdges(cM1M1);
            float l00M1 = RGBToLumaForEdges(c00M1);
            float lP1M1 = RGBToLumaForEdges(cP1M1);

            float lM100 = RGBToLumaForEdges(cM100);
            float l0000 = RGBToLumaForEdges(c0000);
            float lP100 = RGBToLumaForEdges(cP100);

            float lM1P1 = RGBToLumaForEdges(cM1P1);
            float l00P1 = RGBToLumaForEdges(c00P1);
            float lP1P1 = RGBToLumaForEdges(cP1P1);

            // Calculate directional edge differences from center pixel
            float diffR = abs(l0000 - lP100);
            float diffB = abs(l0000 - l00P1);
            float diffL = abs(l0000 - lM100);
            float diffT = abs(l0000 - l00M1);

            // Compute local contrast adaptation to avoid blurring text and high-frequency textures
            float adaptAmount = _LocalContrastAdaptationAmount;
            float localContrastH = max(max(diffL, diffR), max(abs(lM1M1 - l00M1), abs(lM1P1 - l00P1))) * adaptAmount;
            float localContrastV = max(max(diffT, diffB), max(abs(lM1M1 - lM100), abs(lP1M1 - lP100))) * adaptAmount;

            // Apply thresholding with local contrast damping
            float edgeR = (diffR - localContrastH) > _EdgeThreshold ? 1.0 : 0.0;
            float edgeB = (diffB - localContrastV) > _EdgeThreshold ? 1.0 : 0.0;
            float edgeL = (diffL - localContrastH) > _EdgeThreshold ? 1.0 : 0.0;
            float edgeT = (diffT - localContrastV) > _EdgeThreshold ? 1.0 : 0.0;

            // Pack edges: R = Right, G = Bottom, B = Left, A = Top
            return float4(edgeR, edgeB, edgeL, edgeT);
        }

        // ===================================================================================
        // Pass 1: Morphological Edge Tracing & Subpixel Coverage Blending
        // ===================================================================================

        // 3x3 L-shape / corner simple shape blending from Intel CMAA2 2.3
        float4 ComputeSimpleShapeBlendValues(float4 edges, float4 edgesLeft, float4 edgesRight, float4 edgesTop, float4 edgesBottom)
        {
            float fromRight = edges.r;
            float fromBelow = edges.g;
            float fromLeft  = edges.b;
            float fromAbove = edges.a;

            float blurCoeff = _SimpleShapeBlurinessAmount;
            float numberOfEdges = dot(edges, float4(1.0, 1.0, 1.0, 1.0));
            float numberOfEdgesAllAround = dot(edgesLeft.bga + edgesRight.rga + edgesTop.rba + edgesBottom.rgb, float3(1.0, 1.0, 1.0));

            // No blur for straight edges (handled by Z-line search)
            if (numberOfEdges == 1.0)
                blurCoeff = 0.0;

            // L-like step shape / corner (only blur if corner, not parallel edges)
            if (numberOfEdges == 2.0)
            {
                blurCoeff *= ((1.0 - fromBelow * fromAbove) * (1.0 - fromRight * fromLeft));
                blurCoeff *= 0.75;

                float k = 0.9;
                fromRight += k * (edges.g * edgesTop.r     * (1.0 - edgesLeft.g)   + edges.a * edgesBottom.r  * (1.0 - edgesLeft.a));
                fromBelow += k * (edges.b * edgesRight.g   * (1.0 - edgesTop.b)    + edges.r * edgesLeft.g    * (1.0 - edgesTop.r));
                fromLeft  += k * (edges.a * edgesBottom.b  * (1.0 - edgesRight.a)  + edges.g * edgesTop.b     * (1.0 - edgesRight.g));
                fromAbove += k * (edges.r * edgesLeft.a    * (1.0 - edgesBottom.r) + edges.b * edgesRight.a  * (1.0 - edgesBottom.b));
            }

            // Dampen blur when surrounded by many edges to preserve text/texture clarity
            if (_ExtraSharpness > 0.5)
                blurCoeff *= saturate(1.15 - numberOfEdgesAllAround / 8.0);
            else
                blurCoeff *= saturate(1.30 - numberOfEdgesAllAround / 10.0);

            return float4(fromLeft, fromAbove, fromRight, fromBelow) * blurCoeff;
        }

        // Subpixel trapezoidal coverage calculation from Intel CMAA2 2.3
        float ComputeZBlendCoverage(float d1, float d2, float i, float shapeQualityScore)
        {
            const float c_symmetryCorrectionOffset = 0.22;
            float c_dampeningEffect = (_ExtraSharpness > 0.5) ? 0.11 : 0.15;

            float leftOdd = c_symmetryCorrectionOffset * fmod(d1, 2.0);
            float rightOdd = c_symmetryCorrectionOffset * fmod(d2, 2.0);

            float dampenEffect = saturate((d1 + d2 - shapeQualityScore) * c_dampeningEffect);

            float loopFrom = -floor((d1 + 1.0) * 0.5) + 1.0;
            float loopTo   =  floor((d2 + 1.0) * 0.5);

            if (i < loopFrom || i > loopTo)
                return 0.0;

            float totalLength = (loopTo - loopFrom) + 1.0 - leftOdd - rightOdd;
            float lerpStep = 1.0 / max(totalLength, 0.001);
            float lerpFromK = (0.5 - leftOdd - loopFrom) * lerpStep;

            float secondPart = (i > 0.0) ? 1.0 : 0.0;
            float srcOffset = 1.0 - secondPart * 2.0;

            float lerpK = (lerpStep * i + lerpFromK) * srcOffset + secondPart;
            lerpK *= dampenEffect;

            return saturate(lerpK);
        }

        float4 FragCMAA2(VaryingsDefault i) : SV_Target
        {
            float2 uv = i.texcoord;
            float2 texel = _MainTex_TexelSize.xy;

            float4 edges = SAMPLE_TEXTURE2D(_EdgesTex, sampler_EdgesTex, uv);
            float4 centerColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);

            // Debug Mode 1: Edge detection visualization
            if (_DebugMode == 1.0)
            {
                if (dot(edges, float4(1.0, 1.0, 1.0, 1.0)) > 0.0)
                    return float4(lerp(edges.rgb, float3(0.5, 0.5, 0.5), edges.a * 0.2), 1.0);
                return float4(0.0, 0.0, 0.0, 1.0);
            }

            // Early-out if no edge detected at this pixel
            if (dot(edges, float4(1.0, 1.0, 1.0, 1.0)) == 0.0)
            {
                if (_DebugMode == 2.0) return float4(0.0, 0.0, 0.0, 1.0);
                return centerColor;
            }

            // Load 4 direct neighbor edge masks
            float4 edgesLeft   = SAMPLE_TEXTURE2D(_EdgesTex, sampler_EdgesTex, uv + float2(-texel.x, 0.0));
            float4 edgesRight  = SAMPLE_TEXTURE2D(_EdgesTex, sampler_EdgesTex, uv + float2( texel.x, 0.0));
            float4 edgesTop    = SAMPLE_TEXTURE2D(_EdgesTex, sampler_EdgesTex, uv + float2(0.0, -texel.y));
            float4 edgesBottom = SAMPLE_TEXTURE2D(_EdgesTex, sampler_EdgesTex, uv + float2(0.0,  texel.y));

            // 1. Evaluate Simple Shapes (L-shapes / corners)
            float4 simpleBlend = ComputeSimpleShapeBlendValues(edges, edgesLeft, edgesRight, edgesTop, edgesBottom);
            float simpleWeightSum = dot(simpleBlend, float4(1.0, 1.0, 1.0, 1.0));

            float3 blendedColor = centerColor.rgb * (1.0 - simpleWeightSum);
            if (simpleBlend.x > 0.0) // from left
                blendedColor += simpleBlend.x * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-texel.x, 0.0)).rgb;
            if (simpleBlend.y > 0.0) // from above
                blendedColor += simpleBlend.y * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0.0, -texel.y)).rgb;
            if (simpleBlend.z > 0.0) // from right
                blendedColor += simpleBlend.z * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2( texel.x, 0.0)).rgb;
            if (simpleBlend.w > 0.0) // from below
                blendedColor += simpleBlend.w * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0.0,  texel.y)).rgb;

            // 2. Evaluate Complex Shapes (Z-Shapes / Long Diagonal Line Tracing)
            int maxSearch = (int)clamp(_MaxLineLength, 8.0, 86.0);
            float zBlendWeight = 0.0;
            float3 zBlendColor = float3(0.0, 0.0, 0.0);

            // A. Horizontal edge line tracing (for horizontal staircase steps)
            // Pixel has bottom edge (edges.g > 0.5) or top edge (edges.a > 0.5)
            if (edges.g > 0.5 || edges.a > 0.5)
            {
                bool isBottom = (edges.g > 0.5);
                float2 blendDir = isBottom ? float2(0.0, texel.y) : float2(0.0, -texel.y);

                // Trace left along the same edge
                int dLeft = 0;
                [loop]
                for (int stepL = 1; stepL <= maxSearch; stepL++)
                {
                    float4 e = SAMPLE_TEXTURE2D(_EdgesTex, sampler_EdgesTex, uv + float2(-texel.x * stepL, 0.0));
                    if ((isBottom && e.g > 0.5) || (!isBottom && e.a > 0.5))
                        dLeft++;
                    else
                        break;
                }

                // Trace right along the same edge
                int dRight = 0;
                [loop]
                for (int stepR = 1; stepR <= maxSearch; stepR++)
                {
                    float4 e = SAMPLE_TEXTURE2D(_EdgesTex, sampler_EdgesTex, uv + float2(texel.x * stepR, 0.0));
                    if ((isBottom && e.g > 0.5) || (!isBottom && e.a > 0.5))
                        dRight++;
                    else
                        break;
                }

                // Check for Z-step transition at endpoints
                // Step to the right:
                float4 endRight = SAMPLE_TEXTURE2D(_EdgesTex, sampler_EdgesTex, uv + float2(texel.x * dRight, 0.0));
                float4 nextRight = SAMPLE_TEXTURE2D(_EdgesTex, sampler_EdgesTex, uv + float2(texel.x * (dRight + 1), 0.0));
                bool hasStepRight = (endRight.r > 0.5) && ((isBottom && nextRight.a > 0.5) || (!isBottom && nextRight.g > 0.5));

                if (hasStepRight)
                {
                    // Trace length on the other side of the step
                    int d2 = 0;
                    [loop]
                    for (int s = 1; s <= maxSearch; s++)
                    {
                        float4 e = SAMPLE_TEXTURE2D(_EdgesTex, sampler_EdgesTex, uv + float2(texel.x * (dRight + 1 + s), 0.0));
                        if ((isBottom && e.a > 0.5) || (!isBottom && e.g > 0.5))
                            d2++;
                        else
                            break;
                    }

                    float d1Total = (float)(dLeft + dRight + 1);
                    float d2Total = (float)(d2 + 1);
                    float posRelStep = -(float)dRight; // relative offset i from step

                    if ((d1Total + d2Total) >= 5.0)
                    {
                        float weight = ComputeZBlendCoverage(d1Total, d2Total, posRelStep, 0.0);
                        if (weight > zBlendWeight)
                        {
                            zBlendWeight = weight;
                            zBlendColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + blendDir).rgb;
                        }
                    }
                }

                // Step to the left:
                float4 nextLeft = SAMPLE_TEXTURE2D(_EdgesTex, sampler_EdgesTex, uv + float2(-texel.x * (dLeft + 1), 0.0));
                bool hasStepLeft = (nextLeft.r > 0.5) && ((isBottom && nextLeft.a > 0.5) || (!isBottom && nextLeft.g > 0.5));

                if (hasStepLeft)
                {
                    int d1 = 0;
                    [loop]
                    for (int s = 1; s <= maxSearch; s++)
                    {
                        float4 e = SAMPLE_TEXTURE2D(_EdgesTex, sampler_EdgesTex, uv + float2(-texel.x * (dLeft + 1 + s), 0.0));
                        if ((isBottom && e.a > 0.5) || (!isBottom && e.g > 0.5))
                            d1++;
                        else
                            break;
                    }

                    float d1Total = (float)(d1 + 1);
                    float d2Total = (float)(dLeft + dRight + 1);
                    float posRelStep = (float)(dLeft + 1); // relative offset i from step

                    if ((d1Total + d2Total) >= 5.0)
                    {
                        float weight = ComputeZBlendCoverage(d1Total, d2Total, posRelStep, 0.0);
                        if (weight > zBlendWeight)
                        {
                            zBlendWeight = weight;
                            zBlendColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + blendDir).rgb;
                        }
                    }
                }
            }

            // B. Vertical edge line tracing (for vertical staircase steps)
            // Pixel has right edge (edges.r > 0.5) or left edge (edges.b > 0.5)
            if (edges.r > 0.5 || edges.b > 0.5)
            {
                bool isRight = (edges.r > 0.5);
                float2 blendDir = isRight ? float2(texel.x, 0.0) : float2(-texel.x, 0.0);

                // Trace up along the same edge
                int dUp = 0;
                [loop]
                for (int stepU = 1; stepU <= maxSearch; stepU++)
                {
                    float4 e = SAMPLE_TEXTURE2D(_EdgesTex, sampler_EdgesTex, uv + float2(0.0, -texel.y * stepU));
                    if ((isRight && e.r > 0.5) || (!isRight && e.b > 0.5))
                        dUp++;
                    else
                        break;
                }

                // Trace down along the same edge
                int dDown = 0;
                [loop]
                for (int stepD = 1; stepD <= maxSearch; stepD++)
                {
                    float4 e = SAMPLE_TEXTURE2D(_EdgesTex, sampler_EdgesTex, uv + float2(0.0, texel.y * stepD));
                    if ((isRight && e.r > 0.5) || (!isRight && e.b > 0.5))
                        dDown++;
                    else
                        break;
                }

                // Check for Z-step transition at vertical endpoints
                float4 endDown = SAMPLE_TEXTURE2D(_EdgesTex, sampler_EdgesTex, uv + float2(0.0, texel.y * dDown));
                float4 nextDown = SAMPLE_TEXTURE2D(_EdgesTex, sampler_EdgesTex, uv + float2(0.0, texel.y * (dDown + 1)));
                bool hasStepDown = (endDown.g > 0.5) && ((isRight && nextDown.b > 0.5) || (!isRight && nextDown.r > 0.5));

                if (hasStepDown)
                {
                    int d2 = 0;
                    [loop]
                    for (int s = 1; s <= maxSearch; s++)
                    {
                        float4 e = SAMPLE_TEXTURE2D(_EdgesTex, sampler_EdgesTex, uv + float2(0.0, texel.y * (dDown + 1 + s)));
                        if ((isRight && e.b > 0.5) || (!isRight && e.r > 0.5))
                            d2++;
                        else
                            break;
                    }

                    float d1Total = (float)(dUp + dDown + 1);
                    float d2Total = (float)(d2 + 1);
                    float posRelStep = -(float)dDown;

                    if ((d1Total + d2Total) >= 5.0)
                    {
                        float weight = ComputeZBlendCoverage(d1Total, d2Total, posRelStep, 0.0);
                        if (weight > zBlendWeight)
                        {
                            zBlendWeight = weight;
                            zBlendColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + blendDir).rgb;
                        }
                    }
                }

                float4 nextUp = SAMPLE_TEXTURE2D(_EdgesTex, sampler_EdgesTex, uv + float2(0.0, -texel.y * (dUp + 1)));
                bool hasStepUp = (nextUp.g > 0.5) && ((isRight && nextUp.b > 0.5) || (!isRight && nextUp.r > 0.5));

                if (hasStepUp)
                {
                    int d1 = 0;
                    [loop]
                    for (int s = 1; s <= maxSearch; s++)
                    {
                        float4 e = SAMPLE_TEXTURE2D(_EdgesTex, sampler_EdgesTex, uv + float2(0.0, -texel.y * (dUp + 1 + s)));
                        if ((isRight && e.b > 0.5) || (!isRight && e.r > 0.5))
                            d1++;
                        else
                            break;
                    }

                    float d1Total = (float)(d1 + 1);
                    float d2Total = (float)(dUp + dDown + 1);
                    float posRelStep = (float)(dUp + 1);

                    if ((d1Total + d2Total) >= 5.0)
                    {
                        float weight = ComputeZBlendCoverage(d1Total, d2Total, posRelStep, 0.0);
                        if (weight > zBlendWeight)
                        {
                            zBlendWeight = weight;
                            zBlendColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + blendDir).rgb;
                        }
                    }
                }
            }

            // Combine simple shape and complex shape blending
            if (zBlendWeight > 0.0)
            {
                blendedColor = lerp(blendedColor, zBlendColor, zBlendWeight);
            }

            // Debug Mode 2: Show anti-aliasing blend weights heatmap
            if (_DebugMode == 2.0)
            {
                float totalWeight = simpleWeightSum + zBlendWeight;
                return float4(totalWeight, totalWeight * 0.5, 0.0, 1.0);
            }

            return float4(blendedColor, centerColor.a);
        }
    ENDHLSL

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // Pass 0: Edge Detection
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragEdgeDetect
            ENDHLSL
        }

        // Pass 1: CMAA2 Morphological Blend
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragCMAA2
            ENDHLSL
        }
    }
}
