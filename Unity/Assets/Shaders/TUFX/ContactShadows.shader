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
            if (linearDepth <= 0.01 || linearDepth > 2000.0)
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

            // Smooth transition at terminator: eliminates razor-sharp cutoff line on curved cylinders
            float lightFacing = smoothstep(0.0, 0.20, NdotL);
            if (lightFacing <= 0.0001)
            {
                return float4(1.0, 1.0, 1.0, 1.0);
            }

            // Distance fadeout: contact shadows are micro-details, smoothly fade beyond 300m
            float distFade = saturate((300.0 - linearDepth) / 80.0);
            if (distFade <= 0.001)
            {
                return float4(1.0, 1.0, 1.0, 1.0);
            }

            // Contact shadows strictly capture micro-geometry gaps (panel seams, decouplers, landing pads).
            // Ray length is strictly capped to 0.25m so grazing rays NEVER cross and penetrate curved rocket hulls!
            float adaptiveThickness = clamp(max(_Thickness, linearDepth * 0.001), 0.01, 0.08);
            float adaptiveRayLength = clamp(max(_RayLength, linearDepth * 0.003), 0.03, 0.25);

            // Generous normal bias to lift ray origin reliably above polygon facets of curved geometry
            float normalBias = max(0.015, adaptiveThickness * 0.35);
            float3 originPos = centerPos + normal * normalBias;

            // March towards light source in view space
            float3 endPos = originPos + rayDir * adaptiveRayLength;
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

            // Subpixel smooth fade out instead of hard early-out
            float pixelWeight = saturate((rayPixelDist - 0.5) / 2.5);
            if (pixelWeight <= 0.001)
            {
                return float4(1.0, 1.0, 1.0, 1.0);
            }

            // High-frequency dither
            float dither = frac(52.9829189 * frac(dot(i.texcoord * _MainTex_TexelSize.zw, float2(0.06711056, 0.00583715))));

            float invZ_start = 1.0 / originPos.z;
            float invZ_end   = 1.0 / endPos.z;

            // Slope-adaptive bias: prevents cylindrical polygon facets from self-shadowing into checkerboard squares
            float slopeFactor = saturate(1.0 - NdotL);
            float bias = max(0.015, adaptiveThickness * 0.25 + slopeFactor * 0.025 + linearDepth * 0.0008);
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

                if (depthDiff > bias && depthDiff < adaptiveThickness)
                {
                    float occl = 1.0 - saturate(depthDiff / adaptiveThickness);
                    occlusion = max(occlusion, occl);
                    break;
                }
            }

            occlusion *= pixelWeight * distFade;
            float shadow = 1.0 - occlusion * _Intensity * lightFacing;
            return float4(shadow, shadow, shadow, 1.0);
        }

        // Pass 1: 8-tap Edge-preserving bilateral filter for Contact Shadows (eliminates checkerboard dither)
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
            float2 texel = _MainTex_TexelSize.xy * 1.5;

            float sum = centerShadow;
            float totalWeight = 1.0;

            const float2 offsets[8] = {
                float2( 1.2,  0.0), float2(-1.2,  0.0),
                float2( 0.0,  1.2), float2( 0.0, -1.2),
                float2( 0.9,  0.9), float2(-0.9,  0.9),
                float2( 0.9, -0.9), float2(-0.9, -0.9)
            };

            [unroll]
            for (int k = 0; k < 8; k++)
            {
                float2 uv = i.texcoord + offsets[k] * texel;
                float sampleShadow = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).r;
                float sampleRaw = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, uv);
                float sampleDepth = LinearEyeDepth(sampleRaw);

                float depthDiff = abs(centerDepth - sampleDepth);
                float w = exp(-depthDiff / max(0.12, centerDepth * 0.035));

                sum += sampleShadow * w;
                totalWeight += w;
            }

            float filtered = sum / max(0.001, totalWeight);
            return float4(filtered, filtered, filtered, 1.0);
        }

        // Pass 2: Composite Contact Shadows with Scene
        float4 FragContactShadowComposite(VaryingsDefault i) : SV_Target
        {
            float4 scene = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            float shadow = SAMPLE_TEXTURE2D(_ShadowTex, sampler_MainTex, i.texcoord).r;
            return float4(scene.rgb * shadow, scene.a);
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
                #pragma fragment FragContactShadowComposite
            ENDHLSL
        }
    }
}
