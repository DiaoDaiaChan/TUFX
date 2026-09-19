Shader "Hidden/TUFX/GroundTruthAO"
{
    HLSLINCLUDE
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"

        TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
        TEXTURE2D_SAMPLER2D(_CameraDepthTexture, sampler_CameraDepthTexture);
        TEXTURE2D_SAMPLER2D(_AOTex, sampler_AOTex);
        float4 _MainTex_TexelSize;
        float _Radius;
        float _Intensity;
        float _Thickness;
        float _MultiBounce;
        float4 _AOColor;

        float3 ReconstructViewPos(float2 uv, float linearDepth)
        {
            float2 p11_22 = float2(unity_CameraProjection._11, unity_CameraProjection._22);
            float2 clipPos = (uv * 2.0 - 1.0);
            return float3(clipPos / p11_22 * linearDepth, linearDepth);
        }

        // Pass 0: Compute GTAO
        float4 FragGTAO(VaryingsDefault i) : SV_Target
        {
            float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord);
            #if UNITY_REVERSED_Z
                if (rawDepth <= 0.00001)
                    return float4(1.0, 1.0, 1.0, 1.0);
            #else
                if (rawDepth >= 0.99999)
                    return float4(1.0, 1.0, 1.0, 1.0);
            #endif

            float linearDepth = LinearEyeDepth(rawDepth);
            if (linearDepth <= 0.01)
            {
                return float4(1.0, 1.0, 1.0, 1.0);
            }

            float3 centerPos = ReconstructViewPos(i.texcoord, linearDepth);
            
            // Adaptive Scale-Independent Metric (Zero hardcoded distance):
            // Calculate screen-space pixel radius of the AO sampling sphere.
            // When an object or planet is at distance such that the sampling sphere
            // projects to fewer than 2.5 screen pixels, AO cannot be resolved geometrically
            // and sampling adjacent texels causes depth derivative noise and planet flickering.
            float projRadiusPixels = (_Radius / max(0.001, centerPos.z)) * unity_CameraProjection._11 * (_MainTex_TexelSize.z * 0.5);
            if (projRadiusPixels < 2.5)
            {
                return float4(1.0, 1.0, 1.0, 1.0);
            }

            // Reconstruct view-space normal from depth derivatives
            float3 dx = ddx(centerPos);
            float3 dy = ddy(centerPos);
            float3 normal = normalize(cross(dx, dy));
            if (any(isnan(normal)) || any(isinf(normal)))
            {
                return float4(1.0, 1.0, 1.0, 1.0);
            }

            // GTAO horizon search parameters
            float occlusion = 0.0;
            const int NUM_DIRECTIONS = 4;
            const int NUM_STEPS = 4;
            float stepSize = (_Radius / max(0.1, centerPos.z)) / (float)NUM_STEPS;
            stepSize = clamp(stepSize, _MainTex_TexelSize.x, _MainTex_TexelSize.x * 20.0);

            for (int d = 0; d < NUM_DIRECTIONS; d++)
            {
                float angle = (float)d * (3.14159265 / (float)NUM_DIRECTIONS);
                float2 dir = float2(cos(angle), sin(angle));

                float maxHorizon = -1.0;
                for (int s = 1; s <= NUM_STEPS; s++)
                {
                    float2 sampleUV = i.texcoord + dir * (s * stepSize);
                    float sampleDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, sampleUV);
                    #if UNITY_REVERSED_Z
                        if (sampleDepth <= 0.00001) continue;
                    #else
                        if (sampleDepth >= 0.99999) continue;
                    #endif

                    float sampleLinear = LinearEyeDepth(sampleDepth);
                    float3 samplePos = ReconstructViewPos(sampleUV, sampleLinear);

                    float3 diff = samplePos - centerPos;
                    float dist2 = dot(diff, diff);
                    if (dist2 < _Thickness * _Thickness)
                    {
                        float cosAngle = dot(normalize(diff), normal);
                        maxHorizon = max(maxHorizon, cosAngle);
                    }
                }
                if (maxHorizon > 0.0)
                {
                    occlusion += maxHorizon;
                }
            }

            occlusion = 1.0 - (occlusion / (float)NUM_DIRECTIONS) * _Intensity;
            occlusion = saturate(occlusion);

            // Smooth scale-independent pixel-radius fade out
            if (projRadiusPixels < 6.0)
            {
                float fade = saturate((projRadiusPixels - 2.5) / 3.5);
                occlusion = lerp(1.0, occlusion, fade);
            }

            // Multi-bounce approximation (Jimenez et al.)
            float3 multiBounceAO = lerp(float3(occlusion, occlusion, occlusion), 
                                        occlusion / max(float3(0.01, 0.01, 0.01), (1.0 - 0.25 * (1.0 - occlusion))), 
                                        _MultiBounce);

            return float4(multiBounceAO, 1.0);
        }

        // Pass 1: Edge-preserving Bilateral Blur
        float4 FragBilateralBlur(VaryingsDefault i) : SV_Target
        {
            float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord);
            #if UNITY_REVERSED_Z
                if (rawDepth <= 0.00001) return float4(1, 1, 1, 1);
            #else
                if (rawDepth >= 0.99999) return float4(1, 1, 1, 1);
            #endif

            float centerDepth = LinearEyeDepth(rawDepth);
            float projRadiusPixels = (_Radius / max(0.001, centerDepth)) * unity_CameraProjection._11 * (_MainTex_TexelSize.z * 0.5);
            if (projRadiusPixels < 2.5)
            {
                return float4(1, 1, 1, 1);
            }

            float3 sum = float3(0, 0, 0);
            float totalWeight = 0.0;

            for (int x = -2; x <= 2; x++)
            {
                for (int y = -2; y <= 2; y++)
                {
                    float2 offset = float2(x, y) * _MainTex_TexelSize.xy * 1.5;
                    float2 uv = i.texcoord + offset;
                    float d = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, uv));
                    
                    float spatialWeight = exp(-float(x*x + y*y) * 0.25);
                    float depthWeight = exp(-abs(centerDepth - d) * 2.0);
                    float weight = spatialWeight * depthWeight;

                    sum += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb * weight;
                    totalWeight += weight;
                }
            }
            return float4(sum / max(0.0001, totalWeight), 1.0);
        }

        // Pass 2: Composite AO onto Scene Color
        float4 FragComposite(VaryingsDefault i) : SV_Target
        {
            float4 scene = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            float3 ao = SAMPLE_TEXTURE2D(_AOTex, sampler_AOTex, i.texcoord).rgb;
            
            // Safety guard: if G and B are zero due to single-channel format fallback, use R for all three
            if (ao.g == 0.0 && ao.b == 0.0 && ao.r > 0.0)
            {
                ao = ao.rrr;
            }
            ao = saturate(ao);
            ao = lerp(_AOColor.rgb, float3(1, 1, 1), ao);
            return float4(scene.rgb * ao, scene.a);
        }
    ENDHLSL

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // 0: Compute GTAO
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragGTAO
            ENDHLSL
        }

        // 1: Bilateral Blur
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragBilateralBlur
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
