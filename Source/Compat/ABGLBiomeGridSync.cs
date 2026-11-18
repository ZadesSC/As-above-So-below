using System;
using RimWorld;
using Verse;

namespace AsAboveSoBelow;
internal static class ABGLBiomeGridSync
{
    internal static int lastRewritten;
    internal static void Sync(Map map, ABBandMap bands, string why)
    {
        if (map == null || bands == null || !bands.Banded || !ABGuard.On(ABGuard.LevelGen))
        {
            return;
        }

        try
        {
            if (!GeologicalLandformsCompat.BiomeGridEnabled(map) || !GeologicalLandformsCompat.TryBiomeAccessors(map, out var read, out var write))
            {
                return;
            }

            bool flag = GeologicalLandformsCompat.TryEntryAccessors(map, out var entryAt, out var setEntry);
            CellRect cellRect = bands.RectOfBand(bands.surfaceBand);
            int x = map.Size.x;
            int z = map.Size.z;
            int num = 0;
            int num2 = 0;
            for (int i = 0; i < z; i++)
            {
                IntVec3 c = new IntVec3(0, 0, i);
                if (bands.LevelOf(c) == 0)
                {
                    continue;
                }

                bool flag2 = bands.LevelOf(c) > 0;
                for (int j = 0; j < x; j++)
                {
                    IntVec3 intVec = new IntVec3(j, 0, i);
                    BiomeDef biomeDef = ABBandEnv.BiomeOf(map, bands, intVec);
                    if (biomeDef == null || read(intVec) == biomeDef)
                    {
                        continue;
                    }

                    if (flag2 & flag)
                    {
                        IntVec3 intVec2 = bands.Translate(intVec, bands.surfaceBand);
                        if (bands.InGutter(intVec2))
                        {
                            intVec2.z = cellRect.maxZ;
                        }

                        if (intVec2.InBounds(map) && read(intVec2) == biomeDef)
                        {
                            object obj = entryAt(intVec2);
                            if (obj != null)
                            {
                                setEntry(intVec, obj);
                                num2++;
                                continue;
                            }
                        }
                    }

                    write(intVec, biomeDef);
                    num++;
                }
            }

            lastRewritten = num + num2;
            ABLog.Dev("V2: GL biome grid reconciled (" + why + "): " + num2 + " sky cell(s) given the surface entry beneath them, " + num + " cell(s) rewritten to their level's biome.");
        }
        catch (Exception ex)
        {
            Log.ErrorOnce("[As above, So below] V2: GL biome grid reconcile threw (" + why + "): " + ex, 762195906);
        }
    }
}
