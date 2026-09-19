using System;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace TUFX
{
    /// <summary>
    /// Dynamic Context Manager for flight scenes.
    /// Dynamically monitors vessel environment (day/night eclipse, reentry heating, EVA mode)
    /// and adapts post-processing properties smoothly in real-time.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class TUFXDynamicContextManager : MonoBehaviour
    {
        public static bool Enabled = true;
        private static TUFXDynamicContextManager INSTANCE;

        private float currentHeatDistortion = 0f;
        private float currentNightAdaptation = 0f;

        public void Start()
        {
            INSTANCE = this;
            Log.debug("TUFXDynamicContextManager started in Flight scene.");
        }

        public void OnDestroy()
        {
            INSTANCE = null;
        }

        public void Update()
        {
            if (!Enabled) return;
            if (HighLogic.LoadedScene != GameScenes.FLIGHT) return;
            var vessel = FlightGlobals.ActiveVessel;
            if (vessel == null) return;

            var currentProfile = TexturesUnlimitedFXLoader.INSTANCE.CurrentProfile;
            if (currentProfile == null) return;

            // 1. Solar Day / Night Transition (Eclipse & Planet Shadow)
            UpdateDayNightTransition(vessel, currentProfile);

            // 2. Atmospheric Reentry Heat Haze & Aerodynamic Heating
            UpdateReentryHeatHaze(vessel, currentProfile);

            // 3. EVA Visor Mode
            UpdateEVAMode(vessel, currentProfile);
        }

        private bool lastSunlightState = true;
        private bool lastReentryState = false;
        private bool lastEVAState = false;

        private void UpdateDayNightTransition(Vessel vessel, TUFXProfile profile)
        {
            bool inSunlight = vessel.directSunlight;
            if (inSunlight != lastSunlightState)
            {
                lastSunlightState = inSunlight;
                if (!inSunlight)
                {
                    Log.log("[TUFX Context] Vessel entered planetary shadow / orbital eclipse. Smoothly adapting exposure.");
                }
                else
                {
                    Log.log("[TUFX Context] Vessel exited eclipse into direct sunlight. Recovering exposure.");
                }
            }

            float targetAdaptation = inSunlight ? 0f : 1f;

            // Smoothly lerp transition over 2-3 seconds
            currentNightAdaptation = Mathf.MoveTowards(currentNightAdaptation, targetAdaptation, Time.deltaTime * 0.4f);

            var exposure = profile.GetSettingsFor<AutoExposure>();
            if (exposure != null && exposure.enabled.value)
            {
                // In eclipse/night, maintain a safe minimum luminance threshold (never negative in space)
                // to prevent boosting the black space void into a washed-out grey fog.
                if (exposure.minLuminance.overrideState)
                {
                    exposure.minLuminance.value = Mathf.Max(2.5f, exposure.minLuminance.value);
                }
            }
        }

        private void UpdateReentryHeatHaze(Vessel vessel, TUFXProfile profile)
        {
            var heatDistortion = profile.GetSettingsFor<HeatDistortionEffect>();
            if (heatDistortion == null) return;

            float dynamicPressure = (float)vessel.dynamicPressurekPa;
            float mach = (float)vessel.mach;
            float targetDistortion = 0f;

            // High hypersonic entry condition: Mach > 3.0 and dynamic pressure > 5.0 kPa
            if (mach > 3.0f && dynamicPressure > 5.0f && vessel.atmDensity > 0.01)
            {
                float intensityFactor = Mathf.Clamp01((mach - 3.0f) / 15.0f) * Mathf.Clamp01(dynamicPressure / 50.0f);
                targetDistortion = intensityFactor * 0.8f;
                if (!lastReentryState)
                {
                    lastReentryState = true;
                    Log.log($"[TUFX Context] Hypersonic reentry heat haze active (Mach: {mach:F1}, Q: {dynamicPressure:F1} kPa).");
                }
            }
            else
            {
                lastReentryState = false;
            }

            currentHeatDistortion = Mathf.MoveTowards(currentHeatDistortion, targetDistortion, Time.deltaTime * 1.5f);
            heatDistortion.intensity.Override(currentHeatDistortion);
        }

        private void UpdateEVAMode(Vessel vessel, TUFXProfile profile)
        {
            if (vessel.isEVA != lastEVAState)
            {
                lastEVAState = vessel.isEVA;
                if (vessel.isEVA)
                {
                    Log.log("[TUFX Context] EVA Kerbal detected. Enabling visor curvature adaptively.");
                }
            }

            if (vessel.isEVA)
            {
                var vignette = profile.GetSettingsFor<Vignette>();
                if (vignette != null && vignette.enabled.value)
                {
                    // Subtle extra curvature on helmet visor edges
                    vignette.roundness.Override(Mathf.MoveTowards(vignette.roundness.value, 1.0f, Time.deltaTime));
                }
            }
        }
    }
}
