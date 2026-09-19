Shader "Hidden/TUFX/CameraMotionBlur"
{
    HLSLINCLUDE
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"

        TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
        TEXTURE2D_SAMPLER2D(_CameraDepthTexture, sampler_CameraDepthTexture);
        TEXTURE2D_SAMPLER2D(_CameraMotionVectorsTexture, sampler_CameraMotionVectorsTexture);
        float4 _MainTex_TexelSize;

        float4x4 _CurrInvViewProj;
        float4x4 _PrevViewProj;
        float _ShutterScale;
        float _BlurMultiplier;
        float _MaxBlurRadius;
        float _UseMotionVectors;
        int _SampleCount;

        float InterleavedGradientNoise(float2 pixCoord)
        {
            float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
            return frac(magic.z * frac(dot(pixCoord, magic.xy)));
        }

        float2 CalculateVelocity(float2 uv, float rawDepth)
        {
            #if UNITY_UV_STARTS_AT_TOP
            float2 ndcUV = float2(uv.x * 2.0 - 1.0, (1.0 - uv.y) * 2.0 - 1.0);
            #else
            float2 ndcUV = uv * 2.0 - 1.0;
            #endif

            // Sky / Deep Space handling (far plane in reversed-Z or standard-Z)
            #if UNITY_REVERSED_Z
            bool isSky = (rawDepth <= 0.0001);
            #else
            bool isSky = (rawDepth >= 0.9999);
            #endif

            float4 clipPos = float4(ndcUV, isSky ? 0.0005 : rawDepth, 1.0);
            float4 worldH = mul(_CurrInvViewProj, clipPos);
            float3 worldPos = worldH.xyz / max(0.00001, worldH.w);

            if (isSky)
            {
                // Starfield / Deep space at infinity: project rotational ray
                worldPos = normalize(worldPos) * 100000.0;
            }

            float4 prevClip = mul(_PrevViewProj, float4(worldPos, 1.0));
            float2 prevNDC = prevClip.xy / max(0.00001, prevClip.w);

            #if UNITY_UV_STARTS_AT_TOP
            float2 prevUV = float2(prevNDC.x * 0.5 + 0.5, 1.0 - (prevNDC.y * 0.5 + 0.5));
            #else
            float2 prevUV = prevNDC * 0.5 + 0.5;
            #endif

            float2 velocity = (uv - prevUV) * (_ShutterScale * _BlurMultiplier);
            return velocity;
        }

        float4 FragCameraMotionBlur(VaryingsDefault i) : SV_Target
        {
            float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord);
            float2 velocity = 0.0;

            if (_UseMotionVectors > 0.5)
            {
                float2 mv = SAMPLE_TEXTURE2D(_CameraMotionVectorsTexture, sampler_CameraMotionVectorsTexture, i.texcoord).rg;
                if (dot(mv, mv) > 0.0000001)
                {
                    velocity = mv * (_ShutterScale * _BlurMultiplier);
                }
                else
                {
                    velocity = CalculateVelocity(i.texcoord, rawDepth);
                }
            }
            else
            {
                velocity = CalculateVelocity(i.texcoord, rawDepth);
            }

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

            #if UNITY_REVERSED_Z
            bool centerIsSky = (rawDepth <= 0.0001);
            #else
            bool centerIsSky = (rawDepth >= 0.9999);
            #endif
            float centerDepth = centerIsSky ? 100000.0 : LinearEyeDepth(rawDepth);

            float jitter = InterleavedGradientNoise(i.texcoord * _MainTex_TexelSize.zw);
            int samples = clamp(_SampleCount, 4, 16);

            float4 col = float4(0, 0, 0, 0);
            float totalWeight = 0.0;

            [unroll(16)]
            for (int s = 0; s < 16; s++)
            {
                if (s >= samples) break;

                float t = ((float(s) + jitter) / float(samples)) - 0.5;
                float2 sampleUV = i.texcoord + velocity * t;

                // Border clamp
                sampleUV = clamp(sampleUV, 0.001, 0.999);

                // Silhouette protection: background rays must NOT sample foreground rocket geometry
                float tapRawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, sampleUV);
                #if UNITY_REVERSED_Z
                bool tapIsSky = (tapRawDepth <= 0.0001);
                #else
                bool tapIsSky = (tapRawDepth >= 0.9999);
                #endif
                float tapDepth = tapIsSky ? 100000.0 : LinearEyeDepth(tapRawDepth);

                float depthWeight = 1.0;
                if (!centerIsSky && tapDepth < centerDepth * 0.75)
                {
                    // Foreground occluder in front of center pixel: reject to prevent dark hull smearing
                    depthWeight = saturate((tapDepth - centerDepth * 0.4) / max(0.1, centerDepth * 0.35));
                }
                else if (centerIsSky && !tapIsSky && tapDepth < 5000.0)
                {
                    // Sky background sampling foreground rocket: reject
                    depthWeight = 0.0;
                }

                // Triangular weight centered at current pixel
                float w = (1.0 - abs(t * 2.0)) * depthWeight;
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
