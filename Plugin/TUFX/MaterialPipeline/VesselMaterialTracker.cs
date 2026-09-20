using System;
using System.Collections.Generic;
using UnityEngine;

namespace TUFX.MaterialPipeline
{
    public class VesselMaterialScanResult
    {
        public Vessel ScannedVessel;
        public int PartCount;
        public int RendererCount;
        public int MaterialCount;
        public int UniqueTextureCount;
        public List<PartMaterialInfo> PartList = new List<PartMaterialInfo>();
    }

    public class PartMaterialInfo
    {
        public Part VesselPart;
        public List<Renderer> Renderers = new List<Renderer>();
        public List<Material> Materials = new List<Material>();
        public List<Texture> Textures = new List<Texture>();
    }

    /// <summary>
    /// Tracks active vessel parts, renderers, and materials.
    /// Responds to vessel load and stage separation events.
    /// </summary>
    public class VesselMaterialTracker
    {
        public event Action<VesselMaterialScanResult> OnVesselScanned;

        public VesselMaterialScanResult LastScan { get; private set; }

        public void Initialize()
        {
            GameEvents.onVesselLoaded.Add(OnVesselLoaded);
            GameEvents.onVesselSwitching.Add(OnVesselSwitching);
            GameEvents.onVesselChange.Add(OnVesselChange);
            GameEvents.onPartAttach.Add(OnPartModified);
            GameEvents.onPartRemove.Add(OnPartModified);
        }

        public void Destroy()
        {
            GameEvents.onVesselLoaded.Remove(OnVesselLoaded);
            GameEvents.onVesselSwitching.Remove(OnVesselSwitching);
            GameEvents.onVesselChange.Remove(OnVesselChange);
            GameEvents.onPartAttach.Remove(OnPartModified);
            GameEvents.onPartRemove.Remove(OnPartModified);
        }

        private void OnVesselLoaded(Vessel v)
        {
            if (v != null && v == FlightGlobals.ActiveVessel)
            {
                ScanActiveVessel();
            }
        }

        private void OnVesselSwitching(Vessel from, Vessel to)
        {
            ScanVessel(to);
        }

        private void OnVesselChange(Vessel v)
        {
            ScanVessel(v);
        }

        private void OnPartModified(GameEvents.HostTargetAction<Part, Part> action)
        {
            ScanActiveVessel();
        }

        public VesselMaterialScanResult ScanActiveVessel()
        {
            return ScanVessel(FlightGlobals.ActiveVessel);
        }

        public VesselMaterialScanResult ScanVessel(Vessel v)
        {
            var result = new VesselMaterialScanResult
            {
                ScannedVessel = v
            };

            if (v == null || v.parts == null)
            {
                LastScan = result;
                return result;
            }

            result.PartCount = v.parts.Count;
            HashSet<Texture> uniqueTextures = new HashSet<Texture>();
            HashSet<Material> uniqueMaterials = new HashSet<Material>();

            for (int p = 0; p < v.parts.Count; p++)
            {
                Part part = v.parts[p];
                if (part == null) continue;

                var pInfo = new PartMaterialInfo { VesselPart = part };
                var renderers = part.GetComponentsInChildren<Renderer>(true);

                for (int r = 0; r < renderers.Length; r++)
                {
                    Renderer rend = renderers[r];
                    if (rend == null || !rend.enabled) continue;

                    // Skip particle systems, trail, and line renderers
                    if (rend is TrailRenderer || rend is LineRenderer || rend.GetType().Name.Contains("Particle")) continue;

                    pInfo.Renderers.Add(rend);
                    result.RendererCount++;

                    // Use sharedMaterials during scan to inspect without instantiating
                    Material[] mats = rend.sharedMaterials;
                    for (int m = 0; m < mats.Length; m++)
                    {
                        Material mat = mats[m];
                        if (mat == null) continue;

                        pInfo.Materials.Add(mat);
                        uniqueMaterials.Add(mat);

                        if (mat.HasProperty("_MainTex"))
                        {
                            Texture t = mat.GetTexture("_MainTex");
                            if (t != null)
                            {
                                pInfo.Textures.Add(t);
                                uniqueTextures.Add(t);
                            }
                        }
                    }
                }

                result.PartList.Add(pInfo);
            }

            result.MaterialCount = uniqueMaterials.Count;
            result.UniqueTextureCount = uniqueTextures.Count;
            LastScan = result;

            OnVesselScanned?.Invoke(result);
            return result;
        }
    }
}
