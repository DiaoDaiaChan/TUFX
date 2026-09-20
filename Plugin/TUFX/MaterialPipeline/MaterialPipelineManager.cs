using System;
using System.Collections;
using UnityEngine;

namespace TUFX.MaterialPipeline
{
    /// <summary>
    /// Master coordinator for the vessel material pipeline, debug inspector, and progressive AI simulation.
    /// Attached to TexturesUnlimitedFXLoader as a singleton component.
    /// </summary>
    public class MaterialPipelineManager : MonoBehaviour
    {
        public static MaterialPipelineManager Instance { get; private set; }

        public VesselMaterialTracker Tracker { get; private set; }
        public MaterialTextureRegistry Registry { get; private set; }
        public DebugTextureStamper Stamper { get; private set; }
        public DirectMLSuperResEngine SuperResEngine { get; private set; }

        public bool IsHookActive { get; set; } = false;
        public float ProgressiveDelay { get; set; } = 0.10f;
        public string StatusMessage { get; private set; } = "Ready";
        public int CurrentProgress { get; private set; } = 0;
        public int TotalProgress { get; private set; } = 0;
        public bool IsProcessing { get; private set; } = false;

        private Coroutine m_ActiveCoroutine;

        public static void EnsureInstance(GameObject host)
        {
            if (Instance == null && host != null)
            {
                Instance = host.GetComponent<MaterialPipelineManager>();
                if (Instance == null)
                {
                    Instance = host.AddComponent<MaterialPipelineManager>();
                }
            }
        }

        private void Awake()
        {
            Instance = this;
            Tracker = new VesselMaterialTracker();
            Registry = new MaterialTextureRegistry();
            Stamper = new DebugTextureStamper();
            SuperResEngine = new DirectMLSuperResEngine();

            Tracker.Initialize();
        }

        private void OnDestroy()
        {
            if (Tracker != null)
            {
                Tracker.Destroy();
            }
            RestoreOriginalTextures();
            if (Stamper != null)
            {
                Stamper.ClearCache();
            }
            if (SuperResEngine != null)
            {
                SuperResEngine.Dispose();
                SuperResEngine = null;
            }
            Instance = null;
        }

        public void RescanActiveVessel()
        {
            if (Tracker != null)
            {
                var scan = Tracker.ScanActiveVessel();
                StatusMessage = scan != null && scan.ScannedVessel != null
                    ? string.Format("Scanned {0}: {1} parts, {2} mats, {3} textures.",
                        scan.ScannedVessel.vesselName, scan.PartCount, scan.MaterialCount, scan.UniqueTextureCount)
                    : "No active vessel found.";
            }
        }

        /// <summary>
        /// Starts the progressive texture hooking simulation.
        /// </summary>
        public void StartProgressiveDebugStamp()
        {
            if (IsProcessing)
            {
                Debug.LogWarning("[TUFX MaterialPipeline] Already processing a vessel!");
                return;
            }

            var scan = Tracker.ScanActiveVessel();
            if (scan == null || scan.PartCount == 0)
            {
                StatusMessage = "Cannot start: No active vessel parts found.";
                return;
            }

            IsProcessing = true;
            IsHookActive = true;
            CurrentProgress = 0;
            TotalProgress = scan.PartCount;
            StatusMessage = "Starting progressive AI super-resolution simulation...";

            m_ActiveCoroutine = StartCoroutine(Stamper.SimulateProgressiveUpscale(
                scan,
                Registry,
                ProgressiveDelay,
                (cur, tot) =>
                {
                    CurrentProgress = cur;
                    TotalProgress = tot;
                    StatusMessage = string.Format("Hooking Part {0}/{1}...", cur, tot);
                },
                () =>
                {
                    IsProcessing = false;
                    StatusMessage = string.Format("Complete! Hooked {0} materials across {1} parts.",
                        Registry.OverrideCount, TotalProgress);
                    m_ActiveCoroutine = null;
                }
            ));
        }

        /// <summary>
        /// Starts hardware-accelerated AI super-resolution on active vessel parts using DirectML.
        /// </summary>
        public void StartProgressiveAIUpscale()
        {
            if (IsProcessing)
            {
                Debug.LogWarning("[TUFX MaterialPipeline] Already processing a vessel!");
                return;
            }

            if (SuperResEngine == null || !SuperResEngine.IsDirectMLAvailable)
            {
                StatusMessage = "DirectML not available: " + (SuperResEngine != null ? SuperResEngine.InitError : "Engine Null");
                return;
            }

            var scan = Tracker.ScanActiveVessel();
            if (scan == null || scan.PartCount == 0)
            {
                StatusMessage = "Cannot start: No active vessel parts found.";
                return;
            }

            IsProcessing = true;
            IsHookActive = true;
            CurrentProgress = 0;
            TotalProgress = scan.PartCount;
            StatusMessage = $"Running AI Super-Resolution ({SuperResEngine.ActiveModelName})...";

            m_ActiveCoroutine = StartCoroutine(SuperResEngine.SimulateProgressiveAIUpscale(
                scan,
                Registry,
                ProgressiveDelay,
                (cur, tot) =>
                {
                    CurrentProgress = cur;
                    TotalProgress = tot;
                    StatusMessage = string.Format("AI Upscaling Part {0}/{1}...", cur, tot);
                },
                () =>
                {
                    IsProcessing = false;
                    StatusMessage = string.Format("AI Upscale Complete! Enhanced {0} materials across {1} parts.",
                        Registry.OverrideCount, TotalProgress);
                    m_ActiveCoroutine = null;
                }
            ));
        }

        /// <summary>
        /// Restores all original textures and terminates any running coroutine.
        /// </summary>
        public void RestoreOriginalTextures()
        {
            if (m_ActiveCoroutine != null)
            {
                StopCoroutine(m_ActiveCoroutine);
                m_ActiveCoroutine = null;
            }

            IsProcessing = false;
            IsHookActive = false;
            CurrentProgress = 0;

            if (Registry != null)
            {
                int count = Registry.OverrideCount;
                Registry.RestoreAll();
                StatusMessage = string.Format("Restored {0} materials to original textures.", count);
            }

            if (Stamper != null)
            {
                Stamper.ClearCache();
            }

            if (SuperResEngine != null)
            {
                SuperResEngine.ClearCache();
            }
        }
    }
}
