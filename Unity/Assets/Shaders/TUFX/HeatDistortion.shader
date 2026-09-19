Shader "Hidden/TUFX/HeatDistortion"
{
    HLSLINCLUDE
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"

        TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
        float _Intensity; // 0 (off) to 1.0
        float _Speed;
        float _Scale;

        // Procedural 2D noise
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

        float4 FragHeatDistortion(VaryingsDefault i) : SV_Target
        {
            if (_Intensity <= 0.001)
            {
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            }

            float2 uv = i.texcoord;
            float time = _Time.y * _Speed;

            // Compute distortion flow vector
            float n1 = Noise2D(uv * _Scale + float2(0, time));
            float n2 = Noise2D(uv * _Scale + float2(time * 0.7, 0));
            float2 flow = float2(n1 - 0.5, n2 - 0.5) * (_Intensity * 0.05);

            // Slight chromatic aberration on distortion
            float r = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + flow * 1.05).r;
            float g = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + flow).g;
            float b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + flow * 0.95).b;

            return float4(r, g, b, 1.0);
        }
    ENDHLSL

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragHeatDistortion
            ENDHLSL
        }
    }
}
