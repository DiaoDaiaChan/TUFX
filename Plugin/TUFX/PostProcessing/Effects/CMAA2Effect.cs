using System;

namespace UnityEngine.Rendering.PostProcessing
{
    [Serializable]
    [PostProcess(typeof(CMAA2Renderer), PostProcessEvent.AfterStack, "TUFX/Intel Conservative Morphological Anti-Aliasing (CMAA 2)", sortingPriority: 110)]
    public sealed class CMAA2Effect : PostProcessEffectSettings
    {
        [Range(0.02f, 0.25f), Tooltip("Edge detection sensitivity. Lower values detect more subtle edges.")]
        public FloatParameter edgeThreshold = new FloatParameter { value = 0.08f };

        [Range(0.1f, 1.0f), Tooltip("Edge blur coefficient / sharpness retention.")]
        public FloatParameter extraSharpness = new FloatParameter { value = 0.5f };

        public override bool IsEnabledAndSupported(PostProcessRenderContext context)
        {
            return enabled.value && SystemInfo.supportsImageEffects;
        }

        public override void Load(ConfigNode config)
        {
            loadFloatParameter(config, "EdgeThreshold", edgeThreshold);
            loadFloatParameter(config, "ExtraSharpness", extraSharpness);
        }

        public override void Save(ConfigNode config)
        {
            saveFloatParameter(config, "EdgeThreshold", edgeThreshold);
            saveFloatParameter(config, "ExtraSharpness", extraSharpness);
        }
    }

    [UnityEngine.Scripting.Preserve]
    internal sealed class CMAA2Renderer : PostProcessEffectRenderer<CMAA2Effect>
    {
        public override void Render(PostProcessRenderContext context)
        {
            var shader = (TUFX.TexturesUnlimitedFXLoader.INSTANCE != null) ? TUFX.TexturesUnlimitedFXLoader.INSTANCE.getShader("Hidden/TUFX/CMAA2") : null;
            if (shader == null) shader = Shader.Find("Hidden/TUFX/CMAA2");

            if (shader == null)
            {
                context.command.BlitFullscreenTriangle(context.source, context.destination);
                return;
            }

            var sheet = context.propertySheets.Get(shader);
            sheet.properties.SetFloat("_EdgeThreshold", settings.edgeThreshold.value);
            sheet.properties.SetFloat("_ExtraSharpness", settings.extraSharpness.value);

            context.command.BlitFullscreenTriangle(context.source, context.destination, sheet, 0);
        }
    }
}
