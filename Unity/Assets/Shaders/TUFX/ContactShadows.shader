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

        float3 ReconstructViewPos(float2 uv, float depth)
        {
            float linearDepth = LinearEyeDepth(depth);
            float2 p11_22 = float2(unity_CameraProjection._11, unity_CameraProjection._22);
            float2 clipPos = (uv * 2.0 - 1.0);
            return float3(clipPos / p11_22 * linearDepth, linearDepth);
        }

        // Pass 0: Raymarch contact shadows
        float4 FragContactShadows(VaryingsDefault i) : SV_Target
        {
            float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord);
            if (rawDepth <= 0.00001 || rawDepth >= 0.99999)
            {
                return float4(1.0, 1.0, 1.0, 1.0);
            }

            float3 originPos = ReconstructViewPos(i.texcoord, rawDepth);
            float3 rayDir = normalize(_LightDirView);

            // Project light direction into screen space
            float3 endPos = originPos + rayDir * _RayLength;
            float4 endClip = mul(unity_CameraProjection, float4(endPos, 1.0));
            float2 endUV = (endClip.xy / endClip.w) * 0.5 + 0.5;
            float2 rayStepUV = (endUV - i.texcoord) / (float)_RaySteps;

            float shadow = 1.0;
            [unroll(16)]
            for (int s = 1; s <= _RaySteps; s++)
            {
                float2 sampleUV = i.texcoord + rayStepUV * (float)s;
                if (sampleUV.x < 0.0 || sampleUV.x > 1.0 || sampleUV.y < 0.0 || sampleUV.y > 1.0)
                    break;

                float sampleRawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, sampleUV);
                float sampleLinearDepth = LinearEyeDepth(sampleRawDepth);

                float expectedDepth = originPos.z + rayDir.z * (_RayLength * (float)s / (float)_RaySteps);
                float depthDiff = expectedDepth - sampleLinearDepth;

                if (depthDiff > 0.005 && depthDiff < _Thickness)
                {
                    shadow = 1.0 - _Intensity;
                    break;
                }
            }

            return float4(shadow, shadow, shadow, 1.0);
        }

        // Pass 1: Composite Shadow with Scene Color
        float4 FragComposite(VaryingsDefault i) : SV_Target
        {
            float4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            float shadow = SAMPLE_TEXTURE2D(_ShadowTex, sampler_ShadowTex, i.texcoord).r;
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
