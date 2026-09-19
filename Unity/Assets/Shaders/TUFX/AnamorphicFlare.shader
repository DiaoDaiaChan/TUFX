Shader "Hidden/TUFX/AnamorphicFlare"
{
    HLSLINCLUDE
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/Colors.hlsl"

        TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
        TEXTURE2D_SAMPLER2D(_FlareStreakTex, sampler_FlareStreakTex);
        TEXTURE2D_SAMPLER2D(_FlareStreakWideTex, sampler_FlareStreakWideTex);
        TEXTURE2D_SAMPLER2D(_FlareSpikesTex, sampler_FlareSpikesTex);
        TEXTURE2D_SAMPLER2D(_FlareGhostTex, sampler_FlareGhostTex);

        float4 _MainTex_TexelSize;
        float4 _ThresholdParams; // x: threshold, y: threshold - knee, z: knee * 2, w: 0.25 / knee
        float4 _FlareParams;     // x: maxBrightness, y: streakLength, z: dispersion, w: unused

        float _StreakIntensity;
        float4 _StreakColor;
        float _GhostIntensity;
        float _GhostSpread;
        float4 _GhostColor;
        float _SpikeIntensity;
        int _SpikeCount;
        float _SpikeLength;
        float _BlurStep;

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
            // Clamp max brightness to prevent blinding specular blowout on metallic surfaces
            color.rgb = min(_FlareParams.x, color.rgb);
            // Quadratic soft-knee thresholding (smooth transition, no hard specular cutoff)
            color = QuadraticThreshold(color, _ThresholdParams.x, _ThresholdParams.yzw);
            return float4(SafeHDR(color).rgb, 1.0);
        }

        // Pass 1: Horizontal Pre-filter with Spectral Dispersion (9-tap normalized Gaussian)
        float4 FragStreakPreFilter(VaryingsDefault i) : SV_Target
        {
            // Gaussian weights for 9 taps (normalized sum = 1.0)
            static const float weights[5] = { 0.2270270, 0.1945946, 0.1216216, 0.0540541, 0.0162162 };
            float disp = _FlareParams.z * 0.35;
            float baseStep = _MainTex_TexelSize.x * 1.5;

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

        // Pass 2: Cascaded 1D Horizontal Gaussian Blur (Seamless convolution)
        float4 FragHorizontalBlur(VaryingsDefault i) : SV_Target
        {
            static const float weights[5] = { 0.2270270, 0.1945946, 0.1216216, 0.0540541, 0.0162162 };
            float stepX = _MainTex_TexelSize.x * _BlurStep;

            float3 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord).rgb * weights[0];

            [unroll]
            for (int t = 1; t <= 4; t++)
            {
                float w = weights[t];
                float offset = float(t) * stepX;
                float3 s1 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord + float2(offset, 0.0)).rgb;
                float3 s2 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord - float2(offset, 0.0)).rgb;
                col += (s1 + s2) * w;
            }

            return float4(col, 1.0);
        }

        // Pass 3: Diffraction Spikes with IGN continuous ray jitter
        float4 FragSpikes(VaryingsDefault i) : SV_Target
        {
            float3 sum = float3(0, 0, 0);
            int count = clamp(_SpikeCount, 4, 8);
            float angleStep = 3.14159265 / (float)count;

            float jitter = InterleavedGradientNoise(i.texcoord * _MainTex_TexelSize.zw);

            for (int s = 0; s < count; s++)
            {
                float theta = float(s) * angleStep;
                float2 dir = float2(cos(theta), sin(theta)) * _MainTex_TexelSize.xy * _SpikeLength * 1.5;

                [unroll]
                for (int tap = 1; tap <= 12; tap++)
                {
                    // Continuous jittered sample distance avoids discrete stepping beads
                    float t = float(tap) + jitter - 0.5;
                    float weight = exp(-float(tap) * 0.25);
                    sum += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord + dir * t).rgb * weight;
                    sum += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord - dir * t).rgb * weight;
                }
            }

            return float4(sum * (_SpikeIntensity * 0.06), 1.0);
        }

        // Pass 4: Defocused Optical Lens Ghosts with 8-Tap Fibonacci Aperture Disk & Chromatic Rim
        float4 FragGhosts(VaryingsDefault i) : SV_Target
        {
            float2 center = float2(0.5, 0.5);
            float2 toCenter = center - i.texcoord;
            float3 ghostCol = float3(0, 0, 0);

            // 8-Tap Fibonacci spiral aperture bokeh kernel
            static const float2 kApertureDisc[8] = {
                float2( 0.000,  0.000),
                float2( 0.528,  0.412),
                float2(-0.482,  0.615),
                float2(-0.731, -0.298),
                float2(-0.115, -0.852),
                float2( 0.694, -0.551),
                float2( 0.887,  0.192),
                float2( 0.153,  0.921)
            };

            const float ghostScales[4] = { -0.5, 0.35, -0.85, 1.25 };
            const float ghostWeights[4] = { 0.6, 0.8, 0.4, 0.3 };
            const float ghostDefocus[4] = { 18.0, 12.0, 24.0, 8.0 }; // Aperture bokeh radius in texels

            for (int g = 0; g < 4; g++)
            {
                float2 ghostCenterUV = i.texcoord + toCenter * (ghostScales[g] * _GhostSpread);
                float2 dispOffset = toCenter * (_FlareParams.z * 0.02);
                float radius = ghostDefocus[g] * _MainTex_TexelSize.xy * 1.5;

                float3 ghostDiscAcc = float3(0, 0, 0);
                [unroll]
                for (int k = 0; k < 8; k++)
                {
                    float2 sampleUV = ghostCenterUV + kApertureDisc[k] * radius;
                    float r = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, sampleUV + dispOffset).r;
                    float g = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, sampleUV).g;
                    float b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, sampleUV - dispOffset).b;
                    ghostDiscAcc += float3(r, g, b);
                }
                ghostDiscAcc *= 0.125; // Average over aperture disk to dissolve sharp object silhouettes

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

        // Pass 5: Final Composite with Original Scene
        float4 FragComposite(VaryingsDefault i) : SV_Target
        {
            float4 orig = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            // Combine tight core streak and wide anamorphic streak
            float3 streakCore = SAMPLE_TEXTURE2D(_FlareStreakTex, sampler_FlareStreakTex, i.texcoord).rgb;
            float3 streakWide = SAMPLE_TEXTURE2D(_FlareStreakWideTex, sampler_FlareStreakWideTex, i.texcoord).rgb;
            float3 streak = (streakCore * 0.6 + streakWide * 0.7) * (_StreakIntensity * _StreakColor.rgb);

            float3 spikes = SAMPLE_TEXTURE2D(_FlareSpikesTex, sampler_FlareSpikesTex, i.texcoord).rgb;
            float3 ghosts = SAMPLE_TEXTURE2D(_FlareGhostTex, sampler_FlareGhostTex, i.texcoord).rgb;

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

        // 2: Horizontal Blur Convolution
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragHorizontalBlur
            ENDHLSL
        }

        // 3: Continuous Starburst Spikes
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragSpikes
            ENDHLSL
        }

        // 4: Defocused Lens Ghosts & Halo
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragGhosts
            ENDHLSL
        }

        // 5: Composite
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragComposite
            ENDHLSL
        }
    }
}
