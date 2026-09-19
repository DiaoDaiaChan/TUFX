Shader "Hidden/TUFX/SSGI"
{
    HLSLINCLUDE
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"

        TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
        TEXTURE2D_SAMPLER2D(_CameraDepthTexture, sampler_CameraDepthTexture);
        TEXTURE2D_SAMPLER2D(_SSGITex, sampler_SSGITex);

        Texture2D _CameraGBufferTexture0;
        Texture2D _CameraGBufferTexture2;

        float4 _MainTex_TexelSize;
        float2 _NDCToViewMul;
        float2 _NDCToViewAdd;
        float4x4 _WorldToCameraMatrix;

        float _Intensity;
        float _RayLength;
        float _Thickness;
        float _IsDeferred;
        int _RayCount;
        int _RaySteps;
        float4 _BounceColor;

        #define PI 3.14159265358979323846

        float InterleavedGradientNoise(float2 pixCoord)
        {
            float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
            return frac(magic.z * frac(dot(pixCoord, magic.xy)));
        }

        float3 ComputeViewspacePosition(float2 screenUV, float viewspaceDepth)
        {
            float3 ret;
            ret.xy = (_NDCToViewMul * screenUV + _NDCToViewAdd) * viewspaceDepth;
            ret.z = viewspaceDepth;
            return ret;
        }

        float2 ProjectViewToUV(float3 viewPos)
        {
            float2 ndc = viewPos.xy / max(0.0001, viewPos.z);
            return (ndc - _NDCToViewAdd) / _NDCToViewMul;
        }

        float3 ReconstructForwardNormal(float2 uv, float centerDepth)
        {
            float2 texel = _MainTex_TexelSize.xy;
            float cZ = centerDepth;
            float lZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, uv + float2(-texel.x, 0)));
            float rZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, uv + float2( texel.x, 0)));
            float tZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, uv + float2(0,  texel.y)));
            float bZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, uv + float2(0, -texel.y)));

            float3 cP = ComputeViewspacePosition(uv, cZ);
            float3 lP = ComputeViewspacePosition(uv + float2(-texel.x, 0), lZ);
            float3 rP = ComputeViewspacePosition(uv + float2( texel.x, 0), rZ);
            float3 tP = ComputeViewspacePosition(uv + float2(0,  texel.y), tZ);
            float3 bP = ComputeViewspacePosition(uv + float2(0, -texel.y), bZ);

            float3 dx1 = rP - cP;
            float3 dx0 = cP - lP;
            float3 dy1 = tP - cP;
            float3 dy0 = cP - bP;

            float3 dx = (abs(dx1.z) < abs(dx0.z)) ? dx1 : dx0;
            float3 dy = (abs(dy1.z) < abs(dy0.z)) ? dy1 : dy0;

            float3 n = cross(dx, dy);
            float lenSq = dot(n, n);
            if (lenSq < 0.0001) return float3(0, 0, -1);
            return normalize(n);
        }

        void GetTangentSpace(float3 n, out float3 t, out float3 b)
        {
            float3 up = abs(n.z) < 0.999 ? float3(0, 0, 1) : float3(1, 0, 0);
            t = normalize(cross(up, n));
            b = cross(n, t);
        }

        // Pass 0: Half-Resolution Raymarching Pass
        float4 FragSSGIRaymarch(VaryingsDefault i) : SV_Target
        {
            float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord);
            #if UNITY_REVERSED_Z
                if (rawDepth <= 0.00005) return float4(0, 0, 0, 0);
            #else
                if (rawDepth >= 0.99995) return float4(0, 0, 0, 0);
            #endif

            float viewZ = LinearEyeDepth(rawDepth);
            if (viewZ <= 0.05 || viewZ > 5000.0) return float4(0, 0, 0, 0);

            float3 viewPos = ComputeViewspacePosition(i.texcoord, viewZ);
            float3 viewNorm;

            if (_IsDeferred > 0.5)
            {
                float4 gbuf2 = _CameraGBufferTexture2.Load(int3(i.vertex.xy * 2.0, 0));
                float3 worldNorm = gbuf2.rgb * 2.0 - 1.0;
                if (dot(worldNorm, worldNorm) > 0.2)
                {
                    float3 gViewNorm = mul((float3x3)_WorldToCameraMatrix, normalize(worldNorm));
                    gViewNorm.z = -gViewNorm.z;
                    viewNorm = normalize(gViewNorm);
                }
                else
                {
                    viewNorm = ReconstructForwardNormal(i.texcoord, viewZ);
                }
            }
            else
            {
                viewNorm = ReconstructForwardNormal(i.texcoord, viewZ);
            }

            float3 tangent, bitangent;
            GetTangentSpace(viewNorm, tangent, bitangent);

            float dither = InterleavedGradientNoise(i.vertex.xy);
            int rayCount = clamp(_RayCount, 2, 8);
            int raySteps = clamp(_RaySteps, 4, 16);
            float stepSize = _RayLength / float(raySteps);

            float3 accumulatedLight = float3(0, 0, 0);
            float validSamples = 0.0;

            [unroll(8)]
            for (int r = 0; r < 8; r++)
            {
                if (r >= rayCount) break;

                // Golden ratio spiral for cosine-weighted hemisphere distribution
                float alpha = 2.0 * PI * (float(r) * 0.61803398875 + dither);
                float radius = sqrt((float(r) + 0.5) / float(rayCount));
                float rx = radius * cos(alpha);
                float ry = radius * sin(alpha);
                float rz = sqrt(max(0.001, 1.0 - radius * radius));

                float3 rayDir = normalize(rx * tangent + ry * bitangent + rz * viewNorm);
                float3 rayOrigin = viewPos + viewNorm * 0.05;

                [unroll(16)]
                for (int s = 1; s <= 16; s++)
                {
                    if (s > raySteps) break;

                    float t = (float(s) - 0.5 + dither * 0.5) * stepSize;
                    float3 marchPos = rayOrigin + rayDir * t;

                    float2 sampleUV = ProjectViewToUV(marchPos);
                    if (any(sampleUV < 0.0) || any(sampleUV > 1.0)) break;

                    float sampleRawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, sampleUV);
                    float sampleEyeDepth = LinearEyeDepth(sampleRawDepth);

                    float depthDelta = marchPos.z - sampleEyeDepth;
                    if (depthDelta > 0.02 && depthDelta < _Thickness)
                    {
                        // Ray hit geometry! Sample irradiance from hit position
                        float3 hitColor = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, sampleUV, 1.0).rgb;

                        // Edge fade and distance falloff
                        float2 edgeDist = abs(sampleUV - 0.5) * 2.0;
                        float edgeFade = saturate(1.0 - max(edgeDist.x, edgeDist.y));
                        float distFalloff = saturate(1.0 - t / _RayLength);

                        accumulatedLight += hitColor * (edgeFade * distFalloff);
                        validSamples += 1.0;
                        break;
                    }
                }
            }

            float3 indirect = accumulatedLight / max(1.0, float(rayCount));
            return float4(saturate(indirect), 1.0);
        }

        // Pass 1: Edge-Preserving Bilateral Denoise & Upsample
        float4 FragSSGIDenoise(VaryingsDefault i) : SV_Target
        {
            float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord);
            #if UNITY_REVERSED_Z
                if (rawDepth <= 0.00005) return float4(0, 0, 0, 0);
            #else
                if (rawDepth >= 0.99995) return float4(0, 0, 0, 0);
            #endif

            float centerDepth = LinearEyeDepth(rawDepth);
            float4 centerSample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);

            float3 sum = centerSample.rgb;
            float totalWeight = 1.0;
            float2 texel = _MainTex_TexelSize.xy * 2.0;

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

                float weight = exp(-depthDiff / max(0.05, centerDepth * 0.03)) * (k < 4 ? 1.0 : 0.7);
                float4 tapCol = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);

                sum += tapCol.rgb * weight;
                totalWeight += weight;
            }

            return float4(sum / max(0.0001, totalWeight), 1.0);
        }

        // Pass 2: Composite Indirect Light onto Scene
        float4 FragSSGIComposite(VaryingsDefault i) : SV_Target
        {
            float4 scene = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            float3 indirect = SAMPLE_TEXTURE2D(_SSGITex, sampler_SSGITex, i.texcoord).rgb;

            if (any(isnan(indirect)) || any(isinf(indirect)))
            {
                return scene;
            }

            float3 albedo = float3(0.5, 0.5, 0.5);
            if (_IsDeferred > 0.5)
            {
                float4 gbuf0 = _CameraGBufferTexture0.Load(int3(i.vertex.xy, 0));
                if (dot(gbuf0.rgb, 1.0) > 0.01)
                {
                    albedo = gbuf0.rgb;
                }
            }
            else
            {
                albedo = saturate(scene.rgb * 1.2);
            }

            float3 bounce = indirect * albedo * (_Intensity * _BounceColor.rgb);
            return float4(scene.rgb + bounce, scene.a);
        }

    ENDHLSL

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // 0: Raymarching
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragSSGIRaymarch
            ENDHLSL
        }

        // 1: Denoise
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragSSGIDenoise
            ENDHLSL
        }

        // 2: Composite
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragSSGIComposite
            ENDHLSL
        }
    }
}
