using System;
using UnityEngine;

namespace UnityEngine.Rendering.PostProcessing
{
    [Serializable]
    [PostProcess(typeof(SSGIRenderer), PostProcessEvent.BeforeStack, "TUFX/Screen Space Global Illumination (SSGI)", sortingPriority: 22)]
    public sealed class SSGIEffect : PostProcessEffectSettings
    {
        [Range(0.25f, 1.0f), Tooltip("Resolution scale of the SSGI pass (1.0 = Full Resolution, 0.5 = Half Resolution, 0.25 = Quarter Resolution).")]
        public FloatParameter resolutionScale = new FloatParameter { value = 0.5f };

        [Range(0f, 5f), Tooltip("Global illumination bounce brightness multiplier.")]
        public FloatParameter intensity = new FloatParameter { value = 2.0f };

        [Range(2, 8), Tooltip("Number of hemisphere rays per pixel.")]
        public IntParameter rayCount = new IntParameter { value = 4 };

        [Range(4, 16), Tooltip("Number of march steps per ray.")]
        public IntParameter raySteps = new IntParameter { value = 8 };

        [Range(0.5f, 30f), Tooltip("Maximum distance indirect light can bounce (meters).")]
        public FloatParameter rayLength = new FloatParameter { value = 12.0f };

        [Range(0.1f, 5f), Tooltip("Geometry thickness acceptance threshold.")]
        public FloatParameter thickness = new FloatParameter { value = 1.2f };

        [Tooltip("Custom indirect bounce tint color.")]
        public ColorParameter bounceColor = new ColorParameter { value = Color.white };

        public override bool IsEnabledAndSupported(PostProcessRenderContext context)
        {
            return enabled.value && intensity.value > 0f && SystemInfo.supportsImageEffects;
        }

        public override void Load(ConfigNode config)
        {
            loadFloatParameter(config, "ResolutionScale", resolutionScale);
            loadFloatParameter(config, "Intensity", intensity);
            loadIntParameter(config, "RayCount", rayCount);
            loadIntParameter(config, "RaySteps", raySteps);
            loadFloatParameter(config, "RayLength", rayLength);
            loadFloatParameter(config, "Thickness", thickness);
            loadColorParameter(config, "BounceColor", bounceColor);
        }

        public override void Save(ConfigNode config)
        {
            saveFloatParameter(config, "ResolutionScale", resolutionScale);
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

            float scale = Mathf.Clamp(settings.resolutionScale.value, 0.25f, 1.0f);
            int rtW = Mathf.Max(1, Mathf.RoundToInt(context.width * scale));
            int rtH = Mathf.Max(1, Mathf.RoundToInt(context.height * scale));
            int rtRawSSGI = Shader.PropertyToID("_SSGIRaw");
            int rtDenoiseSSGI_H = Shader.PropertyToID("_SSGIDenoise_H");
            int rtDenoiseSSGI_V = Shader.PropertyToID("_SSGIDenoise_V");

            var cmd = context.command;
            cmd.GetTemporaryRT(rtRawSSGI, rtW, rtH, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);
            cmd.GetTemporaryRT(rtDenoiseSSGI_H, rtW, rtH, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);
            cmd.GetTemporaryRT(rtDenoiseSSGI_V, rtW, rtH, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);

            // Pass 0: Raymarching at half resolution with Karis anti-firefly weighting
            cmd.BlitFullscreenTriangle(context.source, rtRawSSGI, sheet, 0);

            // Pass 1: Horizontal bilateral denoise (wide-band 9-tap)
            cmd.BlitFullscreenTriangle(rtRawSSGI, rtDenoiseSSGI_H, sheet, 1);

            // Pass 2: Vertical bilateral denoise (wide-band 9-tap)
            cmd.BlitFullscreenTriangle(rtDenoiseSSGI_H, rtDenoiseSSGI_V, sheet, 2);

            // Pass 3: Composite onto scene color
            cmd.SetGlobalTexture("_SSGITex", rtDenoiseSSGI_V);
            cmd.BlitFullscreenTriangle(context.source, context.destination, sheet, 3);

            cmd.ReleaseTemporaryRT(rtRawSSGI);
            cmd.ReleaseTemporaryRT(rtDenoiseSSGI_H);
            cmd.ReleaseTemporaryRT(rtDenoiseSSGI_V);
        }
    }
}
