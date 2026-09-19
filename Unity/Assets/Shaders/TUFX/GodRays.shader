Shader "Hidden/TUFX/GodRays"
{
    HLSLINCLUDE
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"

        TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
        TEXTURE2D_SAMPLER2D(_CameraDepthTexture, sampler_CameraDepthTexture);
        TEXTURE2D(_RaysTex);
        float4 _MainTex_TexelSize;
        float2 _SunScreenPos; // Viewport UV of the Sun
        float _SunVisible;    // 1 if Sun is in front of camera hemisphere, 0 if behind
        float _Threshold;
        float _Density;
        float _Decay;
        float _Weight;
        float _Intensity;
        float4 _RayColor;

        // Pass 0: Procedural Sun Light Source with Scene Depth Occlusion
        // Only the Sun emits God Rays; spacecraft and engines can ONLY block light (casting shadows)
        float4 FragExtract(VaryingsDefault i) : SV_Target
        {
            if (_SunVisible <= 0.0)
            {
                return float4(0, 0, 0, 0);
            }

            float2 sunVec = (i.texcoord - _SunScreenPos) * float2(_MainTex_TexelSize.z / _MainTex_TexelSize.w, 1.0);
            float sunDist = length(sunVec);

            // Confine procedural solar light disc and corona
            if (sunDist > 0.35)
            {
                return float4(0, 0, 0, 0);
            }

            // Core solar disc + exponential corona
            float sunDisc = saturate((0.04 - sunDist) / 0.015) * 3.0;
            float sunCorona = exp(-sunDist * 14.0) * 1.2;
            float sunSource = sunDisc + sunCorona;

            // Depth occlusion: spacecraft, vessels, and terrain block the Sun
            float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord);
            #if UNITY_REVERSED_Z
                bool isSky = (rawDepth <= 0.0001);
            #else
                bool isSky = (rawDepth >= 0.9999);
            #endif
            float linearDepth = LinearEyeDepth(rawDepth);

            // If an object is closer than 3000m and not sky, it fully blocks the sun
            float occl = (isSky || linearDepth > 3000.0) ? 1.0 : saturate((linearDepth - 20.0) / 100.0);

            return float4(sunSource * occl, sunSource * occl, sunSource * occl, 1.0);
        }

        // Pass 1: Radial blur marching towards the Sun with continuous IGN jitter
        float4 FragRadialBlur(VaryingsDefault i) : SV_Target
        {
            if (_SunVisible <= 0.0)
            {
                return float4(0, 0, 0, 0);
            }

            float dither = frac(52.9829189 * frac(dot(i.texcoord * _MainTex_TexelSize.zw, float2(0.06711056, 0.00583715))));
            // Step towards Sun from current pixel
            float2 toSun = _SunScreenPos - i.texcoord;
            float2 stepDelta = toSun * (1.0 / 36.0) * _Density;
            float2 uv = i.texcoord + stepDelta * (dither * 0.85);
            float illuminationDecay = 1.0;
            float3 color = float3(0, 0, 0);

            [unroll(36)]
            for (int s = 0; s < 36; s++)
            {
                float3 sampleColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb;
                color += sampleColor * (illuminationDecay * _Weight);
                illuminationDecay *= _Decay;
                uv += stepDelta;
            }

            return float4(color * (_Intensity * _RayColor.rgb), 1.0);
        }

        // Pass 2: Additive Composite
        float4 FragComposite(VaryingsDefault i) : SV_Target
        {
            float4 scene = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            float3 rays = SAMPLE_TEXTURE2D(_RaysTex, sampler_MainTex, i.texcoord).rgb;
            return float4(scene.rgb + rays, scene.a);
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
