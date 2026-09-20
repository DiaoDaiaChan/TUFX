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

        // Pass 0: Subpixel Conservative Celestial Sun Extraction with Solid Occlusion
        // Uses 4-tap subpixel depth testing to accurately capture thin lattice struts, trusses, and antennas.
        // Completely blocks sunlight behind solid spacecraft parts and solar panels (preventing light leakage).
        float4 FragExtract(VaryingsDefault i) : SV_Target
        {
            if (_SunVisible <= 0.0)
            {
                return float4(0, 0, 0, 0);
            }

            float2 sunVec = (i.texcoord - _SunScreenPos) * float2(_MainTex_TexelSize.z / _MainTex_TexelSize.w, 1.0);
            float sunDist = length(sunVec);

            // Confine solar source strictly to celestial solar body scale (radius 0.035)
            if (sunDist > 0.035)
            {
                return float4(0, 0, 0, 0);
            }

            // 4-tap sub-pixel conservative depth test to capture thin truss lattice struts and antenna masts
            float2 halfTex = _MainTex_TexelSize.xy * 0.5;
            float rawD0 = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord + float2(-halfTex.x, -halfTex.y));
            float rawD1 = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord + float2( halfTex.x, -halfTex.y));
            float rawD2 = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord + float2(-halfTex.x,  halfTex.y));
            float rawD3 = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord + float2( halfTex.x,  halfTex.y));

            #if UNITY_REVERSED_Z
                float sky0 = (rawD0 <= 0.00005) ? 1.0 : 0.0;
                float sky1 = (rawD1 <= 0.00005) ? 1.0 : 0.0;
                float sky2 = (rawD2 <= 0.00005) ? 1.0 : 0.0;
                float sky3 = (rawD3 <= 0.00005) ? 1.0 : 0.0;
            #else
                float sky0 = (rawD0 >= 0.99995) ? 1.0 : 0.0;
                float sky1 = (rawD1 >= 0.99995) ? 1.0 : 0.0;
                float sky2 = (rawD2 >= 0.99995) ? 1.0 : 0.0;
                float sky3 = (rawD3 >= 0.99995) ? 1.0 : 0.0;
            #endif

            // Fractional visibility: solid solar panels have occl = 0, empty truss openings have occl = 1,
            // and thin lattice struts have anti-aliased subpixel fractional values
            float occl = (sky0 + sky1 + sky2 + sky3) * 0.25;
            if (occl <= 0.001)
            {
                return float4(0, 0, 0, 0);
            }

            // Compact celestial solar disc emitter:
            // Core radius 0.014 + soft limb darkening falloff up to 0.026
            float sunDisc = saturate((0.014 - sunDist) / 0.007);
            float sunLimb = saturate((0.026 - sunDist) / 0.012) * 0.45;
            float solarEmitter = saturate(sunDisc + sunLimb) * _SunVisible;

            float sunSource = solarEmitter * occl;
            return float4(sunSource, sunSource, sunSource, 1.0);
        }

        // Pass 1: 64-step High-density Radial Blur marching towards the Sun
        // Completely eliminates the hollow core donut/annulus artifact (fixing Image 2)
        // Resolves crisp, continuous light shafts through micro-truss lattice gaps (fixing Image 1)
        float4 FragRadialBlur(VaryingsDefault i) : SV_Target
        {
            if (_SunVisible <= 0.0)
            {
                return float4(0, 0, 0, 0);
            }

            float2 aspectVec = float2(_MainTex_TexelSize.z / _MainTex_TexelSize.w, 1.0);
            float distToSun = length((i.texcoord - _SunScreenPos) * aspectVec);

            // Interleaved gradient noise with subtle amplitude to break banding without visible dither grain
            float dither = frac(52.9829189 * frac(dot(i.texcoord * _MainTex_TexelSize.zw, float2(0.06711056, 0.00583715))));

            // Step towards Sun from current pixel: 64 high-density steps
            float2 toSun = _SunScreenPos - i.texcoord;
            float2 stepDelta = toSun * (1.0 / 64.0) * _Density;
            float2 uv = i.texcoord + stepDelta * (dither * 0.75);

            float raySum = 0.0;
            float totalWeight = 0.0;
            float illuminationDecay = 1.0;
            float decayStep = pow(clamp(_Decay, 0.85, 0.99), 36.0 / 64.0);

            [unroll(64)]
            for (int s = 0; s < 64; s++)
            {
                float tap = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).r;
                raySum += tap * illuminationDecay;
                totalWeight += illuminationDecay;
                illuminationDecay *= decayStep;
                uv += stepDelta;
            }

            // Optical transmission along light path
            float pathTransmission = raySum / max(0.001, totalWeight * 0.22);

            // Physical metric distance falloff across screen
            float falloffCoeff = max(0.8, (1.05 - _Decay) * 35.0);
            float distanceFalloff = exp(-distToSun * falloffCoeff);

            // Monotonic soft-knee highlight compression:
            // Prevents nuclear blowout at high intensities while strictly guaranteeing NO DONUT HOLE / DARK ANNULUS!
            float beam = 1.0 - exp(-pathTransmission * (_Intensity * max(0.1, _Weight) * 4.0));
            float rayIntensity = beam * distanceFalloff;

            return float4(rayIntensity * _RayColor.rgb, 1.0);
        }

        // Pass 2: Anti-Blowout Tone-Protected Composite
        float4 FragComposite(VaryingsDefault i) : SV_Target
        {
            float4 scene = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            float3 rays = SAMPLE_TEXTURE2D(_RaysTex, sampler_MainTex, i.texcoord).rgb;

            // Highlight headroom protection:
            // In deep space / vessel shadows: full volumetric contrast!
            // In already bright regions (sky / direct glare): soft compression prevents overexposing to pure white!
            float sceneLuma = dot(scene.rgb, float3(0.2126, 0.7152, 0.0722));
            float headroom = saturate(1.0 - sceneLuma * 0.65);

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
