using System;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace TUFX
{
    /// <summary>
    /// Dynamic Context Manager for flight scenes.
    /// Dynamically monitors the vessel environment (orbital eclipse, hypersonic reentry, hard vacuum, EVA)
    /// and adapts post-processing properties smoothly in real-time.
    ///
    /// Every value this manager takes over is leased: the profile author's configured value is captured on
    /// the first frame the context becomes active and handed back verbatim once the context no longer
    /// applies, so flying through an eclipse never permanently rewrites a saved profile.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class TUFXDynamicContextManager : MonoBehaviour
    {
        public static bool Enabled = true;
        private static TUFXDynamicContextManager INSTANCE;

        private float currentHeatDistortion = 0f;
        private float currentEclipseBlend = 0f;
        private float currentVacuumBlend = 0f;
        private float currentEVABlend = 0f;

        private bool lastSunlightState = true;
        private bool lastReentryState = false;
        private bool lastEVAState = false;
        private bool lastVacuumState = false;

        // Leased parameters -- see ParameterLease below
        private readonly ParameterLease bloomIntensity = new ParameterLease();
        private readonly ParameterLease bloomThreshold = new ParameterLease();
        private readonly ParameterLease exposureMinLuminance = new ParameterLease();
        private readonly ParameterLease halationIntensity = new ParameterLease();
        private readonly ParameterLease godRaysIntensity = new ParameterLease();
        private readonly ParameterLease chromaticAberrationIntensity = new ParameterLease();
        private readonly ParameterLease vignetteRoundness = new ParameterLease();
        private readonly ParameterLease heatDistortionIntensity = new ParameterLease();

        /// <summary>
        /// Takes temporary ownership of a single FloatParameter belonging to the active profile.
        /// Captures the authored value/overrideState on first contact and restores them on release,
        /// so user-authored profiles are never silently mutated by the context manager.
        /// </summary>
        private sealed class ParameterLease
        {
            private FloatParameter parameter;
            private float authoredValue;
            private bool authoredOverrideState;
            private bool captured;

            /// <summary>True while this lease is driving a parameter.</summary>
            public bool IsActive => captured;

            /// <summary>The value the profile author configured, or 0 if nothing has been leased.</summary>
            public float AuthoredValue => captured ? authoredValue : 0f;

            /// <summary>
            /// Binds the lease to the parameter and captures the authored value. Safe to call every frame:
            /// the authored value is captured once per parameter instance, and re-captured automatically if
            /// the user switches to a different profile (which hands us a different parameter instance).
            /// </summary>
            public bool Capture(FloatParameter target)
            {
                if (target == null)
                {
                    Release();
                    return false;
                }

                if (!captured || !ReferenceEquals(parameter, target))
                {
                    // Changing profiles: give the previous parameter back before taking the new one.
                    if (captured)
                        Release();

                    parameter = target;
                    authoredValue = target.value;
                    authoredOverrideState = target.overrideState;
                    captured = true;
                }

                return true;
            }

            /// <summary>Drives the parameter, marking it as overridden so it actually reaches the renderer.</summary>
            public void Drive(float value)
            {
                if (captured && parameter != null)
                    parameter.Override(value);
            }

            /// <summary>Hands the parameter back to the profile exactly as the author left it.</summary>
            public void Release()
            {
                if (captured && parameter != null)
                {
                    parameter.value = authoredValue;
                    parameter.overrideState = authoredOverrideState;
                }

                captured = false;
                parameter = null;
            }
        }

        public void Start()
        {
            INSTANCE = this;
            Log.debug("TUFXDynamicContextManager started in Flight scene.");
        }

        public void OnDestroy()
        {
            ReleaseAllLeases();
            INSTANCE = null;
        }

        public void OnDisable()
        {
            ReleaseAllLeases();
        }

        private void ReleaseAllLeases()
        {
            bloomIntensity.Release();
            bloomThreshold.Release();
            exposureMinLuminance.Release();
            halationIntensity.Release();
            godRaysIntensity.Release();
            chromaticAberrationIntensity.Release();
            vignetteRoundness.Release();
            heatDistortionIntensity.Release();
        }

        public void Update()
        {
            if (!Enabled)
            {
                ReleaseAllLeases();
                currentHeatDistortion = currentEclipseBlend = currentVacuumBlend = currentEVABlend = 0f;
                return;
            }

            if (HighLogic.LoadedScene != GameScenes.FLIGHT) return;
            var vessel = FlightGlobals.ActiveVessel;
            if (vessel == null) return;

            var currentProfile = TexturesUnlimitedFXLoader.INSTANCE.CurrentProfile;
            if (currentProfile == null) return;

            float dt = Time.deltaTime;

            // 1. Solar Day / Night Transition (Eclipse & Planet Shadow)
            UpdateDayNightTransition(vessel, currentProfile, dt);

            // 2. Atmospheric Reentry Heat Haze & Aerodynamic Heating
            UpdateReentryHeatHaze(vessel, currentProfile, dt);

            // 3. Deep Space / Hard Vacuum (suppress atmospheric light haze in a vacuum)
            UpdateVacuumMode(vessel, currentProfile, dt);

            // 4. EVA Visor Mode
            UpdateEVAMode(vessel, currentProfile, dt);
        }

        private void UpdateDayNightTransition(Vessel vessel, TUFXProfile profile, float dt)
        {
            bool inSunlight = vessel.directSunlight;
            if (inSunlight != lastSunlightState)
            {
                lastSunlightState = inSunlight;
                if (!inSunlight)
                {
                    Log.log("[TUFX Context] Vessel entered planetary shadow / orbital eclipse. Tightening bloom and dark-noise floor.");
                }
                else
                {
                    Log.log("[TUFX Context] Vessel exited eclipse into direct sunlight. Restoring authored bloom.");
                }
            }

            float targetBlend = inSunlight ? 0f : 1f;
            currentEclipseBlend = Mathf.MoveTowards(currentEclipseBlend, targetBlend, dt * 0.4f);

            // Bloom: pull the glow back and raise the threshold so only genuinely bright sources bloom.
            // Space has no atmosphere to scatter light back, so the authored atmospheric bloom is too hot
            // once the vessel is in shadow.
            var bloom = profile.GetSettingsFor<Bloom>();
            if (bloom != null && bloom.enabled.value)
            {
                bloomIntensity.Capture(bloom.intensity);
                bloomThreshold.Capture(bloom.threshold);
                float bloomScale = Mathf.Lerp(1f, 0.70f, currentEclipseBlend);
                float thresholdScale = Mathf.Lerp(1f, 1.15f, currentEclipseBlend);
                bloomIntensity.Drive(bloomIntensity.AuthoredValue * bloomScale);
                bloomThreshold.Drive(bloomThreshold.AuthoredValue * thresholdScale);
            }
            else
            {
                bloomIntensity.Release();
                bloomThreshold.Release();
            }

            // Auto exposure: hold a safe minimum luminance floor in shadow. The previous implementation
            // only edited minLuminance when the profile already had an override on that field, which meant
            // the adaptation silently did nothing on every other profile. Drive it through the lease so the
            // override actually reaches the renderer, and so the authored value comes back on exit.
            var exposure = profile.GetSettingsFor<AutoExposure>();
            if (exposure != null && exposure.enabled.value)
            {
                exposureMinLuminance.Capture(exposure.minLuminance);
                float floored = Mathf.Max(2.5f, exposureMinLuminance.AuthoredValue);
                exposureMinLuminance.Drive(Mathf.Lerp(exposureMinLuminance.AuthoredValue, floored, currentEclipseBlend));
            }
            else
            {
                exposureMinLuminance.Release();
            }
        }

        private void UpdateReentryHeatHaze(Vessel vessel, TUFXProfile profile, float dt)
        {
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

            currentHeatDistortion = Mathf.MoveTowards(currentHeatDistortion, targetDistortion, dt * 1.5f);

            var heatDistortion = profile.GetSettingsFor<HeatDistortionEffect>();
            if (heatDistortion != null && heatDistortion.enabled.value)
            {
                heatDistortionIntensity.Capture(heatDistortion.intensity);
                heatDistortionIntensity.Drive(currentHeatDistortion);
            }
            else
            {
                heatDistortionIntensity.Release();
            }

            // Drive the film halation alongside the heat haze, as the plasma sheath glow should bloom
            // onto the film just like a real long-exposure reentry shot.
            float halationBlend = Mathf.Clamp01(currentHeatDistortion / 0.8f);
            var halation = profile.GetSettingsFor<Halation>();
            if (halation != null && halation.enabled.value)
            {
                halationIntensity.Capture(halation.intensity);
                halationIntensity.Drive(halationIntensity.AuthoredValue * Mathf.Lerp(1f, 1.6f, halationBlend));
            }
            else
            {
                halationIntensity.Release();
            }
        }

        private void UpdateVacuumMode(Vessel vessel, TUFXProfile profile, float dt)
        {
            bool inVacuum = vessel.atmDensity <= 0.00001 || !vessel.mainBody.atmosphere;

            if (inVacuum != lastVacuumState)
            {
                lastVacuumState = inVacuum;
                Log.log(inVacuum
                    ? "[TUFX Context] Left the atmosphere: suppressing atmospheric light haze for hard vacuum."
                    : "[TUFX Context] Atmospheric entry: re-enabling atmospheric light haze.");
            }

            currentVacuumBlend = Mathf.MoveTowards(currentVacuumBlend, inVacuum ? 1f : 0f, dt * 0.5f);

            // Keep GodRays responsive to user profile settings across all flight/orbital regimes
            godRaysIntensity.Release();
        }

        private void UpdateEVAMode(Vessel vessel, TUFXProfile profile, float dt)
        {
            if (vessel.isEVA != lastEVAState)
            {
                lastEVAState = vessel.isEVA;
                if (vessel.isEVA)
                {
                    Log.log("[TUFX Context] EVA Kerbal detected. Enabling visor curvature and fringe adaptively.");
                }
                else
                {
                    Log.log("[TUFX Context] EVA ended. Restoring authored lens/vignette values.");
                }
            }

            currentEVABlend = Mathf.MoveTowards(currentEVABlend, vessel.isEVA ? 1f : 0f, dt * 0.8f);

            // Helmet visor: slight edge curvature
            var vignette = profile.GetSettingsFor<Vignette>();
            if (vignette != null && vignette.enabled.value)
            {
                vignetteRoundness.Capture(vignette.roundness);
                vignetteRoundness.Drive(Mathf.Lerp(vignetteRoundness.AuthoredValue, 1.0f, currentEVABlend));
            }
            else
            {
                vignetteRoundness.Release();
            }

            // Helmet visor: the thick curved glass of a real visor fringes the image edges. Only driven when
            // the profile actually carries a Chromatic Aberration effect -- the manager never injects effects
            // into a profile behind the user's back.
            var chromatic = profile.GetSettingsFor<ChromaticAberration>();
            if (chromatic != null && chromatic.enabled.value)
            {
                chromaticAberrationIntensity.Capture(chromatic.intensity);
                float visorFringe = Mathf.Max(chromaticAberrationIntensity.AuthoredValue, 0.15f);
                chromaticAberrationIntensity.Drive(Mathf.Lerp(chromaticAberrationIntensity.AuthoredValue, visorFringe, currentEVABlend));
            }
            else
            {
                chromaticAberrationIntensity.Release();
            }
        }
    }
}
