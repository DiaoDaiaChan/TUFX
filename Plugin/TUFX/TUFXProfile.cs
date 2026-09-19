using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace TUFX
{

    /// <summary>
    /// Enumeration of the built-in effect classes, to provide mapping between the name of the class and creation of an instance of that class.
    /// Used in order to provide run-time profile creation from a ConfigNode based configuration system.
    /// </summary>
    public enum BuiltinEffect
    {
        AmbientOcclusion,
        AutoExposure,
        Bloom,
        ChromaticAberration,
        ColorGrading,
        DepthOfField,
        Grain,
        LensDistortion,
        MotionBlur,
        Scattering,
        Vignette,
        ContrastAdaptiveSharpening,
        ModernTonemapping,
        Halation,
        AnamorphicFlare,
        GroundTruthAO,
        ContactShadows,
        GodRays,
        SpectralBokeh,
        ScreenSpaceReflections,
        HeatDistortion,
        CMAA2,
        CameraMotionBlur,
        FSRUpscaler,
        SSGI,
        SubsurfaceScattering
    }

    public class TUFXProfileManager
    {

        public static PostProcessEffectSettings CreateEmptySettingsForEffect(BuiltinEffect effect)
        {
            switch (effect)
            {
                case BuiltinEffect.AmbientOcclusion:
                    return ScriptableObject.CreateInstance<AmbientOcclusion>();
                case BuiltinEffect.AutoExposure:
                    return ScriptableObject.CreateInstance<AutoExposure>();
                case BuiltinEffect.Bloom:
                    return ScriptableObject.CreateInstance<Bloom>();
                case BuiltinEffect.ChromaticAberration:
                    return ScriptableObject.CreateInstance<ChromaticAberration>();
                case BuiltinEffect.ColorGrading:
                    return ScriptableObject.CreateInstance<ColorGrading>();
                case BuiltinEffect.DepthOfField:
                    return ScriptableObject.CreateInstance<DepthOfField>();
                case BuiltinEffect.Grain:
                    return ScriptableObject.CreateInstance<Grain>();
                case BuiltinEffect.LensDistortion:
                    return ScriptableObject.CreateInstance<LensDistortion>();
                case BuiltinEffect.MotionBlur:
                    return ScriptableObject.CreateInstance<MotionBlur>();
                case BuiltinEffect.Scattering:
                    return ScriptableObject.CreateInstance<TUBISEffect>();
                case BuiltinEffect.Vignette:
                    return ScriptableObject.CreateInstance<Vignette>();
                case BuiltinEffect.ContrastAdaptiveSharpening:
                    return ScriptableObject.CreateInstance<ContrastAdaptiveSharpening>();
                case BuiltinEffect.ModernTonemapping:
                    return ScriptableObject.CreateInstance<ModernTonemapping>();
                case BuiltinEffect.Halation:
                    return ScriptableObject.CreateInstance<Halation>();
                case BuiltinEffect.AnamorphicFlare:
                    return ScriptableObject.CreateInstance<AnamorphicFlare>();
                case BuiltinEffect.GroundTruthAO:
                    return ScriptableObject.CreateInstance<GroundTruthAO>();
                case BuiltinEffect.ContactShadows:
                    return ScriptableObject.CreateInstance<ContactShadows>();
                case BuiltinEffect.GodRays:
                    return ScriptableObject.CreateInstance<GodRays>();
                case BuiltinEffect.SpectralBokeh:
                    return ScriptableObject.CreateInstance<SpectralBokeh>();
                case BuiltinEffect.ScreenSpaceReflections:
                    return ScriptableObject.CreateInstance<ScreenSpaceReflections>();
                case BuiltinEffect.HeatDistortion:
                    return ScriptableObject.CreateInstance<HeatDistortionEffect>();
                case BuiltinEffect.CMAA2:
                    return ScriptableObject.CreateInstance<CMAA2Effect>();
                case BuiltinEffect.CameraMotionBlur:
                    return ScriptableObject.CreateInstance<CameraMotionBlurEffect>();
                case BuiltinEffect.FSRUpscaler:
                    return ScriptableObject.CreateInstance<EASUUpscaler>();
                case BuiltinEffect.SSGI:
                    return ScriptableObject.CreateInstance<SSGIEffect>();
                case BuiltinEffect.SubsurfaceScattering:
                    return ScriptableObject.CreateInstance<SubsurfaceScatteringEffect>();
                default:
                    break;
            }
            return null;
        }

        public static BuiltinEffect GetBuiltinEffect(PostProcessEffectSettings settings)
        {
            if (settings is AmbientOcclusion) { return BuiltinEffect.AmbientOcclusion; }
            else if (settings is AutoExposure) { return BuiltinEffect.AutoExposure; }
            else if (settings is Bloom) { return BuiltinEffect.Bloom; }
            else if (settings is ChromaticAberration) { return BuiltinEffect.ChromaticAberration; }
            else if (settings is ColorGrading) { return BuiltinEffect.ColorGrading; }
            else if (settings is DepthOfField) { return BuiltinEffect.DepthOfField; }
            else if (settings is Grain) { return BuiltinEffect.Grain; }
            else if (settings is LensDistortion) { return BuiltinEffect.LensDistortion; }
            else if (settings is MotionBlur) { return BuiltinEffect.MotionBlur; }
            else if (settings is TUBISEffect) { return BuiltinEffect.Scattering; }
            else if (settings is Vignette) { return BuiltinEffect.Vignette; }
            else if (settings is ContrastAdaptiveSharpening) { return BuiltinEffect.ContrastAdaptiveSharpening; }
            else if (settings is ModernTonemapping) { return BuiltinEffect.ModernTonemapping; }
            else if (settings is Halation) { return BuiltinEffect.Halation; }
            else if (settings is AnamorphicFlare) { return BuiltinEffect.AnamorphicFlare; }
            else if (settings is GroundTruthAO) { return BuiltinEffect.GroundTruthAO; }
            else if (settings is ContactShadows) { return BuiltinEffect.ContactShadows; }
            else if (settings is GodRays) { return BuiltinEffect.GodRays; }
            else if (settings is SpectralBokeh) { return BuiltinEffect.SpectralBokeh; }
            else if (settings is ScreenSpaceReflections) { return BuiltinEffect.ScreenSpaceReflections; }
            else if (settings is HeatDistortionEffect) { return BuiltinEffect.HeatDistortion; }
            else if (settings is CMAA2Effect) { return BuiltinEffect.CMAA2; }
            else if (settings is CameraMotionBlurEffect) { return BuiltinEffect.CameraMotionBlur; }
            else if (settings is EASUUpscaler) { return BuiltinEffect.FSRUpscaler; }
            else if (settings is SSGIEffect) { return BuiltinEffect.SSGI; }
            else if (settings is SubsurfaceScatteringEffect) { return BuiltinEffect.SubsurfaceScattering; }
            return BuiltinEffect.AmbientOcclusion;
        }

    }

    /// <summary>
    /// Storage of data for a single
    /// </summary>
    public class TUFXProfile
    {

        /// <summary>
        /// Name of the profile
        /// </summary>
        public string ProfileName { get; private set; }

        /// <summary>
        /// Configured value on if HDR is enabled for this profile or not.
        /// </summary>
        public bool HDREnabled { get; set; }

        /// <summary>
        /// Configured AntiAliasing setting for the profile.
        /// </summary>
        public PostProcessLayer.Antialiasing AntiAliasing;

        public PostProcessLayer.Antialiasing SecondaryCameraAntialiasing;

        public SubpixelMorphologicalAntialiasing.Quality SMAAQuality = SubpixelMorphologicalAntialiasing.Quality.High;
        public float TAAJitterSpread = 0.75f;
        public float TAASharpness = 0.25f;
        public float TAAStationaryBlending = 0.90f;
        public float TAAMotionBlending = 0.75f;

        private UrlDir.UrlConfig urlConfig;

        public string CfgPath => urlConfig.parent.url;

        /// <summary>
        /// List of the override settings currently configured for this profile
        /// </summary>
        public readonly List<PostProcessEffectSettings> Settings = new List<PostProcessEffectSettings>();

        /// <summary>
        /// Profile constructor, takes a ConfigNode containing the profile configuration.
        /// </summary>
        /// <param name="node"></param>
        public TUFXProfile(UrlDir.UrlConfig config)
        {
            urlConfig = config;
            LoadProfile(config.config);
        }

        ConfigNode SaveToNode()
        {
			ConfigNode node = new ConfigNode("TUFX_PROFILE");
			node.SetValue("name", ProfileName, true);
            node.SetValue("hdr", HDREnabled, true);
            node.SetValue("antialiasing", AntiAliasing.ToString(), true);
            node.SetValue("secondaryAntialiasing", SecondaryCameraAntialiasing.ToString(), true);
            node.SetValue("smaaQuality", SMAAQuality.ToString(), true);
            node.SetValue("taaJitterSpread", TAAJitterSpread.ToString(), true);
            node.SetValue("taaSharpness", TAASharpness.ToString(), true);
            node.SetValue("taaStationaryBlending", TAAStationaryBlending.ToString(), true);
            node.SetValue("taaMotionBlending", TAAMotionBlending.ToString(), true);
            int len = Settings.Count;
            for (int i = 0; i < len; i++)
            {
                if (Settings[i].enabled)
                {
                    ConfigNode effectNode = new ConfigNode("EFFECT");
                    effectNode.SetValue("name", TUFXProfileManager.GetBuiltinEffect(Settings[i]).ToString(), true);
                    Settings[i].Save(effectNode);
                    node.AddNode(effectNode);
                }
            }

            return node;
        }

        public bool SaveToDisk()
        {
            try
            {
                urlConfig.config = SaveToNode();
				urlConfig.parent.SaveConfigs();
                return true;
			}
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            return false;
        }

        public void ReloadFromNode()
        {
            LoadProfile(urlConfig.config);
        }

        /// <summary>
        /// Loads the profile from the input configuration node.
        /// </summary>
        /// <param name="node"></param>
        void LoadProfile(ConfigNode node)
        {
            ProfileName = node.GetStringValue("name");
            HDREnabled = node.GetBoolValue("hdr", false);
            AntiAliasing = node.GetEnumValue("antialiasing", PostProcessLayer.Antialiasing.None);
            SecondaryCameraAntialiasing = node.GetEnumValue("secondaryAntialiasing", PostProcessLayer.Antialiasing.None);
            SMAAQuality = node.GetEnumValue("smaaQuality", SubpixelMorphologicalAntialiasing.Quality.High);
            TAAJitterSpread = node.GetFloatValue("taaJitterSpread", 0.75f);
            TAASharpness = node.GetFloatValue("taaSharpness", 0.25f);
            TAAStationaryBlending = node.GetFloatValue("taaStationaryBlending", 0.90f);
            TAAMotionBlending = node.GetFloatValue("taaMotionBlending", 0.75f);
            Settings.Clear();
            ConfigNode[] effectNodes = node.GetNodes("EFFECT");
            int len = effectNodes.Length;
            for (int i = 0; i < len; i++)
            {
                BuiltinEffect effect = effectNodes[i].GetEnumValue("name", BuiltinEffect.AmbientOcclusion);
                PostProcessEffectSettings set = TUFXProfileManager.CreateEmptySettingsForEffect(effect);
                set.enabled.Override(true);
                set.Load(effectNodes[i]);
                Settings.Add(set);
            }
        }

        /// <summary>
        /// Returns a Unity PostProcessProfile instance with the settings contained in this TUFXProfile
        /// </summary>
        public PostProcessProfile CreatePostProcessProfile()
        {
            PostProcessProfile profile = ScriptableObject.CreateInstance<PostProcessProfile>();
            int len = Settings.Count;
            for (int i = 0; i < len; i++)
            {
                profile.settings.Add(Settings[i]);
            }
            profile.isDirty = true;
            profile.name = this.ProfileName;
            return profile;
        }

        /// <summary>
        /// Returns the PostProcessEffectSettings present in the settings list for the input Type, or null if no settings of that type are present.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public T GetSettingsFor<T>() where T : PostProcessEffectSettings
        {
            return (T)Settings.FirstOrDefault(m => m is T);
        }

    }

}
