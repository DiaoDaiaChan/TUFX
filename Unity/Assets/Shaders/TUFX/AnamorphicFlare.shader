Shader "Hidden/TUFX/AnamorphicFlare"
{
    HLSLINCLUDE
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/Colors.hlsl"

        TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
        TEXTURE2D(_FlareBaseTex);
        TEXTURE2D(_FlareStreakTex);
        TEXTURE2D(_FlareSpikesTex);
        TEXTURE2D(_FlareGhostTex);

        float4 _MainTex_TexelSize;
        float4 _ThresholdParams; // x: threshold, y: threshold - knee, z: knee * 2, w: 0.25 / knee
        float4 _FlareParams;     // x: maxBrightness, y: streakLength, z: dispersion, w: unused

        float _StreakIntensity;
        float _StreakSpread;
        float4 _StreakColor;
        float _GhostIntensity;
        float _GhostSpread;
        float4 _GhostColor;
        float _SpikeIntensity;
        int _SpikeCount;
        float _SpikeLength;

        // Jimenez's Interleaved Gradient Noise for continuous ray jittering
        float InterleavedGradientNoise(float2 pixCoord)
        {
            float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
            return frac(magic.z * frac(dot(pixCoord, magic.xy)));
        }

        // Pass 0: Soft-Knee Threshold Extraction & Anti-Blowout Highlight Clamping
        float4 FragThreshold(VaryingsDefault i) : SV_Target
        {
            float4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            color.rgb = min(_FlareParams.x, color.rgb);
            color = QuadraticThreshold(color, _ThresholdParams.x, _ThresholdParams.yzw);
            return float4(SafeHDR(color).rgb, 1.0);
        }

        // Pass 1: Horizontal Pre-filter with Spectral Dispersion (creates brilliant core)
        float4 FragStreakPreFilter(VaryingsDefault i) : SV_Target
        {
            static const float weights[5] = { 0.2270270, 0.1945946, 0.1216216, 0.0540541, 0.0162162 };
            float disp = _FlareParams.z * 0.45;
            float baseStep = _MainTex_TexelSize.x * 2.0;

            float3 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord).rgb * weights[0];

            [unroll]
            for (int t = 1; t <= 4; t++)
            {
                float w = weights[t];
                float offset = float(t) * baseStep;

                float2 uvG_pos = i.texcoord + float2(offset, 0.0);
                float2 uvG_neg = i.texcoord - float2(offset, 0.0);

                float2 uvR_pos = i.texcoord + float2(offset * (1.0 + disp), 0.0);
                float2 uvR_neg = i.texcoord - float2(offset * (1.0 + disp), 0.0);

                float2 uvB_pos = i.texcoord + float2(offset * (1.0 - disp), 0.0);
                float2 uvB_neg = i.texcoord - float2(offset * (1.0 - disp), 0.0);

                float r = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvR_pos).r + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvR_neg).r;
                float g = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvG_pos).g + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvG_neg).g;
                float b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvB_pos).b + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvB_neg).b;

                col += float3(r, g, b) * w;
            }

            return float4(col, 1.0);
        }

        // Pass 2: Horizontal 5-tap Gaussian Downsample (halves horizontal resolution)
        float4 FragHorizontalDownsample(VaryingsDefault i) : SV_Target
        {
            float dx = _MainTex_TexelSize.x;
            float3 c0 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord - float2(dx * 2.0, 0.0)).rgb;
            float3 c1 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord - float2(dx * 1.0, 0.0)).rgb;
            float3 c2 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord).rgb;
            float3 c3 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord + float2(dx * 1.0, 0.0)).rgb;
            float3 c4 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord + float2(dx * 2.0, 0.0)).rgb;

            float3 blurred = (c0 + c4) * 0.0625 + (c1 + c3) * 0.25 + c2 * 0.375;
            return float4(blurred, 1.0);
        }

        // Pass 3: Horizontal Upsample & Additive Accumulation
        float4 FragHorizontalUpsampleCombine(VaryingsDefault i) : SV_Target
        {
            float dx = _MainTex_TexelSize.x;
            // 3-tap tent upsample from lower mip
            float3 upsampled = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord - float2(dx * 0.5, 0.0)).rgb * 0.25
                             + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord).rgb * 0.50
                             + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord + float2(dx * 0.5, 0.0)).rgb * 0.25;

            // Additive combination with current mip level
            float3 baseCol = SAMPLE_TEXTURE2D(_FlareBaseTex, sampler_MainTex, i.texcoord).rgb;
            return float4(baseCol + upsampled * _StreakSpread, 1.0);
        }

        // Pass 4: Continuous Starburst Spikes with IGN ray jitter
        float4 FragSpikes(VaryingsDefault i) : SV_Target
        {
            float3 sum = float3(0, 0, 0);
            int count = clamp(_SpikeCount, 4, 8);
            float angleStep = 3.14159265 / (float)count;

            float jitter = InterleavedGradientNoise(i.texcoord * _MainTex_TexelSize.zw);

            for (int s = 0; s < count; s++)
            {
                float theta = float(s) * angleStep;
                float2 dir = float2(cos(theta), sin(theta)) * _MainTex_TexelSize.xy * _SpikeLength * 1.2;

                [unroll]
                for (int tap = 1; tap <= 12; tap++)
                {
                    float t = float(tap) + jitter - 0.5;
                    float weight = exp(-float(tap) * 0.28);
                    sum += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord + dir * t).rgb * weight;
                    sum += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord - dir * t).rgb * weight;
                }
            }

            return float4(sum * (_SpikeIntensity * 0.07), 1.0);
        }

        // Pass 5: Defocused Lens Ghosts & Halo (Continuous Smooth Disc)
        float4 FragGhosts(VaryingsDefault i) : SV_Target
        {
            float2 center = float2(0.5, 0.5);
            float2 toCenter = center - i.texcoord;
            float3 ghostCol = float3(0, 0, 0);

            const float ghostScales[4] = { -0.5, 0.35, -0.85, 1.25 };
            const float ghostWeights[4] = { 0.6, 0.8, 0.4, 0.3 };
            const float ghostDefocus[4] = { 10.0, 6.0, 14.0, 5.0 };

            for (int g = 0; g < 4; g++)
            {
                float2 ghostCenterUV = i.texcoord + toCenter * (ghostScales[g] * _GhostSpread);
                float2 dispOffset = toCenter * (_FlareParams.z * 0.015);
                float2 rad = ghostDefocus[g] * _MainTex_TexelSize.xy;

                // Continuous 4-tap box filter with smooth chromatic dispersion: no discrete dot lattice!
                float3 s0 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, ghostCenterUV - rad + dispOffset).rgb;
                float3 s1 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, ghostCenterUV + float2(rad.x, -rad.y)).rgb;
                float3 s2 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, ghostCenterUV + float2(-rad.x, rad.y)).rgb;
                float3 s3 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, ghostCenterUV + rad - dispOffset).rgb;
                float3 ghostDiscAcc = (s0 + s1 + s2 + s3) * 0.25;

                float distToCenter = length(ghostCenterUV - center);
                float vignette = saturate(1.0 - distToCenter * 1.2);
                ghostCol += ghostDiscAcc * (ghostWeights[g] * vignette);
            }

            // Outer optical halo ring with smooth cosine falloff & chromatic shift
            float haloRadius = 0.55 * _GhostSpread;
            float2 haloDir = normalize(toCenter + float2(1e-4, 1e-4));
            float2 haloUV = i.texcoord + haloDir * haloRadius;
            float haloDist = length(toCenter) - haloRadius;
            float haloWeight = saturate(cos(clamp(haloDist * 14.0, -1.57, 1.57)));
            haloWeight = haloWeight * haloWeight;

            float2 dispHalo = haloDir * (_FlareParams.z * 0.015);
            float haloR = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, haloUV + dispHalo).r;
            float haloG = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, haloUV).g;
            float haloB = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, haloUV - dispHalo).b;
            ghostCol += float3(haloR, haloG, haloB) * (haloWeight * 0.35);

            return float4(ghostCol * (_GhostIntensity * _GhostColor.rgb), 1.0);
        }

        // Pass 6: Final Composite
        float4 FragComposite(VaryingsDefault i) : SV_Target
        {
            float4 orig = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            float3 streak = SAMPLE_TEXTURE2D(_FlareStreakTex, sampler_MainTex, i.texcoord).rgb * (_StreakIntensity * _StreakColor.rgb * 2.0);
            float3 spikes = SAMPLE_TEXTURE2D(_FlareSpikesTex, sampler_MainTex, i.texcoord).rgb;
            float3 ghosts = SAMPLE_TEXTURE2D(_FlareGhostTex, sampler_MainTex, i.texcoord).rgb;

            float3 finalFlare = streak + spikes + ghosts;
            return float4(orig.rgb + finalFlare, orig.a);
        }
    ENDHLSL

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // 0: Soft-Knee Threshold
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragThreshold
            ENDHLSL
        }

        // 1: Horizontal Streak Pre-filter
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragStreakPreFilter
            ENDHLSL
        }

        // 2: Horizontal 5-tap Gaussian Downsample
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragHorizontalDownsample
            ENDHLSL
        }

        // 3: Horizontal Upsample & Combine
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragHorizontalUpsampleCombine
            ENDHLSL
        }

        // 4: Continuous Starburst Spikes
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragSpikes
            ENDHLSL
        }

        // 5: Defocused Lens Ghosts & Halo
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragGhosts
            ENDHLSL
        }

        // 6: Composite
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragComposite
            ENDHLSL
        }
    }
}
