using System;
using UnityEngine;

namespace UnityEngine.Rendering.PostProcessing
{
    public enum HeatDistortionMode
    {
        AutoAdaptive, // Plume & Ground Haze (Smart masking)
        FullScreen    // Global Screen (with UI protection)
    }

    [Serializable]
    public sealed class HeatDistortionModeParameter : ParameterOverride<HeatDistortionMode> {}

    [Serializable]
    [PostProcess(typeof(HeatDistortionRenderer), PostProcessEvent.BeforeStack, "TUFX/Heat Distortion", sortingPriority: 50)]
    public sealed class HeatDistortionEffect : PostProcessEffectSettings
    {
        [Tooltip("Masking mode: AutoAdaptive (distorts around plumes and near ground) or FullScreen.")]
        public HeatDistortionModeParameter mode = new HeatDistortionModeParameter { value = HeatDistortionMode.AutoAdaptive };

        [Range(0f, 2f), Tooltip("Thermal heat wave distortion intensity.")]
        public FloatParameter intensity = new FloatParameter { value = 0.65f };

        [Range(0.2f, 10f), Tooltip("Turbulence animation speed.")]
        public FloatParameter speed = new FloatParameter { value = 2.5f };

        [Range(1f, 30f), Tooltip("Wave spatial frequency / scale.")]
        public FloatParameter scale = new FloatParameter { value = 10.0f };

        [Range(0.5f, 10f), Tooltip("Luminance threshold to detect engine plumes & thrusters.")]
        public FloatParameter plumeThreshold = new FloatParameter { value = 1.5f };

        [Range(100f, 3000f), Tooltip("Maximum altitude (meters) where ground heat shimmer is active.")]
        public FloatParameter groundAltitudeLimit = new FloatParameter { value = 800f };

        public override bool IsEnabledAndSupported(PostProcessRenderContext context)
        {
            return enabled.value && intensity.value > 0.001f;
        }

        public override void Load(ConfigNode config)
        {
            loadEnumParameter(config, "Mode", mode, typeof(HeatDistortionMode));
            loadFloatParameter(config, "Intensity", intensity);
            loadFloatParameter(config, "Speed", speed);
            loadFloatParameter(config, "Scale", scale);
            loadFloatParameter(config, "PlumeThreshold", plumeThreshold);
            loadFloatParameter(config, "GroundAltitudeLimit", groundAltitudeLimit);
        }

        public override void Save(ConfigNode config)
        {
            saveEnumParameter(config, "Mode", mode);
            saveFloatParameter(config, "Intensity", intensity);
            saveFloatParameter(config, "Speed", speed);
            saveFloatParameter(config, "Scale", scale);
            saveFloatParameter(config, "PlumeThreshold", plumeThreshold);
            saveFloatParameter(config, "GroundAltitudeLimit", groundAltitudeLimit);
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

            // Auto-calculate ground haze factor from vessel altitude
            float groundHazeWeight = 0f;
            if (settings.mode.value == HeatDistortionMode.AutoAdaptive && FlightGlobals.ActiveVessel != null)
            {
                double alt = FlightGlobals.ActiveVessel.radarAltitude > 0 ? FlightGlobals.ActiveVessel.radarAltitude : FlightGlobals.ActiveVessel.altitude;
                float limit = Mathf.Max(50f, settings.groundAltitudeLimit.value);
                if (alt < limit)
                {
                    groundHazeWeight = Mathf.Clamp01(1.0f - (float)(alt / limit));
                }
            }

            var sheet = context.propertySheets.Get(shader);
            sheet.properties.SetFloat("_Intensity", settings.intensity.value);
            sheet.properties.SetFloat("_Speed", settings.speed.value);
            sheet.properties.SetFloat("_Scale", settings.scale.value);
            sheet.properties.SetFloat("_PlumeThreshold", settings.plumeThreshold.value);
            sheet.properties.SetFloat("_GroundHazeWeight", groundHazeWeight);
            sheet.properties.SetFloat("_DistortionMode", (settings.mode.value == HeatDistortionMode.FullScreen) ? 1.0f : 0.0f);

            int maskW = Mathf.Max(1, context.width / 4);
            int maskH = Mathf.Max(1, context.height / 4);
            int rtExtract = Shader.PropertyToID("_HeatMaskExtract");
            int rtBlur = Shader.PropertyToID("_HeatMaskTex");

            var cmd = context.command;
            cmd.GetTemporaryRT(rtExtract, maskW, maskH, 0, FilterMode.Bilinear, RenderTextureFormat.R8);
            cmd.GetTemporaryRT(rtBlur, maskW, maskH, 0, FilterMode.Bilinear, RenderTextureFormat.R8);

            // Pass 0: Extract Plume Heat Sources
            cmd.BlitFullscreenTriangle(context.source, rtExtract, sheet, 0);

            // Pass 1: Dilate / Blur Heat Mask into Atmosphere
            cmd.BlitFullscreenTriangle(rtExtract, rtBlur, sheet, 1);

            // Pass 2: Thermal Distortion
            cmd.SetGlobalTexture("_HeatMaskTex", rtBlur);
            cmd.BlitFullscreenTriangle(context.source, context.destination, sheet, 2);

            cmd.ReleaseTemporaryRT(rtExtract);
            cmd.ReleaseTemporaryRT(rtBlur);
        }
    }
}
