using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace AsAboveSoBelow;
internal static class ABBelowForeignLayers
{
    private sealed class Proxy
    {
        public int drop;
        public SectionLayer[] layers;
    }

    private sealed class Owner
    {
        public Map map;
        public readonly List<Proxy> proxies = new List<Proxy>();
    }

    private static readonly string[] WantedTypeNames = new string[5]
    {
        "SectionLayer_SubstructureProps",
        "SectionLayer_GravshipHull",
        "SectionLayer_BridgeProps",
        "SectionLayer_TerrainEdges",
        "SectionLayer_GravshipArmorHull"
    };
    private static Type[] wantedTypes;
    private static bool typesResolved;
    private static AccessTools.FieldRef<Section, Map> sectionMap;
    private static bool sectionMapResolved;
    private static readonly Dictionary<Section, Owner> owners = new Dictionary<Section, Owner>();
    private static readonly Dictionary<Map, List<Mesh>> meshesByMap = new Dictionary<Map, List<Mesh>>();
    internal static int proxiesBuilt;
    internal static int piecesCopied;
    private static readonly Color32 White = new Color32(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);
    private static AccessTools.FieldRef<Section, Map> SectionMap
    {
        get
        {
            if (!sectionMapResolved)
            {
                sectionMapResolved = true;
                try
                {
                    sectionMap = AccessTools.FieldRefAccess<Section, Map>("map");
                }
                catch (Exception ex)
                {
                    Log.WarningOnce("[As above, So below] V2: Section.map not found (" + ex.Message + "); the level below is drawn without its hull and props.", 2822338);
                    sectionMap = null;
                }
            }

            return sectionMap;
        }
    }

    private static Type[] Types()
    {
        if (typesResolved)
        {
            return wantedTypes;
        }

        typesResolved = true;
        List<Type> list = new List<Type>();
        try
        {
            foreach (Type item in typeof(SectionLayer).AllSubclassesNonAbstract())
            {
                if (Array.IndexOf(WantedTypeNames, item.Name) >= 0 && item.GetConstructor(new Type[1] { typeof(Section) }) != null)
                {
                    list.Add(item);
                }
            }
        }
        catch (Exception ex)
        {
            Log.WarningOnce("[As above, So below] V2: could not enumerate section layers for the see-below dressing (" + ex.Message + ").", 2822337);
        }

        wantedTypes = list.ToArray();
        return wantedTypes;
    }

    internal static bool Emit(SectionLayer into, Map map, ABBandMap bands, Section section, HashSet<int> drops)
    {
        if (into == null || map == null || bands == null || section == null || drops == null || drops.Count == 0 || !ABGuard.On(ABGuard.Rendering))
        {
            return false;
        }

        Type[] array = Types();
        if (array.Length == 0 || SectionMap == null)
        {
            return false;
        }

        bool flag = false;
        try
        {
            if (!owners.TryGetValue(section, out var value))
            {
                value = new Owner
                {
                    map = map
                };
                owners[section] = value;
            }

            if (value.map != map)
            {
                value.map = map;
                value.proxies.Clear();
            }

            foreach (int drop in drops)
            {
                if (drop <= 0)
                {
                    continue;
                }

                Proxy proxy = null;
                for (int i = 0; i < value.proxies.Count; i++)
                {
                    if (value.proxies[i].drop == drop)
                    {
                        proxy = value.proxies[i];
                        break;
                    }
                }

                if (proxy == null)
                {
                    proxy = Make(map, section, drop, array);
                    if (proxy == null)
                    {
                        continue;
                    }

                    value.proxies.Add(proxy);
                }

                for (int j = 0; j < proxy.layers.Length; j++)
                {
                    SectionLayer sectionLayer = proxy.layers[j];
                    if (sectionLayer == null)
                    {
                        continue;
                    }

                    try
                    {
                        if (!sectionLayer.Visible)
                        {
                            continue;
                        }

                        Revive(map, sectionLayer);
                        sectionLayer.Regenerate();
                    }
                    catch (Exception e)
                    {
                        Log.WarningOnce("[As above, So below] V2: " + sectionLayer.GetType().Name + " threw while drawing the level below; that dressing is skipped.\n" + Describe(e), sectionLayer.GetType().Name.GetHashCode());
                        proxy.layers[j] = null;
                        continue;
                    }

                    List<LayerSubMesh> subMeshes = sectionLayer.subMeshes;
                    for (int k = 0; k < subMeshes.Count; k++)
                    {
                        LayerSubMesh layerSubMesh = subMeshes[k];
                        Remember(map, layerSubMesh);
                        if (layerSubMesh.verts.Count != 0 && !(layerSubMesh.material == null) && !IsMask(layerSubMesh.material))
                        {
                            flag |= Copy(into, map, bands, layerSubMesh, drop);
                            if (layerSubMesh.mesh != null)
                            {
                                layerSubMesh.mesh.Clear();
                            }
                        }
                    }
                }
            }
        }
        catch (Exception e2)
        {
            ABGuard.Disable(ABGuard.Rendering, e2, "V2 see-below dressing layers");
        }

        return flag;
    }

