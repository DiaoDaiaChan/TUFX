using System;

namespace UnityEngine.Rendering.PostProcessing
{
    [Serializable]
    [PostProcess(typeof(AnamorphicFlareRenderer), PostProcessEvent.BeforeStack, "TUFX/Anamorphic Flare & Starburst")]
    public sealed class AnamorphicFlare : PostProcessEffectSettings
    {
        [Range(0f, 5f), Tooltip("Horizontal anamorphic streak intensity.")]
        public FloatParameter streakIntensity = new FloatParameter { value = 1.0f };

        [Range(0.5f, 10f), Tooltip("Streak horizontal spread length.")]
        public FloatParameter streakLength = new FloatParameter { value = 3.0f };

        [Tooltip("Color tint of the horizontal streak.")]
        public ColorParameter streakColor = new ColorParameter { value = new Color(0.35f, 0.75f, 1.0f, 1.0f) };

        [Range(0f, 5f), Tooltip("Diffraction spikes (starburst) intensity.")]
        public FloatParameter spikeIntensity = new FloatParameter { value = 1.0f };

        [Range(4, 8), Tooltip("Number of diffraction spikes (4, 6, or 8).")]
        public IntParameter spikeCount = new IntParameter { value = 6 };

        [Range(0.5f, 10f), Tooltip("Diffraction spike length.")]
        public FloatParameter spikeLength = new FloatParameter { value = 3.0f };

        [Range(0.5f, 15f), Tooltip("Luminance threshold.")]
        public FloatParameter threshold = new FloatParameter { value = 1.5f };

        public override bool IsEnabledAndSupported(PostProcessRenderContext context)
        {
            return enabled.value && (streakIntensity.value > 0f || spikeIntensity.value > 0f);
        }

        public override void Load(ConfigNode config)
        {
            loadFloatParameter(config, "StreakIntensity", streakIntensity);
            loadFloatParameter(config, "StreakLength", streakLength);
            loadColorParameter(config, "StreakColor", streakColor);
            loadFloatParameter(config, "SpikeIntensity", spikeIntensity);
            loadIntParameter(config, "SpikeCount", spikeCount);
            loadFloatParameter(config, "SpikeLength", spikeLength);
            loadFloatParameter(config, "Threshold", threshold);
        }

        public override void Save(ConfigNode config)
        {
            saveFloatParameter(config, "StreakIntensity", streakIntensity);
            saveFloatParameter(config, "StreakLength", streakLength);
            saveColorParameter(config, "StreakColor", streakColor);
            saveFloatParameter(config, "SpikeIntensity", spikeIntensity);
            saveIntParameter(config, "SpikeCount", spikeCount);
            saveFloatParameter(config, "SpikeLength", spikeLength);
            saveFloatParameter(config, "Threshold", threshold);
        }
    }

    [UnityEngine.Scripting.Preserve]
    internal sealed class AnamorphicFlareRenderer : PostProcessEffectRenderer<AnamorphicFlare>
    {
        public override void Render(PostProcessRenderContext context)
        {
            var shader = (TUFX.TexturesUnlimitedFXLoader.INSTANCE != null) ? TUFX.TexturesUnlimitedFXLoader.INSTANCE.getShader("Hidden/TUFX/AnamorphicFlare") : null;
            if (shader == null) shader = Shader.Find("Hidden/TUFX/AnamorphicFlare");
            if (shader == null) return;

            var sheet = context.propertySheets.Get(shader);
            sheet.properties.SetFloat("_StreakIntensity", settings.streakIntensity.value);
            sheet.properties.SetFloat("_StreakLength", settings.streakLength.value);
            sheet.properties.SetColor("_StreakColor", settings.streakColor.value);
            sheet.properties.SetFloat("_SpikeIntensity", settings.spikeIntensity.value);
            sheet.properties.SetInt("_SpikeCount", settings.spikeCount.value);
            sheet.properties.SetFloat("_SpikeLength", settings.spikeLength.value);
            sheet.properties.SetFloat("_Threshold", settings.threshold.value);

            int width = context.width / 2;
            int height = context.height / 2;
            int rtThresh = Shader.PropertyToID("_FlareThreshold");
            int rtStreak = Shader.PropertyToID("_FlareStreak");
            int rtSpikes = Shader.PropertyToID("_FlareSpikes");

            var cmd = context.command;
            cmd.GetTemporaryRT(rtThresh, width, height, 0, FilterMode.Bilinear, context.sourceFormat);
            cmd.GetTemporaryRT(rtStreak, width, height, 0, FilterMode.Bilinear, context.sourceFormat);
            cmd.GetTemporaryRT(rtSpikes, width, height, 0, FilterMode.Bilinear, context.sourceFormat);

            // Pass 0: Threshold
            cmd.BlitFullscreenTriangle(context.source, rtThresh, sheet, 0);

            // Pass 1: Streak
            cmd.BlitFullscreenTriangle(rtThresh, rtStreak, sheet, 1);

            // Pass 2: Spikes
            cmd.BlitFullscreenTriangle(rtThresh, rtSpikes, sheet, 2);

            // Composite with Scene
            cmd.SetGlobalTexture("_FlareTex", rtStreak);
            cmd.BlitFullscreenTriangle(context.source, context.destination, sheet, 3);

            cmd.ReleaseTemporaryRT(rtThresh);
            cmd.ReleaseTemporaryRT(rtStreak);
            cmd.ReleaseTemporaryRT(rtSpikes);
        }
    }
}
