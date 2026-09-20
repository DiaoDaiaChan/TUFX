using System;
using System.Collections.Generic;
using KSP.Localization;

namespace TUFX
{
    /// <summary>
    /// Centralized i18n localization provider for TUFX.
    /// Supports automatic translation mapping of English UI labels to localized KSP tags,
    /// seamless fallback, and direct tag lookup via KSP.Localization.Localizer.
    /// </summary>
    public static class TUFXLocalizer
    {
        private static readonly Dictionary<string, string> EnglishToTag = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        static TUFXLocalizer()
        {
            try
            {
                InitializeMap();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[TUFX] Failed to initialize TUFXLocalizer map: " + ex);
            }
        }

        private static void Add(string key, string tag)
        {
            EnglishToTag[key] = tag;
        }

        private static void InitializeMap()
        {
            // Navigation, Headers & Buttons
            Add("TUFX: Beyond", "#LOC_TUFX_Title");
            Add("Mode: ", "#LOC_TUFX_Nav_Mode");
            Add("Profiles", "#LOC_TUFX_Nav_Profiles");
            Add("Stock FX", "#LOC_TUFX_Nav_StockFX");
            Add("ExtendFX ★", "#LOC_TUFX_Nav_ExtendFX");
            Add("Profiler", "#LOC_TUFX_Nav_Profiler");
            Add("Performance Monitor", "#LOC_TUFX_Nav_Profiler");
            Add("Return", "#LOC_TUFX_Btn_Return");
            Add("Save Selected", "#LOC_TUFX_Btn_SaveSelected");
            Add("Reload Selected", "#LOC_TUFX_Btn_ReloadSelected");
            Add("Save All", "#LOC_TUFX_Btn_SaveAll");
            Add("Reload All", "#LOC_TUFX_Btn_ReloadAll");
            Add("Close", "#LOC_TUFX_Btn_Close");
            Add("Enable", "#LOC_TUFX_Btn_Enable");
            Add("Disable", "#LOC_TUFX_Btn_Disable");
            Add("Reset Peaks", "#LOC_TUFX_Btn_ResetPeaks");
            Add("Pause Probes", "#LOC_TUFX_Btn_PauseProfiler");
            Add("Resume Probes", "#LOC_TUFX_Btn_ResumeProfiler");
            Add("Stage", "#LOC_TUFX_Col_Stage");
            Add("Effect / Pass Name", "#LOC_TUFX_Col_EffectPass");
            Add("Avg Time", "#LOC_TUFX_Col_AvgTime");
            Add("Share %", "#LOC_TUFX_Col_Share");
            Add("Peak", "#LOC_TUFX_Col_Peak");
            Add("FPS", "#LOC_TUFX_Profiler_FPS");
            Add("Frame Latency", "#LOC_TUFX_Profiler_FrameTime");
            Add("TUFX Post-Process", "#LOC_TUFX_Profiler_TotalTUFX");
            Add("Active Passes", "#LOC_TUFX_Profiler_ActivePasses");

            // Profiles window
            Add("Current Scene: ", "#LOC_TUFX_Label_CurrentScene");
            Add("Current Profile: ", "#LOC_TUFX_Label_CurrentProfile");
            Add("Select a new profile for current scene: ", "#LOC_TUFX_Label_SelectProfile");
            Add("Profile: ", "#LOC_TUFX_Label_Profile");
            Add("No Profile Selected!", "#LOC_TUFX_Label_NoProfile");
            Add("<b>Stock TUFX Post-Processing Effects</b>", "#LOC_TUFX_Title_StockFX");
            Add("Open ExtendFX Suite (FSR, AgX/ACES, GTAO, SSR) >>", "#LOC_TUFX_Btn_OpenExtendFX");
            Add("<color=#55CCFF><b>[ExtendFX] Next-Gen Advanced Visual Effects & Upgrades</b></color>", "#LOC_TUFX_Title_ExtendFX");
            Add("<< Switch to Stock FX", "#LOC_TUFX_Btn_SwitchToStock");
            Add("Nothing selected", "#LOC_TUFX_Label_NothingSelected");
            Add("Effect: ", "#LOC_TUFX_Label_Effect");
            Add("Property: ", "#LOC_TUFX_Label_Property");
            Add("Current: ", "#LOC_TUFX_Label_Current");
            Add("<b>SMAA Quality Preset:</b>", "#LOC_TUFX_Label_SMAAPreset");
            Add("Low", "#LOC_TUFX_Btn_Low");
            Add("Medium", "#LOC_TUFX_Btn_Medium");
            Add("High (Ultra)", "#LOC_TUFX_Btn_HighUltra");
            Add("Lock / Switch to Manual", "#LOC_TUFX_Btn_LockManual");
            Add("Auto-Focus Once", "#LOC_TUFX_Btn_AutoFocusOnce");
            Add("Print SSR Diagnostics to KSP.log", "#LOC_TUFX_Btn_SSRDiagnostics");

            // Effect Headers
            Add("General Settings", "#LOC_TUFX_Effect_General");
            Add("General Profile Settings", "#LOC_TUFX_Effect_General");
            Add("Contrast Adaptive Sharpening", "#LOC_TUFX_Effect_CAS");
            Add("AMD FidelityFX CAS (Sharpening)", "#LOC_TUFX_Effect_CAS");
            Add("AMD FidelityFX FSR 1.0 / CAS (Contrast Adaptive Sharpening)", "#LOC_TUFX_Effect_CAS");
            Add("Modern Tonemapping", "#LOC_TUFX_Effect_ModernTonemapping");
            Add("Modern ACES / Filmic Tonemapping", "#LOC_TUFX_Effect_ModernTonemapping");
            Add("Modern Tonemapping (AgX / ACES / Tony / Filmic)", "#LOC_TUFX_Effect_ModernTonemapping");
            Add("Ground Truth AO (GTAO)", "#LOC_TUFX_Effect_GTAO");
            Add("Contact Shadows", "#LOC_TUFX_Effect_ContactShadows");
            Add("Screen Space Contact Shadows", "#LOC_TUFX_Effect_ContactShadows");
            Add("Screen Space Global Illumination (SSGI)", "#LOC_TUFX_Effect_SSGI");
            Add("Screen Space Subsurface Scattering (SSSS)", "#LOC_TUFX_Effect_SSSS");
            Add("Screen Space Light Shafts & Volumetric Shadows (God Rays)", "#LOC_TUFX_Effect_GodRays");
            Add("Screen Space Light Shafts / Volumetric Shadows (God Rays)", "#LOC_TUFX_Effect_GodRays");
            Add("Screen Space God Rays", "#LOC_TUFX_Effect_GodRays");
            Add("Halation", "#LOC_TUFX_Effect_Halation");
            Add("Halation (Film Bleed)", "#LOC_TUFX_Effect_Halation");
            Add("Film Halation", "#LOC_TUFX_Effect_Halation");
            Add("Anamorphic Flare & Starburst", "#LOC_TUFX_Effect_AnamorphicFlare");
            Add("Heat Distortion", "#LOC_TUFX_Effect_HeatDistortion");
            Add("Hypersonic Reentry Heat Haze", "#LOC_TUFX_Effect_HeatDistortion");
            Add("Camera Motion Blur", "#LOC_TUFX_Effect_CameraMotionBlur");
            Add("Camera Motion Blur (Physical Shutter)", "#LOC_TUFX_Effect_CameraMotionBlur");
            Add("Camera-Motion Blur (Physical Shutter Speed Blur)", "#LOC_TUFX_Effect_CameraMotionBlur");
            Add("Intel CMAA 2 (Anti-Aliasing)", "#LOC_TUFX_Effect_CMAA2");
            Add("Intel Conservative Morphological Anti-Aliasing (CMAA 2)", "#LOC_TUFX_Effect_CMAA2");
            Add("AMD FidelityFX Super Resolution (FSR 1.0)", "#LOC_TUFX_Effect_FSR");
            Add("AMD FidelityFX FSR 1.0 (EASU Reconstruction & Upscaler)", "#LOC_TUFX_Effect_FSR");
            Add("Spectral Bokeh (Chromatic DoF)", "#LOC_TUFX_Effect_SpectralBokeh");
            Add("Anti-Aliasing Control Center (Presets & TAA Tuning)", "#LOC_TUFX_Effect_AA");
            Add("Ambient Occlusion", "#LOC_TUFX_Effect_AmbientOcclusion");
            Add("Auto Exposure", "#LOC_TUFX_Effect_AutoExposure");
            Add("Bloom", "#LOC_TUFX_Effect_Bloom");
            Add("Chromatic Aberration", "#LOC_TUFX_Effect_ChromaticAberration");
            Add("Color Grading", "#LOC_TUFX_Effect_ColorGrading");
            Add("Depth of Field", "#LOC_TUFX_Effect_DepthOfField");
            Add("Grain", "#LOC_TUFX_Effect_Grain");
            Add("Lens Distortion", "#LOC_TUFX_Effect_LensDistortion");
            Add("Motion Blur", "#LOC_TUFX_Effect_MotionBlur");
            Add("Screen Space Reflections", "#LOC_TUFX_Effect_ScreenSpaceReflections");
            Add("Screen Space Reflections (SSR)", "#LOC_TUFX_Effect_ScreenSpaceReflections");
            Add("Vignette", "#LOC_TUFX_Effect_Vignette");
            Add("Temporal Anti-Aliasing (TAA)", "#LOC_TUFX_Effect_TAA");
            Add("Volumetric / Distance Fog", "#LOC_TUFX_Effect_Fog");
            Add("Fog", "#LOC_TUFX_Effect_Fog");
            Add("SMAA / FXAA Antialiasing", "#LOC_TUFX_Effect_SMAAFXAA");
            Add("Spatial Dithering", "#LOC_TUFX_Effect_Dithering");
            Add("Dithering", "#LOC_TUFX_Effect_Dithering");
            Add("Status", "#LOC_TUFX_Col_Status");
            Add("RUNNING", "#LOC_TUFX_Status_Running");
            Add("OFF", "#LOC_TUFX_Status_Off");

            // Parameters
            Add("HDR", "#LOC_TUFX_Param_HDR");
            Add("Resolution Scale", "#LOC_TUFX_Param_ResolutionScale");
            Add("Primary Camera Antialiasing", "#LOC_TUFX_Param_PrimaryAA");
            Add("Secondary Camera Antialiasing", "#LOC_TUFX_Param_SecondaryAA");
            Add("Intensity", "#LOC_TUFX_Param_Intensity");
            Add("Threshold", "#LOC_TUFX_Param_Threshold");
            Add("Radius", "#LOC_TUFX_Param_Radius");
            Add("Thickness", "#LOC_TUFX_Param_Thickness");
            Add("Soft Knee", "#LOC_TUFX_Param_SoftKnee");
            Add("Sharpness", "#LOC_TUFX_Param_Sharpness");
            Add("Sharpness (RCAS)", "#LOC_TUFX_Param_SharpnessRCAS");
            Add("Exposure", "#LOC_TUFX_Param_Exposure");
            Add("Contrast", "#LOC_TUFX_Param_Contrast");
            Add("Saturation", "#LOC_TUFX_Param_Saturation");
            Add("Tonemapper", "#LOC_TUFX_Param_Tonemapper");
            Add("Multi-Bounce", "#LOC_TUFX_Param_MultiBounce");
            Add("Specular Occlusion", "#LOC_TUFX_Param_SpecularOcclusion");
            Add("Color", "#LOC_TUFX_Param_Color");
            Add("Ray Length", "#LOC_TUFX_Param_RayLength");
            Add("Ray Steps", "#LOC_TUFX_Param_RaySteps");
            Add("Steps", "#LOC_TUFX_Param_RaySteps");
            Add("Ray Count", "#LOC_TUFX_Param_RayCount");
            Add("Bounce Color", "#LOC_TUFX_Param_BounceColor");
            Add("Scatter Radius", "#LOC_TUFX_Param_ScatterRadius");
            Add("Depth Threshold", "#LOC_TUFX_Param_DepthThreshold");
            Add("Max Distance", "#LOC_TUFX_Param_MaxDistance");
            Add("Max Distance (m)", "#LOC_TUFX_Param_MaxDistanceM");
            Add("Auto Adapt to Surface", "#LOC_TUFX_Param_AutoAdapt");
            Add("Subsurface Tint", "#LOC_TUFX_Param_SubsurfaceTint");
            Add("Decay", "#LOC_TUFX_Param_Decay");
            Add("Density", "#LOC_TUFX_Param_Density");
            Add("Weight", "#LOC_TUFX_Param_Weight");
            Add("Sample Count", "#LOC_TUFX_Param_SampleCount");
            Add("Samples", "#LOC_TUFX_Param_Samples");
            Add("Space Factor", "#LOC_TUFX_Param_SpaceFactor");
            Add("Space Intensity", "#LOC_TUFX_Param_SpaceIntensity");
            Add("Ray Color", "#LOC_TUFX_Param_RayColor");
            Add("Color Tint", "#LOC_TUFX_Param_ColorTint");
            Add("Streak Intensity", "#LOC_TUFX_Param_StreakIntensity");
            Add("Streak Length", "#LOC_TUFX_Param_StreakLength");
            Add("Streak Color", "#LOC_TUFX_Param_StreakColor");
            Add("Ghost Intensity", "#LOC_TUFX_Param_GhostIntensity");
            Add("Ghost Spread", "#LOC_TUFX_Param_GhostSpread");
            Add("Ghost Color", "#LOC_TUFX_Param_GhostColor");
            Add("Dispersion", "#LOC_TUFX_Param_Dispersion");
            Add("Spike Intensity", "#LOC_TUFX_Param_SpikeIntensity");
            Add("Spike Count", "#LOC_TUFX_Param_SpikeCount");
            Add("Spike Length", "#LOC_TUFX_Param_SpikeLength");
            Add("Max Brightness", "#LOC_TUFX_Param_MaxBrightness");
            Add("Cinematic Letterbox", "#LOC_TUFX_Param_Letterbox");
            Add("Letterbox Ratio", "#LOC_TUFX_Param_LetterboxRatio");
            Add("Mode", "#LOC_TUFX_Param_Mode");
            Add("Speed", "#LOC_TUFX_Param_Speed");
            Add("Scale", "#LOC_TUFX_Param_Scale");
            Add("Plume Threshold", "#LOC_TUFX_Param_PlumeThreshold");
            Add("Ground Altitude Limit", "#LOC_TUFX_Param_GroundAltitudeLimit");
            Add("Source", "#LOC_TUFX_Param_Source");
            Add("Shutter Angle", "#LOC_TUFX_Param_ShutterAngle");
            Add("Blur Multiplier", "#LOC_TUFX_Param_BlurMultiplier");
            Add("Max Blur Pixels", "#LOC_TUFX_Param_MaxBlurPixels");
            Add("Quality", "#LOC_TUFX_Param_Quality");
            Add("Quality Mode", "#LOC_TUFX_Param_QualityMode");
            Add("Sharpness Attenuation", "#LOC_TUFX_Param_SharpnessAttenuation");
            Add("Edge Sensitivity", "#LOC_TUFX_Param_EdgeSensitivity");
            Add("Extra Sharpness", "#LOC_TUFX_Param_ExtraSharpness");
            Add("Auto Focus", "#LOC_TUFX_Param_AutoFocus");
            Add("Auto Focus Speed", "#LOC_TUFX_Param_AutoFocusSpeed");
            Add("Rack Speed", "#LOC_TUFX_Param_RackSpeed");
            Add("Focus Distance", "#LOC_TUFX_Param_FocusDistance");
            Add("Focal Length", "#LOC_TUFX_Param_FocalLength");
            Add("Dispersion Strength", "#LOC_TUFX_Param_DispersionStrength");
            Add("Max Bokeh Radius", "#LOC_TUFX_Param_MaxBokehRadius");
            Add("Anamorphic Ratio", "#LOC_TUFX_Param_AnamorphicRatio");
            Add("Fast Mode", "#LOC_TUFX_Param_FastMode");
            Add("Ambient Only", "#LOC_TUFX_Param_AmbientOnly");
            Add("Colored", "#LOC_TUFX_Param_Colored");
            Add("Rounded", "#LOC_TUFX_Param_Rounded");
            Add("Use Camera Fov", "#LOC_TUFX_Param_UseCameraFov");
            Add("Preset", "#LOC_TUFX_Param_Preset");
            Add("Resolution", "#LOC_TUFX_Param_Resolution");
            Add("Max Iterations", "#LOC_TUFX_Param_MaxIterations");
            Add("Max Iteration Count", "#LOC_TUFX_Param_MaxIterationCount");
            Add("Max March Dist", "#LOC_TUFX_Param_MaxMarchDist");
            Add("Max March Distance", "#LOC_TUFX_Param_MaxMarchDistance");
            Add("Distance Fade", "#LOC_TUFX_Param_DistanceFade");
            Add("Reflection Intensity", "#LOC_TUFX_Param_ReflectionIntensity");
            Add("Forward PBR Bias", "#LOC_TUFX_Param_ForwardPbrBias");
            Add("Smoothness", "#LOC_TUFX_Param_Smoothness");
            Add("Roundness", "#LOC_TUFX_Param_Roundness");
            Add("Size", "#LOC_TUFX_Param_Size");
            Add("Lum Contrib", "#LOC_TUFX_Param_LumContrib");
            Add("NoiseFilterTolerance", "#LOC_TUFX_Param_NoiseFilterTolerance");
            Add("BlurTolerance", "#LOC_TUFX_Param_BlurTolerance");
            Add("UpsampleTolerance", "#LOC_TUFX_Param_UpsampleTolerance");
            Add("ThicknessModifier", "#LOC_TUFX_Param_ThicknessModifier");
            Add("ZBias", "#LOC_TUFX_Param_ZBias");
            Add("DirectLightStr", "#LOC_TUFX_Param_DirectLightStr");
            Add("Filtering", "#LOC_TUFX_Param_Filtering");
            Add("Min", "#LOC_TUFX_Param_Min");
            Add("Max", "#LOC_TUFX_Param_Max");
            Add("EV", "#LOC_TUFX_Param_EV");
            Add("Type", "#LOC_TUFX_Param_Type");
            Add("Speed Up", "#LOC_TUFX_Param_SpeedUp");
            Add("Speed Down", "#LOC_TUFX_Param_SpeedDown");
            Add("Clamp", "#LOC_TUFX_Param_Clamp");
            Add("Diffusion", "#LOC_TUFX_Param_Diffusion");
            Add("Dirt Texture", "#LOC_TUFX_Param_DirtTexture");
            Add("Dirt Intensity", "#LOC_TUFX_Param_DirtIntensity");
            Add("Spectral Texture", "#LOC_TUFX_Param_SpectralTexture");
            Add("Temperature", "#LOC_TUFX_Param_Temperature");
            Add("Tint", "#LOC_TUFX_Param_Tint");
            Add("Hue Shift", "#LOC_TUFX_Param_HueShift");
            Add("Aperture", "#LOC_TUFX_Param_Aperture");
            Add("Max Blur Size", "#LOC_TUFX_Param_MaxBlurSize");
            Add("Red", "#LOC_TUFX_Param_Red");
            Add("Green", "#LOC_TUFX_Param_Green");
            Add("Blue", "#LOC_TUFX_Param_Blue");
            Add("Alpha", "#LOC_TUFX_Param_Alpha");
            Add("Temporal Super-Resolution", "#LOC_TUFX_Param_SuperResolution");
            Add("Dual-Scale Super-Sampling", "#LOC_TUFX_Param_DualScale");
            Add("Overdrive Boost (+0.0~1.5)", "#LOC_TUFX_Param_OverdriveBoost");
        }

