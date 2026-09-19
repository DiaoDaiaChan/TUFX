using System;

namespace UnityEngine.Rendering.PostProcessing
{
    [Serializable]
    [PostProcess(typeof(SpectralBokehRenderer), PostProcessEvent.BeforeStack, "TUFX/Spectral Bokeh (Chromatic DoF)")]
    public sealed class SpectralBokeh : PostProcessEffectSettings
    {
        [Range(0.1f, 500f), Tooltip("Distance to the focus plane in meters.")]
        public FloatParameter focusDistance = new FloatParameter { value = 10f };

        [Range(10f, 300f), Tooltip("Camera lens focal length in mm.")]
        public FloatParameter focalLength = new FloatParameter { value = 50f };

        [Range(0f, 2f), Tooltip("Chromatic dispersion strength on out-of-focus bokeh.")]
        public FloatParameter dispersionStrength = new FloatParameter { value = 0.5f };

        [Range(1f, 20f), Tooltip("Max bokeh blur radius.")]
        public FloatParameter maxBokehRadius = new FloatParameter { value = 8.0f };

        public override bool IsEnabledAndSupported(PostProcessRenderContext context)
        {
            return enabled.value && maxBokehRadius.value > 0.5f && SystemInfo.supportsImageEffects;
        }

        public override void Load(ConfigNode config)
        {
            loadFloatParameter(config, "FocusDistance", focusDistance);
            loadFloatParameter(config, "FocalLength", focalLength);
            loadFloatParameter(config, "DispersionStrength", dispersionStrength);
            loadFloatParameter(config, "MaxBokehRadius", maxBokehRadius);
        }

        public override void Save(ConfigNode config)
        {
            saveFloatParameter(config, "FocusDistance", focusDistance);
            saveFloatParameter(config, "FocalLength", focalLength);
            saveFloatParameter(config, "DispersionStrength", dispersionStrength);
            saveFloatParameter(config, "MaxBokehRadius", maxBokehRadius);
        }
    }

    [UnityEngine.Scripting.Preserve]
    internal sealed class SpectralBokehRenderer : PostProcessEffectRenderer<SpectralBokeh>
    {
        public override DepthTextureMode GetCameraFlags()
        {
            return DepthTextureMode.Depth;
        }

        public override void Render(PostProcessRenderContext context)
        {
            var shader = (TUFX.TexturesUnlimitedFXLoader.INSTANCE != null) ? TUFX.TexturesUnlimitedFXLoader.INSTANCE.getShader("Hidden/TUFX/SpectralBokeh") : null;
            if (shader == null) shader = Shader.Find("Hidden/TUFX/SpectralBokeh");
            if (shader == null) return;

            var sheet = context.propertySheets.Get(shader);
            sheet.properties.SetFloat("_FocusDistance", settings.focusDistance.value);
            sheet.properties.SetFloat("_FocalLength", settings.focalLength.value);
            sheet.properties.SetFloat("_DispersionStrength", settings.dispersionStrength.value);
            sheet.properties.SetFloat("_MaxBokehRadius", settings.maxBokehRadius.value);

            context.command.BlitFullscreenTriangle(context.source, context.destination, sheet, 0);
        }
    }
}
