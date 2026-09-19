Shader "Hidden/TUFX/GodRays"
{
    HLSLINCLUDE
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"

        TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
        TEXTURE2D_SAMPLER2D(_CameraDepthTexture, sampler_CameraDepthTexture);
        TEXTURE2D_SAMPLER2D(_RaysTex, sampler_RaysTex);
        float4 _MainTex_TexelSize;
        float2 _SunScreenPos; // Viewport UV of the Sun
        float _SunVisible;    // 1 if Sun is in front of camera hemisphere, 0 if behind
        float _Threshold;
        float _Density;
        float _Decay;
        float _Weight;
        float _Intensity;
        float4 _RayColor;

        // Pass 0: Extract unoccluded sun / sky light with foreground geometric occlusion
        float4 FragExtract(VaryingsDefault i) : SV_Target
        {
            if (_SunVisible <= 0.0)
            {
                return float4(0, 0, 0, 0);
            }

            float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord);
            float linearDepth = LinearEyeDepth(rawDepth);

            // Foreground geometry (< 150m: rocket, launch pad, local terrain) blocks sunlight and casts rays
            float passThrough = saturate((linearDepth - 10.0) / 140.0);
            #if UNITY_REVERSED_Z
                if (rawDepth <= 0.0001) passThrough = 1.0;
            #else
                if (rawDepth >= 0.9999) passThrough = 1.0;
            #endif

            float4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            float luma = dot(col.rgb, float3(0.2126, 0.7152, 0.0722));

            // Radial falloff from sun center (wide smooth corona so rays fan across entire screen)
            float2 sunVec = (i.texcoord - _SunScreenPos) * float2(_MainTex_TexelSize.z / _MainTex_TexelSize.w, 1.0);
            float sunDist = length(sunVec);
            float coronaFalloff = exp(-sunDist * 1.5);

            float brightness = saturate((luma - _Threshold) / max(0.01, _Threshold));
            float lightEnergy = brightness * passThrough * (coronaFalloff * 0.7 + 0.3);

            return float4(col.rgb * lightEnergy, 1.0);
        }

        // Pass 1: Radial blur raymarching (Volumetric Light Scattering)
        float4 FragRadialBlur(VaryingsDefault i) : SV_Target
        {
            if (_SunVisible <= 0.0)
            {
                return float4(0, 0, 0, 0);
            }

            // High-frequency jitter to dissolve discrete ray steps
            float dither = frac(52.9829189 * frac(dot(i.texcoord * _MainTex_TexelSize.zw, float2(0.06711056, 0.00583715))));
            float2 deltaUV = (i.texcoord - _SunScreenPos) * (1.0 / 32.0) * _Density;
            float2 uv = i.texcoord - deltaUV * (dither * 0.75);
            float illuminationDecay = 1.0;
            float4 color = float4(0, 0, 0, 0);

            [unroll(32)]
            for (int s = 0; s < 32; s++)
            {
                float4 sampleColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                color += sampleColor * (illuminationDecay * _Weight);
                illuminationDecay *= _Decay;
                uv -= deltaUV;
            }

            return color * (_Intensity * _RayColor);
        }

        // Pass 2: Additive Composite with Scene Color
        float4 FragComposite(VaryingsDefault i) : SV_Target
        {
            float4 scene = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            float4 rays = SAMPLE_TEXTURE2D(_RaysTex, sampler_RaysTex, i.texcoord);
            return float4(scene.rgb + rays.rgb, scene.a);
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

        // 1: Radial Blur
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragRadialBlur
            ENDHLSL
        }

        // 2: Composite
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragComposite
            ENDHLSL
        }
    }
}
