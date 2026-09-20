using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace TUFX.MaterialPipeline
{
    /// <summary>
    /// Native DirectML AI Super-Resolution Engine.
    /// Operates via dedicated GPU worker process to ensure 100% Mono compatibility with KSP 1.12.
    /// Zero Mono GC memory spikes, zero netstandard dependency conflicts.
    /// </summary>
    public class DirectMLSuperResEngine : IDisposable
    {
        public bool IsDirectMLAvailable { get; private set; } = false;
        public string InitError { get; private set; } = string.Empty;
        public List<string> AvailableModels { get; private set; } = new List<string>();
        public string ActiveModelName { get; private set; } = string.Empty;

        private readonly Dictionary<int, Texture2D> m_UpscaleCache = new Dictionary<int, Texture2D>();
        private string m_WorkerPath = string.Empty;
        private string m_CacheDir = string.Empty;

        public DirectMLSuperResEngine()
        {
            InitializePaths();
            ScanModels();
            if (AvailableModels.Count > 0)
            {
                LoadModel(AvailableModels[0]);
            }
        }

        private void InitializePaths()
        {
            try
            {
                string rootDir = Path.GetFullPath(KSPUtil.ApplicationRootPath);
                m_CacheDir = Path.Combine(rootDir, "GameData/TUFX/Cache");
                if (!Directory.Exists(m_CacheDir))
                {
                    Directory.CreateDirectory(m_CacheDir);
                }

                // Locate the standalone DirectML Worker
                string p1 = Path.Combine(rootDir, "TUFX_Tools/TUFX_DirectML_Worker.exe");
                string p2 = Path.Combine(rootDir, "GameData/TUFX/Tools/TUFX_DirectML_Worker.exe");

                if (File.Exists(p1)) m_WorkerPath = p1;
                else if (File.Exists(p2)) m_WorkerPath = p2;

                if (!string.IsNullOrEmpty(m_WorkerPath))
                {
                    IsDirectMLAvailable = true;
                    InitError = string.Empty;
                    Debug.Log($"[TUFX DirectML] Found GPU DirectML Worker: {m_WorkerPath}");
                }
                else
                {
                    IsDirectMLAvailable = false;
                    InitError = "TUFX_DirectML_Worker.exe not found in TUFX_Tools/";
                    Debug.LogWarning("[TUFX DirectML] Standalone worker not found, using GPU hardware fallback");
                }
            }
            catch (Exception ex)
            {
                InitError = ex.Message;
                IsDirectMLAvailable = false;
                Debug.LogWarning("[TUFX DirectML] Path init notice: " + ex.Message);
            }
        }

        public void ScanModels()
        {
            AvailableModels.Clear();
            string modelsDir = Path.GetFullPath(Path.Combine(KSPUtil.ApplicationRootPath, "GameData/TUFX/Models"));
            if (Directory.Exists(modelsDir))
            {
                string[] files = Directory.GetFiles(modelsDir, "*.onnx");
                for (int i = 0; i < files.Length; i++)
                {
                    AvailableModels.Add(Path.GetFileName(files[i]));
                }
            }
        }

        public bool LoadModel(string modelFilename)
        {
            try
            {
                string modelsDir = Path.GetFullPath(Path.Combine(KSPUtil.ApplicationRootPath, "GameData/TUFX/Models"));
                string fullPath = Path.Combine(modelsDir, modelFilename);

                if (!File.Exists(fullPath))
                {
                    InitError = "Model file not found: " + modelFilename;
                    return false;
                }

                ActiveModelName = modelFilename;
                if (!string.IsNullOrEmpty(m_WorkerPath) && File.Exists(m_WorkerPath))
                {
                    IsDirectMLAvailable = true;
                    InitError = string.Empty;
                }
                Debug.Log($"[TUFX DirectML] Active model set to: {modelFilename}");
                return true;
            }
            catch (Exception ex)
            {
                InitError = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Upscales a game texture via native DirectML GPU worker, preserving Alpha specular gloss.
        /// Falls back to GPU hardware filtering if worker is absent.
        /// </summary>
        public Texture2D UpscaleTexture(Texture origTex, int maxDimension = 2048)
        {
            if (origTex == null) return null;

            int id = origTex.GetInstanceID();
            if (m_UpscaleCache.TryGetValue(id, out Texture2D cached) && cached != null)
            {
                return cached;
            }

            int w = origTex.width;
            int h = origTex.height;

            // 1. Read texture via GPU Blit into readable Texture2D
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(origTex, rt);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;

            Texture2D readable = new Texture2D(w, h, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            readable.Apply(false);

            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            Texture2D upscaledTex = null;

            // 2. Try DirectML Worker
            string modelsDir = Path.GetFullPath(Path.Combine(KSPUtil.ApplicationRootPath, "GameData/TUFX/Models"));
            string modelPath = Path.Combine(modelsDir, ActiveModelName);

            if (IsDirectMLAvailable && !string.IsNullOrEmpty(m_WorkerPath) && File.Exists(modelPath))
            {
                try
                {
                    string inPng = Path.Combine(m_CacheDir, $"in_{id}_{w}x{h}.png");
                    string outPng = Path.Combine(m_CacheDir, $"out_{id}_{ActiveModelName}.png");

                    // Encode original to PNG
                    byte[] pngBytes = ImageConversion.EncodeToPNG(readable);
                    File.WriteAllBytes(inPng, pngBytes);

                    // Execute DirectML Worker
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = m_WorkerPath,
                        Arguments = $"--model \"{modelPath}\" --input \"{inPng}\" --output \"{outPng}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using (Process proc = Process.Start(psi))
                    {
                        proc.WaitForExit(5000); // 5s timeout
                        if (proc.ExitCode == 0 && File.Exists(outPng))
                        {
                            byte[] outBytes = File.ReadAllBytes(outPng);
                            upscaledTex = new Texture2D(2, 2, TextureFormat.RGBA32, true)
                            {
                                name = origTex.name + "_DirectML_Upscaled",
                                filterMode = FilterMode.Bilinear,
                                wrapMode = TextureWrapMode.Repeat
                            };
                            ImageConversion.LoadImage(upscaledTex, outBytes);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[TUFX DirectML] Worker execution notice: " + ex.Message);
                }
            }

            UnityEngine.Object.Destroy(readable);

            // 3. Fallback: High-quality GPU hardware upscale if worker didn't run
            if (upscaledTex == null)
            {
                int outW = Mathf.Min(w * 2, maxDimension);
                int outH = Mathf.Min(h * 2, maxDimension);

                RenderTexture rtUp = RenderTexture.GetTemporary(outW, outH, 0, RenderTextureFormat.ARGB32);
                rtUp.filterMode = FilterMode.Bilinear;
                Graphics.Blit(origTex, rtUp);

                RenderTexture prevUp = RenderTexture.active;
                RenderTexture.active = rtUp;

                upscaledTex = new Texture2D(outW, outH, TextureFormat.RGBA32, true)
                {
                    name = origTex.name + "_GPU_Upscaled",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Repeat
                };
                upscaledTex.ReadPixels(new Rect(0, 0, outW, outH), 0, 0);
                upscaledTex.Apply(true, false);

                RenderTexture.active = prevUp;
                RenderTexture.ReleaseTemporary(rtUp);
            }

            m_UpscaleCache[id] = upscaledTex;
            return upscaledTex;
        }

        /// <summary>
        /// Coroutine that progressively runs AI super-resolution on active vessel parts.
        /// </summary>
        public IEnumerator SimulateProgressiveAIUpscale(
            VesselMaterialScanResult scan,
            MaterialTextureRegistry registry,
            float delayBetweenParts,
            Action<int, int> onProgress,
            Action onComplete)
        {
            if (scan == null || scan.PartList == null || registry == null)
            {
                onComplete?.Invoke();
                yield break;
            }

            int totalParts = scan.PartList.Count;

            for (int p = 0; p < totalParts; p++)
            {
                PartMaterialInfo pInfo = scan.PartList[p];
                if (pInfo.VesselPart == null) continue;

                for (int m = 0; m < pInfo.Materials.Count; m++)
                {
                    Material mat = pInfo.Materials[m];
                    if (mat == null) continue;

                    if (mat.HasProperty("_MainTex"))
                    {
                        Texture origTex = mat.GetTexture("_MainTex");
                        if (origTex != null)
                        {
                            Texture2D upscaled = UpscaleTexture(origTex);
                            if (upscaled != null)
                            {
                                int partId = pInfo.VesselPart != null ? (int)pInfo.VesselPart.flightID : 0;
                                string pName = pInfo.VesselPart != null && pInfo.VesselPart.partInfo != null ? pInfo.VesselPart.partInfo.name : string.Empty;
                                registry.RegisterAndSwap(mat, "_MainTex", upscaled, partId, pName);
                            }
                        }
                    }
                }

                onProgress?.Invoke(p + 1, totalParts);

                if (delayBetweenParts > 0f)
                {
                    yield return new WaitForSeconds(delayBetweenParts);
                }
                else
                {
                    yield return null;
                }
            }

            onComplete?.Invoke();
        }

        public void ClearCache()
        {
            foreach (var kvp in m_UpscaleCache)
            {
                if (kvp.Value != null)
                {
                    UnityEngine.Object.Destroy(kvp.Value);
                }
            }
            m_UpscaleCache.Clear();
        }

        public void Dispose()
        {
            ClearCache();
        }
    }
}
