Shader "Hidden/TUFX/AnamorphicFlare"
{
    HLSLINCLUDE
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"

        TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
        TEXTURE2D_SAMPLER2D(_FlareStreakTex, sampler_FlareStreakTex);
        TEXTURE2D_SAMPLER2D(_FlareSpikesTex, sampler_FlareSpikesTex);
        TEXTURE2D_SAMPLER2D(_FlareGhostTex, sampler_FlareGhostTex);
        float4 _MainTex_TexelSize;
        float _Threshold;
        float _StreakIntensity;
        float _StreakLength;
        float4 _StreakColor;
        float _GhostIntensity;
        float _GhostSpread;
        float4 _GhostColor;
        float _Dispersion;
        float _SpikeIntensity;
        int _SpikeCount; // 4, 6, or 8
        float _SpikeLength;

        // Pass 0: Threshold extraction with soft knee
        float4 FragThreshold(VaryingsDefault i) : SV_Target
        {
            float4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            float luma = dot(color.rgb, float3(0.2126, 0.7152, 0.0722));
            float val = max(0.0, luma - _Threshold);
            float soft = saturate(val / max(0.1, _Threshold * 0.5));
            return float4(color.rgb * soft, 1.0);
        }

        // Pass 1: Multi-scale horizontal anamorphic streak with spectral dispersion
        float4 FragStreak(VaryingsDefault i) : SV_Target
        {
            float3 col = float3(0, 0, 0);
            float totalWeight = 0.0;
            float baseStep = _MainTex_TexelSize.x * _StreakLength;

            const int TAPS = 16;
            [unroll]
            for (int t = -TAPS; t <= TAPS; t++)
            {
                float xOffset = (float)t;
                // Non-linear power distribution for extreme horizontal screen spread
                float spreadOffset = sign(xOffset) * pow(abs(xOffset), 1.6) * baseStep * 3.5;
                float weight = exp(-abs(xOffset) * 0.18);

                float2 uvG = i.texcoord + float2(spreadOffset, 0.0);
                float2 uvR = i.texcoord + float2(spreadOffset * (1.0 + _Dispersion * 0.35), 0.0);
                float2 uvB = i.texcoord + float2(spreadOffset * (1.0 - _Dispersion * 0.35), 0.0);

                float r = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvR).r;
                float g = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvG).g;
                float b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvB).b;

                col += float3(r, g, b) * weight;
                totalWeight += weight;
            }

            return float4((col / totalWeight) * _StreakColor.rgb, 1.0);
        }

        // Pass 2: Diffraction Spikes (Starburst)
        float4 FragSpikes(VaryingsDefault i) : SV_Target
        {
            float3 sum = float3(0, 0, 0);
            int count = clamp(_SpikeCount, 4, 8);
            float angleStep = 3.14159265 / (float)count;

            for (int s = 0; s < count; s++)
            {
                float theta = float(s) * angleStep;
                float2 dir = float2(cos(theta), sin(theta)) * _MainTex_TexelSize.xy * _SpikeLength * 2.0;
                [unroll]
                for (int tap = 1; tap <= 8; tap++)
                {
                    float weight = 1.0 / (float(tap) * 0.8 + 0.2);
                    sum += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord + dir * float(tap)).rgb * weight;
                    sum += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord - dir * float(tap)).rgb * weight;
                }
            }

            return float4(sum * (_SpikeIntensity * 0.08), 1.0);
        }

        // Pass 3: Optical Lens Ghosting & Halo
        float4 FragGhosts(VaryingsDefault i) : SV_Target
        {
            float2 center = float2(0.5, 0.5);
            float2 toCenter = center - i.texcoord;
            float3 ghostCol = float3(0, 0, 0);

            // Secondary ghost reflections across optical center
            const float ghostScales[4] = { -0.5, 0.35, -0.85, 1.25 };
            const float ghostWeights[4] = { 0.6, 0.8, 0.4, 0.3 };

            for (int g = 0; g < 4; g++)
            {
                float2 ghostUV = i.texcoord + toCenter * (ghostScales[g] * _GhostSpread);
                float2 dispR = toCenter * (_Dispersion * 0.015);
                float2 dispB = -toCenter * (_Dispersion * 0.015);

                float r = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, ghostUV + dispR).r;
                float g = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, ghostUV).g;
                float b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, ghostUV + dispB).b;

                float distToCenter = length(ghostUV - center);
                float vignette = saturate(1.0 - distToCenter * 1.2);
                ghostCol += float3(r, g, b) * (ghostWeights[g] * vignette);
            }

            // Outer optical halo ring
            float2 haloVec = normalize(toCenter + float2(0.0001, 0.0001)) * 0.55 * _GhostSpread;
            float haloDist = length(toCenter) - 0.55 * _GhostSpread;
            float haloWeight = saturate(1.0 - abs(haloDist) * 8.0);
            float3 haloSample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord + haloVec).rgb;
            ghostCol += haloSample * (haloWeight * 0.4);

            return float4(ghostCol * (_GhostIntensity * _GhostColor.rgb), 1.0);
        }

        // Pass 4: Composite Streak + Spikes + Ghosts with Original Scene
        float4 FragComposite(VaryingsDefault i) : SV_Target
        {
            float4 orig = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            float3 streak = SAMPLE_TEXTURE2D(_FlareStreakTex, sampler_FlareStreakTex, i.texcoord).rgb * _StreakIntensity;
            float3 spikes = SAMPLE_TEXTURE2D(_FlareSpikesTex, sampler_FlareSpikesTex, i.texcoord).rgb;
            float3 ghosts = SAMPLE_TEXTURE2D(_FlareGhostTex, sampler_FlareGhostTex, i.texcoord).rgb;

            float3 finalFlare = streak + spikes + ghosts;
            return float4(orig.rgb + finalFlare, orig.a);
        }
    ENDHLSL

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // 0: Threshold
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragThreshold
            ENDHLSL
        }

        // 1: Horizontal Streak
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragStreak
            ENDHLSL
        }

        // 2: Diffraction Spikes
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragSpikes
            ENDHLSL
        }

        // 3: Lens Ghosts & Halo
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragGhosts
            ENDHLSL
        }

        // 4: Composite
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragComposite
            ENDHLSL
        }
    }
}
