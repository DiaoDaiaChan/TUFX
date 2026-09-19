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

            // In our coordinate convention (+Z forward into screen), visible front-facing surfaces
            // must have normal pointing towards camera (-Z).
            // cross(dy, dx) points towards camera (-Z), whereas cross(dx, dy) points into +Z (into the mesh interior!).
            float3 n = cross(dy, dx);
            float lenSq = dot(n, n);
            if (lenSq < 0.0001) return float3(0, 0, -1);
            n = normalize(n);
            if (n.z > 0.0) n = -n;
            return n;
        }

        void GetTangentSpace(float3 n, out float3 t, out float3 b)
        {
            float3 up = abs(n.z) < 0.999 ? float3(0, 0, 1) : float3(1, 0, 0);
            t = normalize(cross(up, n));
            b = cross(n, t);
        }

        // Pass 0: Half-Resolution Raymarching Pass with Firefly Suppression
        float4 FragSSGIRaymarch(VaryingsDefault i) : SV_Target
        {
            float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord);
            #if UNITY_REVERSED_Z
                if (rawDepth <= 0.00005) return float4(0, 0, 0, 0);
            #else
                if (rawDepth >= 0.99995) return float4(0, 0, 0, 0);
            #endif

            float viewZ = LinearEyeDepth(rawDepth);
            if (viewZ <= 0.05 || viewZ > 3000.0) return float4(0, 0, 0, 0);

            float3 viewPos = ComputeViewspacePosition(i.texcoord, viewZ);
            float3 viewNorm;

            if (_IsDeferred > 0.5)
            {
                int2 gbufCoord = int2(i.texcoord * _ScreenParams.xy);
                float4 gbuf2 = _CameraGBufferTexture2.Load(int3(gbufCoord, 0));
                float3 worldNorm = gbuf2.rgb * 2.0 - 1.0;
                if (dot(worldNorm, worldNorm) > 0.2)
                {
                    float3 gViewNorm = mul((float3x3)_WorldToCameraMatrix, normalize(worldNorm));
                    if (gViewNorm.z > 0.0) gViewNorm.z = -gViewNorm.z;
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
            float accumulatedWeight = 0.0;

            // Lift ray origin along normal away from starting surface to prevent self-intersection
            float normalOffset = max(0.08, viewZ * 0.005);
            float3 rayOrigin = viewPos + viewNorm * normalOffset;

            [loop]
            for (int r = 0; r < 8; r++)
            {
                if (r >= rayCount) break;

                // Golden ratio spiral for cosine-weighted hemisphere distribution
                float alpha = 2.0 * PI * (float(r) * 0.61803398875 + dither);
                float radius = sqrt((float(r) + 0.5) / float(rayCount));
                float rx = radius * cos(alpha);
                float ry = radius * sin(alpha);
                // Guarantee rays don't skim parallel to surface horizon
                float rz = sqrt(max(0.04, 1.0 - radius * radius));

                float3 rayDir = normalize(rx * tangent + ry * bitangent + rz * viewNorm);

                [loop]
                for (int s = 1; s <= 16; s++)
                {
                    if (s > raySteps) break;

                    // Start march offset from ray origin
                    float t = (float(s) + dither * 0.5) * stepSize;
                    float3 marchPos = rayOrigin + rayDir * t;

                    // If ray marches behind camera, terminate
                    if (marchPos.z <= 0.1) break;

                    float2 sampleUV = ProjectViewToUV(marchPos);
                    if (any(sampleUV < 0.0) || any(sampleUV > 1.0)) break;

                    // Skip self-neighborhood in screen space (prevents near-field self-intersection)
                    if (length(sampleUV - i.texcoord) < _MainTex_TexelSize.x * 5.0) continue;

                    float sampleRawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, sampleUV);
                    #if UNITY_REVERSED_Z
                        if (sampleRawDepth <= 0.00005) continue;
                    #else
                        if (sampleRawDepth >= 0.99995) continue;
                    #endif

                    float sampleEyeDepth = LinearEyeDepth(sampleRawDepth);

                    float depthDelta = marchPos.z - sampleEyeDepth;
                    float adaptiveThickness = max(_Thickness, marchPos.z * 0.025);
                    float bias = max(0.025, adaptiveThickness * 0.06);

                    if (depthDelta > bias && depthDelta < adaptiveThickness)
                    {
                        // Validate hit surface normal:
                        // A physically valid bounce requires the hit surface to face AGAINST the incident ray (dot(rayDir, hitNorm) < -0.1).
                        // If dot(rayDir, hitNorm) >= -0.1, the ray is hitting from behind or grazing the same hull (self-reflection).
                        float3 hitNorm = ReconstructForwardNormal(sampleUV, sampleEyeDepth);
                        if (dot(rayDir, hitNorm) >= -0.1)
                        {
                            continue; // Self-intersection / grazing / backface hit rejected!
                        }

                        // Ray hit valid opposing geometry! Sample irradiance from hit position
                        float3 hitColor = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, sampleUV, 1.0).rgb;

                        // Edge fade and distance falloff
                        float2 edgeDist = abs(sampleUV - 0.5) * 2.0;
                        float edgeFade = saturate(1.0 - max(edgeDist.x, edgeDist.y));
                        float distFalloff = saturate(1.0 - t / _RayLength);

                        // Karis Anti-Firefly Luminance Weighting:
                        // Suppresses isolated specular hot spots and stipple noise on curved reflectors
                        float hitLuma = dot(hitColor, float3(0.2126, 0.7152, 0.0722));
                        float sampleWeight = 1.0 / (1.0 + hitLuma * 0.6);

                        accumulatedLight += hitColor * (edgeFade * distFalloff * sampleWeight);
                        accumulatedWeight += sampleWeight;
                        break;
                    }
                }
            }

            float3 indirect = (accumulatedWeight > 0.001) ? (accumulatedLight / accumulatedWeight) : float3(0, 0, 0);
            return float4(saturate(indirect), 1.0);
        }

        // Pass 1: Horizontal Edge-Preserving Bilateral Denoise (Wide 9-tap)
        float4 FragSSGIDenoiseH(VaryingsDefault i) : SV_Target
        {
            float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord);
            #if UNITY_REVERSED_Z
                if (rawDepth <= 0.00005) return float4(0, 0, 0, 0);
            #else
                if (rawDepth >= 0.99995) return float4(0, 0, 0, 0);
            #endif

            float centerDepth = LinearEyeDepth(rawDepth);
            float4 centerSample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);

            static const float kernelOffsets[5] = { 0.0, 1.0, 2.0, 3.0, 4.0 };
            static const float kernelWeights[5] = { 0.227027, 0.1945946, 0.1216216, 0.054054, 0.016216 };

            float3 sum = centerSample.rgb * kernelWeights[0];
            float totalWeight = kernelWeights[0];
            float2 texel = float2(_MainTex_TexelSize.x * 2.5, 0.0);

            [unroll]
            for (int k = 1; k < 5; k++)
            {
                float2 uvL = i.texcoord - texel * kernelOffsets[k];
                float2 uvR = i.texcoord + texel * kernelOffsets[k];

                float depthL = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, uvL));
                float depthR = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, uvR));

                float wL = kernelWeights[k] * exp(-abs(centerDepth - depthL) / max(0.04, centerDepth * 0.03));
                float wR = kernelWeights[k] * exp(-abs(centerDepth - depthR) / max(0.04, centerDepth * 0.03));

                sum += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvL).rgb * wL;
                sum += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvR).rgb * wR;
                totalWeight += (wL + wR);
            }

            return float4(sum / max(0.0001, totalWeight), 1.0);
        }

        // Pass 2: Vertical Edge-Preserving Bilateral Denoise (Wide 9-tap)
        float4 FragSSGIDenoiseV(VaryingsDefault i) : SV_Target
        {
            float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord);
            #if UNITY_REVERSED_Z
                if (rawDepth <= 0.00005) return float4(0, 0, 0, 0);
            #else
                if (rawDepth >= 0.99995) return float4(0, 0, 0, 0);
            #endif

            float centerDepth = LinearEyeDepth(rawDepth);
            float4 centerSample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);

            static const float kernelOffsets[5] = { 0.0, 1.0, 2.0, 3.0, 4.0 };
            static const float kernelWeights[5] = { 0.227027, 0.1945946, 0.1216216, 0.054054, 0.016216 };

            float3 sum = centerSample.rgb * kernelWeights[0];
            float totalWeight = kernelWeights[0];
            float2 texel = float2(0.0, _MainTex_TexelSize.y * 2.5);

            [unroll]
            for (int k = 1; k < 5; k++)
            {
                float2 uvB = i.texcoord - texel * kernelOffsets[k];
                float2 uvT = i.texcoord + texel * kernelOffsets[k];

                float depthB = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, uvB));
                float depthT = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, uvT));

                float wB = kernelWeights[k] * exp(-abs(centerDepth - depthB) / max(0.04, centerDepth * 0.03));
                float wT = kernelWeights[k] * exp(-abs(centerDepth - depthT) / max(0.04, centerDepth * 0.03));

                sum += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvB).rgb * wB;
                sum += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvT).rgb * wT;
                totalWeight += (wB + wT);
            }

            return float4(sum / max(0.0001, totalWeight), 1.0);
        }

        // Pass 3: Composite Indirect Light onto Scene
        float4 FragSSGIComposite(VaryingsDefault i) : SV_Target
        {
            float4 scene = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            float3 indirect = SAMPLE_TEXTURE2D(_SSGITex, sampler_MainTex, i.texcoord).rgb;

            if (any(isnan(indirect)) || any(isinf(indirect)))
            {
                return scene;
            }

            float3 albedo = float3(0.5, 0.5, 0.5);
            if (_IsDeferred > 0.5)
            {
                int2 gbufCoord = int2(i.texcoord * _ScreenParams.xy);
                float4 gbuf0 = _CameraGBufferTexture0.Load(int3(gbufCoord, 0));
                if (dot(gbuf0.rgb, 1.0) > 0.01)
                {
                    albedo = gbuf0.rgb;
                }
            }
            else
            {
                albedo = saturate(scene.rgb * 1.2);
            }

            float3 bounce = indirect * (albedo * 0.7 + 0.3) * (_Intensity * _BounceColor.rgb);
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

        // 1: Denoise Horizontal
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragSSGIDenoiseH
            ENDHLSL
        }

        // 2: Denoise Vertical
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragSSGIDenoiseV
            ENDHLSL
        }

        // 3: Composite
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragSSGIComposite
            ENDHLSL
        }
    }
}
