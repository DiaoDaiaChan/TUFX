Shader "Hidden/TUFX/ContactShadows"
{
    HLSLINCLUDE
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"

        TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
        TEXTURE2D_SAMPLER2D(_CameraDepthTexture, sampler_CameraDepthTexture);
        TEXTURE2D_SAMPLER2D(_ShadowTex, sampler_ShadowTex);
        float4 _MainTex_TexelSize;
        float3 _LightDirView; // Light direction in view space
        float _RayLength;
        int _RaySteps;
        float _Intensity;
        float _Thickness;

        float3 ReconstructViewPos(float2 uv, float linearDepth)
        {
            float2 p11_22 = float2(unity_CameraProjection._11, unity_CameraProjection._22);
            float2 clipPos = (uv * 2.0 - 1.0);
            return float3(clipPos / p11_22 * linearDepth, linearDepth);
        }

        // Pass 0: Raymarch contact shadows
        float4 FragContactShadows(VaryingsDefault i) : SV_Target
        {
            float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord);
            #if UNITY_REVERSED_Z
                if (rawDepth <= 0.00001) return float4(1.0, 1.0, 1.0, 1.0);
            #else
                if (rawDepth >= 0.99999) return float4(1.0, 1.0, 1.0, 1.0);
            #endif

            float linearDepth = LinearEyeDepth(rawDepth);
            if (linearDepth <= 0.01)
            {
                return float4(1.0, 1.0, 1.0, 1.0);
            }

            float3 originPos = ReconstructViewPos(i.texcoord, linearDepth);
            float3 rayDir = normalize(_LightDirView);

            // Project light direction into screen space
            float3 endPos = originPos + rayDir * _RayLength;
            float4 endClip = mul(unity_CameraProjection, float4(endPos, 1.0));
            if (abs(endClip.w) < 0.0001) return float4(1.0, 1.0, 1.0, 1.0);
            float2 endUV = (endClip.xy / endClip.w) * 0.5 + 0.5;

            // Adaptive Scale-Independent Metric (Zero hardcoded distance!):
            // Measure the ray length in actual screen pixels.
            // In any celestial scale (Stock, RSS, JNSQ, Beyond Home), distant celestial bodies
            // or terrain project a 15cm ray to a tiny subpixel fraction (< 2.0 pixels).
            // Raymarching subpixel steps on distant curved bodies produces depth-quantization artifacts!
            float2 rayPixelDelta = (endUV - i.texcoord) * _MainTex_TexelSize.zw;
            float rayPixelDist = length(rayPixelDelta);

            if (rayPixelDist < 2.0)
            {
                return float4(1.0, 1.0, 1.0, 1.0);
            }

            // Interleaved gradient noise jitter to eliminate discrete stair-step shadow banding
            float dither = frac(52.9829189 * frac(dot(i.texcoord * _MainTex_TexelSize.zw, float2(0.06711056, 0.00583715))));
            float2 rayDeltaUV = (endUV - i.texcoord);

            float bias = max(0.005, linearDepth * 0.001);
            float shadow = 1.0;
            [unroll(16)]
            for (int s = 1; s <= _RaySteps; s++)
            {
                float t = ((float)s - 0.5 + (dither - 0.5) * 0.95) / (float)_RaySteps;
                float2 sampleUV = i.texcoord + rayDeltaUV * t;
                if (sampleUV.x < 0.0 || sampleUV.x > 1.0 || sampleUV.y < 0.0 || sampleUV.y > 1.0)
                    break;

                float sampleRawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, sampleUV);
                #if UNITY_REVERSED_Z
                    if (sampleRawDepth <= 0.00001) continue;
                #else
                    if (sampleRawDepth >= 0.99999) continue;
                #endif

                float sampleLinearDepth = LinearEyeDepth(sampleRawDepth);

                float expectedDepth = originPos.z + rayDir.z * (_RayLength * t);
                float depthDiff = expectedDepth - sampleLinearDepth;

                if (depthDiff > bias && depthDiff < _Thickness)
                {
                    shadow = 1.0 - _Intensity;
                    break;
                }
            }

            // Smooth scale-independent pixel-span fade out
            if (rayPixelDist < 5.0)
            {
                float pixelFade = saturate((rayPixelDist - 2.0) / 3.0);
                shadow = lerp(1.0, shadow, pixelFade);
            }

            return float4(shadow, shadow, shadow, 1.0);
        }

        // Pass 1: Composite Shadow with Scene Color
        float4 FragComposite(VaryingsDefault i) : SV_Target
        {
            float4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            float shadow = SAMPLE_TEXTURE2D(_ShadowTex, sampler_ShadowTex, i.texcoord).r;
            shadow = saturate(shadow);
            return float4(col.rgb * shadow, col.a);
        }
    ENDHLSL

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // 0: Raymarch
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragContactShadows
            ENDHLSL
        }

        // 1: Composite
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragComposite
            ENDHLSL
        }
    }
}
