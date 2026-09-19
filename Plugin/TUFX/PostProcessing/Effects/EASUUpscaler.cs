using System;
using UnityEngine;

namespace UnityEngine.Rendering.PostProcessing
{
    public enum FSRQuality
    {
        UltraQuality, // 77% scale (1.3x)
        Quality,      // 67% scale (1.5x)
        Balanced,     // 59% scale (1.7x)
        Performance,  // 50% scale (2.0x)
        NativeEdgeAA  // 100% scale (Native Edge Enhancement)
    }

    [Serializable]
    public sealed class FSRQualityParameter : ParameterOverride<FSRQuality> {}

    [Serializable]
    [PostProcess(typeof(EASUUpscalerRenderer), PostProcessEvent.AfterStack, "TUFX/AMD FSR 1.0 Upscaler", sortingPriority: 115)]
    public sealed class EASUUpscaler : PostProcessEffectSettings
    {
        [Tooltip("FSR 1.0 Reconstruction Quality Mode.")]
        public FSRQualityParameter quality = new FSRQualityParameter { value = FSRQuality.Quality };

        [Range(0f, 1f), Tooltip("Integrated RCAS sharpening intensity.")]
        public FloatParameter sharpness = new FloatParameter { value = 0.5f };

        public override bool IsEnabledAndSupported(PostProcessRenderContext context)
        {
            return enabled.value && SystemInfo.supportsImageEffects;
        }

        public override void Load(ConfigNode config)
        {
            loadEnumParameter(config, "Quality", quality, typeof(FSRQuality));
            loadFloatParameter(config, "Sharpness", sharpness);
        }

        public override void Save(ConfigNode config)
        {
            saveEnumParameter(config, "Quality", quality);
            saveFloatParameter(config, "Sharpness", sharpness);
        }
    }

    [UnityEngine.Scripting.Preserve]
    internal sealed class EASUUpscalerRenderer : PostProcessEffectRenderer<EASUUpscaler>
    {
        public override void Render(PostProcessRenderContext context)
        {
            float scale = 0.67f;
            switch (settings.quality.value)
            {
                case FSRQuality.UltraQuality: scale = 0.77f; break;
                case FSRQuality.Quality: scale = 0.67f; break;
                case FSRQuality.Balanced: scale = 0.59f; break;
                case FSRQuality.Performance: scale = 0.50f; break;
                case FSRQuality.NativeEdgeAA: scale = 1.00f; break;
            }

            var easuShader = (TUFX.TexturesUnlimitedFXLoader.INSTANCE != null) ? TUFX.TexturesUnlimitedFXLoader.INSTANCE.getShader("Hidden/TUFX/FidelityFX_EASU") : null;
            if (easuShader == null) easuShader = Shader.Find("Hidden/TUFX/FidelityFX_EASU");
            if (easuShader == null) return;

            var easuSheet = context.propertySheets.Get(easuShader);

            var rcasShader = (TUFX.TexturesUnlimitedFXLoader.INSTANCE != null) ? TUFX.TexturesUnlimitedFXLoader.INSTANCE.getShader("Hidden/TUFX/ContrastAdaptiveSharpening") : null;
            if (rcasShader == null) rcasShader = Shader.Find("Hidden/TUFX/ContrastAdaptiveSharpening");

            var cmd = context.command;
            int fullW = context.width;
            int fullH = context.height;

            if (scale < 0.99f)
            {
                int subW = Mathf.Max(128, (int)(fullW * scale));
                int subH = Mathf.Max(128, (int)(fullH * scale));

                int rtSub = Shader.PropertyToID("_FSRSubRes");
                int rtEASU = Shader.PropertyToID("_FSREASUFull");

                cmd.GetTemporaryRT(rtSub, subW, subH, 0, FilterMode.Bilinear, context.sourceFormat);
                cmd.GetTemporaryRT(rtEASU, fullW, fullH, 0, FilterMode.Bilinear, context.sourceFormat);

                // Pass 1: Render scale downsample
                cmd.BlitFullscreenTriangle(context.source, rtSub);

                // Pass 2: FSR 1.0 EASU 12-tap directional spatial reconstruction
                cmd.BlitFullscreenTriangle(rtSub, rtEASU, easuSheet, 0);

                // Pass 3: RCAS sharpening to destination
                if (rcasShader != null && settings.sharpness.value > 0.01f)
                {
                    var rcasSheet = context.propertySheets.Get(rcasShader);
                    rcasSheet.properties.SetFloat("_Sharpness", settings.sharpness.value);
                    cmd.BlitFullscreenTriangle(rtEASU, context.destination, rcasSheet, 0);
                }
                else
                {
                    cmd.BlitFullscreenTriangle(rtEASU, context.destination);
                }

                cmd.ReleaseTemporaryRT(rtSub);
                cmd.ReleaseTemporaryRT(rtEASU);
            }
            else
            {
                // Native 100% scale
                if (rcasShader != null && settings.sharpness.value > 0.01f)
                {
                    int rtEASU = Shader.PropertyToID("_FSREASUFull");
                    cmd.GetTemporaryRT(rtEASU, fullW, fullH, 0, FilterMode.Bilinear, context.sourceFormat);

                    cmd.BlitFullscreenTriangle(context.source, rtEASU, easuSheet, 0);

                    var rcasSheet = context.propertySheets.Get(rcasShader);
                    rcasSheet.properties.SetFloat("_Sharpness", settings.sharpness.value);
                    cmd.BlitFullscreenTriangle(rtEASU, context.destination, rcasSheet, 0);

                    cmd.ReleaseTemporaryRT(rtEASU);
                }
                else
                {
                    cmd.BlitFullscreenTriangle(context.source, context.destination, easuSheet, 0);
                }
            }
        }
    }
}
