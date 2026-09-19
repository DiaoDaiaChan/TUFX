Shader "Hidden/TUFX/ExposureMetering"
{
    HLSLINCLUDE
        #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"

        TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
        TEXTURE2D_SAMPLER2D(_CameraDepthTexture, sampler_CameraDepthTexture);

        float4 _MeteringParams; // x: mode, y: spaceFloor, z: aspect, w: unused

        float4 FragMetering(VaryingsDefault i) : SV_Target
        {
            int mode = int(_MeteringParams.x);
            float2 uv = i.texcoord;

            if (mode == 1) // Center-Weighted: non-linear radial warp concentrating center pixels
            {
                float2 d = uv - 0.5;
                float r = length(d * float2(_MeteringParams.z, 1.0));
                float warpedR = pow(saturate(r * 1.414), 1.6) * 0.707;
                float2 dir = r > 1e-4 ? d / r : float2(0, 0);
                uv = 0.5 + dir * warpedR;
            }
            else if (mode == 2) // Spot: zooms into central 25% spot
            {
                uv = (i.texcoord - 0.5) * 0.25 + 0.5;
            }

            float4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);

            if (mode == 3) // Vessel Geometry: replace skybox background pixels with center vessel pixel
            {
                float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, uv);
                #if UNITY_REVERSED_Z
                    bool isSky = (rawDepth <= 0.00005);
                #else
                    bool isSky = (rawDepth >= 0.99995);
                #endif
                if (isSky)
                {
                    // In flight, vessel CoM is locked to viewport center (0.5, 0.5)
                    color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(0.5, 0.5));
                }
            }

            return color;
        }
    ENDHLSL

    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            HLSLPROGRAM
                #pragma vertex VertDefault
                #pragma fragment FragMetering
            ENDHLSL
        }
    }
}
