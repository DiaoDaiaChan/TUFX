Shader "Hidden/TUFX/GodRays"
{
    HLSLINCLUDE
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"

        TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
        TEXTURE2D_SAMPLER2D(_CameraDepthTexture, sampler_CameraDepthTexture);
        TEXTURE2D_SAMPLER2D(_RaysTex, sampler_RaysTex);
        float4 _MainTex_TexelSize;
        float2 _SunScreenPos; // Viewport UV of the Sun (0..1)
        float _SunVisible;    // 1 if Sun is in front of camera, 0 if behind
        float _SunDiscRadius; // Apparent sun disc radius, in screen heights
        float _Density;
        float _Decay;
        float _Weight;
        float _Intensity;
        float4 _RayColor;

        // Pass 0: Extract unoccluded sun/sky light mask
        float4 FragExtract(VaryingsDefault i) : SV_Target
        {
            if (_SunVisible <= 0.0)
            {
                return float4(0, 0, 0, 0);
            }

            // Constrain light extraction to the sun disc and surrounding corona neighborhood
            float2 sunVec = (i.texcoord - _SunScreenPos);
            float sunDist = length(sunVec * float2(_MainTex_TexelSize.z / _MainTex_TexelSize.w, 1.0));
            float maskRadius = max(_SunDiscRadius * 3.5, 0.08);
            if (sunDist > maskRadius)
            {
                return float4(0, 0, 0, 0);
            }

            float depth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord);
            // Non-geometry (depth == 0 or 1 depending on reversed-Z) transmits sunlight
            #if UNITY_REVERSED_Z
                float isSky = (depth <= 0.0001) ? 1.0 : 0.0;
            #else
                float isSky = (depth >= 0.9999) ? 1.0 : 0.0;
            #endif

            float4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            float luma = dot(col.rgb, float3(0.2126, 0.7152, 0.0722));
            float sunFalloff = saturate(1.0 - (sunDist / maskRadius));
            sunFalloff = sunFalloff * sunFalloff;

            float extractVal = isSky * saturate((luma - 0.15) * 1.5) * sunFalloff;
            return float4(col.rgb * extractVal, 1.0);
        }

        // Pass 1: Radial blur raymarching (GPU Gems 3 Volumetric Light Scattering)
        float4 FragRadialBlur(VaryingsDefault i) : SV_Target
        {
            if (_SunVisible <= 0.0)
            {
                return float4(0, 0, 0, 0);
            }

            // Interleaved gradient noise to jitter radial samples and eliminate discrete shadow banding
            float dither = frac(52.9829189 * frac(dot(i.texcoord * _MainTex_TexelSize.zw, float2(0.06711056, 0.00583715))));
            float2 deltaUV = (i.texcoord - _SunScreenPos) * (1.0 / 32.0) * _Density;
            float2 uv = i.texcoord - deltaUV * dither;
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

            // Multiply accumulated light rays by Intensity and Tint
            return color * _Intensity * _RayColor;
        }

        // Pass 2: Composite God Rays onto Scene Color
        float4 FragComposite(VaryingsDefault i) : SV_Target
        {
            float4 scene = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            float4 rays = SAMPLE_TEXTURE2D(_RaysTex, sampler_RaysTex, i.texcoord);
            return scene + rays;
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
