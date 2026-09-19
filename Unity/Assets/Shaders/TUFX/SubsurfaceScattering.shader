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
        float _AutoAdapt;
        float4 _SubsurfaceColor;

        // Isotropic separable subsurface kernel weights (neutral diffusion, zero chromatic bias)
        static const float kernelOffsets[6] = { 0.0, 0.05, 0.12, 0.26, 0.58, 1.0 };
        static const float kernelWeights[6] = { 0.24, 0.22, 0.18, 0.14, 0.12, 0.10 };

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
            float totalWeight = kernelWeights[0];

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

                totalColor += (tapColL * wL + tapColR * wR) * kernelWeights[k];
                totalWeight += (wL + wR) * kernelWeights[k];
            }

            float3 blurred = totalColor / max(0.0001, totalWeight);
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
            float totalWeight = kernelWeights[0];

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

                totalColor += (tapColT * wT + tapColB * wB) * kernelWeights[k];
                totalWeight += (wT + wB) * kernelWeights[k];
            }

            float3 blurred = totalColor / max(0.0001, totalWeight);

            // Subsurface diffusion calculation
            float3 diff = blurred - originalCol.rgb;

            float3 tintColor;
            float scatterMask = 1.0;
            if (_AutoAdapt > 0.5)
            {
                // Material Chrominance Auto-Adaptation:
                // Computes the local chrominance vector.
                // On neutral metallic/white satellites (R ≈ G ≈ B), chroma is strictly (1,1,1) -> 0 orange shift!
                // On Kerbals, chroma captures green subcutaneous glow.
                // On icy or colored bodies, chroma adapts to the native hue.
                float luma = max(0.001, dot(originalCol.rgb, float3(0.2126, 0.7152, 0.0722)));
                float3 chroma = clamp(originalCol.rgb / luma, 0.2, 2.5);

                float maxC = max(originalCol.r, max(originalCol.g, originalCol.b));
                float minC = min(originalCol.r, min(originalCol.g, originalCol.b));
                float sat = (maxC - minC) / max(0.01, maxC);

                tintColor = lerp(float3(1.0, 1.0, 1.0), chroma, saturate(sat * 2.0));
                // Temper diffusion on low-saturation hard metallic surfaces to preserve crisp structural edges
                scatterMask = lerp(0.35, 1.0, saturate(sat * 2.5));
            }
            else
            {
                tintColor = _SubsurfaceColor.rgb;
            }

            float3 sssColor = originalCol.rgb + diff * tintColor;

            // Distance smooth fadeout to 0 when approaching _MaxDistance
            float distFade = saturate((_MaxDistance - centerDepth) / max(1.0, _MaxDistance * 0.25));
            float3 result = lerp(originalCol.rgb, sssColor, _Intensity * distFade * scatterMask);
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
