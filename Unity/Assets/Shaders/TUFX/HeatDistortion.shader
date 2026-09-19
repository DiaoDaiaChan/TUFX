Shader "Hidden/TUFX/HeatDistortion"
{
    HLSLINCLUDE
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"

        TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
        TEXTURE2D_SAMPLER2D(_HeatMaskTex, sampler_HeatMaskTex);
        float4 _MainTex_TexelSize;

        float _Intensity;
        float _Speed;
        float _Scale;
        float _PlumeThreshold;
        float _GroundHazeWeight; // 0.0 to 1.0 based on vessel altitude
        float _DistortionMode;   // 0 = Auto Plume & Ground, 1 = Fullscreen

        // Fast high-quality 2D procedural turbulence noise
        float Hash21(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        float Noise2D(float2 p)
        {
            float2 i = floor(p);
            float2 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);

            float a = Hash21(i);
            float b = Hash21(i + float2(1.0, 0.0));
            float c = Hash21(i + float2(0.0, 1.0));
            float d = Hash21(i + float2(1.0, 1.0));

            return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
        }

        // Pass 0: Plume Heat Source Extraction & Dilated Mask
        float4 FragExtractHeatMask(VaryingsDefault i) : SV_Target
        {
            float4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            float luma = dot(col.rgb, float3(0.2126, 0.7152, 0.0722));

            // Extract high-intensity thermal sources (engine flames, plume exhaust, re-entry glow)
            float heat = saturate((luma - _PlumeThreshold) / max(0.1, _PlumeThreshold));
            return float4(heat, heat, heat, 1.0);
        }

        // Pass 1: Dilate and blur heat mask into surrounding atmosphere
        float4 FragBlurHeatMask(VaryingsDefault i) : SV_Target
        {
            float2 texel = _MainTex_TexelSize.xy * 8.0;
            float sum = 0.0;
            float total = 0.0;

            const float2 offsets[9] = {
                float2( 0.0,  0.0),
                float2( 1.0,  0.0), float2(-1.0,  0.0),
                float2( 0.0,  1.0), float2( 0.0, -1.0),
                float2( 1.5,  1.5), float2(-1.5,  1.5),
                float2( 1.5, -1.5), float2(-1.5, -1.5)
            };

            [unroll]
            for (int k = 0; k < 9; k++)
            {
                float w = (k == 0) ? 0.3 : 0.0875;
                sum += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord + offsets[k] * texel).r * w;
                total += w;
            }

            return float4(sum / total, 0, 0, 1.0);
        }

        // Pass 2: Thermal Distortion with Adaptive Plume & Ground Haze Masking
        float4 FragDistort(VaryingsDefault i) : SV_Target
        {
            if (_Intensity <= 0.0001)
            {
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            }

            float2 uv = i.texcoord;
            float time = _Time.y * _Speed;

            // 1. Plume proximity mask (dilated thermal mask from engine flame)
            float plumeMask = SAMPLE_TEXTURE2D(_HeatMaskTex, sampler_HeatMaskTex, uv).r;
            // Expand plume falloff smoothly
            plumeMask = saturate(plumeMask * 2.5);

            // 2. Ground heat haze mask (near horizon / terrain when at low altitude)
            // Ground heat haze peaks near lower-middle of screen and fades towards sky
            float groundMask = saturate((1.0 - uv.y * 1.5)) * _GroundHazeWeight;

            // Composite spatial heat mask
            float activeMask = max(plumeMask, groundMask);

            if (_DistortionMode > 0.5)
            {
                // Fullscreen mode with subtle vignette protection for UI/borders
                float2 vDist = abs(uv - 0.5) * 2.0;
                float borderVignette = saturate(1.0 - max(vDist.x, vDist.y) * 0.5);
                activeMask = borderVignette;
            }

            // Early exit if no thermal heat is active on this pixel
            if (activeMask <= 0.005)
            {
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
            }

            // Turbulent multi-frequency noise field
            float n1 = Noise2D(uv * _Scale + float2(0.0, time * 1.2));
            float n2 = Noise2D(uv * (_Scale * 1.8) + float2(time * 0.8, -time * 0.4));
            float2 flow = float2(n1 - 0.5, n2 - 0.5) * (_Intensity * 0.035 * activeMask);

            // Spectral chromatic aberration on turbulent boundary layer
            float r = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + flow * 1.08).r;
            float g = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + flow).g;
            float b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + flow * 0.92).b;

            return float4(r, g, b, 1.0);
        }
    ENDHLSL

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // 0: Extract Plume Heat
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragExtractHeatMask
            ENDHLSL
        }

        // 1: Dilate / Blur Heat Mask
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragBlurHeatMask
            ENDHLSL
        }

        // 2: Adaptive Distortion
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragDistort
            ENDHLSL
        }
    }
}
