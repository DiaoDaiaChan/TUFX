Shader "Hidden/TUFX/Bloom"
{
    HLSLINCLUDE

        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/Colors.hlsl"
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/Sampling.hlsl"

        TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
        TEXTURE2D_SAMPLER2D(_BloomTex, sampler_BloomTex);
        TEXTURE2D_SAMPLER2D(_AutoExposureTex, sampler_AutoExposureTex);

        float4 _MainTex_TexelSize;
        float  _SampleScale;
        float4 _ColorIntensity;
        float4 _Threshold; // x: threshold value (linear), y: threshold - knee, z: knee * 2, w: 0.25 / knee
        float4 _Params;    // x: clamp, yzw: unused

        // Karis Luminance Weighting (Unreal Engine 4/5 / Karis 2013)
        // Dampens isolated subpixel fireflies on rotating metallic/solar surfaces without dimming broad light sources
        half KarisLumaWeight(half3 color)
        {
            half luma = dot(color, half3(0.2126, 0.7152, 0.0722));
            return 1.0 / (1.0 + luma);
        }

        // Chromaticity-Preserving Soft-Knee Thresholding
        // Retains rich saturated hues (engine exhaust orange, ion blue, green beacons) without flat white washout
        half4 Prefilter(half4 color, float2 uv)
        {
            half autoExposure = SAMPLE_TEXTURE2D(_AutoExposureTex, sampler_AutoExposureTex, uv).r;
            color.rgb *= autoExposure;
            color.rgb = min(_Params.xxx, color.rgb);

            half luma = max(0.0001, dot(color.rgb, half3(0.2126, 0.7152, 0.0722)));
            half rq = clamp(luma - _Threshold.y, 0.0, _Threshold.z);
            rq = _Threshold.w * rq * rq;
            half factor = max(rq, luma - _Threshold.x) / luma;
            factor = saturate(factor);

            return half4(color.rgb * factor, color.a);
        }

        // Karis-Weighted 13-Tap Downsampling (Anti-Firefly)
        half4 DownsampleBox13TapKaris(TEXTURE2D_ARGS(tex, samplerTex), float2 uv, float2 texelSize)
        {
            half4 A = SAMPLE_TEXTURE2D(tex, samplerTex, UnityStereoTransformScreenSpaceTex(uv + texelSize * float2(-1.0, -1.0)));
            half4 B = SAMPLE_TEXTURE2D(tex, samplerTex, UnityStereoTransformScreenSpaceTex(uv + texelSize * float2( 0.0, -1.0)));
            half4 C = SAMPLE_TEXTURE2D(tex, samplerTex, UnityStereoTransformScreenSpaceTex(uv + texelSize * float2( 1.0, -1.0)));
            half4 D = SAMPLE_TEXTURE2D(tex, samplerTex, UnityStereoTransformScreenSpaceTex(uv + texelSize * float2(-0.5, -0.5)));
            half4 E = SAMPLE_TEXTURE2D(tex, samplerTex, UnityStereoTransformScreenSpaceTex(uv + texelSize * float2( 0.5, -0.5)));
            half4 F = SAMPLE_TEXTURE2D(tex, samplerTex, UnityStereoTransformScreenSpaceTex(uv + texelSize * float2(-1.0,  0.0)));
            half4 G = SAMPLE_TEXTURE2D(tex, samplerTex, UnityStereoTransformScreenSpaceTex(uv                                 ));
            half4 H = SAMPLE_TEXTURE2D(tex, samplerTex, UnityStereoTransformScreenSpaceTex(uv + texelSize * float2( 1.0,  0.0)));
            half4 I = SAMPLE_TEXTURE2D(tex, samplerTex, UnityStereoTransformScreenSpaceTex(uv + texelSize * float2(-0.5,  0.5)));
            half4 J = SAMPLE_TEXTURE2D(tex, samplerTex, UnityStereoTransformScreenSpaceTex(uv + texelSize * float2( 0.5,  0.5)));
            half4 K = SAMPLE_TEXTURE2D(tex, samplerTex, UnityStereoTransformScreenSpaceTex(uv + texelSize * float2(-1.0,  1.0)));
            half4 L = SAMPLE_TEXTURE2D(tex, samplerTex, UnityStereoTransformScreenSpaceTex(uv + texelSize * float2( 0.0,  1.0)));
            half4 M = SAMPLE_TEXTURE2D(tex, samplerTex, UnityStereoTransformScreenSpaceTex(uv + texelSize * float2( 1.0,  1.0)));

            // 5 overlapping sub-boxes
            half4 b0 = (D + E + I + J) * 0.25;
            half4 b1 = (A + B + G + F) * 0.25;
            half4 b2 = (B + C + H + G) * 0.25;
            half4 b3 = (F + G + L + K) * 0.25;
            half4 b4 = (G + H + M + L) * 0.25;

            half w0 = KarisLumaWeight(b0.rgb) * 0.5;
            half w1 = KarisLumaWeight(b1.rgb) * 0.125;
            half w2 = KarisLumaWeight(b2.rgb) * 0.125;
            half w3 = KarisLumaWeight(b3.rgb) * 0.125;
            half w4 = KarisLumaWeight(b4.rgb) * 0.125;

            return (b0 * w0 + b1 * w1 + b2 * w2 + b3 * w3 + b4 * w4) / max(0.0001, (w0 + w1 + w2 + w3 + w4));
        }

        // Pass 0: Prefilter 13 taps with Karis anti-flicker
        half4 FragPrefilter13(VaryingsDefault i) : SV_Target
        {
            half4 color = DownsampleBox13TapKaris(TEXTURE2D_PARAM(_MainTex, sampler_MainTex), i.texcoord, UnityStereoAdjustedTexelSize(_MainTex_TexelSize).xy);
            return Prefilter(SafeHDR(color), i.texcoord);
        }

        // Pass 1: Prefilter 4 taps
        half4 FragPrefilter4(VaryingsDefault i) : SV_Target
        {
            half4 color = DownsampleBox4Tap(TEXTURE2D_PARAM(_MainTex, sampler_MainTex), i.texcoord, UnityStereoAdjustedTexelSize(_MainTex_TexelSize).xy);
            return Prefilter(SafeHDR(color), i.texcoord);
        }

        // Pass 2: Downsample 13 taps
        half4 FragDownsample13(VaryingsDefault i) : SV_Target
        {
            return DownsampleBox13Tap(TEXTURE2D_PARAM(_MainTex, sampler_MainTex), i.texcoord, UnityStereoAdjustedTexelSize(_MainTex_TexelSize).xy);
        }

        // Pass 3: Downsample 4 taps
        half4 FragDownsample4(VaryingsDefault i) : SV_Target
        {
            return DownsampleBox4Tap(TEXTURE2D_PARAM(_MainTex, sampler_MainTex), i.texcoord, UnityStereoAdjustedTexelSize(_MainTex_TexelSize).xy);
        }

        // Pass 4 & 5: Upsample and Combine
        half4 Combine(half4 bloom, float2 uv)
        {
            half4 color = SAMPLE_TEXTURE2D(_BloomTex, sampler_BloomTex, uv);
            return bloom + color;
        }

        half4 FragUpsampleTent(VaryingsDefault i) : SV_Target
        {
            half4 bloom = UpsampleTent(TEXTURE2D_PARAM(_MainTex, sampler_MainTex), i.texcoord, UnityStereoAdjustedTexelSize(_MainTex_TexelSize).xy, _SampleScale);
            return Combine(bloom, i.texcoordStereo);
        }

        half4 FragUpsampleBox(VaryingsDefault i) : SV_Target
        {
            half4 bloom = UpsampleBox(TEXTURE2D_PARAM(_MainTex, sampler_MainTex), i.texcoord, UnityStereoAdjustedTexelSize(_MainTex_TexelSize).xy, _SampleScale);
            return Combine(bloom, i.texcoordStereo);
        }

        // Pass 6-8: Debug Overlays
        half4 FragDebugOverlayThreshold(VaryingsDefault i) : SV_Target
        {
            half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoordStereo);
            return half4(Prefilter(SafeHDR(color), i.texcoord).rgb, 1.0);
        }

        half4 FragDebugOverlayTent(VaryingsDefault i) : SV_Target
        {
            half4 bloom = UpsampleTent(TEXTURE2D_PARAM(_MainTex, sampler_MainTex), i.texcoord, UnityStereoAdjustedTexelSize(_MainTex_TexelSize).xy, _SampleScale);
            return half4(bloom.rgb * _ColorIntensity.w * _ColorIntensity.rgb, 1.0);
        }

        half4 FragDebugOverlayBox(VaryingsDefault i) : SV_Target
        {
            half4 bloom = UpsampleBox(TEXTURE2D_PARAM(_MainTex, sampler_MainTex), i.texcoord, UnityStereoAdjustedTexelSize(_MainTex_TexelSize).xy, _SampleScale);
            return half4(bloom.rgb * _ColorIntensity.w * _ColorIntensity.rgb, 1.0);
        }

    ENDHLSL

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // 0: Prefilter 13 taps
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragPrefilter13
            ENDHLSL
        }

        // 1: Prefilter 4 taps
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragPrefilter4
            ENDHLSL
        }

        // 2: Downsample 13 taps
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragDownsample13
            ENDHLSL
        }

        // 3: Downsample 4 taps
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragDownsample4
            ENDHLSL
        }

        // 4: Upsample Tent
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragUpsampleTent
            ENDHLSL
        }

        // 5: Upsample Box
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragUpsampleBox
            ENDHLSL
        }

        // 6: Debug Overlay Threshold
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragDebugOverlayThreshold
            ENDHLSL
        }

        // 7: Debug Overlay Tent
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragDebugOverlayTent
            ENDHLSL
        }

        // 8: Debug Overlay Box
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragDebugOverlayBox
            ENDHLSL
        }
    }
}
