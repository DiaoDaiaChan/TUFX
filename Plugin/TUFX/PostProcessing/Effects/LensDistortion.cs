using System;
using TUFX;

namespace UnityEngine.Rendering.PostProcessing
{
    /// <summary>
    /// This class holds settings for the Lens Distortion effect.
    /// </summary>
    [Serializable]
    [PostProcess(typeof(LensDistortionRenderer), "Unity/Lens Distortion")]
    public sealed class LensDistortion : PostProcessEffectSettings
    {
        /// <summary>
        /// The total amount of distortion to apply.
        /// </summary>
        [Range(-100f, 100f), Tooltip("Total distortion amount.")]
        public FloatParameter intensity = new FloatParameter { value = 0f };

        /// <summary>
        /// Multiplies the intensity value on the x-axis. Setting this value to 0 will disable distortion on this axis.
        /// </summary>
        [Range(0f, 1f), DisplayName("X Multiplier"), Tooltip("Intensity multiplier on the x-axis. Set it to 0 to disable distortion on this axis.")]
        public FloatParameter intensityX = new FloatParameter { value = 1f };

        /// <summary>
        /// Multiplies the intensity value on the y-axis. Setting this value to 0 will disable distortion on this axis.
        /// </summary>
        [Range(0f, 1f), DisplayName("Y Multiplier"), Tooltip("Intensity multiplier on the y-axis. Set it to 0 to disable distortion on this axis.")]
        public FloatParameter intensityY = new FloatParameter { value = 1f };

        /// <summary>
        /// The center point for the distortion (x-axis).
        /// </summary>
        [Space]
        [Range(-1f, 1f), Tooltip("Distortion center point (x-axis).")]
        public FloatParameter centerX = new FloatParameter { value = 0f };

        /// <summary>
        /// The center point for the distortion (y-axis).
        /// </summary>
        [Range(-1f, 1f), Tooltip("Distortion center point (y-axis).")]
        public FloatParameter centerY = new FloatParameter { value = 0f };

        /// <summary>
        /// Anamorphic squeeze ratio. 1.0 is standard spherical lens. Values > 1.0 produce horizontal anamorphic barrel distortion squeeze.
        /// </summary>
        [Range(0.5f, 2.5f), DisplayName("Anamorphic Ratio"), Tooltip("Anamorphic squeeze ratio (1.0 = spherical, 2.0 = 2x anamorphic lens).")]
        public FloatParameter anamorphicRatio = new FloatParameter { value = 1f };

        /// <summary>
        /// Cat's eye optical vignetting factor caused by lens barrel ray clipping.
        /// </summary>
        [Range(0f, 1f), DisplayName("Optical Vignetting"), Tooltip("Physical cat's eye lens barrel vignetting towards frame corners.")]
        public FloatParameter opticalVignetting = new FloatParameter { value = 0f };

        /// <summary>
        /// A global screen scaling factor.
        /// </summary>
        [Space]
        [Range(0.01f, 5f), Tooltip("Global screen scaling.")]
        public FloatParameter scale = new FloatParameter { value = 1f };

        /// <summary>
        /// Returns <c>true</c> if the effect is currently enabled and supported.
        /// </summary>
        /// <param name="context">The current post-processing render context</param>
        /// <returns><c>true</c> if the effect is currently enabled and supported</returns>
        public override bool IsEnabledAndSupported(PostProcessRenderContext context)
        {
            return enabled.value
                && (!Mathf.Approximately(intensity, 0f) || opticalVignetting > 0f)
                && (intensityX > 0f || intensityY > 0f)
                && !RuntimeUtilities.isVREnabled;
        }

        public override void Load(ConfigNode config)
        {
            loadFloatParameter(config, "Intensity", intensity);
            loadFloatParameter(config, "IntensityX", intensityX);
            loadFloatParameter(config, "IntensityY", intensityY);
            loadFloatParameter(config, "AnamorphicRatio", anamorphicRatio);
            loadFloatParameter(config, "OpticalVignetting", opticalVignetting);
            loadFloatParameter(config, "CenterX", centerX);
            loadFloatParameter(config, "CenterY", centerY);
            loadFloatParameter(config, "Scale", scale);
        }

        public override void Save(ConfigNode config)
        {
            saveFloatParameter(config, "Intensity", intensity);
            saveFloatParameter(config, "IntensityX", intensityX);
            saveFloatParameter(config, "IntensityY", intensityY);
            saveFloatParameter(config, "AnamorphicRatio", anamorphicRatio);
            saveFloatParameter(config, "OpticalVignetting", opticalVignetting);
            saveFloatParameter(config, "CenterX", centerX);
            saveFloatParameter(config, "CenterY", centerY);
            saveFloatParameter(config, "Scale", scale);
        }

    }

    [UnityEngine.Scripting.Preserve]
    internal sealed class LensDistortionRenderer : PostProcessEffectRenderer<LensDistortion>
    {
        public override void Render(PostProcessRenderContext context)
        {
            var sheet = context.uberSheet;

            if (!Mathf.Approximately(settings.intensity.value, 0f))
            {
                float amount = 1.6f * Math.Max(Mathf.Abs(settings.intensity.value), 1f);
                float theta = Mathf.Deg2Rad * Math.Min(160f, amount);
                float sigma = 2f * Mathf.Tan(theta * 0.5f);

                float xIntensity = Mathf.Max(settings.intensityX.value * settings.anamorphicRatio.value, 1e-4f);
                float yIntensity = Mathf.Max(settings.intensityY.value, 1e-4f);

                var p0 = new Vector4(settings.centerX.value, settings.centerY.value, xIntensity, yIntensity);
                var p1 = new Vector4(settings.intensity.value >= 0f ? theta : 1f / theta, sigma, 1f / settings.scale.value, settings.intensity.value);

                sheet.EnableKeyword("DISTORT");
                sheet.properties.SetVector(ShaderIDs.Distortion_CenterScale, p0);
                sheet.properties.SetVector(ShaderIDs.Distortion_Amount, p1);
            }

            if (settings.opticalVignetting.value > 0f)
            {
                sheet.EnableKeyword("VIGNETTE");
                sheet.properties.SetColor(ShaderIDs.Vignette_Color, Color.black);
                sheet.properties.SetFloat(ShaderIDs.Vignette_Mode, 0f);
                sheet.properties.SetVector(ShaderIDs.Vignette_Center, new Vector2(0.5f + settings.centerX.value * 0.1f, 0.5f + settings.centerY.value * 0.1f));
                float optIntensity = settings.opticalVignetting.value * 1.8f;
                float optSmoothness = 0.6f * 5f;
                float optRoundness = Mathf.Clamp(1.0f / Mathf.Max(0.1f, settings.anamorphicRatio.value), 0.2f, 2.0f);
                sheet.properties.SetVector(ShaderIDs.Vignette_Settings, new Vector4(optIntensity, optSmoothness, optRoundness, 1f));
            }
        }
    }
}
