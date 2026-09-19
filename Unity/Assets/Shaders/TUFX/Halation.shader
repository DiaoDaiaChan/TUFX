Shader "Hidden/TUFX/Halation"
{
    HLSLINCLUDE
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"

        TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
        TEXTURE2D_SAMPLER2D(_HalationTex, sampler_HalationTex);
        float4 _MainTex_TexelSize;
        float _Threshold;
        float _Intensity;
        float _Radius;
        float4 _ColorTint; // Default warm orange/red: float4(1.0, 0.35, 0.1, 1.0)

        // Pass 0: Brightness extraction and red-tint filtering
        float4 FragExtract(VaryingsDefault i) : SV_Target
        {
            float4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            float luma = dot(color.rgb, float3(0.2126, 0.7152, 0.0722));
            float factor = saturate((luma - _Threshold) / max(0.001, _Threshold));
            
            // Emulate red-light penetration in film emulsion
            float3 halationColor = color.rgb * _ColorTint.rgb * factor;
            return float4(halationColor, 1.0);
        }

        // Pass 1: Horizontal blur
        float4 FragBlurH(VaryingsDefault i) : SV_Target
        {
            float2 step = float2(_MainTex_TexelSize.x * _Radius, 0.0);
            float4 col = float4(0, 0, 0, 0);
            
            col += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord - step * 3.0) * 0.0545;
            col += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord - step * 2.0) * 0.2442;
            col += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord - step * 1.0) * 0.4026;
            col += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord)              * 0.4026;
            col += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord + step * 1.0) * 0.4026;
            col += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord + step * 2.0) * 0.2442;
            col += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord + step * 3.0) * 0.0545;
            
            return col / 1.8052;
        }

        // Pass 2: Vertical blur and composite
        float4 FragComposite(VaryingsDefault i) : SV_Target
        {
            float2 step = float2(0.0, _MainTex_TexelSize.y * _Radius);
            float4 blur = float4(0, 0, 0, 0);

            blur += SAMPLE_TEXTURE2D(_HalationTex, sampler_HalationTex, i.texcoord - step * 3.0) * 0.0545;
            blur += SAMPLE_TEXTURE2D(_HalationTex, sampler_HalationTex, i.texcoord - step * 2.0) * 0.2442;
            blur += SAMPLE_TEXTURE2D(_HalationTex, sampler_HalationTex, i.texcoord - step * 1.0) * 0.4026;
            blur += SAMPLE_TEXTURE2D(_HalationTex, sampler_HalationTex, i.texcoord)              * 0.4026;
            blur += SAMPLE_TEXTURE2D(_HalationTex, sampler_HalationTex, i.texcoord + step * 1.0) * 0.4026;
            blur += SAMPLE_TEXTURE2D(_HalationTex, sampler_HalationTex, i.texcoord + step * 2.0) * 0.2442;
            blur += SAMPLE_TEXTURE2D(_HalationTex, sampler_HalationTex, i.texcoord + step * 3.0) * 0.0545;
            blur = blur / 1.8052;

            float4 orig = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            return orig + blur * _Intensity;
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

        // 1: Blur H
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragBlurH
            ENDHLSL
        }

        // 2: Blur V & Composite
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragComposite
            ENDHLSL
        }
    }
}
