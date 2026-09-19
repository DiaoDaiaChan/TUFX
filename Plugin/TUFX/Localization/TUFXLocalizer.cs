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
        private static readonly Dictionary<string, string> EnglishToTag = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Navigation, Headers & Buttons
            { "TUFX: Beyond", "#LOC_TUFX_Title" },
            { "Mode: ", "#LOC_TUFX_Nav_Mode" },
            { "Profiles", "#LOC_TUFX_Nav_Profiles" },
            { "Stock FX", "#LOC_TUFX_Nav_StockFX" },
            { "ExtendFX ★", "#LOC_TUFX_Nav_ExtendFX" },
            { "Return", "#LOC_TUFX_Btn_Return" },
            { "Save Selected", "#LOC_TUFX_Btn_SaveSelected" },
            { "Reload Selected", "#LOC_TUFX_Btn_ReloadSelected" },
            { "Save All", "#LOC_TUFX_Btn_SaveAll" },
            { "Reload All", "#LOC_TUFX_Btn_ReloadAll" },
            { "Close", "#LOC_TUFX_Btn_Close" },
            { "Enable", "#LOC_TUFX_Btn_Enable" },
            { "Disable", "#LOC_TUFX_Btn_Disable" },

            // Profiles window
            { "Current Scene: ", "#LOC_TUFX_Label_CurrentScene" },
            { "Current Profile: ", "#LOC_TUFX_Label_CurrentProfile" },
            { "Select a new profile for current scene: ", "#LOC_TUFX_Label_SelectProfile" },
            { "Profile: ", "#LOC_TUFX_Label_Profile" },
            { "No Profile Selected!", "#LOC_TUFX_Label_NoProfile" },
            { "<b>Stock TUFX Post-Processing Effects</b>", "#LOC_TUFX_Title_StockFX" },
            { "Open ExtendFX Suite (FSR, AgX/ACES, GTAO, SSR) >>", "#LOC_TUFX_Btn_OpenExtendFX" },
            { "<color=#55CCFF><b>[ExtendFX] Next-Gen Advanced Visual Effects & Upgrades</b></color>", "#LOC_TUFX_Title_ExtendFX" },
            { "<< Switch to Stock FX", "#LOC_TUFX_Btn_SwitchToStock" },
            { "Nothing selected", "#LOC_TUFX_Label_NothingSelected" },
            { "Effect: ", "#LOC_TUFX_Label_Effect" },
            { "Property: ", "#LOC_TUFX_Label_Property" },
            { "Current: ", "#LOC_TUFX_Label_Current" },
            { "<b>SMAA Quality Preset:</b>", "#LOC_TUFX_Label_SMAAPreset" },
            { "Low", "#LOC_TUFX_Btn_Low" },
            { "Medium", "#LOC_TUFX_Btn_Medium" },
            { "High (Ultra)", "#LOC_TUFX_Btn_HighUltra" },
            { "Lock / Switch to Manual", "#LOC_TUFX_Btn_LockManual" },
            { "Auto-Focus Once", "#LOC_TUFX_Btn_AutoFocusOnce" },
            { "Print SSR Diagnostics to KSP.log", "#LOC_TUFX_Btn_SSRDiagnostics" },

            // Effect Headers
            { "General Settings", "#LOC_TUFX_Effect_General" },
            { "General Profile Settings", "#LOC_TUFX_Effect_General" },
            { "Contrast Adaptive Sharpening", "#LOC_TUFX_Effect_CAS" },
            { "AMD FidelityFX CAS (Sharpening)", "#LOC_TUFX_Effect_CAS" },
            { "AMD FidelityFX FSR 1.0 / CAS (Contrast Adaptive Sharpening)", "#LOC_TUFX_Effect_CAS" },
            { "Modern Tonemapping", "#LOC_TUFX_Effect_ModernTonemapping" },
            { "Modern ACES / Filmic Tonemapping", "#LOC_TUFX_Effect_ModernTonemapping" },
            { "Modern Tonemapping (AgX / ACES / Tony / Filmic)", "#LOC_TUFX_Effect_ModernTonemapping" },
            { "Ground Truth AO (GTAO)", "#LOC_TUFX_Effect_GTAO" },
            { "Contact Shadows", "#LOC_TUFX_Effect_ContactShadows" },
            { "Screen Space Contact Shadows", "#LOC_TUFX_Effect_ContactShadows" },
            { "Screen Space Global Illumination (SSGI)", "#LOC_TUFX_Effect_SSGI" },
            { "Screen Space Subsurface Scattering (SSSS)", "#LOC_TUFX_Effect_SSSS" },
            { "Screen Space Light Shafts & Volumetric Shadows (God Rays)", "#LOC_TUFX_Effect_GodRays" },
            { "Screen Space Light Shafts / Volumetric Shadows (God Rays)", "#LOC_TUFX_Effect_GodRays" },
            { "Screen Space God Rays", "#LOC_TUFX_Effect_GodRays" },
            { "Halation", "#LOC_TUFX_Effect_Halation" },
            { "Halation (Film Bleed)", "#LOC_TUFX_Effect_Halation" },
            { "Film Halation", "#LOC_TUFX_Effect_Halation" },
            { "Anamorphic Flare & Starburst", "#LOC_TUFX_Effect_AnamorphicFlare" },
            { "Heat Distortion", "#LOC_TUFX_Effect_HeatDistortion" },
            { "Hypersonic Reentry Heat Haze", "#LOC_TUFX_Effect_HeatDistortion" },
            { "Camera Motion Blur", "#LOC_TUFX_Effect_CameraMotionBlur" },
            { "Camera Motion Blur (Physical Shutter)", "#LOC_TUFX_Effect_CameraMotionBlur" },
            { "Camera-Motion Blur (Physical Shutter Speed Blur)", "#LOC_TUFX_Effect_CameraMotionBlur" },
            { "Intel CMAA 2 (Anti-Aliasing)", "#LOC_TUFX_Effect_CMAA2" },
            { "Intel Conservative Morphological Anti-Aliasing (CMAA 2)", "#LOC_TUFX_Effect_CMAA2" },
            { "AMD FidelityFX Super Resolution (FSR 1.0)", "#LOC_TUFX_Effect_FSR" },
            { "AMD FidelityFX FSR 1.0 (EASU Reconstruction & Upscaler)", "#LOC_TUFX_Effect_FSR" },
            { "Spectral Bokeh (Chromatic DoF)", "#LOC_TUFX_Effect_SpectralBokeh" },
            { "Anti-Aliasing Control Center (Presets & TAA Tuning)", "#LOC_TUFX_Effect_AA" },
            { "Ambient Occlusion", "#LOC_TUFX_Effect_AmbientOcclusion" },
            { "Auto Exposure", "#LOC_TUFX_Effect_AutoExposure" },
            { "Bloom", "#LOC_TUFX_Effect_Bloom" },
            { "Chromatic Aberration", "#LOC_TUFX_Effect_ChromaticAberration" },
            { "Color Grading", "#LOC_TUFX_Effect_ColorGrading" },
            { "Depth of Field", "#LOC_TUFX_Effect_DepthOfField" },
            { "Grain", "#LOC_TUFX_Effect_Grain" },
            { "Lens Distortion", "#LOC_TUFX_Effect_LensDistortion" },
            { "Motion Blur", "#LOC_TUFX_Effect_MotionBlur" },
            { "Screen Space Reflections", "#LOC_TUFX_Effect_ScreenSpaceReflections" },
            { "Screen Space Reflections (SSR)", "#LOC_TUFX_Effect_ScreenSpaceReflections" },
            { "Vignette", "#LOC_TUFX_Effect_Vignette" },

            // Parameters
            { "HDR", "#LOC_TUFX_Param_HDR" },
            { "Resolution Scale", "#LOC_TUFX_Param_ResolutionScale" },
            { "Primary Camera Antialiasing", "#LOC_TUFX_Param_PrimaryAA" },
            { "Secondary Camera Antialiasing", "#LOC_TUFX_Param_SecondaryAA" },
            { "Intensity", "#LOC_TUFX_Param_Intensity" },
            { "Threshold", "#LOC_TUFX_Param_Threshold" },
            { "Radius", "#LOC_TUFX_Param_Radius" },
            { "Thickness", "#LOC_TUFX_Param_Thickness" },
            { "Soft Knee", "#LOC_TUFX_Param_SoftKnee" },
            { "Sharpness", "#LOC_TUFX_Param_Sharpness" },
            { "Sharpness (RCAS)", "#LOC_TUFX_Param_SharpnessRCAS" },
            { "Exposure", "#LOC_TUFX_Param_Exposure" },
            { "Contrast", "#LOC_TUFX_Param_Contrast" },
            { "Saturation", "#LOC_TUFX_Param_Saturation" },
            { "Tonemapper", "#LOC_TUFX_Param_Tonemapper" },
            { "Multi-Bounce", "#LOC_TUFX_Param_MultiBounce" },
            { "Specular Occlusion", "#LOC_TUFX_Param_SpecularOcclusion" },
            { "Color", "#LOC_TUFX_Param_Color" },
            { "Ray Length", "#LOC_TUFX_Param_RayLength" },
            { "Ray Steps", "#LOC_TUFX_Param_RaySteps" },
            { "Steps", "#LOC_TUFX_Param_RaySteps" },
            { "Ray Count", "#LOC_TUFX_Param_RayCount" },
            { "Bounce Color", "#LOC_TUFX_Param_BounceColor" },
            { "Scatter Radius", "#LOC_TUFX_Param_ScatterRadius" },
            { "Depth Threshold", "#LOC_TUFX_Param_DepthThreshold" },
            { "Max Distance", "#LOC_TUFX_Param_MaxDistance" },
            { "Max Distance (m)", "#LOC_TUFX_Param_MaxDistanceM" },
            { "Auto Adapt to Surface", "#LOC_TUFX_Param_AutoAdapt" },
            { "Subsurface Tint", "#LOC_TUFX_Param_SubsurfaceTint" },
            { "Decay", "#LOC_TUFX_Param_Decay" },
            { "Density", "#LOC_TUFX_Param_Density" },
            { "Weight", "#LOC_TUFX_Param_Weight" },
            { "Sample Count", "#LOC_TUFX_Param_SampleCount" },
            { "Samples", "#LOC_TUFX_Param_Samples" },
            { "Space Factor", "#LOC_TUFX_Param_SpaceFactor" },
            { "Space Intensity", "#LOC_TUFX_Param_SpaceIntensity" },
            { "Ray Color", "#LOC_TUFX_Param_RayColor" },
            { "Color Tint", "#LOC_TUFX_Param_ColorTint" },
            { "Streak Intensity", "#LOC_TUFX_Param_StreakIntensity" },
            { "Streak Length", "#LOC_TUFX_Param_StreakLength" },
            { "Streak Color", "#LOC_TUFX_Param_StreakColor" },
            { "Ghost Intensity", "#LOC_TUFX_Param_GhostIntensity" },
            { "Ghost Spread", "#LOC_TUFX_Param_GhostSpread" },
            { "Ghost Color", "#LOC_TUFX_Param_GhostColor" },
            { "Dispersion", "#LOC_TUFX_Param_Dispersion" },
            { "Spike Intensity", "#LOC_TUFX_Param_SpikeIntensity" },
            { "Spike Count", "#LOC_TUFX_Param_SpikeCount" },
            { "Spike Length", "#LOC_TUFX_Param_SpikeLength" },
            { "Max Brightness", "#LOC_TUFX_Param_MaxBrightness" },
            { "Cinematic Letterbox", "#LOC_TUFX_Param_Letterbox" },
            { "Letterbox Ratio", "#LOC_TUFX_Param_LetterboxRatio" },
            { "Mode", "#LOC_TUFX_Param_Mode" },
            { "Speed", "#LOC_TUFX_Param_Speed" },
            { "Scale", "#LOC_TUFX_Param_Scale" },
            { "Plume Threshold", "#LOC_TUFX_Param_PlumeThreshold" },
            { "Ground Altitude Limit", "#LOC_TUFX_Param_GroundAltitudeLimit" },
            { "Source", "#LOC_TUFX_Param_Source" },
            { "Shutter Angle", "#LOC_TUFX_Param_ShutterAngle" },
            { "Blur Multiplier", "#LOC_TUFX_Param_BlurMultiplier" },
            { "Max Blur Pixels", "#LOC_TUFX_Param_MaxBlurPixels" },
            { "Quality", "#LOC_TUFX_Param_Quality" },
            { "Quality Mode", "#LOC_TUFX_Param_QualityMode" },
            { "Sharpness Attenuation", "#LOC_TUFX_Param_SharpnessAttenuation" },
            { "Edge Sensitivity", "#LOC_TUFX_Param_EdgeSensitivity" },
            { "Extra Sharpness", "#LOC_TUFX_Param_ExtraSharpness" },
            { "Auto Focus", "#LOC_TUFX_Param_AutoFocus" },
            { "Auto Focus Speed", "#LOC_TUFX_Param_AutoFocusSpeed" },
            { "Rack Speed", "#LOC_TUFX_Param_RackSpeed" },
            { "Focus Distance", "#LOC_TUFX_Param_FocusDistance" },
            { "Focal Length", "#LOC_TUFX_Param_FocalLength" },
            { "Dispersion Strength", "#LOC_TUFX_Param_DispersionStrength" },
            { "Max Bokeh Radius", "#LOC_TUFX_Param_MaxBokehRadius" },
            { "Anamorphic Ratio", "#LOC_TUFX_Param_AnamorphicRatio" },
            { "Fast Mode", "#LOC_TUFX_Param_FastMode" },
            { "Ambient Only", "#LOC_TUFX_Param_AmbientOnly" },
            { "Colored", "#LOC_TUFX_Param_Colored" },
            { "Rounded", "#LOC_TUFX_Param_Rounded" },
            { "Use Camera Fov", "#LOC_TUFX_Param_UseCameraFov" },
            { "Preset", "#LOC_TUFX_Param_Preset" },
            { "Resolution", "#LOC_TUFX_Param_Resolution" },
            { "Max Iterations", "#LOC_TUFX_Param_MaxIterations" },
            { "Max Iteration Count", "#LOC_TUFX_Param_MaxIterationCount" },
            { "Max March Dist", "#LOC_TUFX_Param_MaxMarchDist" },
            { "Max March Distance", "#LOC_TUFX_Param_MaxMarchDistance" },
            { "Distance Fade", "#LOC_TUFX_Param_DistanceFade" },
            { "Vignette", "#LOC_TUFX_Param_Vignette" },
            { "Reflection Intensity", "#LOC_TUFX_Param_ReflectionIntensity" },
            { "Forward PBR Bias", "#LOC_TUFX_Param_ForwardPbrBias" },
            { "Smoothness", "#LOC_TUFX_Param_Smoothness" },
            { "Roundness", "#LOC_TUFX_Param_Roundness" },
            { "Size", "#LOC_TUFX_Param_Size" },
            { "Lum Contrib", "#LOC_TUFX_Param_LumContrib" },
            { "NoiseFilterTolerance", "#LOC_TUFX_Param_NoiseFilterTolerance" },
            { "BlurTolerance", "#LOC_TUFX_Param_BlurTolerance" },
            { "UpsampleTolerance", "#LOC_TUFX_Param_UpsampleTolerance" },
            { "ThicknessModifier", "#LOC_TUFX_Param_ThicknessModifier" },
            { "ZBias", "#LOC_TUFX_Param_ZBias" },
            { "DirectLightStr", "#LOC_TUFX_Param_DirectLightStr" },
            { "Filtering", "#LOC_TUFX_Param_Filtering" },
            { "Min", "#LOC_TUFX_Param_Min" },
            { "Max", "#LOC_TUFX_Param_Max" },
            { "EV", "#LOC_TUFX_Param_EV" },
            { "Type", "#LOC_TUFX_Param_Type" },
            { "Speed Up", "#LOC_TUFX_Param_SpeedUp" },
            { "Speed Down", "#LOC_TUFX_Param_SpeedDown" },
            { "Clamp", "#LOC_TUFX_Param_Clamp" },
            { "Diffusion", "#LOC_TUFX_Param_Diffusion" },
            { "Dirt Texture", "#LOC_TUFX_Param_DirtTexture" },
            { "Dirt Intensity", "#LOC_TUFX_Param_DirtIntensity" },
            { "Spectral Texture", "#LOC_TUFX_Param_SpectralTexture" },
            { "Temperature", "#LOC_TUFX_Param_Temperature" },
            { "Tint", "#LOC_TUFX_Param_Tint" },
            { "Hue Shift", "#LOC_TUFX_Param_HueShift" },
            { "Aperture", "#LOC_TUFX_Param_Aperture" },
            { "Max Blur Size", "#LOC_TUFX_Param_MaxBlurSize" },
            { "Red", "#LOC_TUFX_Param_Red" },
            { "Green", "#LOC_TUFX_Param_Green" },
            { "Blue", "#LOC_TUFX_Param_Blue" },
            { "Alpha", "#LOC_TUFX_Param_Alpha" }
        };

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

            return text;
        }

        /// <summary>
        /// Localize with an explicit fallback value.
        /// </summary>
        public static string Localize(this string tag, string fallback)
        {
            if (string.IsNullOrEmpty(tag))
                return fallback;

            if (Localizer.Instance != null && Localizer.Tags != null && Localizer.Tags.ContainsKey(tag))
            {
                return Localizer.Format(tag);
            }

            return fallback;
        }
    }
}
