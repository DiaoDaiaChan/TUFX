using ClickThroughFix;
using KSP.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace TUFX
{

    public class ConfigurationGUI : MonoBehaviour
    {

        private static Rect windowRect = new Rect(Screen.width - 920, 40, 825, 620);
        private int windowID = 0;
        private Vector2 scrollPos = new Vector2();
        private Vector2 editScrollPos = new Vector2();
        private Vector2 texScrollPos = new Vector2();
        private Vector2 profilerScrollPos = new Vector2();

        /// <summary>
        /// Cached list of all profile names currently loaded at the time the GUI was created.
        /// </summary>
        private List<string> profileNames = new List<string>();

        /// <summary>
        /// Cached dictionaries of temporary variables used by the UI for user-input values.
        /// </summary>
        private Dictionary<string, string> propertyStringStorage = new Dictionary<string, string>();
        private Dictionary<string, float> propertyFloatStorage = new Dictionary<string, float>();
        private Dictionary<string, bool> effectBoolStorage = new Dictionary<string, bool>();

        /// <summary>
        /// Available textures for the curret 'select texture' assignment.
        /// </summary>
        private List<Texture2D> textures = new List<Texture2D>();
        /// <summary>
        /// Values used by texture selection mode for UI display
        /// </summary>
        private string effect, property, texture;
        /// <summary>
        /// Callback for when texture is selected...
        /// </summary>
        private Action<Texture2D> textureUpdateCallback = null;

        enum GUIMode
        {
            SelectProfile,
            EditProfile,
            ExtendFX,
            PerformanceMonitor,
            SelectTexture,
            EditSpline,
        }

        private GUIMode selectionMode = 0;

        public void Awake()
        {
            windowID = GetInstanceID();
            profileNames.Clear();
            profileNames.AddRange(TexturesUnlimitedFXLoader.INSTANCE.Profiles.Keys);

            gameObject.layer = 5;
            gameObject.AddComponent<RectTransform>();

            var canvasRenderer = gameObject.AddComponent<CanvasRenderer>();
            canvasRenderer.cullTransparentMesh = true;
            var image = gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = new Color(0, 0, 0, 0);
            image.raycastTarget = true;

            if (MainCanvasUtil.MainCanvas != null)
            {
                gameObject.transform.SetParent(MainCanvasUtil.MainCanvas.transform, false);
            }

            var rectTransform = transform as RectTransform;
            if (rectTransform != null)
            {
                rectTransform.anchorMin = new Vector2(0, 0);
                rectTransform.anchorMax = new Vector2(0, 0);
                rectTransform.pivot = new Vector2(0, 0);
            }
        }

        public void OnGUI()
        {
            try
            {
                if (UIMasterController.Instance == null || UIMasterController.Instance.IsUIShowing)
                {
                    string title = "TUFX: Beyond";
                    try
                    {
                        title = title.Localize();
                    }
                    catch
                    {
                    }
                    windowRect = ClickThruBlocker.GUIWindow(windowID, windowRect, updateWindow, title);

                    var rectTransform = transform as RectTransform;
                    if (rectTransform != null)
                    {
                        rectTransform.anchoredPosition = new Vector2(windowRect.x, Screen.height - windowRect.y - windowRect.height);
                        rectTransform.sizeDelta = new Vector2(windowRect.width, windowRect.height);
                    }
                }
            }
            catch (Exception e)
            {
                MonoBehaviour.print("Caught exception while rendering TUFX Settings UI");
                MonoBehaviour.print(e.Message);
                MonoBehaviour.print(System.Environment.StackTrace);
            }
        }

        private void AddLabelRow(string text)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(text);
            GUILayout.EndHorizontal();
        }

        private void SwitchMode(GUIMode newMode)
        {
            this.selectionMode = newMode;
            this.textures.Clear();
            this.effect = this.property = this.texture = string.Empty;
            textureUpdateCallback = null;
        }

        void DrawHeader()
        {
            var currentProfile = TexturesUnlimitedFXLoader.INSTANCE.CurrentProfile;
            var allProfilers = TexturesUnlimitedFXLoader.INSTANCE.Profiles;

            GUILayout.BeginHorizontal();
            GUILayout.Label("Mode: ".Localize(), GUILayout.Width(50));
            GUIMode currentMode = this.selectionMode;

            bool isProfile = (currentMode == GUIMode.SelectProfile);
            bool isStock = (currentMode == GUIMode.EditProfile);
            bool isExtend = (currentMode == GUIMode.ExtendFX);
            bool isProfiler = (currentMode == GUIMode.PerformanceMonitor);

            if (GUILayout.Toggle(isProfile, "Profiles".Localize(), GUI.skin.button, GUILayout.Width(80)) && !isProfile)
            {
                SwitchMode(GUIMode.SelectProfile);
            }
            if (GUILayout.Toggle(isStock, "Stock FX".Localize(), GUI.skin.button, GUILayout.Width(80)) && !isStock)
            {
                SwitchMode(GUIMode.EditProfile);
            }
            Color prevColor = GUI.color;
            if (isExtend) GUI.color = new Color(0.3f, 0.9f, 1.0f);
            if (GUILayout.Toggle(isExtend, "ExtendFX ★".Localize(), GUI.skin.button, GUILayout.Width(100)) && !isExtend)
            {
                SwitchMode(GUIMode.ExtendFX);
            }
            GUI.color = prevColor;

            if (isProfiler) GUI.color = new Color(0.4f, 1.0f, 0.4f);
            else GUI.color = new Color(0.7f, 1.0f, 0.7f);
            if (GUILayout.Toggle(isProfiler, "Profiler".Localize(), GUI.skin.button, GUILayout.Width(85)) && !isProfiler)
            {
                SwitchMode(GUIMode.PerformanceMonitor);
            }
            GUI.color = prevColor;

            if (this.selectionMode == GUIMode.SelectTexture || this.selectionMode == GUIMode.EditSpline)
            {
                if (GUILayout.Button("Return".Localize(), GUILayout.Width(65)))
                {
                    SwitchMode(GUIMode.ExtendFX);
                }
            }

            // save current / reload current
            if (this.selectionMode != GUIMode.SelectTexture && this.selectionMode != GUIMode.EditSpline && this.selectionMode != GUIMode.PerformanceMonitor && currentProfile != null)
            {
                if (GUILayout.Button("Save Selected".Localize(), GUILayout.Width(100)))
                {
                    currentProfile.SaveToDisk();
                    ScreenMessages.PostScreenMessage("#LOC_TUFX_Msg_SavedSelected".Localize("<color=orange>Saved selected profile to cfg</color>"), 5f, ScreenMessageStyle.UPPER_LEFT);
                }
                if (GUILayout.Button("Reload Selected".Localize(), GUILayout.Width(105)))
                {
                    currentProfile.ReloadFromNode();
                    TexturesUnlimitedFXLoader.INSTANCE.RefreshCameras();
                }
            }

            if (this.selectionMode == GUIMode.SelectProfile)
            {
                if (GUILayout.Button("Save All".Localize(), GUILayout.Width(75)))
                {
                    foreach (var profile in allProfilers.Values)
                    {
                        profile.SaveToDisk();
                    }
                    ScreenMessages.PostScreenMessage("#LOC_TUFX_Msg_SavedAll".Localize("<color=orange>Saved all profiles to cfg files</color>"), 5f, ScreenMessageStyle.UPPER_LEFT);
                }
                if (GUILayout.Button("Reload All".Localize(), GUILayout.Width(85)))
                {
                    foreach (var profile in allProfilers.Values)
                    {
                        profile.ReloadFromNode();
                    }
                    TexturesUnlimitedFXLoader.INSTANCE.RefreshCameras();
                }
            }

            if (GUILayout.Button("Close".Localize(), GUILayout.Width(60)))
            {
                TexturesUnlimitedFXLoader.INSTANCE.CloseConfigGui();
            }
            GUILayout.EndHorizontal();
        }

        private void updateWindow(int id)
        {
            DrawHeader();

            if (selectionMode == GUIMode.SelectProfile)
            {
                renderSelectionWindow();
            }
            else if (selectionMode == GUIMode.EditProfile)
            {
                renderConfigurationWindow();
            }
            else if (selectionMode == GUIMode.ExtendFX)
            {
                renderExtendFXWindow();
            }
            else if (selectionMode == GUIMode.PerformanceMonitor)
            {
                renderPerformanceMonitorWindow();
            }
            else if (selectionMode == GUIMode.SelectTexture)
            {
                renderTextureSelectWindow();
            }
            else if (selectionMode == GUIMode.EditSpline)
            {
                renderSplineConfigurationWindow();
            }
            GUI.DragWindow();
        }

        private void renderPerformanceMonitorWindow()
        {
            GUILayout.BeginHorizontal(HighLogic.Skin.box);
            GUILayout.Label("#LOC_TUFX_Title_Profiler".Localize("<b><color=#55FF88>[Profiler]</color> Real-Time Pipeline Telemetry & Execution Probes</b>"));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(TUFX.Performance.TUFXProfiler.Enabled ? "Pause Probes".Localize() : "Resume Probes".Localize(), GUILayout.Width(110)))
            {
                TUFX.Performance.TUFXProfiler.Enabled = !TUFX.Performance.TUFXProfiler.Enabled;
            }
            if (GUILayout.Button("Reset Peaks".Localize(), GUILayout.Width(100)))
            {
                TUFX.Performance.TUFXProfiler.ResetPeaks();
            }
            GUILayout.EndHorizontal();

            GUILayout.Label("#LOC_TUFX_Profiler_Desc".Localize("<size=10><color=grey>Real-time per-effect CPU execution latency and relative GPU dispatch overhead tracked in strict hardware execution order.</color></size>"));

            // Dashboard Metrics Card
            GUILayout.BeginVertical(HighLogic.Skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label(string.Format("<b>{0}:</b> <color=lime>{1:F1}</color>", "FPS".Localize(), TUFX.Performance.TUFXProfiler.Fps), GUILayout.Width(110));
            GUILayout.Label(string.Format("<b>{0}:</b> {1:F2} ms", "Frame Latency".Localize(), TUFX.Performance.TUFXProfiler.FrameTimeMs), GUILayout.Width(160));
            GUILayout.Label(string.Format("<b>{0}:</b> <color=#00e5ff>{1:F3} ms</color>", "TUFX Post-Process".Localize(), TUFX.Performance.TUFXProfiler.AvgTotalPostProcessTimeMs), GUILayout.Width(220));
            GUILayout.Label(string.Format("<b>{0}:</b> <color=yellow>{1}</color> / {2}", "Active Passes".Localize(), TUFX.Performance.TUFXProfiler.ActiveEffectsCount, TUFX.Performance.TUFXProfiler.GetSortedEntries().Count));
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            // Pipeline Stages Legend
            GUILayout.BeginHorizontal();
            GUILayout.Label("<size=10><b>Pipeline: </b> <color=#ffaa00>[1. Opaque]</color>  →  <color=#00e5ff>[2. BeforeStack]</color>  →  <color=#b388ff>[3. Builtins]</color>  →  <color=#69f0ae>[4. AfterStack]</color>  →  <color=#ff4081>[5. FinalPass]</color></size>");
            GUILayout.EndHorizontal();

            // Telemetry Table Header
            GUILayout.BeginHorizontal(HighLogic.Skin.box);
            GUILayout.Label("<b>#</b>", GUILayout.Width(35));
            GUILayout.Label("<b>" + "Stage".Localize() + "</b>", GUILayout.Width(95));
            GUILayout.Label("<b>" + "Effect / Pass Name".Localize() + "</b>", GUILayout.Width(270));
            GUILayout.Label("<b>" + "Status".Localize() + "</b>", GUILayout.Width(70));
            GUILayout.Label("<b>" + "Avg Time".Localize() + "</b>", GUILayout.Width(75));
            GUILayout.Label("<b>" + "Share %".Localize() + "</b>", GUILayout.Width(110));
            GUILayout.Label("<b>" + "Peak".Localize() + "</b>", GUILayout.Width(65));
            GUILayout.EndHorizontal();

            // Scrollable Telemetry List
            profilerScrollPos = GUILayout.BeginScrollView(profilerScrollPos);
            GUILayout.BeginVertical();

            var entries = TUFX.Performance.TUFXProfiler.GetSortedEntries();
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];

                GUILayout.BeginHorizontal(HighLogic.Skin.textArea);

                // 1. Order
                GUILayout.Label(string.Format("<color=grey>#{0:D2}</color>", entry.executionOrder), GUILayout.Width(35));

                // 2. Stage badge
                string stageBadge;
                switch (entry.stage)
                {
                    case TUFX.Performance.PipelineStage.Opaque:
                        stageBadge = "<color=#ffaa00>[Opaque]</color>";
                        break;
                    case TUFX.Performance.PipelineStage.BeforeStack:
                        stageBadge = "<color=#00e5ff>[BeforeStack]</color>";
                        break;
                    case TUFX.Performance.PipelineStage.BuiltinStack:
                        stageBadge = "<color=#b388ff>[Builtins]</color>";
                        break;
                    case TUFX.Performance.PipelineStage.AfterStack:
                        stageBadge = "<color=#69f0ae>[AfterStack]</color>";
                        break;
                    case TUFX.Performance.PipelineStage.FinalPass:
                        stageBadge = "<color=#ff4081>[FinalPass]</color>";
                        break;
                    default:
                        stageBadge = "[Custom]";
                        break;
                }
                GUILayout.Label(stageBadge, GUILayout.Width(95));

                // 3. Name (Localized)
                string locName = !string.IsNullOrEmpty(entry.tag) ? entry.tag.Localize(entry.displayName) : entry.displayName;
                GUILayout.Label(locName, GUILayout.Width(270));

                // 4. Status
                if (entry.isRunning)
                {
                    GUILayout.Label("<color=lime>● RUN</color>", GUILayout.Width(70));
                }
                else
                {
                    GUILayout.Label("<color=grey>○ OFF</color>", GUILayout.Width(70));
                }

                // 5. Avg Time (ms)
                string timeColor = entry.avgTimeMs >= 1.0f ? "red" : (entry.avgTimeMs >= 0.3f ? "yellow" : "white");
                GUILayout.Label(string.Format("<color={0}>{1:F3} ms</color>", timeColor, entry.avgTimeMs), GUILayout.Width(75));

                // 6. Share % and visual bar
                int barBlocks = Mathf.Clamp(Mathf.RoundToInt(entry.relativePercent / 10f), 0, 10);
                string bar = new string('█', barBlocks) + new string('░', 10 - barBlocks);
                GUILayout.Label(string.Format("<color=#00e5ff>{0}</color> {1,4:F1}%", bar, entry.relativePercent), GUILayout.Width(110));

                // 7. Peak (ms)
                GUILayout.Label(string.Format("<color=grey>{0:F3} ms</color>", entry.peakTimeMs), GUILayout.Width(65));

                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
            GUILayout.EndScrollView();
        }

        private void renderSelectionWindow()
        {
            AddLabelRow("Current Scene: ".Localize() + HighLogic.LoadedScene +" map view active: " + MapView.MapIsEnabled + " internal cam active: " + (InternalCamera.Instance != null && InternalCamera.Instance.isActive));
            AddLabelRow("Current Profile: ".Localize() + TexturesUnlimitedFXLoader.INSTANCE.CurrentProfileName);
            AddLabelRow("Select a new profile for current scene: ".Localize());
            scrollPos = GUILayout.BeginScrollView(scrollPos);
            GUILayout.BeginVertical();
            int len = profileNames.Count;
            for (int i = 0; i < len; i++)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Profile: ".Localize() + profileNames[i]))
                {
                    string newProfileName = profileNames[i];
                    Log.debug("Profile Selected: " + newProfileName);
                    TexturesUnlimitedFXLoader.INSTANCE.setProfileForScene(newProfileName, HighLogic.LoadedScene, true);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
            GUILayout.EndScrollView();
        }

        private void renderConfigurationWindow()
        {
            if (TexturesUnlimitedFXLoader.INSTANCE.CurrentProfile == null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("No Profile Selected!".Localize());
                GUILayout.EndHorizontal();
                return;
            }

            GUILayout.BeginHorizontal(HighLogic.Skin.box);
            GUILayout.Label("<b>Stock TUFX Post-Processing Effects</b>".Localize());
            GUILayout.FlexibleSpace();
            Color prev = GUI.color;
            GUI.color = new Color(0.3f, 0.9f, 1.0f);
            if (GUILayout.Button("Open ExtendFX Suite (FSR, AgX/ACES, GTAO, SSR) >>".Localize(), GUILayout.Width(350)))
            {
                SwitchMode(GUIMode.ExtendFX);
            }
            GUI.color = prev;
            GUILayout.EndHorizontal();

            editScrollPos = GUILayout.BeginScrollView(editScrollPos, false, true, (GUILayoutOption[])null);
            renderGeneralSettings();
            renderAmbientOcclusionSettings();
            renderAutoExposureSettings();
            renderBloomSettings();
            renderChromaticAberrationSettings();
            renderColorGradingSettings();
            renderDepthOfFieldSettings();
            renderGrainSettings();
            renderLensDistortionSettings();
            renderMotionBlurSettings();
            renderScatteringSettings();
            renderVignetteSettings();
            GUILayout.EndScrollView();
        }

        private Vector2 extendScrollPos = new Vector2();

        private void renderExtendFXWindow()
        {
            if (TexturesUnlimitedFXLoader.INSTANCE.CurrentProfile == null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("No Profile Selected!".Localize());
                GUILayout.EndHorizontal();
                return;
            }

            GUILayout.BeginHorizontal(HighLogic.Skin.box);
            GUILayout.Label("<color=#55CCFF><b>[ExtendFX] Next-Gen Advanced Visual Effects & Upgrades</b></color>".Localize());
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("<< Switch to Stock FX".Localize(), GUILayout.Width(170)))
            {
                SwitchMode(GUIMode.EditProfile);
            }
            GUILayout.EndHorizontal();

            extendScrollPos = GUILayout.BeginScrollView(extendScrollPos, false, true, (GUILayoutOption[])null);

            // 0. Anti-Aliasing Control Center (Presets & TAA Tuning)
            renderAdvancedAntiAliasingSettings();

            // 1. Intel CMAA 2 (Conservative Morphological Anti-Aliasing)
            renderCMAA2Settings();

            // 1.5 Temporal Anti-Flicker Filter (Brian Karis UE5)
            renderAntiFlickerSettings();

            // 2. AMD FidelityFX FSR 1.0 / EASU Reconstruction & CAS
            renderFSRUpscalerSettings();
            renderCASSettings();

            // 3. Modern Tonemapping (AgX / ACES / Tony / Filmic)
            renderModernTonemappingSettings();

            // 4. Ground Truth Ambient Occlusion (XeGTAO)
            renderGTAOSettings();

            // 4. Screen Space Contact Shadows
            renderContactShadowsSettings();

            // 5. Screen Space Reflections (SSR)
            renderScreenSpaceReflectionsSettings();

            // 6. Film Halation
            renderHalationSettings();

            // 7. Anamorphic Lens Flare & Starburst
            renderAnamorphicFlareSettings();

            // 8. Screen Space Light Shafts / Volumetric Shadows (God Rays)
            renderGodRaysSettings();

            // 9. Spectral Bokeh (Chromatic Depth of Field)
            renderSpectralBokehSettings();

            // 10. Hypersonic Reentry Heat Haze
            renderHeatDistortionSettings();

            // 11. Camera-Motion Blur (Physical Shutter Speed Blur)
            renderCameraMotionBlurSettings();

            // 12. Screen Space Global Illumination (SSGI Diffuse Color Bleeding)
            renderSSGISettings();

            // 13. Screen Space Subsurface Scattering (SSSS Translucent Glow)
            renderSubsurfaceScatteringSettings();

            // 14. Vessel Material Pipeline & AI Super-Resolution Hook (Debug Hook)
            renderMaterialPipelineSettings();

            GUILayout.EndScrollView();
        }

        private void renderTextureSelectWindow()
        {
            GUILayout.BeginVertical();
            GUILayout.Label("Effect: ".Localize() + effect);
            GUILayout.Label("Property: ".Localize() + property);
            GUILayout.Label("Current: ".Localize() + texture);
            texScrollPos = GUILayout.BeginScrollView(texScrollPos);
            int len = textures.Count;
            for (int i = 0; i < len; i++)
            {
                if (GUILayout.Button(textures[i].name, GUILayout.Width(340)))
                {
                    textureUpdateCallback?.Invoke(textures[i]);
                    this.texture = textures[i].name;
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        private void renderSplineConfigurationWindow()//TODO - spline configuration window
        {

        }

        private void initializeTextureSelectMode(string effectName, string propertyName, string currentTextureName, Action<Texture2D> onSelect)
        {
            effect = property = texture = string.Empty;
            textureUpdateCallback = null;
            textures.Clear();
            TUFXEffectTextureList list;
            if (!TexturesUnlimitedFXLoader.INSTANCE.EffectTextureLists.TryGetValue(effectName, out list))
            {
                this.selectionMode = GUIMode.EditProfile;
                return;
            }
            textures.AddRange(list.GetTextures(propertyName));
            effect = effectName;
            property = propertyName;
            texture = currentTextureName;
            textureUpdateCallback = onSelect;
        }

        #region REGION Effect Settings Rendering

        private void renderGeneralSettings()
        {
            bool enabled = true;
            bool showProps = DrawGroupHeader("General Settings", ref enabled);

            if (showProps)
            {
                renderHDRSettings();
                renderAntialiasingSettings();
            }
            GUILayout.EndVertical();
        }

        private void renderHDRSettings()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("HDR".Localize(), GUILayout.Width(200));
            bool enabled = TexturesUnlimitedFXLoader.INSTANCE.CurrentProfile.HDREnabled;
            string buttonText = enabled ? "Disable".Localize() : "Enable".Localize();
			if (GUILayout.Button(buttonText, GUILayout.Width(100)))
			{
                TexturesUnlimitedFXLoader.INSTANCE.CurrentProfile.HDREnabled = !enabled;
				TexturesUnlimitedFXLoader.INSTANCE.RefreshCameras();
			}

			GUILayout.EndHorizontal();
        }

        private void renderAntialiasingSettings()
        {
            bool primaryChanged = AddEnumField("Primary Camera Antialiasing", ref TexturesUnlimitedFXLoader.INSTANCE.CurrentProfile.AntiAliasing);
            bool secondaryChanged = AddEnumField("Secondary Camera Antialiasing", ref TexturesUnlimitedFXLoader.INSTANCE.CurrentProfile.SecondaryCameraAntialiasing);

			if (primaryChanged || secondaryChanged)
            {
                TexturesUnlimitedFXLoader.INSTANCE.RefreshCameras();
            }
            //TODO -- add parameters for the AA modes
            //if (mode == PostProcessLayer.Antialiasing.FastApproximateAntialiasing)
            //{
            //    //AddBoolField("FXAA Fast Mode", ref layer.fastApproximageAntialiasing.fastMode);
            //    //AddBoolField("FXAA Keep Alpha", ref layer.fastApproximageAntialiasing.keepAlpha);
            //}
        }

        private void renderAmbientOcclusionSettings()
        {
            bool showProps = AddEffectHeader("Ambient Occlusion", out AmbientOcclusion ao);
            if (showProps)
            {
                AddEnumParameter("Mode", ao.mode);
                AddFloatParameter("Intensity", ao.intensity, 0, 10);
                AddColorParameter("Color", ao.color);
                AddBoolParameter("Ambient Only", ao.ambientOnly);
                AddFloatParameter("NoiseFilterTolerance", ao.noiseFilterTolerance, -8, 0);
                AddFloatParameter("BlurTolerance", ao.blurTolerance, -8, -1);
                AddFloatParameter("UpsampleTolerance", ao.upsampleTolerance, -12, -1);
                AddFloatParameter("ThicknessModifier", ao.thicknessModifier, 0, 5);
                AddFloatParameter("ZBias", ao.zBias, 0, 1);
                AddFloatParameter("DirectLightStr", ao.directLightingStrength, 0, 1);
                AddFloatParameter("Radius", ao.radius, 0, 1);
            }
            GUILayout.EndVertical();
        }

        private void renderAutoExposureSettings()
        {
            bool showProps = AddEffectHeader("Auto Exposure", out AutoExposure ae);
            if (showProps)
            {
                AddEnumParameter("Metering Mode", ae.meteringMode);
                AddFloatParameter("Space EV Floor", ae.spaceExposureFloor, -9, 9);
                AddVector2Parameter("Filtering", ae.filtering);
                AddFloatParameter("Min Luminance", ae.minLuminance, -9, 9);
                AddFloatParameter("Max Luminance", ae.maxLuminance, -9, 9);
                AddFloatParameter("Key Value", ae.keyValue, 0, 10);
                AddEnumParameter("Eye Adaption", ae.eyeAdaptation);
                AddFloatParameter("Speed Up", ae.speedUp, 0, 10);
                AddFloatParameter("Speed Down", ae.speedDown, 0, 10);
            }
            GUILayout.EndVertical();
        }

        private void renderBloomSettings()
        {
            bool showProps = AddEffectHeader("Bloom", out Bloom bl);
            if (showProps)
            {
                AddFloatParameter("Intensity", bl.intensity, 0, 10);
                AddFloatParameter("Threshold", bl.threshold, 0, 2);
                AddFloatParameter("SoftKnee", bl.softKnee, 0, 1);
                AddFloatParameter("Clamp", bl.clamp, 0, 64000);
                AddFloatParameter("Diffusion", bl.diffusion, 0, 20);
                AddFloatParameter("Anamorphic Ratio", bl.anamorphicRatio, -1, 1);
                AddColorParameter("Color", bl.color);
                AddBoolParameter("Fast Mode", bl.fastMode);
                AddTextureParameter("Dirt Texture", bl.dirtTexture, BuiltinEffect.Bloom.ToString(), "DirtTexture");
                AddFloatParameter("Dirt Intensity", bl.dirtIntensity, 0, 2);
            }
            GUILayout.EndVertical();
        }

        private void renderChromaticAberrationSettings()
        {
            bool showProps = AddEffectHeader("Chromatic Aberration", out ChromaticAberration ca);
            if (showProps)
            {
                AddTextureParameter("Spectral LUT", ca.spectralLut, BuiltinEffect.ChromaticAberration.ToString(), "SpectralLut");
                AddFloatParameter("Intensity", ca.intensity, 0, 1);
                AddBoolParameter("Fast Mode", ca.fastMode);
            }
            GUILayout.EndVertical();
        }

        private void renderColorGradingSettings()
        {
            bool showProps = AddEffectHeader("Color Grading", out ColorGrading cg);
            if (showProps)
            {
                AddEnumParameter("Mode", cg.gradingMode);
                if (cg.gradingMode == GradingMode.External)
                {
                    AddTextureParameter("External LUT", cg.externalLut, BuiltinEffect.ColorGrading.ToString(), "ExternalLut");
                }
                else if (cg.gradingMode == GradingMode.LowDefinitionRange)
                {
                    AddTextureParameter("LDR LUT", cg.ldrLut, BuiltinEffect.ColorGrading.ToString(), "LdrLut");
                    AddFloatParameter("LDR LUT Contrib.", cg.ldrLutContribution, 0, 1);
                }
                else if (cg.gradingMode == GradingMode.HighDefinitionRange)
                {
                    AddEnumParameter("Tonemapper", cg.tonemapper);
                    if (cg.tonemapper == Tonemapper.Custom)
                    {
                        AddFloatParameter("T.Curve Toe Strength", cg.toneCurveToeStrength, 0, 1);
                        AddFloatParameter("T.Curve Toe Length", cg.toneCurveToeLength, 0, 1);
                        AddFloatParameter("T.Curve Shd Strength", cg.toneCurveShoulderStrength, 0, 1);
                        AddFloatParameter("T.Curve Shd Length", cg.toneCurveShoulderLength, 0, 64000);
                        AddFloatParameter("T.Curve Shd Angle", cg.toneCurveShoulderAngle, 0, 1);
                        AddFloatParameter("Tone Curve Gamma", cg.toneCurveGamma, 0.001f, 64000);
                    }
                }
                AddFloatParameter("Temperature", cg.temperature, -100, 100);
                AddFloatParameter("Tint", cg.tint, -100, 100);
                AddColorParameter("ColorFilter", cg.colorFilter);
                AddFloatParameter("HueShift", cg.hueShift, -180, 180);
                AddFloatParameter("Saturation", cg.saturation, -100, 100);
                AddFloatParameter("Brightness", cg.brightness, -100, 100);
                AddFloatParameter("PostExposure", cg.postExposure, -64000, 64000);
                AddFloatParameter("Contrast", cg.contrast, -100, 100);

                AddFloatParameter("RedOutRedIn", cg.mixerRedOutRedIn, -200, 200);
                AddFloatParameter("RedOutGreenIn", cg.mixerRedOutGreenIn, -200, 200);
                AddFloatParameter("RedOutBlueIn", cg.mixerRedOutBlueIn, -200, 200);
                AddFloatParameter("GreenOutRedIn", cg.mixerGreenOutRedIn, -200, 200);
                AddFloatParameter("GreenOutGreenIn", cg.mixerGreenOutGreenIn, -200, 200);
                AddFloatParameter("GreenOutBlueIn", cg.mixerGreenOutBlueIn, -200, 200);
                AddFloatParameter("BlueOutRedIn", cg.mixerBlueOutRedIn, -200, 200);
                AddFloatParameter("BlueOutGreenIn", cg.mixerBlueOutGreenIn, -200, 200);
                AddFloatParameter("BlueOutBlueIn", cg.mixerBlueOutBlueIn, -200, 200);

                AddVector4Parameter("Lift", cg.lift);
                AddVector4Parameter("Gamma", cg.gamma);
                AddVector4Parameter("Gain", cg.gain);

                if (cg.gradingMode == GradingMode.LowDefinitionRange)
                {
                    AddSplineParameter("MasterCurve", cg.masterCurve);
                    AddSplineParameter("RedCurve", cg.redCurve);
                    AddSplineParameter("GreenCurve", cg.greenCurve);
                    AddSplineParameter("BlueCurve", cg.blueCurve);
                }

                AddSplineParameter("HueVsHueCurve", cg.hueVsHueCurve);
                AddSplineParameter("HueVsSatCurve", cg.hueVsSatCurve);
                AddSplineParameter("SatVsSatCurve", cg.satVsSatCurve);
                AddSplineParameter("LumVsSatCurve", cg.lumVsSatCurve);
            }
            GUILayout.EndVertical();
        }

        private void renderDepthOfFieldSettings()
        {
            bool showProps = AddEffectHeader("Depth Of Field", out DepthOfField df);
            if (showProps)
            {
                AddFloatParameter("Focus Distance", df.focusDistance, 0.1f, 64000);
                AddFloatParameter("Aperture", df.aperture, 0.05f, 32f);
                AddFloatParameter("Focal Length", df.focalLength, 1f, 300f);
                AddEnumParameter("Kernel Size", df.kernelSize);
                AddBoolParameter("Use Camera Fov", df.useCameraFov);
            }
            GUILayout.EndVertical();
        }

        private void renderGrainSettings()
        {
            bool showProps = AddEffectHeader("Grain", out Grain gr);
            if (showProps)
            {
                AddBoolParameter("Colored", gr.colored);
                AddFloatParameter("Intensity", gr.intensity, 0, 1);
                AddFloatParameter("Size", gr.size, 0.3f, 3f);
                AddFloatParameter("Lum. Contrib", gr.lumContrib, 0, 1);
            }
            GUILayout.EndVertical();
        }

        private void renderLensDistortionSettings()
        {
            bool showProps = AddEffectHeader("Lens Distortion", out LensDistortion ld);
            if (showProps)
            {
                AddFloatParameter("Intensity", ld.intensity, -100, 100);
                AddFloatParameter("IntensityX", ld.intensityX, 0, 1);
                AddFloatParameter("IntensityY", ld.intensityY, 0, 1);
                AddFloatParameter("Anamorphic Ratio", ld.anamorphicRatio, 0.5f, 2.5f);
                AddFloatParameter("Optical Vignetting", ld.opticalVignetting, 0f, 1f);
                AddFloatParameter("CenterX", ld.centerX, -1, 1);
                AddFloatParameter("CenterY", ld.centerY, -1, 1);
                AddFloatParameter("Scale", ld.scale, 0.01f, 5f);
            }
            GUILayout.EndVertical();
        }

        private void renderMotionBlurSettings()
        {
            bool showProps = AddEffectHeader("Motion Blur", out MotionBlur mb);
            if (showProps)
            {
                AddFloatParameter("Shutter Angle", mb.shutterAngle, 0f, 360f);
                AddIntParameter("Sample Count", mb.sampleCount, 4, 32);
            }
            GUILayout.EndVertical();
        }

        private void renderScatteringSettings()
        {
            //Log.debug("SC start");
            bool showProps = AddEffectHeader("Scattering", out TUBISEffect sc);
            //if (enabled != sc.enabled)
            //{
            //    //TODO
            //}
            if (showProps)
            {
                AddFloatParameter("Exposure", sc.Exposure, 0f, 50f);
            }
            GUILayout.EndVertical();
            //Log.debug("SC end");
        }

        private void renderVignetteSettings()
        {
            bool showProps = AddEffectHeader("Vignette", out Vignette vg);
            if (showProps)
            {
                AddEnumParameter("Mode", vg.mode);
                AddColorParameter("Color", vg.color);
                AddVector2Parameter("Center", vg.center);
                AddFloatParameter("Intensity", vg.intensity, 0, 1);
                AddFloatParameter("Optical Vignetting", vg.opticalVignetting, 0, 1);
                AddFloatParameter("Smoothness", vg.smoothness, 0.01f, 1f);
                AddFloatParameter("Roundness", vg.roundness, 0, 1);
                AddBoolParameter("Rounded", vg.rounded);
                AddTextureParameter("Mask", vg.mask, BuiltinEffect.Vignette.ToString(), "Mask");
                AddFloatParameter("Opacity", vg.opacity, 0, 1);
            }
            GUILayout.EndVertical();
        }

        private void renderAdvancedAntiAliasingSettings()
        {
            var profile = TexturesUnlimitedFXLoader.INSTANCE.CurrentProfile;
            if (profile == null) return;

            GUILayout.BeginVertical(HighLogic.Skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=#FFDD55><b>[Anti-Aliasing Control Center] Next-Gen AA Presets & Tuning</b></color>");
            GUILayout.EndHorizontal();

            GUILayout.Label("<size=10><color=grey>Non-temporal morphological filters (CMAA 2 / SMAA) produce ZERO black ghost trails behind orbital vessels!</color></size>");

            GUILayout.BeginHorizontal();
            // Preset 1: Intel CMAA 2
            bool isCMAA2Active = profile.GetSettingsFor<CMAA2Effect>() != null && profile.GetSettingsFor<CMAA2Effect>().enabled;
            Color prevCol = GUI.color;
            if (isCMAA2Active) GUI.color = new Color(0.4f, 1f, 0.4f);
            if (GUILayout.Button("Intel CMAA 2 (No Ghosting)", GUILayout.Height(28)))
            {
                var cmaa = profile.GetSettingsFor<CMAA2Effect>();
                if (cmaa == null)
                {
                    cmaa = ScriptableObject.CreateInstance<CMAA2Effect>();
                    profile.Settings.Add(cmaa);
                }
                cmaa.enabled.Override(true);
                profile.AntiAliasing = PostProcessLayer.Antialiasing.None;
                TexturesUnlimitedFXLoader.INSTANCE.RefreshCameras();
            }
            GUI.color = prevCol;

            // Preset 2: SMAA Ultra
            bool isSMAAActive = profile.AntiAliasing == PostProcessLayer.Antialiasing.SubpixelMorphologicalAntialiasing;
            if (isSMAAActive) GUI.color = new Color(0.4f, 1f, 0.4f);
            if (GUILayout.Button("SMAA Ultra (Zero Smear)", GUILayout.Height(28)))
            {
                profile.AntiAliasing = PostProcessLayer.Antialiasing.SubpixelMorphologicalAntialiasing;
                profile.SMAAQuality = SubpixelMorphologicalAntialiasing.Quality.High;
                var cmaa = profile.GetSettingsFor<CMAA2Effect>();
                if (cmaa != null) cmaa.enabled.Override(false);
                TexturesUnlimitedFXLoader.INSTANCE.RefreshCameras();
            }
            GUI.color = prevCol;

            // Preset 3: Enhanced TAA
            bool isTAAActive = profile.AntiAliasing == PostProcessLayer.Antialiasing.TemporalAntialiasing;
            if (isTAAActive) GUI.color = new Color(0.4f, 1f, 0.4f);
            if (GUILayout.Button("TAA (Smooth Temporal)", GUILayout.Height(28)))
            {
                profile.AntiAliasing = PostProcessLayer.Antialiasing.TemporalAntialiasing;
                var cmaa = profile.GetSettingsFor<CMAA2Effect>();
                if (cmaa != null) cmaa.enabled.Override(false);
                TexturesUnlimitedFXLoader.INSTANCE.RefreshCameras();
            }
            GUI.color = prevCol;

            // Preset 4: Off
            if (GUILayout.Button("AA Off", GUILayout.Width(60), GUILayout.Height(28)))
            {
                profile.AntiAliasing = PostProcessLayer.Antialiasing.None;
                var cmaa = profile.GetSettingsFor<CMAA2Effect>();
                if (cmaa != null) cmaa.enabled.Override(false);
                TexturesUnlimitedFXLoader.INSTANCE.RefreshCameras();
            }
            GUILayout.EndHorizontal();

            // Render sub-settings depending on camera AA mode
            if (profile.AntiAliasing == PostProcessLayer.Antialiasing.TemporalAntialiasing)
            {
                GUILayout.BeginVertical(HighLogic.Skin.box);
                GUILayout.Label("<b>Enhanced TAA Parameters:</b> <color=grey>(Supports keyboard typing with decimals and slider)</color>");
                
                AddFloatField("Stationary Blending", ref profile.TAAStationaryBlending, 0.50f, 0.98f, () => TexturesUnlimitedFXLoader.INSTANCE.RefreshCameras());
                AddFloatField("Motion Blending", ref profile.TAAMotionBlending, 0.30f, 0.95f, () => TexturesUnlimitedFXLoader.INSTANCE.RefreshCameras());
                AddFloatField("Jitter Spread", ref profile.TAAJitterSpread, 0.10f, 1.0f, () => TexturesUnlimitedFXLoader.INSTANCE.RefreshCameras());
                AddFloatField("TAA Sharpness", ref profile.TAASharpness, 0f, 1.0f, () => TexturesUnlimitedFXLoader.INSTANCE.RefreshCameras());
                GUILayout.EndVertical();
            }
            else if (profile.AntiAliasing == PostProcessLayer.Antialiasing.SubpixelMorphologicalAntialiasing)
            {
                GUILayout.BeginVertical(HighLogic.Skin.box);
                GUILayout.BeginHorizontal();
                GUILayout.Label("<b>SMAA Quality Preset:</b>".Localize(), GUILayout.Width(150));
                var q = profile.SMAAQuality;
                bool isLow = (q == SubpixelMorphologicalAntialiasing.Quality.Low);
                bool isMed = (q == SubpixelMorphologicalAntialiasing.Quality.Medium);
                bool isHigh = (q == SubpixelMorphologicalAntialiasing.Quality.High);

                if (GUILayout.Toggle(isLow, "Low".Localize(), GUI.skin.button) && !isLow)
                {
                    profile.SMAAQuality = SubpixelMorphologicalAntialiasing.Quality.Low;
                    TexturesUnlimitedFXLoader.INSTANCE.RefreshCameras();
                }
                if (GUILayout.Toggle(isMed, "Medium".Localize(), GUI.skin.button) && !isMed)
                {
                    profile.SMAAQuality = SubpixelMorphologicalAntialiasing.Quality.Medium;
                    TexturesUnlimitedFXLoader.INSTANCE.RefreshCameras();
                }
                if (GUILayout.Toggle(isHigh, "High (Ultra)".Localize(), GUI.skin.button) && !isHigh)
                {
                    profile.SMAAQuality = SubpixelMorphologicalAntialiasing.Quality.High;
                    TexturesUnlimitedFXLoader.INSTANCE.RefreshCameras();
                }
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }

            GUILayout.EndVertical();
        }

        private void renderCMAA2Settings()
        {
            bool showProps = AddEffectHeader("Intel Conservative Morphological Anti-Aliasing (CMAA 2)", out CMAA2Effect cmaa);
            if (showProps)
            {
                AddFloatParameter("Edge Sensitivity", cmaa.edgeThreshold, 0.02f, 0.20f);
                AddBoolParameter("Extra Sharpness", cmaa.extraSharpness);
                AddIntParameter("Max Search Length", cmaa.maxSearchLength, 8, 86);
                AddIntParameter("Debug Mode (0=Off,1=Edge,2=Weight)", cmaa.debugMode, 0, 2);
                GUILayout.Label("#LOC_TUFX_Desc_CMAA2".Localize("<size=10><color=grey>Intel's state-of-the-art morphological AA algorithm. Extremely sharp edge smoothing without temporal ghosting or blur.</color></size>"));
            }
            GUILayout.EndVertical();
        }

        private void renderAntiFlickerSettings()
        {
            bool showProps = AddEffectHeader("Temporal Anti-Flicker Filter (Brian Karis UE5)", out AntiFlickerEffect af);
            if (showProps)
            {
                AddEnumParameter("Motion Vectors Source", af.motionSource);
                AddEnumParameter("History Filter", af.historyFilter);
                AddEnumParameter("Clipping Method", af.clippingMode);
                AddFloatParameter("Temporal Stability", af.stability, 0.50f, 0.98f);
                AddFloatParameter("Variance Sharpness", af.sharpness, 0.8f, 2.5f);
                AddBoolParameter("Anti-Firefly Suppression", af.antiFirefly);
                AddBoolParameter("Preserve Vessel Sharpness", af.isolateVessel);
                AddBoolParameter("Temporal Super-Resolution", af.superResolution);
                AddIntParameter("Debug Mode (0=Off,1=Var,2=Hist,3=MV)", af.debugMode, 0, 3);
                GUILayout.Label("#LOC_TUFX_Desc_AntiFlicker".Localize("<size=10><color=grey>Industry-standard Brian Karis (UE5) YCoCg variance clipping, 18-DOP polytope, 5-tap bicubic Catmull-Rom & Deferred motion vectors. Eliminates high-frequency shimmering on trusses, wire antennas, and subpixel edges.</color></size>"));
            }
            GUILayout.EndVertical();
        }

        private void renderFSRUpscalerSettings()
        {
            bool showProps = AddEffectHeader("AMD FidelityFX FSR 1.0 (EASU Reconstruction & Upscaler)", out EASUUpscaler fsr);
            if (showProps)
            {
                AddEnumParameter("Quality Mode", fsr.quality);
                AddFloatParameter("Sharpness (RCAS)", fsr.sharpness, 0f, 1f);
                GUILayout.Label("#LOC_TUFX_Desc_FSR".Localize("<size=10><color=grey>AMD FSR 1.0 Edge-Adaptive Spatial Upsampling (EASU) with 12-tap directional reconstruction. Renders at sub-native scale (50%~77%) for massive FPS gains while preserving razor-sharp geometry.</color></size>"));
            }
            GUILayout.EndVertical();
        }

        private void renderCASSettings()
        {
            bool showProps = AddEffectHeader("AMD FidelityFX FSR 1.0 / CAS (Contrast Adaptive Sharpening)", out ContrastAdaptiveSharpening cas);
            if (showProps)
            {
                AddFloatParameter("Sharpness", cas.sharpness, 0f, 1f);
                AddBoolParameter("Dual-Scale Super-Sampling", cas.dualScale);
                if (cas.dualScale.value)
                {
                    AddFloatParameter("Overdrive Boost (+0.0~1.5)", cas.overdrive, 0f, 1.5f);
                }
                GUILayout.Label("#LOC_TUFX_Desc_CAS".Localize("<size=10><color=grey>AMD FidelityFX edge-directed dynamic sharpening (RCAS). Combine with CMAA 2 / SMAA / TAA above for crystal-clear antialiased visuals!</color></size>"));
            }
            GUILayout.EndVertical();
        }

        private void renderModernTonemappingSettings()
        {
            bool showProps = AddEffectHeader("Modern Tonemapping (AgX / ACES / Tony / Filmic)", out ModernTonemapping mt);
            if (showProps)
            {
                AddEnumParameter("Tonemapper", mt.tonemapper);
                AddFloatParameter("Exposure", mt.exposure, 0.05f, 5f);
                AddFloatParameter("Contrast", mt.contrast, 0.5f, 2.5f);
                AddFloatParameter("Saturation", mt.saturation, 0f, 2f);
                GUILayout.Label("#LOC_TUFX_Desc_ModernTonemapping".Localize("<size=10><color=grey>ACES provides punchy cinematic contrast and deep space blacks. AgX provides smooth highlight roll-off.</color></size>"));
            }
            GUILayout.EndVertical();
        }

        private void renderHalationSettings()
        {
            bool showProps = AddEffectHeader("Halation (Film Bleed)", out Halation h);
            if (showProps)
            {
                AddFloatParameter("Intensity", h.intensity, 0f, 5f);
                AddFloatParameter("Threshold", h.threshold, 0.1f, 10f);
                AddFloatParameter("Radius", h.radius, 0.5f, 10f);
                AddColorParameter("Color Tint", h.colorTint);
                GUILayout.Label("#LOC_TUFX_Desc_Halation".Localize("<size=10><color=grey>Authentic analog 35mm film halation red-orange glow bleeding around high-contrast overexposed edges.</color></size>"));
            }
            GUILayout.EndVertical();
        }

        private void renderAnamorphicFlareSettings()
        {
            bool showProps = AddEffectHeader("Anamorphic Flare & Starburst", out AnamorphicFlare af);
            if (showProps)
            {
                AddFloatParameter("Streak Intensity", af.streakIntensity, 0f, 5f);
                AddFloatParameter("Streak Length", af.streakLength, 0.5f, 15f);
                AddColorParameter("Streak Color", af.streakColor);
                AddFloatParameter("Ghost Intensity", af.ghostIntensity, 0f, 3f);
                AddFloatParameter("Ghost Spread", af.ghostSpread, 0.2f, 2f);
                AddColorParameter("Ghost Color", af.ghostColor);
                AddFloatParameter("Dispersion", af.dispersion, 0f, 2f);
                AddFloatParameter("Spike Intensity", af.spikeIntensity, 0f, 5f);
                AddIntParameter("Spike Count", af.spikeCount, 4, 8);
                AddFloatParameter("Spike Length", af.spikeLength, 0.5f, 10f);
                AddFloatParameter("Threshold", af.threshold, 0.5f, 15f);
                AddFloatParameter("Soft Knee", af.softKnee, 0f, 1f);
                AddFloatParameter("Max Brightness", af.maxBrightness, 5f, 50f);
                AddBoolParameter("Cinematic Letterbox", af.letterbox);
                if (af.letterbox.value)
                {
                    AddFloatParameter("Letterbox Ratio", af.letterboxRatio, 1.85f, 3.0f);
                }
                GUILayout.Label("#LOC_TUFX_Desc_AnamorphicFlare".Localize("<size=10><color=grey>Cinema anamorphic optical flares with horizontal spectral streaks, lens ghosts, diffraction starbursts, and cinematic letterbox black bars.</color></size>"));
            }
            GUILayout.EndVertical();
        }

        private void renderGTAOSettings()
        {
            bool showProps = AddEffectHeader("Ground Truth AO (GTAO)", out GroundTruthAO gtao);
            if (showProps)
            {
                AddFloatParameter("Resolution Scale", gtao.resolutionScale, 0.25f, 1.0f);
                AddFloatParameter("Radius", gtao.radius, 0.05f, 5f);
                AddFloatParameter("Intensity", gtao.intensity, 0f, 4f);
                AddFloatParameter("Thickness", gtao.thickness, 0.1f, 5f);
                AddFloatParameter("Multi-Bounce", gtao.multiBounce, 0f, 1f);
                AddFloatParameter("Specular Occlusion", gtao.specularOcclusion, 0f, 1f);
                AddColorParameter("Color", gtao.color);
                GUILayout.Label("#LOC_TUFX_Desc_GTAO".Localize("<size=10><color=grey>Intel Ground Truth Ambient Occlusion (GTAO) with multi-bounce ambient filling and specular reflection occlusion.</color></size>"));
            }
            GUILayout.EndVertical();
        }

        private void renderContactShadowsSettings()
        {
            bool showProps = AddEffectHeader("Contact Shadows", out ContactShadows cs);
            if (showProps)
            {
                AddFloatParameter("Ray Length", cs.rayLength, 0.05f, 2.0f);
                AddIntParameter("Steps", cs.raySteps, 4, 32);
                AddFloatParameter("Intensity", cs.intensity, 0f, 1f);
                AddFloatParameter("Thickness", cs.thickness, 0.01f, 1.0f);
                AddIntParameter("Debug Mode (0=Off, 1=Red, 2=Mask, 3=Normals)", cs.debugMode, 0, 3);
                GUILayout.Label("#LOC_TUFX_Desc_ContactShadows".Localize("<size=10><color=grey>Screen-space micro-geometry contact shadows between vessel stages, landing gear, and surface details.</color></size>"));
            }
            GUILayout.EndVertical();
        }

        private void renderGodRaysSettings()
        {
            bool showProps = AddEffectHeader("Screen Space Light Shafts & Volumetric Shadows (God Rays)", out GodRays gr);
            if (showProps)
            {
                AddFloatParameter("Intensity", gr.intensity, 0f, 5f);
                AddFloatParameter("Space Intensity", gr.spaceIntensity, 0f, 1f);
                AddFloatParameter("Threshold", gr.threshold, 0.1f, 2f);
                AddFloatParameter("Density", gr.density, 0.1f, 2f);
                AddFloatParameter("Decay", gr.decay, 0.8f, 0.99f);
                AddFloatParameter("Weight", gr.weight, 0.05f, 1f);
                AddColorParameter("Ray Color", gr.rayColor);
                GUILayout.Label("#LOC_TUFX_Desc_GodRays".Localize("<size=10><color=grey>Simulates dramatic Tyndall volumetric light shafts in atmosphere, and crisp optical lens corona in vacuum.</color></size>"));
            }
            GUILayout.EndVertical();
        }

        private void renderSpectralBokehSettings()
        {
            bool showProps = AddEffectHeader("Spectral Bokeh (Chromatic DoF)", out SpectralBokeh sb);
            if (showProps)
            {
                AddBoolParameter("Auto Focus", sb.autoFocus);
                if (sb.autoFocus.value)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(20);
                    GUILayout.Label($"{"Tracking: ".Localize()}<color=#88FF88>{SpectralBokeh.LastFocusedDistance:F1} m</color>", GUILayout.Width(180));
                    if (GUILayout.Button("Lock / Switch to Manual".Localize(), GUILayout.Width(170)))
                    {
                        sb.focusDistance.Override(SpectralBokeh.LastFocusedDistance);
                        sb.autoFocus.Override(false);
                        TexturesUnlimitedFXLoader.INSTANCE.RefreshCameras();
                    }
                    GUILayout.EndHorizontal();

                    AddFloatParameter("Rack Speed", sb.autoFocusSpeed, 1f, 30f);
                }
                else
                {
                    AddFloatParameter("Focus Distance", sb.focusDistance, 0.1f, 500f);
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(20);
                    if (GUILayout.Button("Auto-Focus Once".Localize(), GUILayout.Width(150)))
                    {
                        float instantDist = SpectralBokeh.ComputeTargetDistance(Camera.main, sb.focusDistance.value);
                        sb.focusDistance.Override(instantDist);
                        TexturesUnlimitedFXLoader.INSTANCE.RefreshCameras();
                    }
                    GUILayout.EndHorizontal();
                }

                AddFloatParameter("Focal Length", sb.focalLength, 10f, 300f);
                AddFloatParameter("Anamorphic Ratio", sb.anamorphicRatio, 0.5f, 3.0f);
                AddFloatParameter("Dispersion", sb.dispersionStrength, 0f, 2f);
                AddFloatParameter("Max Bokeh Radius", sb.maxBokehRadius, 1f, 20f);
                GUILayout.Label("#LOC_TUFX_Desc_SpectralBokeh".Localize("<size=10><color=grey>Physically-simulated optical chromatic DoF with auto tracking, rainbow dispersion, and 2x Hollywood oval bokeh.</color></size>"));
            }
            GUILayout.EndVertical();
        }

        private void renderCameraMotionBlurSettings()
        {
            bool showProps = AddEffectHeader("Camera Motion Blur (Physical Shutter)", out CameraMotionBlurEffect cmb);
            if (showProps)
            {
                AddBoolParameter("Preserve Vessel Sharpness", cmb.isolateVessel);
                AddFloatParameter("Shutter Angle", cmb.shutterAngle, 0f, 360f);
                AddFloatParameter("Blur Multiplier", cmb.blurMultiplier, 0.2f, 5f);
                AddIntParameter("Samples", cmb.sampleCount, 4, 24);
                AddFloatParameter("Max Blur Pixels", cmb.maxBlurPixels, 8f, 128f);
                GUILayout.Label("#LOC_TUFX_Desc_CameraMotionBlur".Localize("<size=10><color=grey>Physical rotary shutter speed blur. Isolates tracked spacecraft while blurring high-speed terrain and camera spin.</color></size>"));
            }
            GUILayout.EndVertical();
        }

        private void renderScreenSpaceReflectionsSettings()
        {
            bool showProps = AddEffectHeader("Screen Space Reflections (SSR)", out ScreenSpaceReflections ssr);
            if (showProps)
            {
                bool isDeferred = (Camera.main != null && Camera.main.actualRenderingPath == RenderingPath.DeferredShading);
                bool hasDeferredMod = AssemblyLoader.loadedAssemblies != null && AssemblyLoader.loadedAssemblies.Any(a => a.name.Equals("Deferred", StringComparison.OrdinalIgnoreCase) || a.assembly.GetName().Name.Equals("Deferred", StringComparison.OrdinalIgnoreCase));
                
                GUILayout.BeginVertical(HighLogic.Skin.box);
                GUILayout.Label($"[SSR Pipeline] Status: {(isDeferred ? "<color=green>Deferred Active (Full PBR SSR)</color>" : "<color=yellow>Forward Active (SSSR Mode)</color>")}");
                GUILayout.Label($"Deferred Mod: {(hasDeferredMod ? "Detected" : "Not Detected")}, Camera: {(Camera.main != null ? Camera.main.actualRenderingPath.ToString() : "N/A")}");
                if (GUILayout.Button("Print SSR Diagnostics to KSP.log".Localize(), GUILayout.Width(260)))
                {
                    Log.log($"[TUFX SSR Diagnostics] Camera: {Camera.main?.name}, Path: {Camera.main?.actualRenderingPath}, DeferredMod: {hasDeferredMod}, MotionVectors: {SystemInfo.supportsMotionVectors}, ComputeShaders: {SystemInfo.supportsComputeShaders}, CopyTexture: {SystemInfo.copyTextureSupport}");
                }
                GUILayout.EndVertical();
                AddEnumParameter("Preset", ssr.preset);
                AddIntParameter("Max Iterations", ssr.maximumIterationCount, 4, 128);
                AddEnumParameter("Resolution", ssr.resolution);
                AddFloatParameter("Thickness", ssr.thickness, 1f, 64f);
                AddFloatParameter("Max March Dist", ssr.maximumMarchDistance, 10f, 500f);
                AddFloatParameter("Distance Fade", ssr.distanceFade, 0f, 1f);
                AddFloatParameter("Vignette", ssr.vignette, 0f, 1f);
                AddFloatParameter("Reflection Intensity", ssr.reflectionIntensity, 0f, 2f);
                if (!isDeferred)
                {
                    AddFloatParameter("Forward PBR Bias", ssr.forwardPbrBias, 0.01f, 1f);
                }
            }
            GUILayout.EndVertical();
        }

        private void renderHeatDistortionSettings()
        {
            bool showProps = AddEffectHeader("Heat Distortion", out HeatDistortionEffect hd);
            if (showProps)
            {
                AddEnumParameter("Mode", hd.mode);
                AddFloatParameter("Intensity", hd.intensity, 0f, 2f);
                AddFloatParameter("Speed", hd.speed, 0.2f, 10f);
                AddFloatParameter("Scale", hd.scale, 1f, 30f);
                AddFloatParameter("Plume Threshold", hd.plumeThreshold, 0.3f, 2.0f);
                AddFloatParameter("Ground Altitude Limit", hd.groundAltitudeLimit, 50f, 3000f);
                GUILayout.Label("#LOC_TUFX_Desc_HeatDistortion".Localize("<size=10><color=grey>Adaptive thermal turbulence around engine plumes, supersonic reentry shockwaves, and distant runway shimmer.</color></size>"));
            }
            GUILayout.EndVertical();
        }

        private void renderSSGISettings()
        {
            bool showProps = AddEffectHeader("Screen Space Global Illumination (SSGI)", out SSGIEffect ssgi);
            if (showProps)
            {
                AddFloatParameter("Resolution Scale", ssgi.resolutionScale, 0.25f, 1.0f);
                AddFloatParameter("Intensity", ssgi.intensity, 0f, 5f);
                AddIntParameter("Ray Count", ssgi.rayCount, 2, 8);
                AddIntParameter("Ray Steps", ssgi.raySteps, 4, 16);
                AddFloatParameter("Ray Length (m)", ssgi.rayLength, 0.5f, 30f);
                AddFloatParameter("Thickness", ssgi.thickness, 0.1f, 5f);
                AddColorParameter("Bounce Color", ssgi.bounceColor);
                GUILayout.Label("#LOC_TUFX_Desc_SSGI".Localize("<size=10><color=grey>Single-bounce screen-space diffuse indirect illumination & realistic color bleeding from terrain and nearby vehicle hulls.</color></size>"));
            }
            GUILayout.EndVertical();
        }

        private void renderSubsurfaceScatteringSettings()
        {
            bool showProps = AddEffectHeader("Screen Space Subsurface Scattering (SSSS)", out SubsurfaceScatteringEffect ssss);
            if (showProps)
            {
                AddFloatParameter("Intensity", ssss.intensity, 0f, 1f);
                AddFloatParameter("Max Distance (m)", ssss.maxDistance, 5f, 1000f);
                AddFloatParameter("Scatter Radius", ssss.scatterRadius, 0.5f, 15f);
                AddFloatParameter("Depth Threshold", ssss.depthThreshold, 0.01f, 1f);
                AddBoolParameter("Auto Adapt to Surface", ssss.autoAdapt);
                if (!ssss.autoAdapt.value)
                {
                    AddColorParameter("Subsurface Tint", ssss.subsurfaceColor);
                }
                GUILayout.Label("#LOC_TUFX_Desc_SSSS".Localize("<size=10><color=grey>Separable screen-space subsurface scattering for organic Kerbal skin/EVA translucency. Auto Adapt dynamically matches scattering to surface albedo chrominance, keeping metallic spacecraft clean and neutral while giving Kerbals a rich green subcutaneous glow.</color></size>"));
            }
            GUILayout.EndVertical();
        }

        private bool showMaterialPipeline = true;

        private void renderMaterialPipelineSettings()
        {
            GUILayout.BeginVertical(HighLogic.Skin.box);
            GUILayout.BeginHorizontal();
            showMaterialPipeline = GUILayout.Toggle(showMaterialPipeline, "<color=#55FFAA><b>[Material Pipeline] 飞船材质流式重构与 AI 超分底座 (Debug Hook)</b></color>", GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            if (showMaterialPipeline)
            {
                var mgr = TUFX.MaterialPipeline.MaterialPipelineManager.Instance;
                if (mgr != null)
                {
                    // Scan / Status Info Box
                    GUILayout.BeginVertical(HighLogic.Skin.box);
                    Vessel v = FlightGlobals.ActiveVessel;
                    string vesselName = v != null ? v.vesselName : "None (No Active Vessel)";
                    GUILayout.Label("<b>当前飞船 (Active Vessel):</b> " + vesselName);

                    var lastScan = mgr.Tracker != null ? mgr.Tracker.LastScan : null;
                    if (lastScan != null && lastScan.ScannedVessel == v)
                    {
                        GUILayout.Label(string.Format("扫描统计: 部件数: <b>{0}</b> | 材质数: <b>{1}</b> | 唯一贴图: <b>{2}</b>",
                            lastScan.PartCount, lastScan.MaterialCount, lastScan.UniqueTextureCount));
                    }
                    else
                    {
                        GUILayout.Label("扫描统计: 点击下方按钮立即重新扫描当前飞船");
                    }

                    GUILayout.Label(string.Format("已 Hook 贴图数: <b>{0}</b> | 状态: <color=#FFFF55>{1}</color>",
                        mgr.Registry != null ? mgr.Registry.OverrideCount : 0, mgr.StatusMessage));

                    if (mgr.IsProcessing)
                    {
                        float progress = mgr.TotalProgress > 0 ? (float)mgr.CurrentProgress / mgr.TotalProgress : 0f;
                        GUILayout.HorizontalSlider(progress, 0f, 1f);
                    }
                    GUILayout.EndVertical();

                    // DirectML Engine & Model Info
                    var ai = mgr.SuperResEngine;
                    if (ai != null && ai.IsDirectMLAvailable)
                    {
                        GUILayout.BeginVertical(HighLogic.Skin.box);
                        GUILayout.Label($"<b>DirectML AI 引擎状态:</b> <color=#55FF55><b>GPU 硬件加速已就绪 (DirectX 12)</b></color>");
                        GUILayout.Label($"<b>当前加载模型:</b> <color=#FFFF55><b>{ai.ActiveModelName}</b></color>");

                        if (ai.AvailableModels.Count > 1)
                        {
                            GUILayout.BeginHorizontal();
                            GUILayout.Label("切换模型:", GUILayout.Width(70));
                            for (int m = 0; m < ai.AvailableModels.Count; m++)
                            {
                                string mName = ai.AvailableModels[m];
                                bool isCur = (mName == ai.ActiveModelName);
                                string btnText = isCur ? $"<color=#00FF88><b>[{mName}]</b></color>" : mName;
                                if (GUILayout.Button(btnText, GUILayout.ExpandWidth(false)))
                                {
                                    ai.LoadModel(mName);
                                }
                            }
                            GUILayout.EndHorizontal();
                        }
                        GUILayout.EndVertical();
                    }
                    else if (ai != null)
                    {
                        GUILayout.Label($"<color=#FF7777>DirectML 引擎未激活: {ai.InitError}</color>");
                    }

                    // Control Buttons & Sliders
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("渐进替换间隔 (秒):", GUILayout.Width(130));
                    mgr.ProgressiveDelay = GUILayout.HorizontalSlider(mgr.ProgressiveDelay, 0.02f, 0.50f);
                    GUILayout.Label(mgr.ProgressiveDelay.ToString("F2") + "s", GUILayout.Width(45));
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("重新扫描飞船 (Rescan)"))
                    {
                        mgr.RescanActiveVessel();
                    }

                    GUI.enabled = !mgr.IsProcessing;
                    if (ai != null && ai.IsDirectMLAvailable)
                    {
                        if (GUILayout.Button("<color=#00FFFF><b>启动 DirectML 飞船 AI 超分 (Real AI)</b></color>"))
                        {
                            mgr.StartProgressiveAIUpscale();
                        }
                    }

                    if (GUILayout.Button("<color=#FFFF00><b>逐部件水印测试 (Debug Stamp)</b></color>"))
                    {
                        mgr.StartProgressiveDebugStamp();
                    }
                    GUI.enabled = true;

                    if (GUILayout.Button("一键还原原始贴图 (Restore)"))
                    {
                        mgr.RestoreOriginalTextures();
                    }
                    GUILayout.EndHorizontal();

                    GUILayout.Label("<size=10><color=grey>DirectML AI 超分管线：基于微软官方 DirectML + ONNX Runtime 原生 GPU 硬件加速，零 Python/PyTorch 依赖，在飞船材质流式底座上逐个部件将贴图毫秒级无感超分并热替换。</color></size>");
                }
                else
                {
                    GUILayout.Label("MaterialPipelineManager 未初始化。");
                }
            }
            GUILayout.EndVertical();
        }

        #endregion

        #region REGION - Parameter Rendering Methods

        private bool DrawGroupHeader(string label, ref bool enabled)
        {
			GUILayout.BeginVertical(HighLogic.Skin.box);
            GUILayout.BeginVertical(HighLogic.Skin.box);
            GUILayout.BeginHorizontal();

			if (!effectBoolStorage.TryGetValue(label, out bool showProps))
            {
                effectBoolStorage.Add(label, showProps = true);
            }

            if (GUILayout.Button(showProps ? "v" : ">", GUILayout.Width(20)))
            {
                showProps = !showProps;
				effectBoolStorage[label] = showProps;
			}

			enabled = GUILayout.Toggle(enabled, label.Localize());

			GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            return showProps && enabled;
        }

		private bool AddEffectHeader<T>(string label, out T effect) where T : PostProcessEffectSettings
        {
            effect = TexturesUnlimitedFXLoader.INSTANCE.CurrentProfile.GetSettingsFor<T>();

            bool effectEnabled = effect != null && effect.enabled;
            bool showProps = DrawGroupHeader(label, ref effectEnabled);

            if (effectEnabled && effect == null)
            {
                effect = ScriptableObject.CreateInstance<T>();
                effect.enabled.Override(true);
                if (effect.parameters != null)
                {
                    foreach (var p in effect.parameters)
                    {
                        p.overrideState = true;
                    }
                }
                TexturesUnlimitedFXLoader.INSTANCE.CurrentProfile.Settings.Add(effect);
                TexturesUnlimitedFXLoader.INSTANCE.RefreshCameras();
			}
            else if (effect != null && effect.enabled.value != effectEnabled)
            {
                effect.enabled.Override(effectEnabled);
                TexturesUnlimitedFXLoader.INSTANCE.RefreshCameras();
            }

            if (effect)
            {
                effect.enabled.Override(effectEnabled);
            }

            return showProps;
        }

        bool DrawParamToggle(string label, ParameterOverride param)
        {
            bool prev = param.overrideState;
			param.overrideState = GUILayout.Toggle(param.overrideState, label.Localize(), GUILayout.Width(200), GUILayout.Height(22));
            if (param.overrideState != prev)
            {
                TexturesUnlimitedFXLoader.INSTANCE.RefreshCameras();
            }
            return param.overrideState;
		}

		private void AddEnumParameter<Tenum>(string label, ParameterOverride<Tenum> param)
        {
            Tenum value = param.value;
            Type type = value.GetType();
            Tenum[] values = (Tenum[])Enum.GetValues(type);
            int index = values.IndexOf(value);

            GUILayout.BeginHorizontal();
			if (DrawParamToggle(label, param))
            {
                if (GUILayout.Button("<", GUILayout.Width(110)))
                {
                    index--;
                    if (index < 0) { index = values.Length - 1; }
                    param.Override(values[index]);
                    TexturesUnlimitedFXLoader.INSTANCE.RefreshCameras();
                }
                GUILayout.Label(value.ToString(), GUILayout.Width(220));
                if (GUILayout.Button(">", GUILayout.Width(110)))
                {
                    index++;
                    if (index >= values.Length) { index = 0; }
                    param.Override(values[index]);
                    TexturesUnlimitedFXLoader.INSTANCE.RefreshCameras();
                }
            }
            
            GUILayout.EndHorizontal();
        }

        private bool AddEnumField<Tenum>(string label, ref Tenum param)
        {
            Tenum value = param;
            Type type = value.GetType();
            Tenum[] values = (Tenum[])Enum.GetValues(type);
            int index = values.IndexOf(value);
            GUILayout.BeginHorizontal();
            GUILayout.Label(label.Localize(), GUILayout.Width(300));
            bool changed = false;
            if (GUILayout.Button("<", GUILayout.Width(110)))
            {
                index--;
                if (index < 0) { index = values.Length - 1; }
                param = (values[index]);
                changed = true;
            }
            GUILayout.Label(value.ToString(), GUILayout.Width(220));
            if (GUILayout.Button(">", GUILayout.Width(110)))
            {
                index++;
                if (index >= values.Length) { index = 0; }
                param = (values[index]);
                changed = true;
            }
            GUILayout.EndHorizontal();
            return changed;
        }

        private void AddBoolParameter(string label, ParameterOverride<bool> param)
        {
            GUILayout.BeginHorizontal();

            if (DrawParamToggle(label, param))
            {
                if (GUILayout.Button(param.value.ToString(), GUILayout.Width(110)))
                {
                    param.Override(!param.value);
                    TexturesUnlimitedFXLoader.INSTANCE.RefreshCameras();
                }
            }
            GUILayout.EndHorizontal();
        }

        private void AddIntParameter(string label, ParameterOverride<int> param, int min, int max)
        {
            GUILayout.BeginHorizontal();
            if (DrawParamToggle(label, param))
            {
                string hash = param.GetHashCode().ToString();
                string textValue = string.Empty;
                if (propertyStringStorage.ContainsKey(hash))
                {
                    textValue = propertyStringStorage[hash];
                }
                else
                {
                    textValue = param.value.ToString();
                    propertyStringStorage.Add(hash, textValue);
                }
                string newValue = GUILayout.TextArea(textValue, GUILayout.Width(110));
                if (newValue != textValue)
                {
                    textValue = newValue;
                    if (int.TryParse(textValue, out int v))
                    {
                        param.Override(v);
                        TexturesUnlimitedFXLoader.INSTANCE.RefreshCameras();
                    }
                    propertyStringStorage[hash] = textValue;
                }
                if (!propertyFloatStorage.TryGetValue(hash, out float sliderValue))
                {
                    sliderValue = param.value;
                    propertyFloatStorage[hash] = sliderValue;
                }
                float sliderValue2 = GUILayout.HorizontalSlider(sliderValue, min, max, GUILayout.Width(330));
                if (sliderValue2 != sliderValue)
                {
                    param.Override((int)sliderValue2);
                    textValue = ((int)sliderValue2).ToString();
                    propertyStringStorage[hash] = textValue;
                    propertyFloatStorage[hash] = sliderValue2;
                    TexturesUnlimitedFXLoader.INSTANCE.RefreshCameras();
                }
            }
            GUILayout.EndHorizontal();
        }

        private void AddFloatField(string label, ref float value, float min, float max, Action onChange = null)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(180), GUILayout.Height(22));

            if (propertyStringStorage.ContainsKey(label))
            {
                string oldValue = propertyStringStorage[label];
                string newValue = GUILayout.TextArea(oldValue, GUILayout.Width(60));
                if (newValue != oldValue)
                {
                    propertyStringStorage[label] = newValue;
                    if (float.TryParse(newValue, out float v))
                    {
                        value = v;
                        onChange?.Invoke();
                    }
                }
            }
            else
            {
                propertyStringStorage.Add(label, value.ToString("F2"));
            }

            float sliderVal = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(250));
            if (Math.Abs(sliderVal - value) > 0.0005f)
            {
                value = sliderVal;
                propertyStringStorage[label] = value.ToString("F2");
                onChange?.Invoke();
            }

            GUILayout.EndHorizontal();
        }

        private void AddFloatParameter(string label, ParameterOverride<float> param, float min, float max)
        {
            GUILayout.BeginHorizontal();
            if (DrawParamToggle(label, param))
            {
                string hash = param.GetHashCode().ToString();
                string textValue = string.Empty;
                if (propertyStringStorage.ContainsKey(hash))
                {
                    textValue = propertyStringStorage[hash];
                }
                else
                {
                    textValue = param.value.ToString();
                    propertyStringStorage.Add(hash, textValue);
                }
                string newValue = GUILayout.TextArea(textValue, GUILayout.Width(110));
                if (newValue != textValue)
                {
                    textValue = newValue;
                    if (float.TryParse(textValue, out float v))
                    {
                        param.Override(v);
                        TexturesUnlimitedFXLoader.INSTANCE.RefreshCameras();
                    }
                    propertyStringStorage[hash] = textValue;
                }
                float sliderValue = GUILayout.HorizontalSlider(param.value, min, max, GUILayout.Width(330));
                if (sliderValue != param.value)
                {
                    param.Override(sliderValue);
                    textValue = sliderValue.ToString();
                    propertyStringStorage[hash] = textValue;
                    TexturesUnlimitedFXLoader.INSTANCE.RefreshCameras();
                }
            }
            GUILayout.EndHorizontal();
        }

        private void AddColorParameter(string label, ParameterOverride<Color> param)
        {
            GUILayout.BeginHorizontal();
            if (DrawParamToggle(label, param))
            {
                string hash = param.GetHashCode().ToString();
                AddColorInput("Red".Localize(), hash, ref param.value.r);
                AddColorInput("Green".Localize(), hash, ref param.value.g);
                AddColorInput("Blue".Localize(), hash, ref param.value.b);
                AddColorInput("Alpha".Localize(), hash, ref param.value.a);
            }
            GUILayout.EndHorizontal();
        }

        private void AddColorInput(string name, string hash, ref float val)
        {
            string key = hash + name;
            string curTextVal = string.Empty;
            if (propertyStringStorage.ContainsKey(key))
            {
                curTextVal = propertyStringStorage[key];
            }
            else
            {
                curTextVal = val.ToString();
                propertyStringStorage.Add(key, curTextVal);
            }
            string newValue = GUILayout.TextArea(curTextVal, GUILayout.Width(110));
            if (newValue != curTextVal)
            {
                curTextVal = newValue;
                if (float.TryParse(curTextVal, out float v))
                {
                    val = v;
                    TexturesUnlimitedFXLoader.INSTANCE.RefreshCameras();
                }
                propertyStringStorage[key] = curTextVal;
            }
        }

        private void AddVector2Parameter(string label, ParameterOverride<Vector2> param)
        {
            GUILayout.BeginHorizontal();
            if (DrawParamToggle(label, param))
            {
                string hash = param.GetHashCode().ToString();
                AddColorInput("X", hash, ref param.value.x);
                AddColorInput("Y", hash, ref param.value.y);
            }
            GUILayout.EndHorizontal();
        }

        private void AddVector4Parameter(string label, ParameterOverride<Vector4> param)
        {
            GUILayout.BeginHorizontal();
            if (DrawParamToggle(label, param))
            {
                string hash = param.GetHashCode().ToString();
                AddColorInput("X", hash, ref param.value.x);
                AddColorInput("Y", hash, ref param.value.y);
                AddColorInput("Z", hash, ref param.value.z);
                AddColorInput("W", hash, ref param.value.w);
            }
            GUILayout.EndHorizontal();
        }

        private void AddSplineParameter(string label, ParameterOverride<Spline> param)//TODO spine parameter configuration rendering
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("TODO - Spline parameter.");
            GUILayout.EndHorizontal();
        }

        private void AddTextureParameter(string label, ParameterOverride<Texture> param, string effect, string paramName)
        {
            GUILayout.BeginHorizontal();
            if (DrawParamToggle(label, param))
            {
                string texLabel = param.value == null ? "Nothing selected".Localize() : param.value.name;
                if (GUILayout.Button(texLabel, GUILayout.Width(440)))
                {
                    this.selectionMode = GUIMode.SelectTexture;
                    this.texScrollPos = new Vector2();
                    Action<Texture2D> update = (a) => 
                    {
                        param.Override(a);
                    };
                    initializeTextureSelectMode(effect, paramName, texLabel, update);
                }
            }
            GUILayout.EndHorizontal();
        }

        #endregion

    }

}
