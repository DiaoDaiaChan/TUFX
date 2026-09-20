Shader "Hidden/TUFX/Halation"
{
    HLSLINCLUDE
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/Colors.hlsl"
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/Sampling.hlsl"

        TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
        TEXTURE2D_SAMPLER2D(_HalationTightTex, sampler_HalationTightTex);
        TEXTURE2D_SAMPLER2D(_HalationWideTex, sampler_HalationWideTex);
        TEXTURE2D_SAMPLER2D(_HalationBaseTex, sampler_HalationBaseTex);

        float4 _MainTex_TexelSize;
        float _Threshold;
        float _Intensity;
        float _Radius;
        float4 _ColorTint; // CineStill warm orange-red: float4(1.0, 0.36, 0.12, 1.0)
        float _SampleScale;

        // Pass 0: Soft-Knee Threshold Extraction & Film Emulsion Penetration
        float4 FragExtract(VaryingsDefault i) : SV_Target
        {
            float4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            float luma = dot(color.rgb, float3(0.2126, 0.7152, 0.0722));

            // Soft-knee threshold to prevent harsh cutoffs while protecting daytime diffuse hulls
            float knee = max(0.1, _Threshold * 0.25);
            float minThresh = max(0.0, _Threshold - knee);
            float factor = 0.0;
            if (luma > minThresh)
            {
                float rq = clamp(luma - minThresh, 0.0, 2.0 * knee);
                rq = (0.25 / knee) * rq * rq;
                factor = max(rq, luma - _Threshold) / max(luma, 1e-4);
            }

            float3 halationSeed = color.rgb * factor;
            return float4(SafeHDR(halationSeed), 1.0);
        }

        // Pass 1: Anti-Aliased 13-Tap Box Downsampling (Continuous Jimenez Filter)
        float4 FragDownsample(VaryingsDefault i) : SV_Target
        {
            half4 col = DownsampleBox13Tap(TEXTURE2D_PARAM(_MainTex, sampler_MainTex), i.texcoord, UnityStereoAdjustedTexelSize(_MainTex_TexelSize).xy);
            return half4(SafeHDR(col).rgb, 1.0);
        }

        // Pass 2: Continuous 9-Tap Tent Upsampling with Additive Base Accumulation
        float4 FragUpsample(VaryingsDefault i) : SV_Target
        {
            half4 upsampled = UpsampleTent(TEXTURE2D_PARAM(_MainTex, sampler_MainTex), i.texcoord, UnityStereoAdjustedTexelSize(_MainTex_TexelSize).xy, _SampleScale);
            half3 base = SAMPLE_TEXTURE2D(_HalationBaseTex, sampler_MainTex, i.texcoord).rgb;
            return half4(base + upsampled.rgb, 1.0);
        }

        // Pass 3: Dual-Scale CineStill Halation Composite onto Scene
        float4 FragComposite(VaryingsDefault i) : SV_Target
        {
            float4 scene = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            float3 tightGlow = SAMPLE_TEXTURE2D(_HalationTightTex, sampler_MainTex, i.texcoord).rgb;
            float3 wideBleed = SAMPLE_TEXTURE2D(_HalationWideTex, sampler_MainTex, i.texcoord).rgb;

            // CineStill 800T characteristic: sharp red boundary glow + broad warm halo
            float3 halation = (tightGlow * 0.6 + wideBleed * 0.4) * (_Intensity * _ColorTint.rgb);
            // Highlight tone compression to prevent nuclear blowout on extreme bright sources
            float halationLuma = dot(halation, float3(0.2126, 0.7152, 0.0722));
            halation = halation / (1.0 + 0.35 * halationLuma);
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

        // 1: Downsample
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragDownsample
            ENDHLSL
        }

        // 2: Upsample
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragUpsample
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
