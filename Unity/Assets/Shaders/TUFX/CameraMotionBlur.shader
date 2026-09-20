Shader "Hidden/TUFX/CameraMotionBlur"
{
    HLSLINCLUDE
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"

        TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
        TEXTURE2D_SAMPLER2D(_CameraDepthTexture, sampler_CameraDepthTexture);
        float4 _MainTex_TexelSize;

        float2 _NDCToViewMul;
        float2 _NDCToViewAdd;
        float4x4 _RotMatrix;
        float3 _CamTranslationView;
        float _VesselMaxDepth;
        float _ShutterScale;
        float _BlurMultiplier;
        float _MaxBlurRadius;
        int _SampleCount;

        float InterleavedGradientNoise(float2 pixCoord)
        {
            float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
            return frac(magic.z * frac(dot(pixCoord, magic.xy)));
        }

        float3 ReconstructViewPos(float2 uv, float linearDepth)
        {
            float3 ret;
            ret.xy = (_NDCToViewMul * uv + _NDCToViewAdd) * linearDepth;
            ret.z = linearDepth;
            return ret;
        }

        float2 CalculateVelocity(float2 uv, float rawDepth, float linearDepth)
        {
            #if UNITY_REVERSED_Z
            bool isSky = (rawDepth <= 0.0001);
            #else
            bool isSky = (rawDepth >= 0.9999);
            #endif

            // For sky/starfield, use a fixed deep distance to capture rotational motion
            float z = isSky ? 20000.0 : linearDepth;
            float3 viewPos = ReconstructViewPos(uv, z);

            // Determine if this pixel is part of the external world terrain rushing past
            // or the tracked vessel (which moves along with the camera)
            bool isWorld = (!isSky && _VesselMaxDepth > 0.0 && linearDepth > _VesselMaxDepth);

            // Rotate into previous camera orientation
            float3 prevViewPos = mul((float3x3)_RotMatrix, viewPos);

            // External world terrain/clouds receives camera translation (speed streaks)
            if (isWorld)
            {
                prevViewPos += _CamTranslationView;
            }

            if (prevViewPos.z <= 0.01)
            {
                prevViewPos.z = 0.01;
            }

            // Project to previous screen UV
            float2 prevUV;
            prevUV.x = (prevViewPos.x / prevViewPos.z - _NDCToViewAdd.x) / _NDCToViewMul.x;
            prevUV.y = (prevViewPos.y / prevViewPos.z - _NDCToViewAdd.y) / _NDCToViewMul.y;

            float2 velocity = (uv - prevUV) * (_ShutterScale * _BlurMultiplier);
            return velocity;
        }

        float4 FragCameraMotionBlur(VaryingsDefault i) : SV_Target
        {
            float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord);
            #if UNITY_REVERSED_Z
            bool centerIsSky = (rawDepth <= 0.0001);
            #else
            bool centerIsSky = (rawDepth >= 0.9999);
            #endif
            float linearDepth = centerIsSky ? 20000.0 : LinearEyeDepth(rawDepth);

            float2 velocity = CalculateVelocity(i.texcoord, rawDepth, linearDepth);
            float speedInPixels = length(velocity * _MainTex_TexelSize.zw);

            // Strict deadzone: below 1.5 screen pixels, return 100% crisp raw scene
            // Completely preserves native sharpness for stationary launchpad or slow orbital flight
            if (speedInPixels < 1.5)
            {
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            }

            // Smooth transition into motion blur to eliminate popping
            float blurFade = smoothstep(1.5, 3.5, speedInPixels);

            // Clamp max velocity in pixel space
            if (speedInPixels > _MaxBlurRadius)
            {
                velocity = (velocity / speedInPixels) * _MaxBlurRadius;
                speedInPixels = _MaxBlurRadius;
            }

            float jitter = InterleavedGradientNoise(i.texcoord * _MainTex_TexelSize.zw);
            int samples = clamp(_SampleCount, 4, 24);

            float4 col = float4(0, 0, 0, 0);
            float totalWeight = 0.0;

            [unroll(24)]
            for (int s = 0; s < 24; s++)
            {
                if (s >= samples) break;

                float t = ((float(s) + jitter) / float(samples)) - 0.5;
                float2 sampleUV = i.texcoord + velocity * t;

                // Border clamp
                sampleUV = clamp(sampleUV, 0.001, 0.999);

                // Silhouette protection:
                float tapRawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, sampleUV);
                #if UNITY_REVERSED_Z
                bool tapIsSky = (tapRawDepth <= 0.0001);
                #else
                bool tapIsSky = (tapRawDepth >= 0.9999);
                #endif
                float tapDepth = tapIsSky ? 20000.0 : LinearEyeDepth(tapRawDepth);

                float depthWeight = 1.0;
                if (!centerIsSky && tapDepth < linearDepth * 0.6)
                {
                    depthWeight = saturate((tapDepth - linearDepth * 0.3) / max(0.1, linearDepth * 0.3));
                }

                // Smooth bell / triangular weight
                float w = (1.0 - abs(t * 1.8)) * depthWeight;
                col += SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, sampleUV, 0.0) * w;
                totalWeight += w;
            }

            float4 original = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            if (totalWeight <= 0.001)
            {
                return original;
            }

            float4 blurred = col / totalWeight;
            return lerp(original, blurred, blurFade);
        }
    ENDHLSL

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragCameraMotionBlur
            ENDHLSL
        }
    }
}