        /// <summary>
        /// Localize a string via KSP.Localization.Localizer.
        /// If the string starts with '#', queries the tag directly.
        /// Otherwise looks up the English-to-tag mapping table.
        /// Returns the original string if no translation tag is registered.
        /// </summary>
        public static string Localize(this string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            try
            {
                if (text.StartsWith("#"))
                {
                    if (Localizer.Instance != null && Localizer.Tags != null && Localizer.Tags.ContainsKey(text))
                    {
                        return Localizer.Format(text);
                    }
                    return text;
                }

                if (EnglishToTag.TryGetValue(text, out string tag))
                {
                    if (Localizer.Instance != null && Localizer.Tags != null && Localizer.Tags.ContainsKey(tag))
                    {
                        return Localizer.Format(tag);
                    }
                }
            }
            catch (Exception ex)
            {
                // Fallback gracefully on any exception without disrupting callers
                UnityEngine.Debug.LogWarning("[TUFX] Error localizing string '" + text + "': " + ex.Message);
            }

            return text;
        }

        /// <summary>
        /// Localize with an explicit fallback value.
        /// </summary>
        public static string Localize(this string tag, string fallback)
        {
            if (string.IsNullOrEmpty(tag))
                return fallback;

            try
            {
                if (Localizer.Instance != null && Localizer.Tags != null && Localizer.Tags.ContainsKey(tag))
                {
                    return Localizer.Format(tag);
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[TUFX] Error localizing tag '" + tag + "': " + ex.Message);
            }

            return fallback;
        }
    }
}
