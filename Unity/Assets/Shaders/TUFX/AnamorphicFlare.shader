Shader "Hidden/TUFX/AnamorphicFlare"
{
    HLSLINCLUDE
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"

        TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
        TEXTURE2D_SAMPLER2D(_FlareTex, sampler_FlareTex);
        float4 _MainTex_TexelSize;
        float _Threshold;
        float _StreakIntensity;
        float _StreakLength;
        float4 _StreakColor;
        float _SpikeIntensity;
        int _SpikeCount; // 4, 6, or 8
        float _SpikeLength;

        // Pass 0: Threshold extraction
        float4 FragThreshold(VaryingsDefault i) : SV_Target
        {
            float4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            float luma = dot(color.rgb, float3(0.2126, 0.7152, 0.0722));
            float val = max(0.0, luma - _Threshold);
            return float4(color.rgb * (val / max(0.001, luma)), 1.0);
        }

        // Pass 1: Horizontal 1D Streak Blur
        float4 FragStreak(VaryingsDefault i) : SV_Target
        {
            float4 sum = float4(0, 0, 0, 0);
            float totalWeight = 0.0;
            float step = _MainTex_TexelSize.x * _StreakLength * 2.0;

            [unroll]
            for (int tap = -7; tap <= 7; tap++)
            {
                float weight = exp(-abs((float)tap) * 0.35);
                float2 uv = i.texcoord + float2(tap * step, 0.0);
                sum += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) * weight;
                totalWeight += weight;
            }

            return (sum / totalWeight) * _StreakColor;
        }

        // Pass 2: Diffraction Spikes (Starburst)
        float4 FragSpikes(VaryingsDefault i) : SV_Target
        {
            float4 sum = float4(0, 0, 0, 0);
            int count = clamp(_SpikeCount, 4, 8);
            float angleStep = 3.14159265 / (float)count;

            for (int s = 0; s < count; s++)
            {
                float theta = s * angleStep;
                float2 dir = float2(cos(theta), sin(theta)) * _MainTex_TexelSize.xy * _SpikeLength;
                [unroll]
                for (int tap = 1; tap <= 5; tap++)
                {
                    float weight = 1.0 / (float)tap;
                    sum += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord + dir * tap) * weight;
                    sum += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord - dir * tap) * weight;
                }
            }

            return sum * (_SpikeIntensity * 0.1);
        }

        // Pass 3: Composite Streak + Spikes with Original Scene
        float4 FragComposite(VaryingsDefault i) : SV_Target
        {
            float4 orig = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            float4 flare = SAMPLE_TEXTURE2D(_FlareTex, sampler_FlareTex, i.texcoord);
            return orig + flare * _StreakIntensity;
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

        // 3: Composite
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragComposite
            ENDHLSL
        }
    }
}
