using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace TUFX.Performance
{
    public enum PipelineStage
    {
        Opaque,
        BeforeStack,
        BuiltinStack,
        AfterStack,
        FinalPass
    }

    /// <summary>
    /// Represents profile telemetry for a single post-processing effect in the pipeline.
    /// </summary>
    public class EffectProfileEntry
    {
        public string id;
        public string displayName;
        public string tag;
        public PipelineStage stage;
        public int executionOrder;
        public bool isRunning;
        public float lastTimeMs;
        public float avgTimeMs;
        public float peakTimeMs;
        public float relativePercent;
    }

    /// <summary>
    /// High-precision performance profiler and probe telemetry system for TUFX.
    /// Captures execution and command recording time for each effect across all pipeline stages,
    /// ordered strictly by deterministic execution order.
    /// </summary>
    public static class TUFXProfiler
    {
        private static readonly Dictionary<string, EffectProfileEntry> Catalog = new Dictionary<string, EffectProfileEntry>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<EffectProfileEntry> SortedEntries = new List<EffectProfileEntry>();

        public static bool Enabled = true;
        public static float TotalPostProcessTimeMs { get; private set; }
        public static float AvgTotalPostProcessTimeMs { get; private set; }
        public static float Fps { get; private set; }
        public static float FrameTimeMs { get; private set; }
        public static int ActiveEffectsCount { get; private set; }

        private static long frameStartTimestamp;
        private static float fpsAccumulator = 0f;
        private static int fpsFrames = 0;
        private static float fpsTimer = 0f;

        static TUFXProfiler()
        {
            RegisterAllKnownEffects();
        }

        private static void Register(string id, string displayName, string tag, PipelineStage stage, int order)
        {
            var entry = new EffectProfileEntry
            {
                id = id,
                displayName = displayName,
                tag = tag,
                stage = stage,
                executionOrder = order,
                isRunning = false,
                lastTimeMs = 0f,
                avgTimeMs = 0f,
                peakTimeMs = 0f,
                relativePercent = 0f
            };

            Catalog[id] = entry;
            SortedEntries.Add(entry);
        }

        private static void RegisterAllKnownEffects()
        {
            Catalog.Clear();
            SortedEntries.Clear();

            // 1. Opaque & G-Buffer Stage
            Register("AmbientOcclusion", "Ambient Occlusion (Deferred/Opaque)", "#LOC_TUFX_Effect_AmbientOcclusion", PipelineStage.Opaque, 1);
            Register("ScreenSpaceReflections", "Screen Space Reflections (SSR)", "#LOC_TUFX_Effect_ScreenSpaceReflections", PipelineStage.Opaque, 2);
            Register("Fog", "Volumetric / Distance Fog", "#LOC_TUFX_Effect_Fog", PipelineStage.Opaque, 3);

            // 2. BeforeStack Stage (Ordered by execution / sortingPriority)
            Register("TemporalAntialiasing", "Temporal Anti-Aliasing (TAA)", "#LOC_TUFX_Effect_TAA", PipelineStage.BeforeStack, 9);
            Register("SpectralBokeh", "Spectral Bokeh (Chromatic DoF)", "#LOC_TUFX_Effect_SpectralBokeh", PipelineStage.BeforeStack, 10);
            Register("GroundTruthAO", "Ground Truth AO (GTAO)", "#LOC_TUFX_Effect_GTAO", PipelineStage.BeforeStack, 20);
            Register("SSGIEffect", "Screen Space Global Illumination (SSGI)", "#LOC_TUFX_Effect_SSGI", PipelineStage.BeforeStack, 22);
            Register("SubsurfaceScatteringEffect", "Screen Space Subsurface Scattering (SSSS)", "#LOC_TUFX_Effect_SSSS", PipelineStage.BeforeStack, 24);
            Register("CameraMotionBlurEffect", "Camera Motion Blur (Physical Rotary Shutter)", "#LOC_TUFX_Effect_CameraMotionBlur", PipelineStage.BeforeStack, 25);
            Register("ContactShadows", "Screen Space Contact Shadows", "#LOC_TUFX_Effect_ContactShadows", PipelineStage.BeforeStack, 30);
            Register("GodRays", "Screen Space God Rays & Volumetric Shafts", "#LOC_TUFX_Effect_GodRays", PipelineStage.BeforeStack, 40);
            Register("HeatDistortionEffect", "Hypersonic Reentry Heat Distortion", "#LOC_TUFX_Effect_HeatDistortion", PipelineStage.BeforeStack, 50);
            Register("AnamorphicFlare", "Anamorphic Lens Flare & Starburst", "#LOC_TUFX_Effect_AnamorphicFlare", PipelineStage.BeforeStack, 60);
            Register("Halation", "35mm Analog Film Halation", "#LOC_TUFX_Effect_Halation", PipelineStage.BeforeStack, 70);

            // 3. Builtin Stack Stage (Ordered by execution inside RenderBuiltins)
            Register("DepthOfField", "Depth of Field (Optical Bokeh)", "#LOC_TUFX_Effect_DepthOfField", PipelineStage.BuiltinStack, 80);
            Register("MotionBlur", "Motion Blur (Per-Object Vector)", "#LOC_TUFX_Effect_MotionBlur", PipelineStage.BuiltinStack, 82);
            Register("AutoExposure", "Auto Exposure (Eye Adaptation)", "#LOC_TUFX_Effect_AutoExposure", PipelineStage.BuiltinStack, 84);
            Register("LensDistortion", "Lens Distortion", "#LOC_TUFX_Effect_LensDistortion", PipelineStage.BuiltinStack, 86);
            Register("ChromaticAberration", "Chromatic Aberration", "#LOC_TUFX_Effect_ChromaticAberration", PipelineStage.BuiltinStack, 88);
            Register("Bloom", "Bloom & Lens Dirt", "#LOC_TUFX_Effect_Bloom", PipelineStage.BuiltinStack, 90);
            Register("ColorGrading", "Color Grading & Tonemapping", "#LOC_TUFX_Effect_ColorGrading", PipelineStage.BuiltinStack, 92);
            Register("Vignette", "Lens Vignette", "#LOC_TUFX_Effect_Vignette", PipelineStage.BuiltinStack, 94);
            Register("Grain", "Film Grain", "#LOC_TUFX_Effect_Grain", PipelineStage.BuiltinStack, 96);

            // 4. AfterStack Stage (Ordered by sortingPriority)
            Register("ModernTonemapping", "Modern Tonemapping (AgX / ACES / Filmic)", "#LOC_TUFX_Effect_ModernTonemapping", PipelineStage.AfterStack, 100);
            Register("CMAA2Effect", "Intel CMAA 2 (Anti-Aliasing)", "#LOC_TUFX_Effect_CMAA2", PipelineStage.AfterStack, 110);
            Register("EASUUpscaler", "AMD FidelityFX FSR 1.0 (EASU)", "#LOC_TUFX_Effect_FSR", PipelineStage.AfterStack, 115);
            Register("ContrastAdaptiveSharpening", "AMD FidelityFX CAS (Sharpening)", "#LOC_TUFX_Effect_CAS", PipelineStage.AfterStack, 120);

            // 5. Final Pass Stage
            Register("SubpixelMorphologicalAntialiasing", "SMAA / FXAA Antialiasing", "#LOC_TUFX_Effect_SMAAFXAA", PipelineStage.FinalPass, 130);
            Register("Dithering", "Spatial Dithering", "#LOC_TUFX_Effect_Dithering", PipelineStage.FinalPass, 135);

            // Sort permanently by execution order
            SortedEntries.Sort((a, b) => a.executionOrder.CompareTo(b.executionOrder));
        }

        public static void BeginFrame()
        {
            if (!Enabled) return;

            frameStartTimestamp = Stopwatch.GetTimestamp();

            for (int i = 0; i < SortedEntries.Count; i++)
            {
                SortedEntries[i].isRunning = false;
            }

            // Update FPS counter
            fpsTimer += Time.unscaledDeltaTime;
            fpsAccumulator += 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime);
            fpsFrames++;
            if (fpsTimer >= 0.5f)
            {
                Fps = fpsAccumulator / fpsFrames;
                FrameTimeMs = (1000f / Mathf.Max(1f, Fps));
                fpsAccumulator = 0f;
                fpsFrames = 0;
                fpsTimer = 0f;
            }
        }

        public static void RecordSample(string effectName, float ms)
        {
            if (!Enabled) return;

            if (Catalog.TryGetValue(effectName, out EffectProfileEntry entry))
            {
                entry.isRunning = true;
                entry.lastTimeMs = ms;
                // Exponential moving average smoothing (alpha = 0.15)
                entry.avgTimeMs = entry.avgTimeMs <= 0.0001f ? ms : Mathf.Lerp(entry.avgTimeMs, ms, 0.15f);
                if (ms > entry.peakTimeMs)
                {
                    entry.peakTimeMs = ms;
                }
            }
            else
            {
                // Dynamically register if not in catalog
                entry = new EffectProfileEntry
                {
                    id = effectName,
                    displayName = effectName,
                    tag = effectName,
                    stage = PipelineStage.BeforeStack,
                    executionOrder = 999,
                    isRunning = true,
                    lastTimeMs = ms,
                    avgTimeMs = ms,
                    peakTimeMs = ms,
                    relativePercent = 0f
                };
                Catalog[effectName] = entry;
                SortedEntries.Add(entry);
                SortedEntries.Sort((a, b) => a.executionOrder.CompareTo(b.executionOrder));
            }
        }

        public static void EndFrame()
        {
            if (!Enabled) return;

            float currentTotal = 0f;
            int activeCount = 0;

            for (int i = 0; i < SortedEntries.Count; i++)
            {
                var entry = SortedEntries[i];
                if (entry.isRunning)
                {
                    currentTotal += entry.avgTimeMs;
                    activeCount++;
                }
                else
                {
                    entry.lastTimeMs = 0f;
                    // Decay inactive entries slowly so they don't pop abruptly
                    entry.avgTimeMs = Mathf.Lerp(entry.avgTimeMs, 0f, 0.08f);
                }
            }

            TotalPostProcessTimeMs = currentTotal;
            AvgTotalPostProcessTimeMs = AvgTotalPostProcessTimeMs <= 0.0001f 
                ? currentTotal 
                : Mathf.Lerp(AvgTotalPostProcessTimeMs, currentTotal, 0.15f);
            ActiveEffectsCount = activeCount;

            // Compute percentage of total time for each effect
            for (int i = 0; i < SortedEntries.Count; i++)
            {
                var entry = SortedEntries[i];
                entry.relativePercent = (AvgTotalPostProcessTimeMs > 0.0001f && entry.isRunning)
                    ? (entry.avgTimeMs / AvgTotalPostProcessTimeMs) * 100f
                    : 0f;
            }
        }

        public static List<EffectProfileEntry> GetSortedEntries()
        {
            return SortedEntries;
        }

        public static void ResetPeaks()
        {
            for (int i = 0; i < SortedEntries.Count; i++)
            {
                SortedEntries[i].peakTimeMs = SortedEntries[i].avgTimeMs;
            }
        }
    }
}
