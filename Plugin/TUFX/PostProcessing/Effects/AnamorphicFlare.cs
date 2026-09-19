using System;

namespace UnityEngine.Rendering.PostProcessing
{
    [Serializable]
    [PostProcess(typeof(AnamorphicFlareRenderer), PostProcessEvent.BeforeStack, "TUFX/Anamorphic Flare & Starburst", sortingPriority: 60)]
    public sealed class AnamorphicFlare : PostProcessEffectSettings
    {
        [Range(0f, 5f), Tooltip("Horizontal anamorphic streak intensity.")]
        public FloatParameter streakIntensity = new FloatParameter { value = 1.0f };

        [Range(0.5f, 15f), Tooltip("Streak horizontal spread length.")]
        public FloatParameter streakLength = new FloatParameter { value = 4.0f };

        [Tooltip("Color tint of the horizontal streak.")]
        public ColorParameter streakColor = new ColorParameter { value = new Color(0.25f, 0.7f, 1.0f, 1.0f) };

        [Range(0f, 3f), Tooltip("Lens ghosting & halo intensity.")]
        public FloatParameter ghostIntensity = new FloatParameter { value = 0.8f };

        [Range(0.2f, 2.0f), Tooltip("Lens ghost spread factor.")]
        public FloatParameter ghostSpread = new FloatParameter { value = 1.0f };

        [Tooltip("Color tint of lens ghosts.")]
        public ColorParameter ghostColor = new ColorParameter { value = new Color(0.4f, 0.75f, 1.0f, 1.0f) };

        [Range(0f, 2f), Tooltip("Spectral dispersion on streaks and ghosts.")]
        public FloatParameter dispersion = new FloatParameter { value = 0.6f };

        [Range(0f, 5f), Tooltip("Diffraction spikes (starburst) intensity.")]
        public FloatParameter spikeIntensity = new FloatParameter { value = 0.8f };

        [Range(4, 8), Tooltip("Number of diffraction spikes (4, 6, or 8).")]
        public IntParameter spikeCount = new IntParameter { value = 6 };

        [Range(0.5f, 10f), Tooltip("Diffraction spike length.")]
        public FloatParameter spikeLength = new FloatParameter { value = 3.0f };

        [Range(0.5f, 15f), Tooltip("Luminance threshold.")]
        public FloatParameter threshold = new FloatParameter { value = 1.5f };

        [Range(0f, 1f), Tooltip("Soft threshold knee to prevent hard specular clipping.")]
        public FloatParameter softKnee = new FloatParameter { value = 0.5f };

        [Range(5f, 50f), Tooltip("Maximum brightness clamp to prevent specular blowout on metallic surfaces.")]
        public FloatParameter maxBrightness = new FloatParameter { value = 25.0f };

        public override bool IsEnabledAndSupported(PostProcessRenderContext context)
        {
            return enabled.value && (streakIntensity.value > 0f || spikeIntensity.value > 0f || ghostIntensity.value > 0f);
        }

        public override void Load(ConfigNode config)
        {
            loadFloatParameter(config, "StreakIntensity", streakIntensity);
            loadFloatParameter(config, "StreakLength", streakLength);
            loadColorParameter(config, "StreakColor", streakColor);
            loadFloatParameter(config, "GhostIntensity", ghostIntensity);
            loadFloatParameter(config, "GhostSpread", ghostSpread);
            loadColorParameter(config, "GhostColor", ghostColor);
            loadFloatParameter(config, "Dispersion", dispersion);
            loadFloatParameter(config, "SpikeIntensity", spikeIntensity);
            loadIntParameter(config, "SpikeCount", spikeCount);
            loadFloatParameter(config, "SpikeLength", spikeLength);
            loadFloatParameter(config, "Threshold", threshold);
            loadFloatParameter(config, "SoftKnee", softKnee);
            loadFloatParameter(config, "MaxBrightness", maxBrightness);
        }

