Shader "Hidden/TUFX/AntiFlicker"
{
    HLSLINCLUDE
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"

        TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
        TEXTURE2D_SAMPLER2D(_PrevColorTex, sampler_PrevColorTex);
        TEXTURE2D_SAMPLER2D(_CameraDepthTexture, sampler_CameraDepthTexture);
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

        float2 CalculatePrevUV(float2 uv, float rawDepth, float linearDepth)
        {
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

            float2 prevUV;
            prevUV.x = (prevViewPos.x / prevViewPos.z - _NDCToViewAdd.x) / _NDCToViewMul.x;
            prevUV.y = (prevViewPos.y / prevViewPos.z - _NDCToViewAdd.y) / _NDCToViewMul.y;
            return prevUV;
        }

        struct Output
        {
            float4 destination : SV_Target0;
            float4 history : SV_Target1;
        };

        float4 ResolveAntiFlicker(float2 uv, out float4 outHistory)
        {
            float3 centerColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb;

            // 1. Gather 3x3 neighborhood statistics in YCoCg space
            float3 m1 = 0.0;
            float3 m2 = 0.0;
            float totalWeight = 0.0;
            float3 boxMin = 100000.0;
            float3 boxMax = -100000.0;

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

                float w = (_AntiFirefly > 0.5) ? (1.0 / (1.0 + max(0.0, ycocg.x))) : 1.0;
                m1 += ycocg * w;
                m2 += ycocg * ycocg * w;
                totalWeight += w;
            }

            float3 mean = m1 / max(totalWeight, 0.0001);
            float3 variance = max(0.0, (m2 / max(totalWeight, 0.0001)) - (mean * mean));
            float3 stdDev = sqrt(variance);

            float3 vMin = mean - _VarianceSharpness * stdDev;
            float3 vMax = mean + _VarianceSharpness * stdDev;

            // Relaxed / clamped variance bounding box
            boxMin = max(boxMin, vMin);
            boxMax = min(boxMax, vMax);

            // 2. Reprojection & History sampling
            float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, uv);
            #if UNITY_REVERSED_Z
            bool isSky = (rawDepth <= 0.0001);
            #else
            bool isSky = (rawDepth >= 0.9999);
            #endif
            float linearDepth = isSky ? 20000.0 : LinearEyeDepth(rawDepth);

            float2 prevUV = CalculatePrevUV(uv, rawDepth, linearDepth);
            bool offScreen = (prevUV.x < 0.0 || prevUV.x > 1.0 || prevUV.y < 0.0 || prevUV.y > 1.0);

            if (offScreen || _ResetHistory > 0.5)
            {
                outHistory = float4(centerColor, 1.0);
                return float4(centerColor, 1.0);
            }

            float3 histRGB = SAMPLE_TEXTURE2D(_PrevColorTex, sampler_PrevColorTex, prevUV).rgb;
            float3 histYCoCg = RGBToYCoCg(histRGB);

            // 3. Brian Karis (UE4/UE5) Variance Clipping
            float3 clampedHistYCoCg = clamp(histYCoCg, boxMin, boxMax);
            float3 clampedHistRGB = max(0.0, YCoCgToRGB(clampedHistYCoCg));

            // 4. Temporal blending with anti-firefly luma weighting
            float3 finalRGB;
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

            outHistory = float4(finalRGB, 1.0);

            // Diagnostic visualization
            if (_DebugMode == 1)
            {
                float diff = length(centerColor - clampedHistRGB);
                return float4(diff * 8.0, diff * 2.0, 0.0, 1.0);
            }
            else if (_DebugMode == 2)
            {
                return float4(clampedHistRGB, 1.0);
            }

            return float4(finalRGB, 1.0);
        }

        Output FragAntiFlickerMRT(VaryingsDefault i)
        {
            Output o;
            float4 hist;
            o.destination = ResolveAntiFlicker(i.texcoord, hist);
            o.history = hist;
            return o;
        }

        float4 FragAntiFlickerSingle(VaryingsDefault i) : SV_Target
        {
            float4 hist;
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
