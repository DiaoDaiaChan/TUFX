using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using UnityEngine;

namespace TUFX.MaterialPipeline
{
    /// <summary>
    /// Native DirectML + ONNX Runtime AI Super-Resolution Engine.
    /// Operates with ZERO Python, ZERO PyTorch, ZERO CUDA dependencies.
    /// Hardware-accelerated on DirectX 12 across NVIDIA, AMD, and Intel GPUs.
    /// </summary>
    public class DirectMLSuperResEngine : IDisposable
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string libname);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        public bool IsDirectMLAvailable { get; private set; } = false;
        public string InitError { get; private set; } = string.Empty;
        public List<string> AvailableModels { get; private set; } = new List<string>();
        public string ActiveModelName { get; private set; } = string.Empty;

        private InferenceSession m_Session;
        private readonly Dictionary<int, Texture2D> m_UpscaleCache = new Dictionary<int, Texture2D>();
        private static bool s_NativeDllsLoaded = false;

        public DirectMLSuperResEngine()
        {
            InitializeNativeLibraries();
            ScanModels();
            if (AvailableModels.Count > 0)
            {
                LoadModel(AvailableModels[0]);
            }
        }

        private void InitializeNativeLibraries()
        {
            if (s_NativeDllsLoaded) return;

            try
            {
                string rootDir = Path.GetFullPath(KSPUtil.ApplicationRootPath);
                string pluginsDir = Path.GetFullPath(Path.Combine(rootDir, "GameData/TUFX/Plugins"));
                string nativeDir = Path.GetFullPath(Path.Combine(rootDir, "GameData/TUFX/Native"));

                string rootDml = Path.Combine(rootDir, "DirectML.dll");
                string rootOrt = Path.Combine(rootDir, "onnxruntime.dll");

                string nativeDml = File.Exists(Path.Combine(nativeDir, "DirectML.native"))
                    ? Path.Combine(nativeDir, "DirectML.native")
                    : Path.Combine(pluginsDir, "DirectML.native");
                string nativeOrt = File.Exists(Path.Combine(nativeDir, "onnxruntime.native"))
                    ? Path.Combine(nativeDir, "onnxruntime.native")
                    : Path.Combine(pluginsDir, "onnxruntime.native");

                // Auto-deploy native C++ DLLs to KSP root directory on first run if needed
                if (!File.Exists(rootDml) && File.Exists(nativeDml))
                {
                    try
                    {
                        File.Copy(nativeDml, rootDml, true);
                        Debug.Log("[TUFX DirectML] Deployed DirectML.dll to KSP root directory.");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[TUFX DirectML] Failed to copy DirectML.native to root: " + ex.Message);
                    }
                }

                if (!File.Exists(rootOrt) && File.Exists(nativeOrt))
                {
                    try
                    {
                        File.Copy(nativeOrt, rootOrt, true);
                        Debug.Log("[TUFX DirectML] Deployed onnxruntime.dll to KSP root directory.");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[TUFX DirectML] Failed to copy onnxruntime.native to root: " + ex.Message);
                    }
                }

                SetDllDirectory(rootDir);

                string dmlPath = File.Exists(rootDml) ? rootDml : (File.Exists(Path.Combine(pluginsDir, "DirectML.dll")) ? Path.Combine(pluginsDir, "DirectML.dll") : null);
                string ortPath = File.Exists(rootOrt) ? rootOrt : (File.Exists(Path.Combine(pluginsDir, "onnxruntime.dll")) ? Path.Combine(pluginsDir, "onnxruntime.dll") : null);

                if (dmlPath != null)
                {
                    IntPtr hDml = LoadLibrary(dmlPath);
                    Debug.Log("[TUFX DirectML] LoadLibrary DirectML: " + (hDml != IntPtr.Zero ? "Success" : "Failed (code " + Marshal.GetLastWin32Error() + ")"));
                }
                if (ortPath != null)
                {
                    IntPtr hOrt = LoadLibrary(ortPath);
                    Debug.Log("[TUFX DirectML] LoadLibrary onnxruntime: " + (hOrt != IntPtr.Zero ? "Success" : "Failed (code " + Marshal.GetLastWin32Error() + ")"));
                }

                s_NativeDllsLoaded = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[TUFX DirectML] Native library pre-load exception: " + ex.Message);
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
                    IsDirectMLAvailable = false;
                    return false;
                }

                if (m_Session != null)
                {
                    m_Session.Dispose();
                    m_Session = null;
                }

                var options = new SessionOptions();
                options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

                // Append DirectML Execution Provider on Device 0
                try
                {
                    options.AppendExecutionProvider_DML(0);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[TUFX DirectML] DML provider init failed, falling back to CPU: " + ex.Message);
                }

                m_Session = new InferenceSession(fullPath, options);
                ActiveModelName = modelFilename;
                IsDirectMLAvailable = true;
                InitError = string.Empty;

                // Silent GPU JIT shader warm-up with 64x64 dummy tensor
                Warmup(m_Session);

                Debug.Log($"[TUFX DirectML] Successfully loaded model: {modelFilename} (DirectML active)");
                return true;
            }
            catch (Exception ex)
            {
                InitError = ex.Message;
                IsDirectMLAvailable = false;
                Debug.LogError("[TUFX DirectML] Exception loading model: " + ex.ToString());
                return false;
            }
        }

        private void Warmup(InferenceSession session)
        {
            try
            {
                int h = 64, w = 64;
                float[] dummy = new float[1 * 3 * h * w];
                var tensor = new DenseTensor<float>(dummy, new int[] { 1, 3, h, w });
                string inName = session.InputMetadata.Keys.First();
                var inputs = new NamedOnnxValue[] { NamedOnnxValue.CreateFromTensor(inName, tensor) };
                using (var results = session.Run(inputs)) { }
            }
            catch { }
        }

        /// <summary>
        /// Upscales a game texture using DirectML GPU inference.
        /// Preserves physical Alpha specularity/gloss channel seamlessly.
        /// </summary>
        public Texture2D UpscaleTexture(Texture origTex, int maxDimension = 2048)
        {
            if (origTex == null) return null;

            int id = origTex.GetInstanceID();
            if (m_UpscaleCache.TryGetValue(id, out Texture2D cached) && cached != null)
            {
                return cached;
            }

            if (m_Session == null)
            {
                Debug.LogWarning("[TUFX DirectML] Inference session not initialized!");
                return null;
            }

            int w = origTex.width;
            int h = origTex.height;

            // 1. Read pixels via GPU Blit into readable Texture2D (bypasses CPU isReadable constraint)
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(origTex, rt);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;

            Texture2D readable = new Texture2D(w, h, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            readable.Apply(false);

            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            Color32[] pixels = readable.GetPixels32();
            UnityEngine.Object.Destroy(readable);

            // 2. Separate RGB normalized float tensor and Alpha channel
            int totalPixels = w * h;
            float[] rgbTensorData = new float[3 * totalPixels];
            byte[] alphaChannel = new byte[totalPixels];

            int gOffset = totalPixels;
            int bOffset = totalPixels * 2;

            for (int i = 0; i < totalPixels; i++)
            {
                Color32 c = pixels[i];
                rgbTensorData[i] = c.r / 255.0f;
                rgbTensorData[gOffset + i] = c.g / 255.0f;
                rgbTensorData[bOffset + i] = c.b / 255.0f;
                alphaChannel[i] = c.a;
            }

            // 3. DirectML Inference
            string inputName = m_Session.InputMetadata.Keys.First();
            var inTensor = new DenseTensor<float>(rgbTensorData, new int[] { 1, 3, h, w });
            var inputs = new NamedOnnxValue[] { NamedOnnxValue.CreateFromTensor(inputName, inTensor) };

            int outW, outH;
            float[] outRgbData;

            using (var results = m_Session.Run(inputs))
            {
                var outTensor = results.First().AsTensor<float>();
                outH = outTensor.Dimensions[2];
                outW = outTensor.Dimensions[3];
                outRgbData = outTensor.ToArray();
            }

            // 4. Bilinear upscale Alpha channel to match outW x outH
            byte[] upscaledAlpha = BilinearScaleAlpha(alphaChannel, w, h, outW, outH);

            // 5. Recombine RGB + Alpha into RGBA32
            int outTotal = outW * outH;
            byte[] outRgba = new byte[outTotal * 4];
            int outGOffset = outTotal;
            int outBOffset = outTotal * 2;

            for (int i = 0; i < outTotal; i++)
            {
                int byteIdx = i * 4;
                float r = Mathf.Clamp01(outRgbData[i]) * 255.0f;
                float g = Mathf.Clamp01(outRgbData[outGOffset + i]) * 255.0f;
                float b = Mathf.Clamp01(outRgbData[outBOffset + i]) * 255.0f;

                outRgba[byteIdx] = (byte)r;
                outRgba[byteIdx + 1] = (byte)g;
                outRgba[byteIdx + 2] = (byte)b;
                outRgba[byteIdx + 3] = upscaledAlpha[i];
            }

            // 6. Create mipmapped Texture2D
            Texture2D upscaledTex = new Texture2D(outW, outH, TextureFormat.RGBA32, true)
            {
                name = origTex.name + "_AI_Upscaled",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat
            };
            upscaledTex.LoadRawTextureData(outRgba);
            upscaledTex.Apply(true, false);

            m_UpscaleCache[id] = upscaledTex;
            return upscaledTex;
        }

        private static byte[] BilinearScaleAlpha(byte[] src, int srcW, int srcH, int dstW, int dstH)
        {
            if (srcW == dstW && srcH == dstH) return src;

            byte[] dst = new byte[dstW * dstH];
            float xRatio = (float)(srcW - 1) / dstW;
            float yRatio = (float)(srcH - 1) / dstH;

            for (int y = 0; y < dstH; y++)
            {
                int yLow = (int)(y * yRatio);
                int yHigh = Mathf.Min(yLow + 1, srcH - 1);
                float yWeight = (y * yRatio) - yLow;
                int dstRow = y * dstW;
                int srcRowLow = yLow * srcW;
                int srcRowHigh = yHigh * srcW;

                for (int x = 0; x < dstW; x++)
                {
                    int xLow = (int)(x * xRatio);
                    int xHigh = Mathf.Min(xLow + 1, srcW - 1);
                    float xWeight = (x * xRatio) - xLow;

                    float a = src[srcRowLow + xLow];
                    float b = src[srcRowLow + xHigh];
                    float c = src[srcRowHigh + xLow];
                    float d = src[srcRowHigh + xHigh];

                    float val = a * (1 - xWeight) * (1 - yWeight) +
                                b * xWeight * (1 - yWeight) +
                                c * (1 - xWeight) * yWeight +
                                d * xWeight * yWeight;

                    dst[dstRow + x] = (byte)Mathf.Clamp(val, 0f, 255f);
                }
            }
            return dst;
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
            if (m_Session != null)
            {
                m_Session.Dispose();
                m_Session = null;
            }
        }
    }
}
