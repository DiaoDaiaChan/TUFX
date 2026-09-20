Shader "Hidden/TUFX/HeatDistortion"
{
    HLSLINCLUDE
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"

        TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
        TEXTURE2D_SAMPLER2D(_HeatMaskTex, sampler_HeatMaskTex);
        TEXTURE2D_SAMPLER2D(_CameraDepthTexture, sampler_CameraDepthTexture);
        float4 _MainTex_TexelSize;

        float2 _SunScreenPos;
        float _VesselDepth;
        float _ReentryHeat;
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

            // Mask out celestial Sun disc so it never triggers heat distortion
            float2 aspectVec = float2(_MainTex_TexelSize.z / _MainTex_TexelSize.w, 1.0);
            float distToSun = length((i.texcoord - _SunScreenPos) * aspectVec);
            if (distToSun < 0.055)
            {
                return float4(0, 0, 0, 1);
            }

            // Depth check:
            float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord);
            #if UNITY_REVERSED_Z
            bool isSky = (rawDepth <= 0.0001);
            #else
            bool isSky = (rawDepth >= 0.9999);
            #endif

            // Extract thermal sources (engine plume, rocket exhaust, afterburners)
            // Soft threshold starting at user plume threshold to reliably capture all plumes
            float minLuma = max(0.4, _PlumeThreshold * 0.75);
            float maxLuma = minLuma + 0.65;
            float heat = smoothstep(minLuma, maxLuma, luma);

            // Reentry heat addition: during hypersonic flight, plasma glow around vessel triggers heat
            if (_ReentryHeat > 0.0)
            {
                float linearDepth = isSky ? 100000.0 : LinearEyeDepth(rawDepth);
                float vesselProx = saturate(1.0 - abs(linearDepth - _VesselDepth) / max(10.0, _VesselDepth * 0.55));
                heat = max(heat, _ReentryHeat * vesselProx * 0.85);
            }

            return float4(heat, heat, heat, 1.0);
        }

        // Pass 1: Multi-scale dilation and Gaussian blur for smooth plume envelope
        float4 FragBlurHeatMask(VaryingsDefault i) : SV_Target
        {
            float2 texel = _MainTex_TexelSize.xy * 4.0;
            float maxVal = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord).r;
            float sum = maxVal * 0.25;
            float total = 0.25;

            const float2 offsets[12] = {
                float2( 1.0,  0.0), float2(-1.0,  0.0),
                float2( 0.0,  1.0), float2( 0.0, -1.0),
                float2( 1.5,  1.5), float2(-1.5,  1.5),
                float2( 1.5, -1.5), float2(-1.5, -1.5),
                float2( 3.0,  0.0), float2(-3.0,  0.0),
                float2( 0.0,  3.0), float2( 0.0, -3.0)
            };

            [unroll]
            for (int k = 0; k < 12; k++)
            {
                float v = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord + offsets[k] * texel).r;
                maxVal = max(maxVal, v);
                float w = (k < 8) ? 0.07 : 0.04;
                sum += v * w;
                total += w;
            }

            // Dilate core + smooth Gaussian envelope
            float dilated = lerp(sum / total, maxVal, 0.45);
            return float4(dilated, dilated, dilated, 1.0);
        }

        // Pass 2: Thermal Distortion with Adaptive Plume, Reentry & Ground Haze Masking
        float4 FragDistort(VaryingsDefault i) : SV_Target
        {
            if (_Intensity <= 0.0001)
            {
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            }

            float2 uv = i.texcoord;
            float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, uv);
            #if UNITY_REVERSED_Z
            bool isSky = (rawDepth <= 0.0001);
            #else
            bool isSky = (rawDepth >= 0.9999);
            #endif
            float linearDepth = isSky ? 100000.0 : LinearEyeDepth(rawDepth);

            float time = _Time.y * _Speed;

            // 1. Plume & Reentry proximity mask (dilated thermal mask from engine flame)
            float plumeMask = SAMPLE_TEXTURE2D(_HeatMaskTex, sampler_HeatMaskTex, uv).r;

            // 2. Ground heat haze mask (near horizon / terrain when at low altitude)
            // CRUCIAL: Ground mirage ONLY affects distant ground/runway (linearDepth > 35m)!
            // The active spacecraft itself (linearDepth < 30m) is 100% IMMUNE to ground wobble!
            float groundDistWeight = (!isSky) ? saturate((linearDepth - 35.0) / 75.0) : 0.0;
            float groundMask = groundDistWeight * saturate(1.0 - uv.y * 1.3) * _GroundHazeWeight;

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
            float2 noiseUV = uv * _Scale;
            float n1 = Noise2D(noiseUV + float2(time * 1.1, time * 0.7));
            float n2 = Noise2D(noiseUV * 1.6 + float2(-time * 0.8, time * 1.3));
            float n3 = Noise2D(noiseUV * 3.2 + float2(time * 1.5, -time * 1.1));

            float2 flow = float2(n1 - 0.5 + (n3 - 0.5) * 0.35, n2 - 0.5 + (n3 - 0.5) * 0.35);
            // Realistic physical displacement amplitude: ~4 to 7 pixels max at intensity 1.0!
            flow *= (_Intensity * 0.006 * activeMask);

            // Subtle spectral dispersion (chromatic refraction)
            float r = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + flow * 1.02).r;
            float g = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + flow).g;
            float b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + flow * 0.98).b;

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
