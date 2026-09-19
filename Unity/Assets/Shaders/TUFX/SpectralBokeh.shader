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

        float CalculateCoC(float depth)
        {
            float linearDepth = LinearEyeDepth(depth);
            // Circle of Confusion formula
            float s = max(0.001, linearDepth);
            float f = _FocalLength * 0.001; // mm to meters
            float d = _FocusDistance;
            float coc = abs(s - d) / s * (f * f / max(0.001, d - f));
            return clamp(coc * 1000.0, 0.0, _MaxBokehRadius);
        }

        float4 FragSpectralBokeh(VaryingsDefault i) : SV_Target
        {
            float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord);
            float coc = CalculateCoC(rawDepth);

            if (coc <= 0.5)
            {
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            }

            // Disk sample with spectral dispersion (R, G, B channel separation)
            float3 col = float3(0, 0, 0);
            float totalWeight = 0.0;
            const int SAMPLES = 16;
            
            [unroll(16)]
            for (int s = 0; s < SAMPLES; s++)
            {
                float angle = (float)s * (6.2831853 / (float)SAMPLES);
                float radius = sqrt((float)s / (float)SAMPLES) * coc;
                float2 offset = float2(cos(angle), sin(angle)) * _MainTex_TexelSize.xy * radius;

                // Spectral dispersion offsets
                float2 offsetR = offset * (1.0 + _DispersionStrength);
                float2 offsetG = offset;
                float2 offsetB = offset * (1.0 - _DispersionStrength);

                float r = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord + offsetR).r;
                float g = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord + offsetG).g;
                float b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord + offsetB).b;

                col += float3(r, g, b);
                totalWeight += 1.0;
            }

            return float4(col / totalWeight, 1.0);
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
