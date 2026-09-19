using System;
using UnityEngine;

namespace UnityEngine.Rendering.PostProcessing
{
    [Serializable]
    [PostProcess(typeof(SubsurfaceScatteringRenderer), PostProcessEvent.BeforeStack, "TUFX/Screen Space Subsurface Scattering (SSSS)", sortingPriority: 24)]
    public sealed class SubsurfaceScatteringEffect : PostProcessEffectSettings
    {
        [Range(0f, 1f), Tooltip("Subsurface scattering effect intensity.")]
        public FloatParameter intensity = new FloatParameter { value = 0.4f };

        [Range(0.5f, 15f), Tooltip("Diffusion scatter radius.")]
        public FloatParameter scatterRadius = new FloatParameter { value = 3.5f };

        [Range(0.01f, 0.5f), Tooltip("Bilateral depth discontinuity threshold (meters) to prevent edge bleeding.")]
        public FloatParameter depthThreshold = new FloatParameter { value = 0.08f };

        [Range(2f, 50f), Tooltip("Maximum distance (meters) for SSSS to activate. Beyond this range, full scene sharpness is preserved.")]
        public FloatParameter maxDistance = new FloatParameter { value = 25.0f };

        [Tooltip("Automatically adapt subsurface tint to the surface material's native chrominance (keeps satellites neutral, gives Kerbals organic green glow).")]
        public BoolParameter autoAdapt = new BoolParameter { value = true };

        [Tooltip("Subsurface scatter tint color (used when Auto Adapt is disabled).")]
        public ColorParameter subsurfaceColor = new ColorParameter { value = new Color(1.0f, 0.92f, 0.85f, 1.0f) };

        public override bool IsEnabledAndSupported(PostProcessRenderContext context)
        {
            return enabled.value && intensity.value > 0f && SystemInfo.supportsImageEffects;
        }

        public override void Load(ConfigNode config)
        {
            loadFloatParameter(config, "Intensity", intensity);
            loadFloatParameter(config, "ScatterRadius", scatterRadius);
            loadFloatParameter(config, "DepthThreshold", depthThreshold);
            loadFloatParameter(config, "MaxDistance", maxDistance);
            loadBoolParameter(config, "AutoAdapt", autoAdapt);
            loadColorParameter(config, "SubsurfaceColor", subsurfaceColor);
        }

        public override void Save(ConfigNode config)
        {
            saveFloatParameter(config, "Intensity", intensity);
            saveFloatParameter(config, "ScatterRadius", scatterRadius);
            saveFloatParameter(config, "DepthThreshold", depthThreshold);
            saveFloatParameter(config, "MaxDistance", maxDistance);
            saveBoolParameter(config, "AutoAdapt", autoAdapt);
            saveColorParameter(config, "SubsurfaceColor", subsurfaceColor);
        }
    }

    [UnityEngine.Scripting.Preserve]
    internal sealed class SubsurfaceScatteringRenderer : PostProcessEffectRenderer<SubsurfaceScatteringEffect>
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
                ? TUFX.TexturesUnlimitedFXLoader.INSTANCE.getShader("Hidden/TUFX/SubsurfaceScattering")
                : null;
            if (shader == null) shader = Shader.Find("Hidden/TUFX/SubsurfaceScattering");
            if (shader == null)
            {
                context.command.BlitFullscreenTriangle(context.source, context.destination);
                return;
            }

            var sheet = context.propertySheets.Get(shader);
            sheet.properties.SetFloat("_Intensity", settings.intensity.value);
            sheet.properties.SetFloat("_ScatterRadius", settings.scatterRadius.value);
            sheet.properties.SetFloat("_DepthThreshold", settings.depthThreshold.value);
            sheet.properties.SetFloat("_MaxDistance", settings.maxDistance.value);
            sheet.properties.SetFloat("_AutoAdapt", settings.autoAdapt.value ? 1.0f : 0.0f);
            sheet.properties.SetColor("_SubsurfaceColor", settings.subsurfaceColor.value);

            int width = context.width;
            int height = context.height;
            int rtIntermediate = Shader.PropertyToID("_SSSSIntermediate");

            var cmd = context.command;
            cmd.GetTemporaryRT(rtIntermediate, width, height, 0, FilterMode.Bilinear, context.sourceFormat);

            // Pass 0: Horizontal Blur
            cmd.BlitFullscreenTriangle(context.source, rtIntermediate, sheet, 0);

            // Pass 1: Vertical Blur & Composite
            cmd.SetGlobalTexture("_SSSSIntermediate", rtIntermediate);
            cmd.BlitFullscreenTriangle(context.source, context.destination, sheet, 1);

            cmd.ReleaseTemporaryRT(rtIntermediate);
        }
    }
}
