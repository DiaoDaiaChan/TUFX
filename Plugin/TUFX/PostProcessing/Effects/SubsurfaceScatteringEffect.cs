using System;
using UnityEngine;

namespace UnityEngine.Rendering.PostProcessing
{
    [Serializable]
    [PostProcess(typeof(SubsurfaceScatteringRenderer), PostProcessEvent.BeforeStack, "TUFX/Screen Space Subsurface Scattering (SSSS)", sortingPriority: 24)]
    public sealed class SubsurfaceScatteringEffect : PostProcessEffectSettings
    {
        [Range(0f, 1f), Tooltip("Subsurface scattering effect intensity.")]
        public FloatParameter intensity = new FloatParameter { value = 0.5f };

        [Range(0.1f, 10f), Tooltip("Diffusion scatter radius in millimeters/screen scale.")]
        public FloatParameter scatterRadius = new FloatParameter { value = 2.5f };

        [Range(0.01f, 0.5f), Tooltip("Bilateral depth discontinuity threshold (meters) to prevent edge bleeding.")]
        public FloatParameter depthThreshold = new FloatParameter { value = 0.05f };

        [Tooltip("Subsurface scatter tint color (e.g. warm peach for skin/EVA, or cool cyan for polar ice).")]
        public ColorParameter subsurfaceColor = new ColorParameter { value = new Color(1.0f, 0.85f, 0.75f, 1.0f) };

        public override bool IsEnabledAndSupported(PostProcessRenderContext context)
        {
            return enabled.value && intensity.value > 0f && SystemInfo.supportsImageEffects;
        }

        public override void Load(ConfigNode config)
        {
            loadFloatParameter(config, "Intensity", intensity);
            loadFloatParameter(config, "ScatterRadius", scatterRadius);
            loadFloatParameter(config, "DepthThreshold", depthThreshold);
            loadColorParameter(config, "SubsurfaceColor", subsurfaceColor);
        }

        public override void Save(ConfigNode config)
        {
            saveFloatParameter(config, "Intensity", intensity);
            saveFloatParameter(config, "ScatterRadius", scatterRadius);
            saveFloatParameter(config, "DepthThreshold", depthThreshold);
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
            sheet.properties.SetColor("_SubsurfaceColor", settings.subsurfaceColor.value);

            int width = context.width;
            int height = context.height;
            int rtIntermediate = Shader.PropertyToID("_SSSSIntermediate");

            var cmd = context.command;
            cmd.GetTemporaryRT(rtIntermediate, width, height, 0, FilterMode.Bilinear, context.sourceFormat);

            // Pass 0: Horizontal SSSS
            cmd.BlitFullscreenTriangle(context.source, rtIntermediate, sheet, 0);

            // Pass 1: Vertical SSSS & Composite
            cmd.SetGlobalTexture("_SSSSIntermediate", rtIntermediate);
            cmd.BlitFullscreenTriangle(context.source, context.destination, sheet, 1);

            cmd.ReleaseTemporaryRT(rtIntermediate);
        }
    }
}
