using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TUFX.MaterialPipeline
{
    /// <summary>
    /// GPU-accelerated texture stamping utility and progressive coroutine simulator.
    /// Safely stamps "DEBUG [AI-SUPERRES]" watermark onto textures without CPU texture readback.
    /// </summary>
    public class DebugTextureStamper
    {
        private Material m_StampMaterial;
        private readonly Dictionary<int, RenderTexture> m_StampedCache = new Dictionary<int, RenderTexture>();

        private Material GetStampMaterial()
        {
            if (m_StampMaterial == null)
            {
                Shader s = (TexturesUnlimitedFXLoader.INSTANCE != null)
                    ? TexturesUnlimitedFXLoader.INSTANCE.getShader("Hidden/TUFX/DebugTextureStamper")
                    : null;
                if (s == null) s = Shader.Find("Hidden/TUFX/DebugTextureStamper");

                if (s != null)
                {
                    m_StampMaterial = new Material(s) { hideFlags = HideFlags.DontSave };
                }
            }
            return m_StampMaterial;
        }

        public RenderTexture StampTexture(Texture origTex, int partIndex, MaterialTextureRegistry registry)
        {
            if (origTex == null) return null;

            int id = origTex.GetInstanceID();
            if (m_StampedCache.TryGetValue(id, out RenderTexture cached) && cached != null && cached.IsCreated())
            {
                return cached;
            }

            Material mat = GetStampMaterial();
            if (mat == null)
            {
                Debug.LogWarning("[TUFX MaterialPipeline] DebugTextureStamper shader not found!");
                return null;
            }

            int w = Mathf.Max(128, origTex.width);
            int h = Mathf.Max(128, origTex.height);

            RenderTexture rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
            {
                name = "TUFX_Stamped_" + origTex.name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat
            };
            rt.Create();

            mat.SetFloat("_PartIndex", (float)partIndex);
            mat.SetFloat("_StampAlpha", 0.85f);

            Graphics.Blit(origTex, rt, mat, 0);

            m_StampedCache[id] = rt;
            if (registry != null)
            {
                registry.TrackRenderTexture(rt);
            }

            return rt;
        }

        public void ClearCache()
        {
            m_StampedCache.Clear();
            if (m_StampMaterial != null)
            {
                UnityEngine.Object.Destroy(m_StampMaterial);
                m_StampMaterial = null;
            }
        }

        /// <summary>
        /// Coroutine that simulates progressive, part-by-part AI super-resolution texture replacement.
        /// Replaces parts with a configurable delay, visually demonstrating the runtime hot-swap.
        /// </summary>
        public IEnumerator SimulateProgressiveUpscale(
            VesselMaterialScanResult scan,
            MaterialTextureRegistry registry,
            float delayPerPart,
            Action<int, int> onProgress,
            Action onComplete)
        {
            if (scan == null || scan.PartList == null || registry == null)
            {
                onComplete?.Invoke();
                yield break;
            }

            int totalParts = scan.PartList.Count;
            for (int i = 0; i < totalParts; i++)
            {
                PartMaterialInfo pInfo = scan.PartList[i];
                if (pInfo.VesselPart == null) continue;

                int partId = (int)pInfo.VesselPart.flightID;
                string partName = pInfo.VesselPart.partInfo != null ? pInfo.VesselPart.partInfo.title : pInfo.VesselPart.name;

                for (int r = 0; r < pInfo.Renderers.Count; r++)
                {
                    Renderer rend = pInfo.Renderers[r];
                    if (rend == null) continue;

                    // Access renderer.materials to instantiate per-part material safely
                    Material[] mats = rend.materials;
                    for (int m = 0; m < mats.Length; m++)
                    {
                        Material mat = mats[m];
                        if (mat == null) continue;

                        if (mat.HasProperty("_MainTex"))
                        {
                            Texture origTex = mat.GetTexture("_MainTex");
                            if (origTex != null)
                            {
                                RenderTexture stamped = StampTexture(origTex, i, registry);
                                if (stamped != null)
                                {
                                    registry.RegisterAndSwap(mat, "_MainTex", stamped, partId, partName);
                                }
                            }
                        }
                    }
                }

                onProgress?.Invoke(i + 1, totalParts);

                if (delayPerPart > 0.001f)
                {
                    yield return new WaitForSeconds(delayPerPart);
                }
                else
                {
                    yield return null;
                }
            }

            onComplete?.Invoke();
        }
    }
}
