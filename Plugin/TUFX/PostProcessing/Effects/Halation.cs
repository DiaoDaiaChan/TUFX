using System;
using UnityEngine;

namespace UnityEngine.Rendering.PostProcessing
{
    [Serializable]
    [PostProcess(typeof(HalationRenderer), PostProcessEvent.BeforeStack, "TUFX/Halation", sortingPriority: 70)]
    public sealed class Halation : PostProcessEffectSettings
    {
        [Range(0f, 5f), Tooltip("Halation intensity.")]
        public FloatParameter intensity = new FloatParameter { value = 1.2f };

        [Range(0.1f, 5f), Tooltip("Luminance threshold above which halation triggers.")]
        public FloatParameter threshold = new FloatParameter { value = 0.75f };

        [Range(0.5f, 10f), Tooltip("Halation glow radius / blur spread.")]
        public FloatParameter radius = new FloatParameter { value = 3.5f };

        [Tooltip("Color tint of the halation diffusion (warm red/orange CineStill 800T style).")]
        public ColorParameter colorTint = new ColorParameter { value = new Color(1.0f, 0.28f, 0.10f, 1.0f) };

        public override bool IsEnabledAndSupported(PostProcessRenderContext context)
        {
            return enabled.value && intensity.value > 0f;
        }

        public override void Load(ConfigNode config)
        {
            loadFloatParameter(config, "Intensity", intensity);
            loadFloatParameter(config, "Threshold", threshold);
            loadFloatParameter(config, "Radius", radius);
            loadColorParameter(config, "ColorTint", colorTint);
        }

        public override void Save(ConfigNode config)
        {
            saveFloatParameter(config, "Intensity", intensity);
            saveFloatParameter(config, "Threshold", threshold);
            saveFloatParameter(config, "Radius", radius);
            saveColorParameter(config, "ColorTint", colorTint);
        }
    }

    [UnityEngine.Scripting.Preserve]
    internal sealed class HalationRenderer : PostProcessEffectRenderer<Halation>
    {
        public override void Render(PostProcessRenderContext context)
        {
            var shader = (TUFX.TexturesUnlimitedFXLoader.INSTANCE != null) ? TUFX.TexturesUnlimitedFXLoader.INSTANCE.getShader("Hidden/TUFX/Halation") : null;
            if (shader == null) shader = Shader.Find("Hidden/TUFX/Halation");
            if (shader == null) return;

            var sheet = context.propertySheets.Get(shader);
            sheet.properties.SetFloat("_Intensity", settings.intensity.value);
            sheet.properties.SetFloat("_Threshold", settings.threshold.value);
            sheet.properties.SetColor("_ColorTint", settings.colorTint.value);

            int width = Mathf.Max(1, context.width / 2);
            int height = Mathf.Max(1, context.height / 2);

            int rtExtract = Shader.PropertyToID("_HalationExtract");
            int rtTemp = Shader.PropertyToID("_HalationTemp");
            int rtTight = Shader.PropertyToID("_HalationTightTex");
            int rtWide = Shader.PropertyToID("_HalationWideTex");

            var cmd = context.command;
            cmd.GetTemporaryRT(rtExtract, width, height, 0, FilterMode.Bilinear, context.sourceFormat);
            cmd.GetTemporaryRT(rtTemp, width, height, 0, FilterMode.Bilinear, context.sourceFormat);
            cmd.GetTemporaryRT(rtTight, width, height, 0, FilterMode.Bilinear, context.sourceFormat);
            cmd.GetTemporaryRT(rtWide, width, height, 0, FilterMode.Bilinear, context.sourceFormat);

            // Pass 0: Soft-Knee Extract
            cmd.BlitFullscreenTriangle(context.source, rtExtract, sheet, 0);

            float r = Mathf.Max(0.5f, settings.radius.value);

            // Tight Diffusion (Pass 1 H + Pass 2 V)
            sheet.properties.SetFloat("_Radius", r * 1.5f);
            cmd.BlitFullscreenTriangle(rtExtract, rtTemp, sheet, 1);
            cmd.BlitFullscreenTriangle(rtTemp, rtTight, sheet, 2);

            // Wide Diffusion (Pass 1 H + Pass 2 V)
            sheet.properties.SetFloat("_Radius", r * 6.0f);
            cmd.BlitFullscreenTriangle(rtExtract, rtTemp, sheet, 1);
            cmd.BlitFullscreenTriangle(rtTemp, rtWide, sheet, 2);

            // Pass 3: Composite
            cmd.SetGlobalTexture("_HalationTightTex", rtTight);
            cmd.SetGlobalTexture("_HalationWideTex", rtWide);
            cmd.BlitFullscreenTriangle(context.source, context.destination, sheet, 3);

            cmd.ReleaseTemporaryRT(rtExtract);
            cmd.ReleaseTemporaryRT(rtTemp);
            cmd.ReleaseTemporaryRT(rtTight);
            cmd.ReleaseTemporaryRT(rtWide);
        }
    }
}
