Shader "Hidden/TUFX/ContactShadows"
{
    HLSLINCLUDE
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"

        TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
        TEXTURE2D_SAMPLER2D(_CameraDepthTexture, sampler_CameraDepthTexture);
        TEXTURE2D_SAMPLER2D(_ShadowTex, sampler_ShadowTex);
        Texture2D _CameraGBufferTexture2;
        float4x4 _WorldToCameraMatrix;
        float _IsDeferred;
        float4 _MainTex_TexelSize;
        float2 _NDCToViewMul;
        float2 _NDCToViewAdd;
        float3 _LightDirView; // Light direction in view space
        float _RayLength;
        int _RaySteps;
        float _Intensity;
        float _Thickness;
        float _DebugMode;

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
                bool isSky = (rawDepth <= 0.00001);
            #else
                bool isSky = (rawDepth >= 0.99999);
            #endif

            if (isSky)
            {
                return (_DebugMode >= 3.0) ? float4(0.0, 0.0, 0.0, 1.0) : float4(1.0, 1.0, 1.0, 1.0);
            }

            float linearDepth = LinearEyeDepth(rawDepth);
            if (linearDepth <= 0.01 || linearDepth > 2000.0)
            {
                return (_DebugMode >= 3.0) ? float4(0.0, 0.0, 0.0, 1.0) : float4(1.0, 1.0, 1.0, 1.0);
            }

            float2 texel = _MainTex_TexelSize.xy;
            float pixCZ = linearDepth;
            float pixLZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord + float2(-texel.x, 0.0)));
            float pixRZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord + float2( texel.x, 0.0)));
            float pixTZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord + float2(0.0,  texel.y)));
            float pixBZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord + float2(0.0, -texel.y)));

            float3 centerPos = ReconstructViewPos(i.texcoord, pixCZ);
            float3 leftPos   = ReconstructViewPos(i.texcoord + float2(-texel.x, 0.0), pixLZ);
            float3 rightPos  = ReconstructViewPos(i.texcoord + float2( texel.x, 0.0), pixRZ);
            float3 topPos    = ReconstructViewPos(i.texcoord + float2(0.0,  texel.y), pixTZ);
            float3 botPos    = ReconstructViewPos(i.texcoord + float2(0.0, -texel.y), pixBZ);

            float3 normal;
            bool hasGBufferNormal = false;

            // When Deferred shading is active, fetch true smooth hardware normal from GBuffer2 to eliminate polygon quad faceting
            if (_IsDeferred > 0.5)
            {
                float4 gbufNorm = _CameraGBufferTexture2.Load(int3(i.vertex.xy, 0));
                float3 worldNorm = gbufNorm.rgb * 2.0 - 1.0;
                if (dot(worldNorm, worldNorm) > 0.2)
                {
                    float3 gViewNorm = mul((float3x3)_WorldToCameraMatrix, normalize(worldNorm));
                    gViewNorm.z = -gViewNorm.z;
                    normal = normalize(gViewNorm);
                    hasGBufferNormal = true;
                }
            }

            if (!hasGBufferNormal)
            {
                // High-quality screen-space central-difference normal reconstruction
                float3 ddx = rightPos - leftPos;
                float3 ddy = topPos - botPos;
                normal = cross(ddy, ddx);
                float lenSq = dot(normal, normal);
                normal = (lenSq > 0.00001) ? normalize(normal) : float3(0.0, 0.0, -1.0);
            }

            // Normal in reconstructed view-space should point towards camera (-Z)
            if (normal.z > 0.0)
            {
                normal = -normal;
            }

            if (_DebugMode >= 3.0)
            {
                return float4(normal * 0.5 + 0.5, 1.0);
            }

            float3 rayDir = normalize(_LightDirView);
            float NdotL = dot(normal, rayDir);

            // Smooth transition at terminator: surfaces facing away from light smoothly fade out, zero pop
            float lightFacing = smoothstep(-0.05, 0.25, NdotL);

            // Distance fadeout: smoothly fade between 150m and 500m
            float distFade = saturate((500.0 - linearDepth) / 350.0);
            if (distFade <= 0.001)
            {
                return float4(1.0, 1.0, 1.0, 1.0);
            }

            // Adaptive Ray length and thickness: scale proportionally with distance
            float maxRayLength = max(_RayLength, linearDepth * 0.015);
            float thickness = max(_Thickness, linearDepth * 0.008);

            // Normal bias lifts ray off surface to avoid self-shadowing acne on curved surfaces
            float normalBias = max(0.015, thickness * 0.20);
            float3 originPos = centerPos + normal * normalBias;

            // March towards light source in view space
            float3 endPos = originPos + rayDir * maxRayLength;
            if (endPos.z <= 0.01) endPos.z = 0.01;

            float2 startUV = i.texcoord;
            float2 endUV;
            endUV.x = (endPos.x / endPos.z - _NDCToViewAdd.x) / _NDCToViewMul.x;
            endUV.y = (endPos.y / endPos.z - _NDCToViewAdd.y) / _NDCToViewMul.y;

            float2 rayDeltaUV = endUV - startUV;
            float rayPixelDist = length(rayDeltaUV * _MainTex_TexelSize.zw);

            // Subpixel fade out only when ray is essentially zero pixels on screen
            float pixelWeight = saturate(rayPixelDist / 1.5);
            if (pixelWeight <= 0.001)
            {
                return float4(1.0, 1.0, 1.0, 1.0);
            }

            // High-frequency dither
            float dither = frac(52.9829189 * frac(dot(i.texcoord * _MainTex_TexelSize.zw, float2(0.06711056, 0.00583715))));

            float invZ_start = 1.0 / originPos.z;
            float invZ_end   = 1.0 / endPos.z;

            float bias = max(0.006, linearDepth * 0.0006);
            float occlusion = 0.0;
            int steps = clamp(_RaySteps, 4, 32);

            [loop]
            for (int s = 1; s <= steps; s++)
            {
                float t = ((float)s - 0.5 + (dither - 0.5) * 0.8) / (float)steps;
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

                // Dynamic curvature bias avoids false self-shadowing on curved surfaces
                float dynamicBias = bias + t * max(0.008, thickness * 0.15);

                if (depthDiff > dynamicBias && depthDiff < thickness)
                {
                    // Solid occlusion across occluder body with smooth fadeout near the tail
                    float tail = (depthDiff - thickness * 0.7) / max(0.001, thickness * 0.3);
                    float occl = 1.0 - saturate(tail);
                    float contactFalloff = 1.0 - t * 0.35;
                    occlusion = max(occlusion, occl * contactFalloff);
                    break;
                }
            }

            occlusion *= pixelWeight * distFade;
            // Smoothly modulate shadow with lightFacing to guarantee zero pop and seamless terminator transition
            float shadow = lerp(1.0, 1.0 - saturate(occlusion * _Intensity), lightFacing);

            return float4(shadow, shadow, shadow, 1.0);
        }

        // Pass 1: 8-tap Edge-preserving bilateral filter for Contact Shadows
        float4 FragContactShadowDenoise(VaryingsDefault i) : SV_Target
        {
            if (_DebugMode >= 3.0)
            {
                // Passthrough for Normals
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            }

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
            float shadow = SAMPLE_TEXTURE2D(_ShadowTex, sampler_ShadowTex, i.texcoord).r;

            float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord);
            #if UNITY_REVERSED_Z
                bool isSky = (rawDepth <= 0.00001);
            #else
                bool isSky = (rawDepth >= 0.99999);
            #endif

            if (_DebugMode == 1.0)
            {
                // Debug Mode 1: Red Highlight Overlay on Scene
                if (isSky) return scene * 0.4;
                float occl = saturate((1.0 - shadow) * 1.5);
                float luma = dot(scene.rgb, float3(0.299, 0.587, 0.114));
                float3 baseScene = lerp(scene.rgb, float3(luma, luma, luma), 0.5) * 0.5;
                float3 redHighlight = float3(1.0, 0.12, 0.12);
                return float4(lerp(baseScene, redHighlight, occl), scene.a);
            }
            if (_DebugMode == 2.0)
            {
                // Debug Mode 2: Clay Model Shadow Mask
                if (isSky) return float4(0.0, 0.0, 0.0, 1.0);
                return float4(shadow, shadow, shadow, 1.0);
            }
            if (_DebugMode >= 3.0)
            {
                // Debug Mode 3: Reconstructed Normals
                if (isSky) return float4(0.0, 0.0, 0.0, 1.0);
                return SAMPLE_TEXTURE2D(_ShadowTex, sampler_ShadowTex, i.texcoord);
            }

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
