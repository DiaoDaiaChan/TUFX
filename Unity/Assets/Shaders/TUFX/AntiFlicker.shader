Shader "Hidden/TUFX/AntiFlicker"
{
    HLSLINCLUDE
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"

        TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
        TEXTURE2D_SAMPLER2D(_PrevColorTex, sampler_PrevColorTex);
        TEXTURE2D_SAMPLER2D(_CameraDepthTexture, sampler_CameraDepthTexture);
        TEXTURE2D_SAMPLER2D(_CameraMotionVectorsTexture, sampler_CameraMotionVectorsTexture);
        float4 _MainTex_TexelSize;

        float2 _NDCToViewMul;
        float2 _NDCToViewAdd;
        float4x4 _RotMatrix;
        float3 _CamTranslationView;
        float _VesselMaxDepth;
        float _Stability;
        float _VarianceSharpness;
        float _AntiFirefly;
        float _ResetHistory;
        int _DebugMode;
        int _MotionVectorSource; // 0: Auto, 1: Deferred, 2: CameraOnly
        int _HistoryFilter;      // 0: BicubicCatmullRom5Tap, 1: Bilinear
        int _ClippingMode;       // 0: KDOP18, 1: VarianceAABB
        float _SuperResolution;  // 0.0: Standard Anti-Flicker, 1.0: Temporal Super-Resolution

        // Color Space conversion: RGB <-> YCoCg
        float3 RGBToYCoCg(float3 c)
        {
            float Y  = dot(c, float3(0.25, 0.5, 0.25));
            float Co = dot(c, float3(0.5, 0.0, -0.5));
            float Cg = dot(c, float3(-0.25, 0.5, -0.25));
            return float3(Y, Co, Cg);
        }

        float3 YCoCgToRGB(float3 c)
        {
            float Y  = c.x;
            float Co = c.y;
            float Cg = c.z;
            float R = Y + Co - Cg;
            float G = Y + Cg;
            float B = Y - Co - Cg;
            return float3(R, G, B);
        }

        float3 ReconstructViewPos(float2 uv, float linearDepth)
        {
            float3 ret;
            ret.xy = (_NDCToViewMul * uv + _NDCToViewAdd) * linearDepth;
            ret.z = linearDepth;
            return ret;
        }

        float4 CalculatePrevUV(float2 uv, float rawDepth, float linearDepth)
        {
            float2 prevUV = uv;
            float2 motion = float2(0.0, 0.0);

            // 1. Try Deferred Motion Vectors if requested
            if (_MotionVectorSource != 2) // 0: Auto or 1: Deferred
            {
                float2 mv = SAMPLE_TEXTURE2D_LOD(_CameraMotionVectorsTexture, sampler_CameraMotionVectorsTexture, uv, 0).xy;
                if (dot(mv, mv) > 1e-11)
                {
                    prevUV = uv - mv;
                    motion = mv;
                    return float4(prevUV.x, prevUV.y, motion.x, motion.y);
                }
                else if (_MotionVectorSource == 1) // Deferred Forced
                {
                    prevUV = uv - mv;
                    motion = mv;
                    return float4(prevUV.x, prevUV.y, motion.x, motion.y);
                }
            }

            // 2. Analytical Camera Matrix Reprojection Fallback
            #if UNITY_REVERSED_Z
            bool isSky = (rawDepth <= 0.0001);
            #else
            bool isSky = (rawDepth >= 0.9999);
            #endif

            float z = isSky ? 20000.0 : linearDepth;
            float3 viewPos = ReconstructViewPos(uv, z);

            bool isWorld = (!isSky && (_VesselMaxDepth <= 0.0 || linearDepth > _VesselMaxDepth));

            float3 prevViewPos = mul((float3x3)_RotMatrix, viewPos);
            if (isWorld)
            {
                prevViewPos += _CamTranslationView;
            }

            if (prevViewPos.z <= 0.01)
            {
                prevViewPos.z = 0.01;
            }

            prevUV.x = (prevViewPos.x / prevViewPos.z - _NDCToViewAdd.x) / _NDCToViewMul.x;
            prevUV.y = (prevViewPos.y / prevViewPos.z - _NDCToViewAdd.y) / _NDCToViewMul.y;
            motion = uv - prevUV;
            return float4(prevUV.x, prevUV.y, motion.x, motion.y);
        }

        // 5-Tap Bicubic Catmull-Rom filtering with GPU hardware bilinear optimization
        float3 SampleHistoryBicubic5Tap(float2 uv)
        {
            float2 samplePos = uv * _MainTex_TexelSize.zw;
            float2 tc = floor(samplePos - 0.5) + 0.5;
            float2 f = samplePos - tc;
            float2 f2 = f * f;
            float2 f3 = f2 * f;

            // Catmull-Rom weights
            float2 w0 = f2 - 0.5 * (f3 + f);
            float2 w1 = 1.5 * f3 - 2.5 * f2 + 1.0;
            float2 w3 = 0.5 * (f3 - f2);
            float2 w2 = 1.0 - w0 - w1 - w3;

            float2 w12 = w1 + w2;
            float2 tc12 = _MainTex_TexelSize.xy * (tc + w2 / max(w12, 0.00001));
            float2 tc0  = _MainTex_TexelSize.xy * (tc - 1.0);
            float2 tc3  = _MainTex_TexelSize.xy * (tc + 2.0);

            float4 c_center = SAMPLE_TEXTURE2D_LOD(_PrevColorTex, sampler_PrevColorTex, float2(tc12.x, tc12.y), 0);
            float4 c_top    = SAMPLE_TEXTURE2D_LOD(_PrevColorTex, sampler_PrevColorTex, float2(tc12.x, tc0.y), 0);
            float4 c_bottom = SAMPLE_TEXTURE2D_LOD(_PrevColorTex, sampler_PrevColorTex, float2(tc12.x, tc3.y), 0);
            float4 c_left   = SAMPLE_TEXTURE2D_LOD(_PrevColorTex, sampler_PrevColorTex, float2(tc0.x,  tc12.y), 0);
            float4 c_right  = SAMPLE_TEXTURE2D_LOD(_PrevColorTex, sampler_PrevColorTex, float2(tc3.x,  tc12.y), 0);

            float centerBoost = (_SuperResolution > 0.5) ? 1.25 : 1.0;
            float totalWeight = (w12.x * w12.y * centerBoost) + (w12.x * w0.y) + (w12.x * w3.y) + (w0.x * w12.y) + (w3.x * w12.y);
            float3 result = (c_center.rgb * (w12.x * w12.y * centerBoost) +
                             c_top.rgb    * (w12.x * w0.y) +
                             c_bottom.rgb * (w12.x * w3.y) +
                             c_left.rgb   * (w0.x * w12.y) +
                             c_right.rgb  * (w3.x * w12.y)) / max(totalWeight, 0.0001);
            return max(float3(0.0, 0.0, 0.0), result);
        }

        struct Output
        {
            float4 destination : SV_Target0;
            float4 history : SV_Target1;
        };

        float4 ResolveAntiFlicker(float2 uv, out float4 outHistory)
        {
            float3 centerColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb;
            outHistory = float4(centerColor, 1.0);
            float3 finalRGB = centerColor;

            // 1. Gather 3x3 neighborhood statistics in YCoCg space
            float3 m1 = float3(0.0, 0.0, 0.0);
            float3 m2 = float3(0.0, 0.0, 0.0);
            float totalWeight = 0.0;
            float3 boxMin = float3(100000.0, 100000.0, 100000.0);
            float3 boxMax = float3(-100000.0, -100000.0, -100000.0);

            // 18-DOP diagonal extremal values
            float dMin0 = 100000.0, dMax0 = -100000.0; // Y + Co
            float dMin1 = 100000.0, dMax1 = -100000.0; // Y - Co
            float dMin2 = 100000.0, dMax2 = -100000.0; // Y + Cg
            float dMin3 = 100000.0, dMax3 = -100000.0; // Y - Cg
            float dMin4 = 100000.0, dMax4 = -100000.0; // Co + Cg
            float dMin5 = 100000.0, dMax5 = -100000.0; // Co - Cg

            static const int2 kOffsets[9] = {
                int2(-1, -1), int2( 0, -1), int2( 1, -1),
                int2(-1,  0), int2( 0,  0), int2( 1,  0),
                int2(-1,  1), int2( 0,  1), int2( 1,  1)
            };

            [unroll]
            for (int k = 0; k < 9; k++)
            {
                float2 sampleUV = uv + kOffsets[k] * _MainTex_TexelSize.xy;
                float3 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, sampleUV).rgb;
                float3 ycocg = RGBToYCoCg(col);

                boxMin = min(boxMin, ycocg);
                boxMax = max(boxMax, ycocg);

                // Track 18-DOP diagonal projections
                float p0 = ycocg.x + ycocg.y;
                float p1 = ycocg.x - ycocg.y;
                float p2 = ycocg.x + ycocg.z;
                float p3 = ycocg.x - ycocg.z;
                float p4 = ycocg.y + ycocg.z;
                float p5 = ycocg.y - ycocg.z;

                dMin0 = min(dMin0, p0); dMax0 = max(dMax0, p0);
                dMin1 = min(dMin1, p1); dMax1 = max(dMax1, p1);
                dMin2 = min(dMin2, p2); dMax2 = max(dMax2, p2);
                dMin3 = min(dMin3, p3); dMax3 = max(dMax3, p3);
                dMin4 = min(dMin4, p4); dMax4 = max(dMax4, p4);
                dMin5 = min(dMin5, p5); dMax5 = max(dMax5, p5);

                float w = (_AntiFirefly > 0.5) ? (1.0 / (1.0 + max(0.0, ycocg.x))) : 1.0;
                m1 += ycocg * w;
                m2 += ycocg * ycocg * w;
                totalWeight += w;
            }

            float3 mean = m1 / max(totalWeight, 0.0001);
            float3 variance = max(float3(0.0, 0.0, 0.0), (m2 / max(totalWeight, 0.0001)) - (mean * mean));
            float3 stdDev = sqrt(variance);

            float3 vMin = mean - _VarianceSharpness * stdDev;
            float3 vMax = mean + _VarianceSharpness * stdDev;

            // Relaxed / clamped variance bounding box
            boxMin = max(boxMin, vMin);
            boxMax = min(boxMax, vMax);

            // 2. Reprojection & History sampling (Motion Vectors + Camera Matrix)
            float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, uv);
            #if UNITY_REVERSED_Z
            bool isSky = (rawDepth <= 0.0001);
            #else
            bool isSky = (rawDepth >= 0.9999);
            #endif
            float linearDepth = isSky ? 20000.0 : LinearEyeDepth(rawDepth);

            float4 reproject = CalculatePrevUV(uv, rawDepth, linearDepth);
            float2 prevUV = reproject.xy;
            float2 pixelMotion = reproject.zw;
            bool offScreen = (prevUV.x < 0.0 || prevUV.x > 1.0 || prevUV.y < 0.0 || prevUV.y > 1.0);

            if (offScreen || _ResetHistory > 0.5)
            {
                outHistory = float4(centerColor, 1.0);
                return float4(centerColor, 1.0);
            }

            // 3. Sample history: 5-Tap Bicubic Catmull-Rom or Bilinear
            float3 histRGB = centerColor;
            if (_HistoryFilter == 0)
            {
                histRGB = SampleHistoryBicubic5Tap(prevUV);
            }
            else
            {
                histRGB = SAMPLE_TEXTURE2D(_PrevColorTex, sampler_PrevColorTex, prevUV).rgb;
            }
            float3 histYCoCg = RGBToYCoCg(histRGB);

            // 4. History Clipping: 18-DOP Convex Polytope or Standard Variance AABB
            float3 clampedHistYCoCg = clamp(histYCoCg, boxMin, boxMax);

            if (_ClippingMode == 0) // 18-DOP Convex Polytope Clipping
            {
                // Diagonal 0: Y + Co
                float v0 = clampedHistYCoCg.x + clampedHistYCoCg.y;
                float c0 = clamp(v0, dMin0, dMax0);
                float diff0 = (c0 - v0) * 0.5;
                clampedHistYCoCg.x += diff0;
                clampedHistYCoCg.y += diff0;

                // Diagonal 1: Y - Co
                float v1 = clampedHistYCoCg.x - clampedHistYCoCg.y;
                float c1 = clamp(v1, dMin1, dMax1);
                float diff1 = (c1 - v1) * 0.5;
                clampedHistYCoCg.x += diff1;
                clampedHistYCoCg.y -= diff1;

                // Diagonal 2: Y + Cg
                float v2 = clampedHistYCoCg.x + clampedHistYCoCg.z;
                float c2 = clamp(v2, dMin2, dMax2);
                float diff2 = (c2 - v2) * 0.5;
                clampedHistYCoCg.x += diff2;
                clampedHistYCoCg.z += diff2;

                // Diagonal 3: Y - Cg
                float v3 = clampedHistYCoCg.x - clampedHistYCoCg.z;
                float c3 = clamp(v3, dMin3, dMax3);
                float diff3 = (c3 - v3) * 0.5;
                clampedHistYCoCg.x += diff3;
                clampedHistYCoCg.z -= diff3;

                // Diagonal 4: Co + Cg
                float v4 = clampedHistYCoCg.y + clampedHistYCoCg.z;
                float c4 = clamp(v4, dMin4, dMax4);
                float diff4 = (c4 - v4) * 0.5;
                clampedHistYCoCg.y += diff4;
                clampedHistYCoCg.z += diff4;

                // Diagonal 5: Co - Cg
                float v5 = clampedHistYCoCg.y - clampedHistYCoCg.z;
                float c5 = clamp(v5, dMin5, dMax5);
                float diff5 = (c5 - v5) * 0.5;
                clampedHistYCoCg.y += diff5;
                clampedHistYCoCg.z -= diff5;

                // Safety re-clamp to AABB box
                clampedHistYCoCg = clamp(clampedHistYCoCg, boxMin, boxMax);
            }

            float3 clampedHistRGB = max(float3(0.0, 0.0, 0.0), YCoCgToRGB(clampedHistYCoCg));

            // 5. Temporal blending with anti-firefly luma weighting
            if (_AntiFirefly > 0.5)
            {
                float centerY = RGBToYCoCg(centerColor).x;
                float histY = clampedHistYCoCg.x;
                float wCenter = (1.0 - _Stability) / (1.0 + max(0.0, centerY));
                float wHist = _Stability / (1.0 + max(0.0, histY));
                finalRGB = (centerColor * wCenter + clampedHistRGB * wHist) / max(0.0001, (wCenter + wHist));
            }
            else
            {
                finalRGB = lerp(centerColor, clampedHistRGB, _Stability);
            }

            // Temporal Super-Resolution: reconstruct subpixel high-frequency details on stable tracking geometry
            if (_SuperResolution > 0.5)
            {
                float velWeight = saturate(1.0 - length(pixelMotion * _MainTex_TexelSize.zw) * 0.5);
                float3 subpixelDelta = clampedHistRGB - centerColor;
                finalRGB = saturate(finalRGB + subpixelDelta * (0.15 * velWeight));
            }

            outHistory = float4(finalRGB, 1.0);

            // Diagnostic visualization modes
            if (_DebugMode == 1)
            {
                // Stabilized variance difference heatmap
                float diff = length(centerColor - clampedHistRGB);
                return float4(diff * 8.0, diff * 2.0, 0.0, 1.0);
            }
            else if (_DebugMode == 2)
            {
                // Clamped history preview
                return float4(clampedHistRGB, 1.0);
            }
            else if (_DebugMode == 3)
            {
                // Real-time Motion Vector visualization: R = X, G = Y
                float2 vel = abs(pixelMotion) * 50.0;
                return float4(vel.x, vel.y, 0.0, 1.0);
            }

            return float4(finalRGB, 1.0);
        }

        Output FragAntiFlickerMRT(VaryingsDefault i)
        {
            Output o;
            float4 hist = float4(0.0, 0.0, 0.0, 1.0);
            o.destination = ResolveAntiFlicker(i.texcoord, hist);
            o.history = hist;
            return o;
        }

        float4 FragAntiFlickerSingle(VaryingsDefault i) : SV_Target
        {
            float4 hist = float4(0.0, 0.0, 0.0, 1.0);
            return ResolveAntiFlicker(i.texcoord, hist);
        }

    ENDHLSL

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // Pass 0: MRT (Destination + History)
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragAntiFlickerMRT
            ENDHLSL
        }

        // Pass 1: Single Target Fallback
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragAntiFlickerSingle
            ENDHLSL
        }
    }
}
