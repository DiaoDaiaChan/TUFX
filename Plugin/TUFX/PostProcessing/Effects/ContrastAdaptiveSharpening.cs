using System;

namespace UnityEngine.Rendering.PostProcessing
{
    [Serializable]
    [PostProcess(typeof(ContrastAdaptiveSharpeningRenderer), PostProcessEvent.AfterStack, "TUFX/Contrast Adaptive Sharpening", sortingPriority: 120)]
    public sealed class ContrastAdaptiveSharpening : PostProcessEffectSettings
    {
        [Range(0f, 1f), Tooltip("Sharpening intensity (0 = disabled, 1 = maximum).")]
        public FloatParameter sharpness = new FloatParameter { value = 0.5f };

        public override bool IsEnabledAndSupported(PostProcessRenderContext context)
        {
            return enabled.value && sharpness.value > 0f;
        }

        public override void Load(ConfigNode config)
        {
            loadFloatParameter(config, "Sharpness", sharpness);
        }

        public override void Save(ConfigNode config)
        {
            saveFloatParameter(config, "Sharpness", sharpness);
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
            context.command.BlitFullscreenTriangle(context.source, context.destination, sheet, 0);
        }
    }
}
