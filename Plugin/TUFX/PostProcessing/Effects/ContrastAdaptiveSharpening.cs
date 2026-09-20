using System;

namespace UnityEngine.Rendering.PostProcessing
{
    [Serializable]
    [PostProcess(typeof(ContrastAdaptiveSharpeningRenderer), PostProcessEvent.AfterStack, "TUFX/Contrast Adaptive Sharpening", sortingPriority: 120)]
    public sealed class ContrastAdaptiveSharpening : PostProcessEffectSettings
    {
        [Range(0f, 1f), Tooltip("Sharpening intensity (0 = disabled, 1 = maximum).")]
        public FloatParameter sharpness = new FloatParameter { value = 0.5f };

        [Tooltip("Enable Dual-Scale Super-Sampling: samples both 1x micro-textures and 1.75x macro structural contours with anti-ringing protection.")]
        public BoolParameter dualScale = new BoolParameter { value = false };

        [Range(0f, 1.5f), Tooltip("Overdrive boost intensity when Dual-Scale Super-Sampling is enabled.")]
        public FloatParameter overdrive = new FloatParameter { value = 0.5f };

        public override bool IsEnabledAndSupported(PostProcessRenderContext context)
        {
            return enabled.value && (sharpness.value > 0f || (dualScale.value && overdrive.value > 0f));
        }

        public override void Load(ConfigNode config)
        {
            loadFloatParameter(config, "Sharpness", sharpness);
            loadBoolParameter(config, "DualScale", dualScale);
            loadFloatParameter(config, "Overdrive", overdrive);
        }

        public override void Save(ConfigNode config)
        {
            saveFloatParameter(config, "Sharpness", sharpness);
            saveBoolParameter(config, "DualScale", dualScale);
            saveFloatParameter(config, "Overdrive", overdrive);
        }
    }

    [UnityEngine.Scripting.Preserve]
    internal sealed class ContrastAdaptiveSharpeningRenderer : PostProcessEffectRenderer<ContrastAdaptiveSharpening>
    {
        public override void Render(PostProcessRenderContext context)
        {
            var shader = (TUFX.TexturesUnlimitedFXLoader.INSTANCE != null) ? TUFX.TexturesUnlimitedFXLoader.INSTANCE.getShader("Hidden/TUFX/ContrastAdaptiveSharpening") : null;
            if (shader == null) shader = Shader.Find("Hidden/TUFX/ContrastAdaptiveSharpening");
            if (shader == null) return;

            var sheet = context.propertySheets.Get(shader);
            sheet.properties.SetFloat("_Sharpness", settings.sharpness.value);
            sheet.properties.SetFloat("_DualScale", settings.dualScale.value ? 1.0f : 0.0f);
            sheet.properties.SetFloat("_Overdrive", settings.overdrive.value);
            context.command.BlitFullscreenTriangle(context.source, context.destination, sheet, 0);
        }
    }
}