        public override void Save(ConfigNode config)
        {
            saveFloatParameter(config, "StreakIntensity", streakIntensity);
            saveFloatParameter(config, "StreakLength", streakLength);
            saveColorParameter(config, "StreakColor", streakColor);
            saveFloatParameter(config, "GhostIntensity", ghostIntensity);
            saveFloatParameter(config, "GhostSpread", ghostSpread);
            saveColorParameter(config, "GhostColor", ghostColor);
            saveFloatParameter(config, "Dispersion", dispersion);
            saveFloatParameter(config, "SpikeIntensity", spikeIntensity);
            saveIntParameter(config, "SpikeCount", spikeCount);
            saveFloatParameter(config, "SpikeLength", spikeLength);
            saveFloatParameter(config, "Threshold", threshold);
            saveFloatParameter(config, "SoftKnee", softKnee);
            saveFloatParameter(config, "MaxBrightness", maxBrightness);
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

            // Soft-knee threshold setup
            float lthresh = Mathf.GammaToLinearSpace(settings.threshold.value);
            float knee = lthresh * Mathf.Clamp01(settings.softKnee.value) + 1e-5f;
            var thresholdVec = new Vector4(lthresh, lthresh - knee, knee * 2f, 0.25f / knee);
            sheet.properties.SetVector("_ThresholdParams", thresholdVec);

            sheet.properties.SetVector("_FlareParams", new Vector4(
                Mathf.GammaToLinearSpace(settings.maxBrightness.value),
                settings.streakLength.value,
                settings.dispersion.value,
                0f
            ));

            sheet.properties.SetFloat("_StreakIntensity", settings.streakIntensity.value);
            sheet.properties.SetFloat("_StreakLength", settings.streakLength.value);
            sheet.properties.SetColor("_StreakColor", settings.streakColor.value);
            sheet.properties.SetFloat("_GhostIntensity", settings.ghostIntensity.value);
            sheet.properties.SetFloat("_GhostSpread", settings.ghostSpread.value);
            sheet.properties.SetColor("_GhostColor", settings.ghostColor.value);
            sheet.properties.SetFloat("_SpikeIntensity", settings.spikeIntensity.value);
            sheet.properties.SetInt("_SpikeCount", settings.spikeCount.value);
            sheet.properties.SetFloat("_SpikeLength", settings.spikeLength.value);

            int width = context.width / 2;
            int height = context.height / 2;
            int hStreak = Mathf.Max(1, context.height / 4);

            int rtThresh = Shader.PropertyToID("_FlareThreshold");
            int rtStreakCore = Shader.PropertyToID("_FlareStreakCore");
            int rtStreakPing = Shader.PropertyToID("_FlareStreakPing");
            int rtStreakPong = Shader.PropertyToID("_FlareStreakPong");
            int rtSpikes = Shader.PropertyToID("_FlareSpikesTex");
            int rtGhosts = Shader.PropertyToID("_FlareGhostTex");

            var cmd = context.command;
            cmd.GetTemporaryRT(rtThresh, width, height, 0, FilterMode.Bilinear, context.sourceFormat);
            cmd.GetTemporaryRT(rtStreakCore, width, hStreak, 0, FilterMode.Bilinear, context.sourceFormat);
            cmd.GetTemporaryRT(rtStreakPing, width, hStreak, 0, FilterMode.Bilinear, context.sourceFormat);
            cmd.GetTemporaryRT(rtStreakPong, width, hStreak, 0, FilterMode.Bilinear, context.sourceFormat);
            cmd.GetTemporaryRT(rtSpikes, width, height, 0, FilterMode.Bilinear, context.sourceFormat);
            cmd.GetTemporaryRT(rtGhosts, width, height, 0, FilterMode.Bilinear, context.sourceFormat);

            // Pass 0: Soft-Knee Threshold Extraction & Anti-Blowout Clamp
            cmd.BlitFullscreenTriangle(context.source, rtThresh, sheet, 0);

            // Pass 1: Horizontal Streak Pre-filter with Spectral Dispersion (creates brilliant tight core)
            cmd.BlitFullscreenTriangle(rtThresh, rtStreakCore, sheet, 1);

            // Pass 2: Cascaded Ping-Pong Horizontal Gaussian Convolution (creates ultra-wide continuous streak)
            float sLen = Mathf.Max(0.5f, settings.streakLength.value);
            sheet.properties.SetFloat("_BlurStep", 3.0f * sLen);
            cmd.BlitFullscreenTriangle(rtStreakCore, rtStreakPing, sheet, 2);

            sheet.properties.SetFloat("_BlurStep", 10.0f * sLen);
            cmd.BlitFullscreenTriangle(rtStreakPing, rtStreakPong, sheet, 2);

            sheet.properties.SetFloat("_BlurStep", 32.0f * sLen);
            cmd.BlitFullscreenTriangle(rtStreakPong, rtStreakPing, sheet, 2);

            sheet.properties.SetFloat("_BlurStep", 90.0f * sLen);
            cmd.BlitFullscreenTriangle(rtStreakPing, rtStreakPong, sheet, 2);

            // Pass 3: Diffraction Spikes (Continuous Starburst with IGN jitter)
            cmd.BlitFullscreenTriangle(rtThresh, rtSpikes, sheet, 3);

            // Pass 4: Lens Ghosts (Aperture Bokeh Defocus with 8-Tap Fibonacci Disk)
            cmd.BlitFullscreenTriangle(rtThresh, rtGhosts, sheet, 4);

            // Pass 5: Composite with Scene
            cmd.SetGlobalTexture("_FlareStreakTex", rtStreakCore);
            cmd.SetGlobalTexture("_FlareStreakWideTex", rtStreakPong);
            cmd.SetGlobalTexture("_FlareSpikesTex", rtSpikes);
            cmd.SetGlobalTexture("_FlareGhostTex", rtGhosts);
            cmd.BlitFullscreenTriangle(context.source, context.destination, sheet, 5);

            cmd.ReleaseTemporaryRT(rtThresh);
            cmd.ReleaseTemporaryRT(rtStreakCore);
            cmd.ReleaseTemporaryRT(rtStreakPing);
            cmd.ReleaseTemporaryRT(rtStreakPong);
            cmd.ReleaseTemporaryRT(rtSpikes);
            cmd.ReleaseTemporaryRT(rtGhosts);
        }
    }
}
