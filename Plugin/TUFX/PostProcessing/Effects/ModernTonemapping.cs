using System;

namespace UnityEngine.Rendering.PostProcessing
{
    public enum ModernTonemapper
    {
        AgX = 0,
        TonyMcMapface = 1,
        Filmic = 2
    }

    [Serializable]
    public sealed class ModernTonemapperParameter : ParameterOverride<ModernTonemapper> { }

    [Serializable]
    [PostProcess(typeof(ModernTonemappingRenderer), PostProcessEvent.AfterStack, "TUFX/Modern Tonemapping")]
    public sealed class ModernTonemapping : PostProcessEffectSettings
    {
        [Tooltip("Modern Tonemapper algorithm: AgX, Tony McMapface, or Filmic.")]
        public ModernTonemapperParameter tonemapper = new ModernTonemapperParameter { value = ModernTonemapper.AgX };

        [Range(0.01f, 10f), Tooltip("Pre-exposure adjustment multiplier.")]
        public FloatParameter exposure = new FloatParameter { value = 1.0f };

        [Range(0f, 2f), Tooltip("Post-tonemap saturation multiplier.")]
        public FloatParameter saturation = new FloatParameter { value = 1.0f };

        public override bool IsEnabledAndSupported(PostProcessRenderContext context)
        {
            return enabled.value;
        }

        public override void Load(ConfigNode config)
        {
            loadEnumParameter(config, "Tonemapper", tonemapper, typeof(ModernTonemapper));
            loadFloatParameter(config, "Exposure", exposure);
            loadFloatParameter(config, "Saturation", saturation);
        }

        public override void Save(ConfigNode config)
        {
            saveEnumParameter(config, "Tonemapper", tonemapper);
            saveFloatParameter(config, "Exposure", exposure);
            saveFloatParameter(config, "Saturation", saturation);
        }
    }

    [UnityEngine.Scripting.Preserve]
    internal sealed class ModernTonemappingRenderer : PostProcessEffectRenderer<ModernTonemapping>
    {
        public override void Render(PostProcessRenderContext context)
        {
            var shader = (TUFX.TexturesUnlimitedFXLoader.INSTANCE != null) ? TUFX.TexturesUnlimitedFXLoader.INSTANCE.getShader("Hidden/TUFX/ModernTonemapping") : null;
            if (shader == null) shader = Shader.Find("Hidden/TUFX/ModernTonemapping");
            if (shader == null) return;

            var sheet = context.propertySheets.Get(shader);
            sheet.properties.SetInt("_Mode", (int)settings.tonemapper.value);
            sheet.properties.SetFloat("_Exposure", settings.exposure.value);
            sheet.properties.SetFloat("_Saturation", settings.saturation.value);

            context.command.BlitFullscreenTriangle(context.source, context.destination, sheet, 0);
        }
    }
}