    private static string Describe(Exception e)
    {
        if (e == null)
        {
            return "(null exception)";
        }

        string text = e.GetType().FullName + ": " + e.Message;
        string text2 = e.StackTrace ?? string.Empty;
        if (text2.Length > 900)
        {
            text2 = text2.Substring(0, 900) + " ...";
        }

        if (e.InnerException != null)
        {
            text = text + " <- " + e.InnerException.GetType().FullName + ": " + e.InnerException.Message;
        }

        return text + "\n" + text2;
    }

    private static Proxy Make(Map map, Section section, int drop, Type[] types)
    {
        Section section2;
        try
        {
            section2 = (Section)RuntimeHelpers.GetUninitializedObject(typeof(Section));
            section2.botLeft = new IntVec3(section.botLeft.x, 0, section.botLeft.z - drop);
            SectionMap(section2) = map;
        }
        catch (Exception ex)
        {
            Log.WarningOnce("[As above, So below] V2: could not build a proxy section (" + ex.Message + "); the level below is drawn without its hull and props.", 2822339);
            return null;
        }

        SectionLayer[] array = new SectionLayer[types.Length];
        int num = 0;
        for (int i = 0; i < types.Length; i++)
        {
            try
            {
                array[i] = (SectionLayer)Activator.CreateInstance(types[i], section2);
                num++;
            }
            catch (Exception ex2)
            {
                Log.WarningOnce("[As above, So below] V2: " + types[i].Name + " could not be built on a proxy section (" + ex2.Message + "); that dressing is skipped below.", types[i].Name.GetHashCode() ^ 0x5A5A);
                array[i] = null;
            }
        }

        if (num == 0)
        {
            return null;
        }

        proxiesBuilt++;
        return new Proxy
        {
            drop = drop,
            layers = array
        };
    }

    private static bool IsMask(Material m)
    {
        Shader shader = m.shader;
        if (shader == null)
        {
            return false;
        }

        return shader == ShaderDatabase.GravshipMaskMasked || shader == ShaderDatabase.IndoorMaskMasked;
    }

    private static void Revive(Map map, SectionLayer layer)
    {
        List<LayerSubMesh> subMeshes = layer.subMeshes;
        for (int i = 0; i < subMeshes.Count; i++)
        {
            LayerSubMesh layerSubMesh = subMeshes[i];
            if (layerSubMesh.mesh == null)
            {
                layerSubMesh.mesh = new Mesh();
                layerSubMesh.finalized = false;
                Remember(map, layerSubMesh);
            }
        }
    }

    private static void Remember(Map map, LayerSubMesh sm)
    {
        if (!(sm?.mesh == null))
        {
            if (!meshesByMap.TryGetValue(map, out var value))
            {
                value = new List<Mesh>();
                meshesByMap[map] = value;
            }

            if (!value.Contains(sm.mesh))
            {
                value.Add(sm.mesh);
            }
        }
    }

