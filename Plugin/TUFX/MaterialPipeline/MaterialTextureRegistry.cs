using System;
using System.Collections.Generic;
using UnityEngine;

namespace TUFX.MaterialPipeline
{
    /// <summary>
    /// Safely tracks original part material textures and manages hot-swapping and rollback.
    /// Guarantees that the original game textures are never corrupted or lost.
    /// </summary>
    public class MaterialTextureRegistry
    {
        public class OverrideEntry
        {
            public Material TargetMaterial;
            public string PropertyName;
            public Texture OriginalTexture;
            public Texture ReplacementTexture;
            public int PartFlightId;
            public string PartName;
        }

        private readonly Dictionary<string, OverrideEntry> m_Overrides = new Dictionary<string, OverrideEntry>();
        private readonly List<RenderTexture> m_AllocatedRenderTextures = new List<RenderTexture>();

        public int OverrideCount => m_Overrides.Count;

        private string MakeKey(Material mat, string prop)
        {
            return (mat != null ? mat.GetInstanceID().ToString() : "null") + "_" + prop;
        }

        /// <summary>
        /// Registers a material's original texture and swaps it with the replacement texture.
        /// </summary>
        public bool RegisterAndSwap(Material mat, string propName, Texture newTex, int partId = 0, string partName = "")
        {
            if (mat == null || newTex == null) return false;
            if (!mat.HasProperty(propName)) return false;

            string key = MakeKey(mat, propName);
            if (!m_Overrides.TryGetValue(key, out OverrideEntry entry))
            {
                Texture origTex = mat.GetTexture(propName);
                entry = new OverrideEntry
                {
                    TargetMaterial = mat,
                    PropertyName = propName,
                    OriginalTexture = origTex,
                    ReplacementTexture = newTex,
                    PartFlightId = partId,
                    PartName = partName
                };
                m_Overrides[key] = entry;
            }
            else
            {
                entry.ReplacementTexture = newTex;
            }

            mat.SetTexture(propName, newTex);
            return true;
        }

        /// <summary>
        /// Restores all hooked materials back to their original textures.
        /// </summary>
        public void RestoreAll()
        {
            foreach (var kvp in m_Overrides)
            {
                OverrideEntry entry = kvp.Value;
                if (entry.TargetMaterial != null && entry.OriginalTexture != null)
                {
                    if (entry.TargetMaterial.HasProperty(entry.PropertyName))
                    {
                        entry.TargetMaterial.SetTexture(entry.PropertyName, entry.OriginalTexture);
                    }
                }
            }
            m_Overrides.Clear();
            ReleaseAllocatedTextures();
        }

        /// <summary>
        /// Tracks dynamically generated RenderTextures to safely release when reverting.
        /// </summary>
        public void TrackRenderTexture(RenderTexture rt)
        {
            if (rt != null && !m_AllocatedRenderTextures.Contains(rt))
            {
                m_AllocatedRenderTextures.Add(rt);
            }
        }

        public void ReleaseAllocatedTextures()
        {
            for (int i = 0; i < m_AllocatedRenderTextures.Count; i++)
            {
                if (m_AllocatedRenderTextures[i] != null)
                {
                    m_AllocatedRenderTextures[i].Release();
                    UnityEngine.Object.Destroy(m_AllocatedRenderTextures[i]);
                }
            }
            m_AllocatedRenderTextures.Clear();
        }

        public bool IsHooked(Material mat, string propName)
        {
            return m_Overrides.ContainsKey(MakeKey(mat, propName));
        }
    }
}
