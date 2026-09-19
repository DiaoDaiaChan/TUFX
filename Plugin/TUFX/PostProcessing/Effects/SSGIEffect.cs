using System;
using UnityEngine;

namespace UnityEngine.Rendering.PostProcessing
{
    [Serializable]
    [PostProcess(typeof(SSGIRenderer), PostProcessEvent.BeforeStack, "TUFX/Screen Space Global Illumination (SSGI)", sortingPriority: 22)]
    public sealed class SSGIEffect : PostProcessEffectSettings
    {
        [Range(0f, 4f), Tooltip("Global illumination bounce brightness multiplier.")]
        public FloatParameter intensity = new FloatParameter { value = 1.0f };

        [Range(2, 8), Tooltip("Number of hemisphere rays per pixel.")]
        public IntParameter rayCount = new IntParameter { value = 4 };

        [Range(4, 16), Tooltip("Number of march steps per ray.")]
        public IntParameter raySteps = new IntParameter { value = 8 };

        [Range(0.5f, 20f), Tooltip("Maximum distance indirect light can bounce (meters).")]
        public FloatParameter rayLength = new FloatParameter { value = 5.0f };

        [Range(0.1f, 3f), Tooltip("Geometry thickness acceptance threshold.")]
        public FloatParameter thickness = new FloatParameter { value = 1.0f };

        [Tooltip("Custom indirect bounce tint color.")]
        public ColorParameter bounceColor = new ColorParameter { value = Color.white };

        public override bool IsEnabledAndSupported(PostProcessRenderContext context)
        {
            return enabled.value && intensity.value > 0f && SystemInfo.supportsImageEffects;
        }

        public override void Load(ConfigNode config)
        {
            loadFloatParameter(config, "Intensity", intensity);
            loadIntParameter(config, "RayCount", rayCount);
            loadIntParameter(config, "RaySteps", raySteps);
            loadFloatParameter(config, "RayLength", rayLength);
            loadFloatParameter(config, "Thickness", thickness);
            loadColorParameter(config, "BounceColor", bounceColor);
        }

        public override void Save(ConfigNode config)
        {
            saveFloatParameter(config, "Intensity", intensity);
            saveIntParameter(config, "RayCount", rayCount);
            saveIntParameter(config, "RaySteps", raySteps);
            saveFloatParameter(config, "RayLength", rayLength);
            saveFloatParameter(config, "Thickness", thickness);
            saveColorParameter(config, "BounceColor", bounceColor);
        }
    }

    [UnityEngine.Scripting.Preserve]
    internal sealed class SSGIRenderer : PostProcessEffectRenderer<SSGIEffect>
    {
        public override DepthTextureMode GetCameraFlags()
        {
            return DepthTextureMode.Depth;
        }

        public override void Render(PostProcessRenderContext context)
        {
            if (context.camera == null || HighLogic.LoadedScene == GameScenes.MAINMENU ||
                (ScaledCamera.Instance != null && context.camera == ScaledCamera.Instance.cam))
            {
                context.command.BlitFullscreenTriangle(context.source, context.destination);
                return;
            }

            var shader = (TUFX.TexturesUnlimitedFXLoader.INSTANCE != null) 
                ? TUFX.TexturesUnlimitedFXLoader.INSTANCE.getShader("Hidden/TUFX/SSGI") 
                : null;
            if (shader == null) shader = Shader.Find("Hidden/TUFX/SSGI");
            if (shader == null)
            {
                context.command.BlitFullscreenTriangle(context.source, context.destination);
                return;
            }

            var sheet = context.propertySheets.Get(shader);

            Matrix4x4 proj = context.camera.projectionMatrix;
            float p00 = Mathf.Max(0.0001f, proj.m00);
            float p11 = Mathf.Max(0.0001f, proj.m11);
            float p02 = proj.m02;
            float p12 = proj.m12;

            Vector2 ndcToViewMul = new Vector2(2.0f / p00, 2.0f / p11);
            Vector2 ndcToViewAdd = new Vector2(-(1.0f + p02) / p00, -(1.0f + p12) / p11);

            sheet.properties.SetVector("_NDCToViewMul", ndcToViewMul);
            sheet.properties.SetVector("_NDCToViewAdd", ndcToViewAdd);
            sheet.properties.SetFloat("_Intensity", settings.intensity.value);
            sheet.properties.SetInt("_RayCount", settings.rayCount.value);
            sheet.properties.SetInt("_RaySteps", settings.raySteps.value);
            sheet.properties.SetFloat("_RayLength", settings.rayLength.value);
            sheet.properties.SetFloat("_Thickness", settings.thickness.value);
            sheet.properties.SetColor("_BounceColor", settings.bounceColor.value);

            bool isDeferred = context.camera != null && context.camera.actualRenderingPath == RenderingPath.DeferredShading;
            sheet.properties.SetFloat("_IsDeferred", isDeferred ? 1.0f : 0.0f);
            if (isDeferred)
            {
                sheet.properties.SetMatrix("_WorldToCameraMatrix", context.camera.worldToCameraMatrix);
            }

            int halfW = Mathf.Max(1, context.width / 2);
            int halfH = Mathf.Max(1, context.height / 2);
            int rtRawSSGI = Shader.PropertyToID("_SSGIRaw");
            int rtDenoiseSSGI = Shader.PropertyToID("_SSGIDenoise");

            var cmd = context.command;
            cmd.GetTemporaryRT(rtRawSSGI, halfW, halfH, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);
            cmd.GetTemporaryRT(rtDenoiseSSGI, halfW, halfH, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);

            // Pass 0: Raymarching at half resolution
            cmd.BlitFullscreenTriangle(context.source, rtRawSSGI, sheet, 0);

            // Pass 1: Edge-preserving bilateral denoise
            cmd.BlitFullscreenTriangle(rtRawSSGI, rtDenoiseSSGI, sheet, 1);

            // Pass 2: Composite onto scene color
            cmd.SetGlobalTexture("_SSGITex", rtDenoiseSSGI);
            cmd.BlitFullscreenTriangle(context.source, context.destination, sheet, 2);

            cmd.ReleaseTemporaryRT(rtRawSSGI);
            cmd.ReleaseTemporaryRT(rtDenoiseSSGI);
        }
    }
}
