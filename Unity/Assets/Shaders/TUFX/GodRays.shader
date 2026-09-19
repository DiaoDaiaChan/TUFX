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

        // Pass 0: Sun Light Source Extraction with Scene Depth Occlusion
        // Extracts the Sun light source using _Threshold, so God Rays can trigger
        // at natural, moderate brightness without needing an overexposed blinding sun!
        float4 FragExtract(VaryingsDefault i) : SV_Target
        {
            if (_SunVisible <= 0.0)
            {
                return float4(0, 0, 0, 0);
            }

            float2 sunVec = (i.texcoord - _SunScreenPos) * float2(_MainTex_TexelSize.z / _MainTex_TexelSize.w, 1.0);
            float sunDist = length(sunVec);

            // Confine solar source to natural celestial scale (max 0.12, instead of bloated 0.35)
            if (sunDist > 0.12)
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

            // Sample actual scene color around the sun
            float4 sceneCol = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            float luma = dot(sceneCol.rgb, float3(0.2126, 0.7152, 0.0722));

            // Normalized threshold extraction:
            // Triggers easily at moderate sun brightness (e.g. luma >= _Threshold)
            // without requiring the sun to be artificially overexposed to crazy values!
            float extracted = max(0.0, luma - _Threshold);
            float normSource = saturate(extracted / max(0.15, 1.2 - _Threshold));

            // Celestial anchor disc (tight, radius 0.018) + soft inner corona (radius 0.08)
            float sunDisc = saturate((0.018 - sunDist) / 0.008) * 1.0;
            float sunCorona = exp(-sunDist * 32.0) * 0.7;
            float anchorSource = (sunDisc + sunCorona) * _SunVisible;

            // Combine scene brightness with solar anchor so god rays always emerge cleanly
            float sunSource = max(normSource, anchorSource) * occl * _SunVisible;

            return float4(sunSource, sunSource, sunSource, 1.0);
        }

        // Pass 1: Radial blur marching towards the Sun with continuous IGN jitter
        float4 FragRadialBlur(VaryingsDefault i) : SV_Target
        {
            if (_SunVisible <= 0.0)
            {
                return float4(0, 0, 0, 0);
            }

            float dither = frac(52.9829189 * frac(dot(i.texcoord * _MainTex_TexelSize.zw, float2(0.06711056, 0.00583715))));
            // Step towards Sun from current pixel
            float2 toSun = _SunScreenPos - i.texcoord;
            float2 stepDelta = toSun * (1.0 / 36.0) * _Density;
            float2 uv = i.texcoord + stepDelta * (dither * 0.85);
            float illuminationDecay = 1.0;
            float3 color = float3(0, 0, 0);

            [unroll(36)]
            for (int s = 0; s < 36; s++)
            {
                float3 sampleColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb;
                color += sampleColor * (illuminationDecay * _Weight);
                illuminationDecay *= _Decay;
                uv += stepDelta;
            }

            return float4(color * (_Intensity * _RayColor.rgb), 1.0);
        }

        // Pass 2: Anti-Blowout Tone-Protected Composite
        float4 FragComposite(VaryingsDefault i) : SV_Target
        {
            float4 scene = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            float3 rays = SAMPLE_TEXTURE2D(_RaysTex, sampler_MainTex, i.texcoord).rgb;

            // Core sun disc protection:
            // Light shafts stream OUTWARD from the sun; don't double-expose the sun center into a nuclear blob!
            float2 sunVec = (i.texcoord - _SunScreenPos) * float2(_MainTex_TexelSize.z / _MainTex_TexelSize.w, 1.0);
            float sunDist = length(sunVec);
            float coreDamp = smoothstep(0.005, 0.035, sunDist);

            // Hard clamp on rays to prevent triggering wild bloom blowout
            rays = min(rays, 1.8);

            // Highlight protection:
            // In deep space / atmospheric shadow: rays shine through brilliantly with full volumetric contrast.
            // In already saturated bright sky: soft roll-off prevents overexposing to solid white!
            float sceneLuma = dot(scene.rgb, float3(0.2126, 0.7152, 0.0722));
            float highlightRollOff = saturate(1.0 - sceneLuma * 0.35);

            float3 finalRays = rays * lerp(coreDamp, 1.0, 0.2) * highlightRollOff;
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
