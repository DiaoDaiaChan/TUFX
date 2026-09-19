using System;

namespace UnityEngine.Rendering.PostProcessing
{
    [Serializable]
    [PostProcess(typeof(HalationRenderer), PostProcessEvent.BeforeStack, "TUFX/Halation", sortingPriority: 70)]
    public sealed class Halation : PostProcessEffectSettings
    {
        [Range(0f, 5f), Tooltip("Halation intensity.")]
        public FloatParameter intensity = new FloatParameter { value = 1.0f };

        [Range(0.1f, 10f), Tooltip("Luminance threshold above which halation triggers.")]
        public FloatParameter threshold = new FloatParameter { value = 1.2f };

        [Range(0.5f, 10f), Tooltip("Halation glow radius / blur spread.")]
        public FloatParameter radius = new FloatParameter { value = 2.5f };

        [Tooltip("Color tint of the halation diffusion (warm red/orange by default).")]
        public ColorParameter colorTint = new ColorParameter { value = new Color(1.0f, 0.35f, 0.12f, 1.0f) };

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
            sheet.properties.SetFloat("_Radius", settings.radius.value);
            sheet.properties.SetColor("_ColorTint", settings.colorTint.value);

            int width = context.width / 2;
            int height = context.height / 2;
            int rtExtract = Shader.PropertyToID("_HalationExtract");
            int rtBlurH = Shader.PropertyToID("_HalationBlurH");

            var cmd = context.command;
            cmd.GetTemporaryRT(rtExtract, width, height, 0, FilterMode.Bilinear, context.sourceFormat);
            cmd.GetTemporaryRT(rtBlurH, width, height, 0, FilterMode.Bilinear, context.sourceFormat);

            // Pass 0: Extract
            cmd.BlitFullscreenTriangle(context.source, rtExtract, sheet, 0);

            // Pass 1: Blur H
            cmd.BlitFullscreenTriangle(rtExtract, rtBlurH, sheet, 1);

            // Pass 2: Blur V & Composite
            cmd.SetGlobalTexture("_HalationTex", rtBlurH);
            cmd.BlitFullscreenTriangle(context.source, context.destination, sheet, 2);

            cmd.ReleaseTemporaryRT(rtExtract);
            cmd.ReleaseTemporaryRT(rtBlurH);
        }
    }
}
