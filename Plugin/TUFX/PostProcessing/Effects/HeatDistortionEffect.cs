using System;

namespace UnityEngine.Rendering.PostProcessing
{
    [Serializable]
    [PostProcess(typeof(HeatDistortionRenderer), PostProcessEvent.BeforeStack, "TUFX/Heat Distortion")]
    public sealed class HeatDistortionEffect : PostProcessEffectSettings
    {
        [Range(0f, 2f), Tooltip("Thermal heat wave distortion intensity.")]
        public FloatParameter intensity = new FloatParameter { value = 0.5f };

        [Range(0.2f, 10f), Tooltip("Turbulence animation speed.")]
        public FloatParameter speed = new FloatParameter { value = 2.0f };

        [Range(1f, 30f), Tooltip("Wave spatial frequency / scale.")]
        public FloatParameter scale = new FloatParameter { value = 8.0f };

        public override bool IsEnabledAndSupported(PostProcessRenderContext context)
        {
            return enabled.value && intensity.value > 0.001f;
        }

        public override void Load(ConfigNode config)
        {
            loadFloatParameter(config, "Intensity", intensity);
            loadFloatParameter(config, "Speed", speed);
            loadFloatParameter(config, "Scale", scale);
        }

        public override void Save(ConfigNode config)
        {
            saveFloatParameter(config, "Intensity", intensity);
            saveFloatParameter(config, "Speed", speed);
            saveFloatParameter(config, "Scale", scale);
        }
    }

    [UnityEngine.Scripting.Preserve]
    internal sealed class HeatDistortionRenderer : PostProcessEffectRenderer<HeatDistortionEffect>
    {
        public override void Render(PostProcessRenderContext context)
        {
            var shader = (TUFX.TexturesUnlimitedFXLoader.INSTANCE != null) ? TUFX.TexturesUnlimitedFXLoader.INSTANCE.getShader("Hidden/TUFX/HeatDistortion") : null;
            if (shader == null) shader = Shader.Find("Hidden/TUFX/HeatDistortion");
            if (shader == null) return;

            var sheet = context.propertySheets.Get(shader);
            sheet.properties.SetFloat("_Intensity", settings.intensity.value);
            sheet.properties.SetFloat("_Speed", settings.speed.value);
            sheet.properties.SetFloat("_Scale", settings.scale.value);

            context.command.BlitFullscreenTriangle(context.source, context.destination, sheet, 0);
        }
    }
}