    internal static void ReleaseAll(Map map)
    {
        if (map == null)
        {
            return;
        }

        List<Section> list = null;
        foreach (KeyValuePair<Section, Owner> owner in owners)
        {
            if (owner.Value.map == map)
            {
                (list ?? (list = new List<Section>())).Add(owner.Key);
            }
        }

        if (list != null)
        {
            for (int i = 0; i < list.Count; i++)
            {
                owners.Remove(list[i]);
            }
        }

        if (!meshesByMap.TryGetValue(map, out var value))
        {
            return;
        }

        for (int j = 0; j < value.Count; j++)
        {
            if (value[j] != null)
            {
                UnityEngine.Object.Destroy(value[j]);
            }
        }

        value.Clear();
        meshesByMap.Remove(map);
    }

    private static bool Copy(SectionLayer into, Map map, ABBandMap bands, LayerSubMesh sm, int drop)
    {
        LayerSubMesh subMesh = into.GetSubMesh(sm.material);
        if (subMesh == null)
        {
            return false;
        }

        int count = sm.verts.Count;
        bool flag = sm.colors.Count == count;
        bool flag2 = sm.uvs.Count == count;
        bool useColors = flag || subMesh.colors.Count > 0;
        bool useUvs = flag2 || subMesh.uvs.Count > 0;
        Pad(subMesh, useColors, useUvs);
        bool flag3 = count % 4 == 0 && sm.tris.Count == count / 4 * 6;
        Vector3 lift = new Vector3(0f, 0f, drop);
        bool result = false;
        if (!flag3)
        {
            int count2 = subMesh.verts.Count;
            for (int i = 0; i < count; i++)
            {
                AddVert(subMesh, sm, i, lift, useColors, flag, useUvs, flag2);
            }

            for (int j = 0; j < sm.tris.Count; j++)
            {
                subMesh.tris.Add(sm.tris[j] + count2);
            }

            return count > 0;
        }

        for (int k = 0; k < count; k += 4)
        {
            Vector3 vector = (sm.verts[k] + sm.verts[k + 1] + sm.verts[k + 2] + sm.verts[k + 3]) * 0.25f;
            IntVec3 c = new IntVec3(Mathf.FloorToInt(vector.x), 0, Mathf.FloorToInt(vector.z));
            IntVec3 intVec = new IntVec3(c.x, 0, c.z + drop);
            if (c.InBounds(map) && intVec.InBounds(map) && ABBands.TryResolveVisibleFrom(map, bands, intVec, requireUnfogged: false, out var below, out var _) && bands.BandOf(below) == bands.BandOf(c))
            {
                int count3 = subMesh.verts.Count;
                for (int l = 0; l < 4; l++)
                {
                    AddVert(subMesh, sm, k + l, lift, useColors, flag, useUvs, flag2);
                }

                int num = k / 4 * 6;
                for (int m = 0; m < 6; m++)
                {
                    subMesh.tris.Add(sm.tris[num + m] - k + count3);
                }

                result = true;
                piecesCopied++;
            }
        }

        return result;
    }

    private static void AddVert(LayerSubMesh mine, LayerSubMesh sm, int i, Vector3 lift, bool useColors, bool srcColors, bool useUvs, bool srcUvs)
    {
        mine.verts.Add(sm.verts[i] + lift);
        if (useColors)
        {
            mine.colors.Add(srcColors ? sm.colors[i] : White);
        }

        if (useUvs)
        {
            mine.uvs.Add(srcUvs ? sm.uvs[i] : Vector3.zero);
        }
    }

    private static void Pad(LayerSubMesh mine, bool useColors, bool useUvs)
    {
        int count = mine.verts.Count;
        if (useColors)
        {
            while (mine.colors.Count < count)
            {
                mine.colors.Add(White);
            }
        }

        if (useUvs)
        {
            while (mine.uvs.Count < count)
            {
                mine.uvs.Add(Vector3.zero);
            }
        }
    }
}
