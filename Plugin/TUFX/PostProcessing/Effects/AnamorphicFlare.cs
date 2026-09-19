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
        private const int k_MaxPyramidLevels = 5;
        private static readonly int[] m_MipsDown = new int[k_MaxPyramidLevels];
        private static readonly int[] m_MipsUp = new int[k_MaxPyramidLevels];

        static AnamorphicFlareRenderer()
        {
            for (int k = 0; k < k_MaxPyramidLevels; k++)
            {
                m_MipsDown[k] = Shader.PropertyToID("_FlareMipDown_" + k);
                m_MipsUp[k] = Shader.PropertyToID("_FlareMipUp_" + k);
            }
        }

        public override void Init()
        {
            // Redundant guarantee
            for (int k = 0; k < k_MaxPyramidLevels; k++)
            {
                m_MipsDown[k] = Shader.PropertyToID("_FlareMipDown_" + k);
                m_MipsUp[k] = Shader.PropertyToID("_FlareMipUp_" + k);
            }
        }

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
            int rtSpikes = Shader.PropertyToID("_FlareSpikesTex");
            int rtGhosts = Shader.PropertyToID("_FlareGhostTex");

            var cmd = context.command;
            cmd.GetTemporaryRT(rtThresh, width, height, 0, FilterMode.Bilinear, context.sourceFormat);

            // Pass 0: Soft-Knee Threshold Extraction & Anti-Blowout Clamp
            cmd.BlitFullscreenTriangle(context.source, rtThresh, sheet, 0);

            // Horizontal Mip Pyramid for continuous, gapless Anamorphic Streaks
            bool hasStreak = settings.streakIntensity.value > 0f;
            if (hasStreak)
            {
                int tw = width;
                for (int k = 0; k < k_MaxPyramidLevels; k++)
                {
                    cmd.GetTemporaryRT(m_MipsDown[k], tw, hStreak, 0, FilterMode.Bilinear, context.sourceFormat);
                    cmd.GetTemporaryRT(m_MipsUp[k], tw, hStreak, 0, FilterMode.Bilinear, context.sourceFormat);
                    tw = Mathf.Max(1, tw / 2);
                }

                // Pass 1: Horizontal Streak Pre-filter with Spectral Dispersion
                cmd.BlitFullscreenTriangle(rtThresh, m_MipsDown[0], sheet, 1);

                // Pass 2: Horizontal 5-Tap Gaussian Downsampling
                for (int k = 1; k < k_MaxPyramidLevels; k++)
                {
                    cmd.BlitFullscreenTriangle(m_MipsDown[k - 1], m_MipsDown[k], sheet, 2);
                }

                // Pass 3: Horizontal Upsample & Additive Accumulation
                float streakSpread = Mathf.Clamp(0.55f + settings.streakLength.value * 0.04f, 0.5f, 0.95f);
                sheet.properties.SetFloat("_StreakSpread", streakSpread);

                int lastUp = m_MipsDown[k_MaxPyramidLevels - 1];
                for (int k = k_MaxPyramidLevels - 2; k >= 0; k--)
                {
                    cmd.SetGlobalTexture("_FlareBaseTex", m_MipsDown[k]);
                    cmd.BlitFullscreenTriangle(lastUp, m_MipsUp[k], sheet, 3);
                    lastUp = m_MipsUp[k];
                }

                cmd.SetGlobalTexture("_FlareStreakTex", lastUp);
            }
            else
            {
                cmd.SetGlobalTexture("_FlareStreakTex", RuntimeUtilities.blackTexture);
            }

            // Pass 4: Diffraction Spikes (Continuous Starburst with IGN jitter)
            if (settings.spikeIntensity.value > 0f)
            {
                cmd.GetTemporaryRT(rtSpikes, width, height, 0, FilterMode.Bilinear, context.sourceFormat);
                cmd.BlitFullscreenTriangle(rtThresh, rtSpikes, sheet, 4);
                cmd.SetGlobalTexture("_FlareSpikesTex", rtSpikes);
            }
            else
            {
                cmd.SetGlobalTexture("_FlareSpikesTex", RuntimeUtilities.blackTexture);
            }

            // Pass 5: Lens Ghosts (Aperture Bokeh Defocus with 8-Tap Fibonacci Disk)
            if (settings.ghostIntensity.value > 0f)
            {
                cmd.GetTemporaryRT(rtGhosts, width, height, 0, FilterMode.Bilinear, context.sourceFormat);
                cmd.BlitFullscreenTriangle(rtThresh, rtGhosts, sheet, 5);
                cmd.SetGlobalTexture("_FlareGhostTex", rtGhosts);
            }
            else
            {
                cmd.SetGlobalTexture("_FlareGhostTex", RuntimeUtilities.blackTexture);
            }

            // Pass 6: Final Composite with Scene
            cmd.BlitFullscreenTriangle(context.source, context.destination, sheet, 6);

            // Cleanup
            cmd.ReleaseTemporaryRT(rtThresh);
            if (hasStreak)
            {
                for (int k = 0; k < k_MaxPyramidLevels; k++)
                {
                    cmd.ReleaseTemporaryRT(m_MipsDown[k]);
                    cmd.ReleaseTemporaryRT(m_MipsUp[k]);
                }
            }
            if (settings.spikeIntensity.value > 0f)
            {
                cmd.ReleaseTemporaryRT(rtSpikes);
            }
            if (settings.ghostIntensity.value > 0f)
            {
                cmd.ReleaseTemporaryRT(rtGhosts);
            }
        }
    }
}
