///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Copyright (C) 2016-2021, Intel Corporation 
// SPDX-License-Identifier: MIT
//
// XeGTAO is based on GTAO/GTSO "Jimenez et al. / Practical Real-Time Strategies for Accurate Indirect Occlusion", 
// https://www.activision.com/cdn/research/Practical_Real_Time_Strategies_for_Accurate_Indirect_Occlusion_NEW%20VERSION_COLOR.pdf
// Implementation: Filip Strugar (filip.strugar@intel.com), Steve Mccalla <stephen.mccalla@intel.com>
// Adapted for Unity PostProcessing Stack & TUFX
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

Shader "Hidden/TUFX/GroundTruthAO"
{
    HLSLINCLUDE
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"

        TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
        TEXTURE2D_SAMPLER2D(_CameraDepthTexture, sampler_CameraDepthTexture);
        TEXTURE2D_SAMPLER2D(_AOTex, sampler_AOTex);

        Texture2D _CameraGBufferTexture1;
        Texture2D _CameraGBufferTexture2;

        float4x4 _WorldToCameraMatrix;
        float _IsDeferred;
        float _SpecularOcclusion;

        float4 _MainTex_TexelSize;
        float2 _NDCToViewMul;
        float2 _NDCToViewAdd;
        float _EffectRadius;
        float _Intensity;
        float _Thickness;
        float _MultiBounce;
        float4 _AOColor;

        #define XE_GTAO_PI          3.14159265358979323846
        #define XE_GTAO_PI_HALF     1.57079632679489661923

        // Fast sqrt approximation (Drobot2014a / Jimenez2016)
        float XeGTAO_FastSqrt(float x)
        {
            return asfloat(0x1fbd1df5 + (asint(x) >> 1));
        }

        // Fast acos approximation (Sebastien Lagarde)
        float XeGTAO_FastACos(float inX)
        { 
            float x = abs(inX); 
            float res = -0.156583 * x + XE_GTAO_PI_HALF; 
            res *= XeGTAO_FastSqrt(max(0.0, 1.0 - x)); 
            return (inX >= 0.0) ? res : XE_GTAO_PI - res; 
        }

        // Inputs: screen UV [0, 1] and viewspace depth Z, output: viewspace position
        float3 XeGTAO_ComputeViewspacePosition(float2 screenUV, float viewspaceDepth)
        {
            float3 ret;
            ret.xy = (_NDCToViewMul * screenUV + _NDCToViewAdd) * viewspaceDepth;
            ret.z = viewspaceDepth;
            return ret;
        }

        // Depth edge calculation across cross neighbors
        float4 XeGTAO_CalculateEdges(float centerZ, float leftZ, float rightZ, float topZ, float bottomZ)
        {
            float4 edgesLRTB = float4(leftZ, rightZ, topZ, bottomZ) - centerZ;

            float slopeLR = (edgesLRTB.y - edgesLRTB.x) * 0.5;
            float slopeTB = (edgesLRTB.w - edgesLRTB.z) * 0.5;
            float4 edgesLRTBSlopeAdjusted = edgesLRTB + float4(slopeLR, -slopeLR, slopeTB, -slopeTB);
            edgesLRTB = min(abs(edgesLRTB), abs(edgesLRTBSlopeAdjusted));
            return saturate(1.25 - edgesLRTB / max(0.001, centerZ * 0.015));
        }

        // Robust normal reconstruction from 4 cross depths with edge discontinuity weighting
        // Replaces ddx/ddy entirely: 100% safe from divergence and DX11 driver TDR crashes!
        float3 XeGTAO_CalculateNormal(float4 edgesLRTB, float3 pixCenterPos, float3 pixLPos, float3 pixRPos, float3 pixTPos, float3 pixBPos)
        {
            float4 acceptedNormals = saturate(float4(
                edgesLRTB.x * edgesLRTB.z,
                edgesLRTB.z * edgesLRTB.y,
                edgesLRTB.y * edgesLRTB.w,
                edgesLRTB.w * edgesLRTB.x
            ) + 0.02);

            float3 L = normalize(pixLPos - pixCenterPos);
            float3 R = normalize(pixRPos - pixCenterPos);
            float3 T = normalize(pixTPos - pixCenterPos);
            float3 B = normalize(pixBPos - pixCenterPos);

            float3 pixelNormal = acceptedNormals.x * cross(L, T) +
                                 acceptedNormals.y * cross(T, R) +
                                 acceptedNormals.z * cross(R, B) +
                                 acceptedNormals.w * cross(B, L);

            float lenSq = dot(pixelNormal, pixelNormal);
            if (lenSq < 0.0001)
            {
                return float3(0.0, 0.0, -1.0);
            }
            return pixelNormal * rsqrt(lenSq);
        }

        // Pass 0: Intel XeGTAO Horizon Compute Pass
        float4 FragXeGTAO(VaryingsDefault i) : SV_Target
        {
            float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord);
            #if UNITY_REVERSED_Z
                if (rawDepth <= 0.00001) return float4(1, 1, 1, 1);
            #else
                if (rawDepth >= 0.99999) return float4(1, 1, 1, 1);
            #endif

            float viewspaceZ = LinearEyeDepth(rawDepth);
            if (viewspaceZ <= 0.02)
            {
                return float4(1, 1, 1, 1);
            }

            // Scale-independent screen-space metric (zero hardcoded distance!):
            // Calculate screen-space pixel radius of the physical AO sphere.
            // If the sphere projects to fewer than 2.0 pixels, geometry cannot be resolved.
            float pixelSizeAtZ = viewspaceZ * _NDCToViewMul.x * _MainTex_TexelSize.x;
            float screenspaceRadius = _EffectRadius / max(0.0001, pixelSizeAtZ);
            if (screenspaceRadius < 2.0)
            {
                return float4(1, 1, 1, 1);
            }

            // Cross depth samples for Intel XeGTAO edge & normal calculation
            // In Unity UV coordinates: +Y is TOP, -Y is BOTTOM
            float2 texel = _MainTex_TexelSize.xy;
            float pixLZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord + float2(-texel.x, 0)));
            float pixRZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord + float2( texel.x, 0)));
            float pixTZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord + float2(0,  texel.y)));
            float pixBZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord + float2(0, -texel.y)));

            float4 edgesLRTB = XeGTAO_CalculateEdges(viewspaceZ, pixLZ, pixRZ, pixTZ, pixBZ);

            float3 centerPosRaw = XeGTAO_ComputeViewspacePosition(i.texcoord, viewspaceZ);
            float3 leftPos      = XeGTAO_ComputeViewspacePosition(i.texcoord + float2(-texel.x, 0), pixLZ);
            float3 rightPos     = XeGTAO_ComputeViewspacePosition(i.texcoord + float2( texel.x, 0), pixRZ);
            float3 topPos       = XeGTAO_ComputeViewspacePosition(i.texcoord + float2(0,  texel.y), pixTZ);
            float3 botPos       = XeGTAO_ComputeViewspacePosition(i.texcoord + float2(0, -texel.y), pixBZ);

            float3 viewspaceNormal = XeGTAO_CalculateNormal(edgesLRTB, centerPosRaw, leftPos, rightPos, topPos, botPos);

            // In Deferred shading mode, use high-precision hardware normals from GBuffer2
            if (_IsDeferred > 0.5)
            {
                float4 gbufNorm = _CameraGBufferTexture2.Load(int3(i.vertex.xy, 0));
                float3 worldNorm = gbufNorm.rgb * 2.0 - 1.0;
                if (dot(worldNorm, worldNorm) > 0.2)
                {
                    float3 gViewNorm = mul((float3x3)_WorldToCameraMatrix, normalize(worldNorm));
                    gViewNorm.z = -gViewNorm.z;
                    viewspaceNormal = normalize(gViewNorm);
                }
            }

            float3 viewVec = normalize(-centerPosRaw);

            // Ensure normal faces towards the camera
            if (dot(viewspaceNormal, viewVec) < 0.0)
            {
                viewspaceNormal = -viewspaceNormal;
            }

            // Intel XeGTAO: Nudge center pixel slightly towards camera to eliminate self-shadowing precision artifacts on flat planes
            float centerZ = viewspaceZ * 0.9995;
            float3 centerPos = XeGTAO_ComputeViewspacePosition(i.texcoord, centerZ);

            // Spatial dither / pseudo-random rotation per pixel
            float2 screenPosPixels = i.texcoord * _MainTex_TexelSize.zw;
            float spatialNoise = frac(52.9829189 * frac(dot(screenPosPixels, float2(0.06711056, 0.00583715))));

            // Horizon angle search parameters
            const int SLICES = 3;
            const int STEPS_PER_SLICE = 3;
            float visibility = 0.0;
            float maxScreenRadius = min(screenspaceRadius, 120.0);

            // Precomputed falloff constants (Intel XeGTAO Section 4.3)
            float effectRadius = _EffectRadius;
            float falloffRange = 0.615 * effectRadius;
            float falloffFrom  = effectRadius * (1.0 - 0.615);
            float falloffMul   = -1.0 / max(0.0001, falloffRange);
            float falloffAdd   = falloffFrom / max(0.0001, falloffRange) + 1.0;
            float thinOccluderCompensation = _Thickness;

            [unroll]
            for (int slice = 0; slice < SLICES; slice++)
            {
                float phi = (float(slice) + spatialNoise) * (XE_GTAO_PI / float(SLICES));
                float cosPhi = cos(phi);
                float sinPhi = sin(phi);
                float2 omega = float2(cosPhi, sinPhi);
                float3 directionVec = float3(cosPhi, sinPhi, 0.0);

                // Project normal into the slice plane (Jimenez et al. 2016 eq 8-11)
                float3 orthoDirectionVec = directionVec - dot(directionVec, viewVec) * viewVec;
                float3 axisVec = normalize(cross(orthoDirectionVec, viewVec));
                float3 projectedNormalVec = viewspaceNormal - axisVec * dot(viewspaceNormal, axisVec);

                float projLen = length(projectedNormalVec);
                if (projLen < 0.0001)
                {
                    visibility += 1.0;
                    continue;
                }

                float signNorm = sign(dot(orthoDirectionVec, projectedNormalVec));
                float cosNorm = saturate(dot(projectedNormalVec, viewVec) / projLen);
                float n = signNorm * acos(cosNorm);

                // Initial low horizon angles (tangent planes)
                float lowHorizonCos0 = cos(n + XE_GTAO_PI_HALF);
                float lowHorizonCos1 = cos(n - XE_GTAO_PI_HALF);
                float horizonCos0 = lowHorizonCos0;
                float horizonCos1 = lowHorizonCos1;

                float2 stepVector = omega * (maxScreenRadius * texel);

                [unroll]
                for (int step = 1; step <= STEPS_PER_SLICE; step++)
                {
                    // Quadratic distribution prioritizing near contact geometry
                    float s = (float(step) + spatialNoise) / float(STEPS_PER_SLICE);
                    s = s * s;

                    float2 sampleUV0 = i.texcoord + stepVector * s;
                    float2 sampleUV1 = i.texcoord - stepVector * s;

                    float linD0 = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, sampleUV0));
                    float linD1 = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, sampleUV1));

                    float3 samplePos0 = XeGTAO_ComputeViewspacePosition(sampleUV0, linD0);
                    float3 samplePos1 = XeGTAO_ComputeViewspacePosition(sampleUV1, linD1);

                    float3 diff0 = samplePos0 - centerPos;
                    float3 diff1 = samplePos1 - centerPos;

                    float dist0 = length(diff0);
                    float dist1 = length(diff1);

                    float3 hVec0 = diff0 / max(0.0001, dist0);
                    float3 hVec1 = diff1 / max(0.0001, dist1);

                    // Intel XeGTAO distance falloff & thin occluder compensation
                    float falloffDist0 = length(float3(diff0.xy, diff0.z * (1.0 + thinOccluderCompensation)));
                    float falloffDist1 = length(float3(diff1.xy, diff1.z * (1.0 + thinOccluderCompensation)));
                    float weight0 = saturate(falloffDist0 * falloffMul + falloffAdd);
                    float weight1 = saturate(falloffDist1 * falloffMul + falloffAdd);

                    float shc0 = dot(hVec0, viewVec);
                    float shc1 = dot(hVec1, viewVec);

                    // Discard samples outside radius by smoothly blending back to tangent horizon
                    shc0 = lerp(lowHorizonCos0, shc0, weight0);
                    shc1 = lerp(lowHorizonCos1, shc1, weight1);

                    horizonCos0 = max(horizonCos0, shc0);
                    horizonCos1 = max(horizonCos1, shc1);
                }

                // Analytical inner integral (Jimenez et al. 2016 eq 7)
                float h0 = -acos(clamp(horizonCos1, -1.0, 1.0));
                float h1 =  acos(clamp(horizonCos0, -1.0, 1.0));

                // Clamp horizons to hemisphere above surface tangent
                h0 = n + clamp(h0 - n, -XE_GTAO_PI_HALF, XE_GTAO_PI_HALF);
                h1 = n + clamp(h1 - n, -XE_GTAO_PI_HALF, XE_GTAO_PI_HALF);

                float iarc0 = (cosNorm + 2.0 * h0 * sin(n) - cos(2.0 * h0 - n)) * 0.25;
                float iarc1 = (cosNorm + 2.0 * h1 * sin(n) - cos(2.0 * h1 - n)) * 0.25;

                visibility += projLen * (iarc0 + iarc1);
            }

            visibility /= float(SLICES);
            visibility = saturate(visibility);
            visibility = pow(visibility, 0.5); // Intel XeGTAO FinalValuePower curve
            visibility = max(0.03, visibility);

            // Apply Intensity curve
            float ao = 1.0 - (1.0 - visibility) * _Intensity;
            ao = saturate(ao);

            // Smooth scale-independent pixel fade out for tiny radii at extreme distances
            if (screenspaceRadius < 6.0)
            {
                float fade = saturate((screenspaceRadius - 2.0) / 4.0);
                ao = lerp(1.0, ao, fade);
            }

            // Intel / Jimenez Multi-bounce color approximation
            float3 multiBounceAO = lerp(float3(ao, ao, ao),
                                        ao / max(float3(0.01, 0.01, 0.01), (1.0 - 0.25 * (1.0 - ao))),
                                        _MultiBounce);

            // Pack minimum edge weight in alpha for bilateral filter
            float minEdge = min(min(edgesLRTB.x, edgesLRTB.y), min(edgesLRTB.z, edgesLRTB.w));
            return float4(saturate(multiBounceAO), minEdge);
        }

        // Pass 1: Edge-preserving Cross Bilateral Denoising Filter
        float4 FragXeGTAODenoise(VaryingsDefault i) : SV_Target
        {
            float4 centerSample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            float centerAO = centerSample.r;
            float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord);
            #if UNITY_REVERSED_Z
                if (rawDepth <= 0.00001) return float4(1, 1, 1, 1);
            #else
                if (rawDepth >= 0.99999) return float4(1, 1, 1, 1);
            #endif

            float centerDepth = LinearEyeDepth(rawDepth);
            float2 texel = _MainTex_TexelSize.xy * 1.5;

            float3 sum = centerSample.rgb;
            float totalWeight = 1.0;

            // 8-direction cross and diagonal bilateral taps
            const float2 offsets[8] = {
                float2( 1.0,  0.0), float2(-1.0,  0.0),
                float2( 0.0,  1.0), float2( 0.0, -1.0),
                float2( 0.7,  0.7), float2(-0.7,  0.7),
                float2( 0.7, -0.7), float2(-0.7, -0.7)
            };

            [unroll]
            for (int k = 0; k < 8; k++)
            {
                float2 uv = i.texcoord + offsets[k] * texel;
                float tapDepth = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, uv));
                float depthDiff = abs(centerDepth - tapDepth);

                // Geometry-aware edge weight (relative to distance)
                float weight = exp(-depthDiff / max(0.02, centerDepth * 0.03)) * (k < 4 ? 1.0 : 0.7);
                float4 tapAO = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);

                sum += tapAO.rgb * weight;
                totalWeight += weight;
            }

            float3 filtered = sum / max(0.0001, totalWeight);
            return float4(saturate(filtered), 1.0);
        }

        // Pass 2: Composite AO onto Scene Color
        float4 FragXeGTAOComposite(VaryingsDefault i) : SV_Target
        {
            float4 scene = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            float3 ao = SAMPLE_TEXTURE2D(_AOTex, sampler_AOTex, i.texcoord).rgb;

            if (any(isnan(ao)) || any(isinf(ao)))
            {
                ao = float3(1.0, 1.0, 1.0);
            }

            ao = saturate(ao);

            // Deferred PBR Specular Occlusion (Lagarde / Frostbite model)
            if (_IsDeferred > 0.5 && _SpecularOcclusion > 0.001)
            {
                float4 gbuffer1 = _CameraGBufferTexture1.Load(int3(i.vertex.xy, 0));
                float4 gbuffer2 = _CameraGBufferTexture2.Load(int3(i.vertex.xy, 0));

                if (dot(gbuffer2.rgb, 1.0) > 0.01)
                {
                    float3 worldNorm = normalize(gbuffer2.rgb * 2.0 - 1.0);
                    float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord);
                    float viewspaceZ = LinearEyeDepth(rawDepth);
                    float3 viewPos = XeGTAO_ComputeViewspacePosition(i.texcoord, viewspaceZ);
                    float3 V = normalize(-viewPos);

                    float3 viewNorm = mul((float3x3)_WorldToCameraMatrix, worldNorm);
                    viewNorm.z = -viewNorm.z;
                    viewNorm = normalize(viewNorm);

                    float NdotV = saturate(dot(viewNorm, V));
                    float smoothness = saturate(gbuffer1.a);
                    float roughness = 1.0 - smoothness;

                    // Lagarde PBR Specular Occlusion:
                    // specAO = saturate(pow(NdotV + ao, exp2(-16.0 * roughness - 1.0)) - 1.0 + ao)
                    float expTerm = exp2(-16.0 * roughness - 1.0);
                    float3 specAO = saturate(pow(max(0.0001, NdotV + ao), expTerm) - 1.0 + ao);

                    // Specular reflectivity factor from GBuffer1 (dielectrics ~0.04, metals ~0.6-1.0)
                    float specReflectivity = max(gbuffer1.r, max(gbuffer1.g, gbuffer1.b));
                    float pbrBlend = saturate(specReflectivity * smoothness * 2.0) * _SpecularOcclusion;

                    ao = lerp(ao, specAO, pbrBlend);
                }
            }

            ao = lerp(_AOColor.rgb, float3(1.0, 1.0, 1.0), ao);
            return float4(scene.rgb * ao, scene.a);
        }
    ENDHLSL

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // 0: XeGTAO Compute Pass
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragXeGTAO
            ENDHLSL
        }

        // 1: XeGTAO Bilateral Denoise
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragXeGTAODenoise
            ENDHLSL
        }

        // 2: Composite
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragXeGTAOComposite
            ENDHLSL
        }
    }
}
