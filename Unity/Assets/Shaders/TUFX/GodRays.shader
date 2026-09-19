Shader "Hidden/TUFX/GodRays"
{
    HLSLINCLUDE
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"

        TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
        TEXTURE2D_SAMPLER2D(_CameraDepthTexture, sampler_CameraDepthTexture);
        TEXTURE2D(_RaysTex);
        float4 _MainTex_TexelSize;
        float2 _SunScreenPos; // Viewport UV of the Sun
        float _SunVisible;    // 1 if Sun is in front of camera hemisphere, 0 if behind
        float _Threshold;
        float _Density;
        float _Decay;
        float _Weight;
        float _Intensity;
        float4 _RayColor;

        // Pass 0: True Celestial Sun Light Source Extraction with Scene Depth Occlusion
        // Confines the solar emitter strictly to the actual celestial Sun body scale.
        // This prevents stock KSP camera glare billboards from bloating the light source to 200px,
        // allowing spacecraft, solar panels, and antennas to cast sharp, continuous volumetric shadows
        // at ANY occlusion level (10%, 50%, 80%) smoothly without abrupt 'instant burst' popping!
        float4 FragExtract(VaryingsDefault i) : SV_Target
        {
            if (_SunVisible <= 0.0)
            {
                return float4(0, 0, 0, 0);
            }

            float2 sunVec = (i.texcoord - _SunScreenPos) * float2(_MainTex_TexelSize.z / _MainTex_TexelSize.w, 1.0);
            float sunDist = length(sunVec);

            // Confine solar source strictly to celestial solar body scale (radius 0.042)
            if (sunDist > 0.045)
            {
                return float4(0, 0, 0, 0);
            }

            // Depth occlusion: spacecraft, vessels, and terrain block the Sun
            float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord);
            #if UNITY_REVERSED_Z
                bool isSky = (rawDepth <= 0.0001);
            #else
                bool isSky = (rawDepth >= 0.9999);
            #endif
            float linearDepth = LinearEyeDepth(rawDepth);
            float occl = (isSky || linearDepth > 3000.0) ? 1.0 : saturate((linearDepth - 20.0) / 100.0);

            if (occl <= 0.001)
            {
                return float4(0, 0, 0, 0);
            }

            // True celestial solar disc emitter with smooth limb darkening:
            // Tight core (radius 0.024) + soft limb falloff (up to 0.042)
            float sunDisc = saturate((0.024 - sunDist) / 0.014);
            float sunLimb = saturate((0.042 - sunDist) / 0.020) * 0.45;
            float solarEmitter = saturate(sunDisc + sunLimb) * _SunVisible;

            // Modulate with scene depth occlusion: when spacecraft blocks the sun, solarEmitter is cleanly cut
            float sunSource = solarEmitter * occl;

            return float4(sunSource, sunSource, sunSource, 1.0);
        }

        // Pass 1: Radial blur marching towards the Sun with continuous IGN jitter
        float4 FragRadialBlur(VaryingsDefault i) : SV_Target
        {
            if (_SunVisible <= 0.0)
            {
                return float4(0, 0, 0, 0);
            }

            float2 aspectVec = float2(_MainTex_TexelSize.z / _MainTex_TexelSize.w, 1.0);
            float distToSun = length((i.texcoord - _SunScreenPos) * aspectVec);

            // 1. Solar Core Cutout Protection:
            // Smoothly suppresses ray accumulation inside the actual solar disc so it NEVER turns into a nuclear blowout!
            // Inside sun disc (dist < 0.015): 0.0 (Sun disc remains 100% pristine and crisp)
            // Ramps up smoothly between 0.015 and 0.050 to 1.0 (rays fan outward from the rim)
            float coreCutout = smoothstep(0.015, 0.050, distToSun);

            // Interleaved gradient noise for continuous, noise-free streak dithering
            float dither = frac(52.9829189 * frac(dot(i.texcoord * _MainTex_TexelSize.zw, float2(0.06711056, 0.00583715))));

            // Step towards Sun from current pixel
            float2 toSun = _SunScreenPos - i.texcoord;
            float2 stepDelta = toSun * (1.0 / 36.0) * _Density;
            float2 uv = i.texcoord + stepDelta * (dither * 0.85);

            float raySum = 0.0;

            [unroll(36)]
            for (int s = 0; s < 36; s++)
            {
                float tap = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).r;
                raySum += tap;
                uv += stepDelta;
            }

            // 2. Optical Transmission Fraction:
            // When ray passes through the unoccluded solar disc (width ~ 5-7 steps), raySum reaches ~5.5.
            // Normalizing by 5.5 gives a clean [0, 1] linear transmission fraction!
            float pathTransmission = saturate(raySum * (1.0 / 5.5));

            // 3. Physical Metric Distance Falloff:
            // Ray gracefully falls off across screen distance
            float falloffCoeff = max(0.8, (1.05 - _Decay) * 45.0);
            float distanceFalloff = exp(-distToSun * falloffCoeff);

            // Combined ray beam: high intensity in space/atmosphere, 0 at sun core
            float rayIntensity = pathTransmission * distanceFalloff * coreCutout * (_Intensity * 2.4);

            return float4(rayIntensity * _RayColor.rgb, 1.0);
        }

        // Pass 2: Anti-Blowout Tone-Protected Composite
        float4 FragComposite(VaryingsDefault i) : SV_Target
        {
            float4 scene = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            float3 rays = SAMPLE_TEXTURE2D(_RaysTex, sampler_MainTex, i.texcoord).rgb;

            // Highlight headroom protection:
            // In deep space / vessel shadows: 100% full volumetric contrast!
            // In already bright regions (sky / direct glare): soft compression prevents overexposing to pure white!
            float sceneLuma = dot(scene.rgb, float3(0.2126, 0.7152, 0.0722));
            float headroom = saturate(1.0 - sceneLuma * 0.45);

            float3 finalRays = rays * headroom;
            return float4(scene.rgb + finalRays, scene.a);
        }
    ENDHLSL

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // 0: Extract
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragExtract
            ENDHLSL
        }

        // 1: Radial Blur
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragRadialBlur
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
