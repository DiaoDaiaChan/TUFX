Shader "Hidden/TUFX/SpectralBokeh"
{
    HLSLINCLUDE
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"

        TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
        TEXTURE2D_SAMPLER2D(_CameraDepthTexture, sampler_CameraDepthTexture);
        float4 _MainTex_TexelSize;
        float _FocusDistance;
        float _FocalLength;
        float _DispersionStrength;
        float _MaxBokehRadius;
        float _AnamorphicRatio;

        float CalculateCoC(float depth)
        {
            float linearDepth = LinearEyeDepth(depth);
            float s = max(0.01, linearDepth);
            float f = max(0.001, _FocalLength * 0.001); // mm to meters
            float d = max(f + 0.01, max(0.01, _FocusDistance));
            
            // Optical thin-lens Circle of Confusion (f/1.8 equivalent aperture)
            float aperture = f / 1.8;
            float cocMeters = aperture * (abs(s - d) / s) * (f / max(0.001, d - f));
            
            // Project sensor size (36mm x 24mm full frame, 0.024m height) to screen pixels
            float screenH = _MainTex_TexelSize.w > 100.0 ? _MainTex_TexelSize.w : 1080.0;
            float cocPixels = cocMeters * (screenH / 0.024);

            return clamp(cocPixels, 0.0, _MaxBokehRadius);
        }

        float4 FragSpectralBokeh(VaryingsDefault i) : SV_Target
        {
            float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord);
            float coc = CalculateCoC(rawDepth);

            if (coc <= 0.2)
            {
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            }

            // Anamorphic squeeze ratio (2.0 = classic Hollywood cinema vertical oval bokeh)
            float aspect = max(0.2, _AnamorphicRatio);
            float invSqrtAspect = 1.0 / sqrt(aspect);
            float sqrtAspect = sqrt(aspect);

            // Golden angle Vogel disk sampling (uniform bokeh disc coverage)
            float3 col = float3(0, 0, 0);
            float totalWeight = 0.0;
            const int SAMPLES = 20;
            
            [unroll(20)]
            for (int s = 0; s < SAMPLES; s++)
            {
                // Golden angle = 2.39996323 radians (~137.5 degrees)
                float angle = (float)s * 2.39996323;
                float radius = sqrt(((float)s + 0.5) / (float)SAMPLES) * coc;
                float2 unitDir = float2(cos(angle) * invSqrtAspect, sin(angle) * sqrtAspect);
                float2 offset = unitDir * _MainTex_TexelSize.xy * radius;

                // Spectral dispersion offsets (wavelength-dependent focal shift)
                float2 offsetR = offset * (1.0 + _DispersionStrength * 0.5);
                float2 offsetG = offset;
                float2 offsetB = offset * (1.0 - _DispersionStrength * 0.5);

                float r = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord + offsetR).r;
                float g = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord + offsetG).g;
                float b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord + offsetB).b;

                // Subtle edge weighting for realistic bright-ring bokeh
                float sampleWeight = 1.0 + 0.3 * (radius / max(0.1, coc));

                col += float3(r, g, b) * sampleWeight;
                totalWeight += sampleWeight;
            }

            float4 orig = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            float3 blurred = col / totalWeight;
            float blend = smoothstep(0.2, 1.0, coc);
            return float4(lerp(orig.rgb, blurred, blend), orig.a);
        }
    ENDHLSL

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragSpectralBokeh
            ENDHLSL
        }
    }
}
