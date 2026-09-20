using System;

namespace UnityEngine.Rendering.PostProcessing
{
    [Serializable]
    [PostProcess(typeof(CMAA2Renderer), PostProcessEvent.AfterStack, "TUFX/Intel Conservative Morphological Anti-Aliasing (CMAA 2)", sortingPriority: 110)]
    public sealed class CMAA2Effect : PostProcessEffectSettings
    {
        [Range(0.02f, 0.20f), Tooltip("Edge detection sensitivity. Lower values detect more subtle edges (Intel High: 0.07, Ultra: 0.05, Medium: 0.10).")]
        public FloatParameter edgeThreshold = new FloatParameter { value = 0.07f };

        [Tooltip("Extra sharpness mode: preserves pin-sharp text, UI, decals, and pinpoint stars while preventing excessive morphological blur.")]
        public BoolParameter extraSharpness = new BoolParameter { value = true };

        [Range(8, 86), Tooltip("Longest edge line search distance in pixels. Higher values produce smoother gradients along long diagonal edges (Default: 64, Max: 86).")]
        public IntParameter maxSearchLength = new IntParameter { value = 64 };

        [Range(0, 2), Tooltip("Diagnostic display: 0 = Normal, 1 = Show Detected Edges, 2 = Show Anti-Aliasing Blend Weights.")]
        public IntParameter debugMode = new IntParameter { value = 0 };

        public override bool IsEnabledAndSupported(PostProcessRenderContext context)
        {
            return enabled.value && SystemInfo.supportsImageEffects;
        }

        public override void Load(ConfigNode config)
        {
            loadFloatParameter(config, "EdgeThreshold", edgeThreshold);
            loadBoolParameter(config, "ExtraSharpness", extraSharpness);
            loadIntParameter(config, "MaxSearchLength", maxSearchLength);
            loadIntParameter(config, "DebugMode", debugMode);
        }

        public override void Save(ConfigNode config)
        {
            saveFloatParameter(config, "EdgeThreshold", edgeThreshold);
            saveBoolParameter(config, "ExtraSharpness", extraSharpness);
            saveIntParameter(config, "MaxSearchLength", maxSearchLength);
            saveIntParameter(config, "DebugMode", debugMode);
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
            var cmd = context.command;

            float threshold = settings.edgeThreshold.value;
            bool extraSharp = settings.extraSharpness.value;
            float localContrast = extraSharp ? 0.15f : 0.10f;
            float simpleBlur = extraSharp ? 0.07f : 0.10f;
            int maxLine = Mathf.Clamp(settings.maxSearchLength.value, 8, 86);
            int debug = settings.debugMode.value;

            sheet.properties.SetFloat("_EdgeThreshold", threshold);
            sheet.properties.SetFloat("_ExtraSharpness", extraSharp ? 1.0f : 0.0f);
            sheet.properties.SetFloat("_LocalContrastAdaptationAmount", localContrast);
            sheet.properties.SetFloat("_SimpleShapeBlurinessAmount", simpleBlur);
            sheet.properties.SetFloat("_MaxLineLength", (float)maxLine);
            sheet.properties.SetFloat("_DebugMode", (float)debug);

            int rtEdges = Shader.PropertyToID("_EdgesTex");
            cmd.GetTemporaryRT(rtEdges, context.width, context.height, 0, FilterMode.Point, RenderTextureFormat.ARGB32);

            // Pass 0: Edge Detection with Local Contrast Adaptation
            cmd.BlitFullscreenTriangle(context.source, rtEdges, sheet, 0);

            // Pass 1: Morphological Edge Tracing & Subpixel Coverage Blending
            cmd.SetGlobalTexture("_EdgesTex", rtEdges);
            cmd.BlitFullscreenTriangle(context.source, context.destination, sheet, 1);

            cmd.ReleaseTemporaryRT(rtEdges);
        }
    }
}
