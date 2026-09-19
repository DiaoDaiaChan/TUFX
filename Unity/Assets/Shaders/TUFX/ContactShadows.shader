Shader "Hidden/TUFX/ContactShadows"
{
    HLSLINCLUDE
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"

        TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
        TEXTURE2D_SAMPLER2D(_CameraDepthTexture, sampler_CameraDepthTexture);
        TEXTURE2D_SAMPLER2D(_ShadowTex, sampler_ShadowTex);
        float4 _MainTex_TexelSize;
        float2 _NDCToViewMul;
        float2 _NDCToViewAdd;
        float3 _LightDirView; // Light direction in view space
        float _RayLength;
        int _RaySteps;
        float _Intensity;
        float _Thickness;

        float3 ReconstructViewPos(float2 uv, float linearDepth)
        {
            float3 ret;
            ret.xy = (_NDCToViewMul * uv + _NDCToViewAdd) * linearDepth;
            ret.z = linearDepth;
            return ret;
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

            float2 texel = _MainTex_TexelSize.xy;
            float pixLZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord + float2(-texel.x, 0)));
            float pixRZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord + float2( texel.x, 0)));
            float pixTZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord + float2(0,  texel.y)));
            float pixBZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord + float2(0, -texel.y)));

            float3 centerPos = ReconstructViewPos(i.texcoord, linearDepth);
            float3 leftPos   = ReconstructViewPos(i.texcoord + float2(-texel.x, 0), pixLZ);
            float3 rightPos  = ReconstructViewPos(i.texcoord + float2( texel.x, 0), pixRZ);
            float3 topPos    = ReconstructViewPos(i.texcoord + float2(0,  texel.y), pixTZ);
            float3 botPos    = ReconstructViewPos(i.texcoord + float2(0, -texel.y), pixBZ);

            float3 dx = rightPos - leftPos;
            float3 dy = topPos - botPos;
            // Cross product: Top x Right points towards camera in our left-handed view space
            float3 normal = cross(dy, dx);
            float lenSq = dot(normal, normal);
            if (lenSq > 0.0001)
            {
                normal *= rsqrt(lenSq);
            }
            else
            {
                normal = float3(0.0, 0.0, -1.0);
            }

            float3 viewVec = normalize(-centerPos);
            if (dot(normal, viewVec) < 0.0)
            {
                normal = -normal;
            }

            float3 rayDir = normalize(_LightDirView);
            float NdotL = dot(normal, rayDir);

            // Backside or steep grazing angle surfaces cannot receive direct sunlight contact shadows.
            // Smoothly fade out when approaching terminator to eliminate edge acne.
            if (NdotL <= 0.0)
            {
                return float4(1.0, 1.0, 1.0, 1.0);
            }
            float lightFacing = saturate(NdotL * 5.0);

            // Normal bias pushes ray origin slightly above the surface, preventing curved surfaces from self-shadowing
            float normalBias = max(0.003, linearDepth * 0.0015);
            float3 originPos = centerPos + normal * normalBias;

            // Project 3D end position to screen UV without relying on inversion-prone projection matrices
            float3 endPos = originPos + rayDir * _RayLength;
            if (endPos.z <= 0.01) return float4(1.0, 1.0, 1.0, 1.0);

            float2 startUV;
            startUV.x = (originPos.x / originPos.z - _NDCToViewAdd.x) / _NDCToViewMul.x;
            startUV.y = (originPos.y / originPos.z - _NDCToViewAdd.y) / _NDCToViewMul.y;

            float2 endUV;
            endUV.x = (endPos.x / endPos.z - _NDCToViewAdd.x) / _NDCToViewMul.x;
            endUV.y = (endPos.y / endPos.z - _NDCToViewAdd.y) / _NDCToViewMul.y;

            float2 rayDeltaUV = endUV - startUV;
            float2 rayPixelDelta = rayDeltaUV * _MainTex_TexelSize.zw;
            float rayPixelDist = length(rayPixelDelta);

            // Subpixel early-out: if ray is shorter than 1.5 pixels, contact shadows cannot be resolved
            if (rayPixelDist < 1.5)
            {
                return float4(1.0, 1.0, 1.0, 1.0);
            }

            // High-frequency dither to eliminate banding
            float dither = frac(52.9829189 * frac(dot(i.texcoord * _MainTex_TexelSize.zw, float2(0.06711056, 0.00583715))));

            float invZ_start = 1.0 / originPos.z;
            float invZ_end   = 1.0 / endPos.z;

            float bias = max(0.006, linearDepth * 0.002);
            float occlusion = 0.0;

            [unroll(16)]
            for (int s = 1; s <= _RaySteps; s++)
            {
                float t = ((float)s - 0.5 + (dither - 0.5) * 0.8) / (float)_RaySteps;
                float2 sampleUV = startUV + rayDeltaUV * t;
                if (sampleUV.x < 0.0 || sampleUV.x > 1.0 || sampleUV.y < 0.0 || sampleUV.y > 1.0)
                    break;

                float sampleRawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, sampleUV);
                #if UNITY_REVERSED_Z
                    if (sampleRawDepth <= 0.00001) continue;
                #else
                    if (sampleRawDepth >= 0.99999) continue;
                #endif

                float sampleLinearDepth = LinearEyeDepth(sampleRawDepth);

                // Perspective-correct depth along the screen-space ray
                float expectedDepth = 1.0 / lerp(invZ_start, invZ_end, t);
                float depthDiff = expectedDepth - sampleLinearDepth;

                if (depthDiff > bias && depthDiff < _Thickness)
                {
                    float occl = 1.0 - saturate(depthDiff / _Thickness);
                    occlusion = max(occlusion, occl);
                    break;
                }
            }

            // Smooth scale-independent pixel-span fade out
            if (rayPixelDist < 5.0)
            {
                float pixelFade = saturate((rayPixelDist - 1.5) / 3.5);
                occlusion *= pixelFade;
            }

            float shadow = 1.0 - occlusion * _Intensity * lightFacing;
            return float4(shadow, shadow, shadow, 1.0);
        }

        // Pass 1: Edge-preserving bilateral filter for Contact Shadows
        float4 FragContactShadowDenoise(VaryingsDefault i) : SV_Target
        {
            float centerShadow = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord).r;
            float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord);
            #if UNITY_REVERSED_Z
                if (rawDepth <= 0.00001) return float4(1.0, 1.0, 1.0, 1.0);
            #else
                if (rawDepth >= 0.99999) return float4(1.0, 1.0, 1.0, 1.0);
            #endif

            float centerDepth = LinearEyeDepth(rawDepth);
            float2 texel = _MainTex_TexelSize.xy * 1.25;

            float sum = centerShadow;
            float totalWeight = 1.0;

            const float2 offsets[4] = {
                float2( 1.0,  0.0), float2(-1.0,  0.0),
                float2( 0.0,  1.0), float2( 0.0, -1.0)
            };

            [unroll]
            for (int k = 0; k < 4; k++)
            {
                float2 uv = i.texcoord + offsets[k] * texel;
                float tapDepth = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, uv));
                float depthDiff = abs(centerDepth - tapDepth);

                float weight = exp(-depthDiff / max(0.01, centerDepth * 0.02));
                float tapShadow = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).r;

                sum += tapShadow * weight;
                totalWeight += weight;
            }

            float filtered = sum / max(0.001, totalWeight);
            return float4(filtered, filtered, filtered, 1.0);
        }

        // Pass 2: Composite Shadow with Scene Color
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

        // 1: Denoise
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragContactShadowDenoise
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
