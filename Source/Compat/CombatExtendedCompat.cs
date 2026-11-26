using System.Reflection.Emit;
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace AsAboveSoBelow
{
    /// <summary>
    /// COMBAT EXTENDED, CROSS-LEVEL: firing through openings under CE's ballistics, plus the
    /// HIGH-GROUND MODEL the user specified - shooters above their target get suppression
    /// resistance and tighter aim; pawns below fire from above get suppressed harder.
    ///
    /// ⚠ WHY THE VANILLA BRIDGE DOES NOTHING UNDER CE. CE replaces the whole layer: verbs
    /// derive from Verb_LaunchProjectileCE with their OWN shot-line method
    /// (TryFindCEShootLineFromTo - virtual, never calls Verb.TryFindShootLineFromTo), their
    /// own accuracy model (ShiftVecReport, not ShotReport), and their own projectile
    /// (ProjectileCE : ThingWithComps - NOT Verse.Projectile - flying a simulated ballistic
    /// arc with real collision checks along its 2D path). Every §41 patch keys on the vanilla
    /// types, so under CE cross-level combat was simply inert: raw distance made every
    /// cross-band pair "out of range" and nobody ever fired.
    ///
    /// ⚠⚠ AND CE'S PROJECTILE MUST NEVER FLY THE RAW PATH. A vanilla projectile is a visual
    /// that impacts at a precomputed destination; a CE round COLLIDES with what it crosses.
    /// Launched raw across bands it would fly a Slot of gutter and foreign terrain,
    /// suppressing and hitting things on levels the shot never touched in fiction. So the
    /// same three-step shape as §41, at CE's seams:
    ///   1. LEGALITY at TryFindCEShootLineFromTo (permit via ABShaft, DENY the rest - the
    ///      §41c rule; CE's own code would otherwise decide on raw range),
    ///   2. AIM SPACE localized (ShiftVecReportFor's distance cell + ShiftTarget's aim
    ///      vector - CE's bearing and ballistic angle both come from these),
    ///   3. ORIGIN remapped into the target's band at ProjectileCE.Launch, so the round
    ///      flies band-locally where the target actually is - collisions, suppression and
    ///      impact all land on the right level. The relay draws it through ceiling holes
    ///      like any other round (it is registered as a Thing; the relay was retyped for
    ///      exactly this).
    ///
    /// ⚠ EVERY PATCH HERE IS REFLECTION-ONLY AND SHAPE-VALIDATED. No CE type appears in any
    /// signature (project law; also what lets this compile and load without CE installed).
    /// Each Prepare() checks the member exists AND its parameter shape matches before
    /// patching; a future CE rename degrades that one seam to vanilla behaviour and the
    /// combat report says which seam is missing, instead of a red error at startup.
    ///
    /// ⚠ THE HIGH-GROUND NUMBERS (user's spec, constants for now):
    ///   * Suppression is ONE SIGNED RULE: amount x1.5 when the shooter is ABOVE the victim,
    ///     x0.5 when BELOW. That yields both halves of the spec at once - upper pawns resist
    ///     fire from below, lower pawns crumble under plunging fire.
    ///   * Accuracy: shooting DOWN tightens sway and spread to 80%. No bonus shooting up.
    /// </summary>
    public static class ABCECompat
    {
        public const float SuppressionFromAboveMult = 1.5f;

        public const float SuppressionFromBelowMult = 0.5f;

        public const float HighGroundSwayMult = 0.8f;

        public const float HighGroundSpreadMult = 0.8f;

        private static bool resolved;

        private static Type tVerbCE;

        private static Type tProjectileCE;

        private static Type tShiftVecReport;

        internal static FieldInfo fCover;

        internal static FieldInfo fSmoke;

        internal static FieldInfo fSway;

        internal static FieldInfo fSpread;

        internal static FieldInfo fLauncher;

        internal static bool suppressionSeamFound;

        // Observe-only counters for `AB2: combat report`.
        public static int ceShotsSolved;

        public static int ceShotsDenied;

        public static int ceOriginsRemapped;

        public static int ceSuppressionScaled;

        public static int ceHighGroundShots;

        public static void ResetCounters()
        {
            ceShotsSolved = 0;
            ceShotsDenied = 0;
            ceOriginsRemapped = 0;
            ceSuppressionScaled = 0;
            ceHighGroundShots = 0;
            ceTurretOrdersLocalized = 0;
            ceFireArcsLocalized = 0;
            ceLeanSolves = 0;
            ceCoverEyeReads = 0;
            ceOpeningEmergences = 0;
            ceLaunchUnsolved = 0;
            ceRayCastsRemapped = 0;
            ceFusesAdjusted = 0;
            ceCeilingEntries = 0;
            ceCeilingPullbacks = 0;
        }

        public static string CounterReport()
        {
            if (!Active)
            {
                return "CE: not loaded";
            }

            return "CE: solved=" + ceShotsSolved + " denied=" + ceShotsDenied + " originsRemapped=" + ceOriginsRemapped + " suppressionScaled=" + ceSuppressionScaled + " highGroundShots=" + ceHighGroundShots + " turretOrders=" + ceTurretOrdersLocalized + " fireArcs=" + ceFireArcsLocalized + " leanSolves=" + ceLeanSolves + " coverEyeReads=" + ceCoverEyeReads + " openingEmergences=" + ceOpeningEmergences + " unsolvedLaunches=" + ceLaunchUnsolved + " rayCasts=" + ceRayCastsRemapped + " fusesAdjusted=" + ceFusesAdjusted + " ceilingEntries=" + ceCeilingEntries + " ceilingPullbacks=" + ceCeilingPullbacks + (suppressionSeamFound ? "" : " | ⚠ suppression seam MISSING (CE renamed it)") + (coverSeamArmed ? "" : " | ⚠ cover walk NOT ARMED (cross-level cover stripped instead)") + (heightSeamArmed ? "" : " | ⚠ instant-shot height seam NOT ARMED") + ((tProjectilePropsCE == null || pGravityPerWidth != null) ? "" : " | ⚠ GravityPerWidth NOT FOUND (every remapped round treated as straight)") + ((mBoundsFor != null) ? "" : (boundsThrew ? " | ⚠ GetBoundsFor switched off after throwing (ceiling entries ignore the target's box)" : " | ⚠ GetBoundsFor NOT FOUND (ceiling entries ignore the target's box)")) + (launchSeamFound ? "" : " | ⚠ launch seam MISSING (rounds fly on the shooter's level)");
        }

        public static bool Active
        {
            get
            {
                Resolve();
                return tVerbCE != null;
            }
        }

        internal static Type VerbCE { get { Resolve(); return tVerbCE; } }

        internal static Type ProjectileCE { get { Resolve(); return tProjectileCE; } }

        private static void Resolve()
        {
            if (resolved)
            {
                return;
            }

            resolved = true;
            tVerbCE = AccessTools.TypeByName("CombatExtended.Verb_LaunchProjectileCE");
            tProjectileCE = AccessTools.TypeByName("CombatExtended.ProjectileCE");
            tShiftVecReport = AccessTools.TypeByName("CombatExtended.ShiftVecReport");
            tTurretCE = AccessTools.TypeByName("CombatExtended.Building_TurretGunCE");
            tFireArcCE = AccessTools.TypeByName("CombatExtended.CompFireArc");
            tProjectilePropsCE = AccessTools.TypeByName("CombatExtended.ProjectilePropertiesCE");
            Type type = AccessTools.TypeByName("CombatExtended.CE_Utility");
            if (type != null)
            {
                mBoundsFor = AccessTools.Method(type, "GetBoundsFor", new Type[1] { typeof(Thing) });
                if (mBoundsFor != null && (!mBoundsFor.IsStatic || mBoundsFor.ReturnType != typeof(Bounds)))
                {
                    mBoundsFor = null;
                }
            }

            if (tShiftVecReport != null)
            {
                fCover = AccessTools.Field(tShiftVecReport, "cover");
                fSmoke = AccessTools.Field(tShiftVecReport, "smokeDensity");
                fSway = AccessTools.Field(tShiftVecReport, "swayDegrees");
                fSpread = AccessTools.Field(tShiftVecReport, "spreadDegrees");
                fShotDist = Typed(AccessTools.Field(tShiftVecReport, "shotDist"), typeof(float));
            }

            if (tProjectileCE != null)
            {
                fLauncher = AccessTools.Field(tProjectileCE, "launcher");
                fIntendedTarget = Typed(AccessTools.Field(tProjectileCE, "intendedTarget"), typeof(LocalTargetInfo));
                fIntendedTargetHeight = Typed(AccessTools.Field(tProjectileCE, "intendedTargetHeight"), typeof(float));
                fMinCollision = Typed(AccessTools.Field(tProjectileCE, "minCollisionDistance"), typeof(float));
                fIgnoreRoof = Typed(AccessTools.Field(tProjectileCE, "ignoreRoof"), typeof(bool));
                fShotAngle = Typed(AccessTools.Field(tProjectileCE, "shotAngle"), typeof(float));
                fShotRotation = Typed(AccessTools.Field(tProjectileCE, "shotRotation"), typeof(float));
                fShotHeight = Typed(AccessTools.Field(tProjectileCE, "shotHeight"), typeof(float));
                fShotSpeed = Typed(AccessTools.Field(tProjectileCE, "shotSpeed"), typeof(float));
                fRoundGravity = Typed(AccessTools.Field(tProjectileCE, "GravityPerWidth"), typeof(float));
            }

            if (tProjectilePropsCE != null)
            {
                fArmingDelay = Typed(AccessTools.Field(tProjectilePropsCE, "armingDelay"), typeof(int));
                pGravityPerWidth = AccessTools.Property(tProjectilePropsCE, "GravityPerWidth");
                if (pGravityPerWidth != null && pGravityPerWidth.PropertyType != typeof(float))
                {
                    pGravityPerWidth = null;
                }
            }
        }

        /// <summary>Signed suppression scale for a victim at <paramref name="victimBand"/>
        /// taking fire whose true shooter stands at <paramref name="shooterBand"/>.</summary>
        internal static float SuppressionScale(int shooterBand, int victimBand)
        {
            if (shooterBand > victimBand)
            {
                return SuppressionFromAboveMult;
            }
            if (shooterBand < victimBand)
            {
                return SuppressionFromBelowMult;
            }
            return 1f;
        }
        internal static void AdjustFuse(Thing proj, Remap r, float shotSpeed, float angleBefore, float angleAfter)
        {
            Type type = proj.GetType();
            if (!fuseFields.TryGetValue(type, out var value))
            {
                value = Typed(AccessTools.Field(type, "ticksToBurst"), typeof(int));
                fuseFields[type] = value;
            }

            if (value == null)
            {
                return;
            }

            int num = (int)value.GetValue(proj);
            float num2 = shotSpeed / 60f * Mathf.Cos(angleBefore);
            float num3 = shotSpeed / 60f * Mathf.Cos(angleAfter);
            if (num > 0 && !(num2 < 0.001f))
            {
                float num4 = ((num3 < 0.001f) ? 0f : (r.newDist / num3));
                int a = 1;
                if (fArmingDelay != null && proj.def?.projectile != null && fArmingDelay.DeclaringType.IsInstanceOfType(proj.def.projectile))
                {
                    a = Mathf.Max(1, (int)fArmingDelay.GetValue(proj.def.projectile));
                }

                int num5 = Mathf.Max(a, num + Mathf.RoundToInt(num4 - r.oldDist / num2));
                if (num5 != num)
                {
                    value.SetValue(proj, num5);
                    ceFusesAdjusted++;
                }
            }
        }

        private static bool TryCeilingEntry(LocalTargetInfo intended, Map map, bool straight, float g, float shotSpeed, float angleIn, Vector2 dir, float targetHeight, ref Remap r, ref Vector2 aimThere, ref Vector2 newOrigin, out float angle)
        {
            angle = 0f;
            Vector2 vector = aimThere;
            Vector2 vector2 = newOrigin;
            bool pulledBack = false;
            float num = Mathf.Max(2f, targetHeight + 0.25f);
            float num2 = num - targetHeight;
            if ((vector - vector2).magnitude < 0.5f)
            {
                vector2 = vector - dir * 0.5f;
                pulledBack = true;
            }

            Vector2 normalized = (vector - vector2).normalized;
            bool flag = TryTargetBounds(intended, map, out var box);
            if (flag)
            {
                Bounds bounds = box;
                bounds.Expand(0.1f);
                for (int i = 0; i < 40; i++)
                {
                    if (!bounds.Contains(new Vector3(vector2.x, num, vector2.y)))
                    {
                        break;
                    }

                    vector2 -= normalized * 0.25f;
                    pulledBack = true;
                }
            }

            float num3 = (flag ? (Mathf.Abs(normalized.x) * box.extents.x + Mathf.Abs(normalized.y) * box.extents.z) : 0f);
            if (flag && (vector - vector2).magnitude < num3 + 0.1f)
            {
                vector2 = vector - normalized * (num3 + 0.1f);
                pulledBack = true;
            }

            float magnitude = (vector - vector2).magnitude;
            float num4 = HeightAt(straight, g, shotSpeed, r.oldHeight, angleIn, r.oldDist) - targetHeight;
            float num5 = 6f * Mathf.Max(1f, magnitude);
            if (flag)
            {
                float num6 = box.max.y - targetHeight;
                if (num6 > 0f && num6 < num2 * 0.95f)
                {
                    num5 = Mathf.Max(num5, (magnitude * num6 + num2 * num3) / (num2 - num6) + 0.25f);
                }
            }

            float value;
            if (num4 >= num2 * 0.95f)
            {
                value = num5;
            }
            else
            {
                value = ((!(num4 >= 0f)) ? ((magnitude * num4 - num2 * num3) / (num2 - num4)) : ((magnitude * num4 + num2 * num3) / (num2 - num4)));
            }

            value = Mathf.Clamp(value, 0f - (magnitude - 0.1f), num5);
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return false;
            }

            vector += normalized * value;
            if (!TryBallisticAngle(straight, g, shotSpeed, (vector - vector2).magnitude, 0f - num2, out angle))
            {
                return false;
            }

            aimThere = vector;
            newOrigin = vector2;
            r.newHeight = num;
            r.alongBearing = value;
            r.pulledBack = pulledBack;
            r.runToTarget = magnitude;
            return true;
        }

        internal static bool TryRemap(Thing proj, Map map, ABBandMap bands, ref Vector2 origin, ref float shotAngle, ref float shotRotation, ref float shotHeight, float shotSpeed, bool instant, out Remap r)
        {
            r = default;
            if (fIntendedTarget == null)
            {
                return false;
            }

            IntVec3 c = new IntVec3(Mathf.FloorToInt(origin.x), 0, Mathf.FloorToInt(origin.y));
            if (!c.InBounds(map))
            {
                return false;
            }

            LocalTargetInfo intended = (LocalTargetInfo)fIntendedTarget.GetValue(proj);
            if (!intended.IsValid || !intended.Cell.InBounds(map))
            {
                return false;
            }

            int num = bands.BandOf(c);
            int num2 = bands.BandOf(intended.Cell);
            if (num == num2 || bands.InGutter(intended.Cell))
            {
                return false;
            }

            r.bandFrom = num;
            r.bandTo = num2;
            float num3 = (num2 - num) * bands.Slot;
            r.solved = ABCombatRelay.TryPeekSolutionFor(num, out var sol) && sol.targetBand == num2;
            r.sol = (r.solved ? sol : default(ABShotSolution));
            Vector3 vector = ((intended.HasThing && intended.Thing.Spawned && intended.Thing.Map == map) ? intended.Thing.TrueCenter() : intended.Cell.ToVector3Shifted());
            Vector2 vector2 = new Vector2(vector.x, vector.z - num3);
            r.oldDist = Mathf.Max(0.5f, (vector2 - origin).magnitude);
            Vector2 aimThere = origin + FlightDir(shotRotation) * r.oldDist + new Vector2(0f, num3);
            r.viaOpening = r.solved && !sol.overhead && sol.opening.IsValid;
            Vector2 newOrigin;
            if (r.viaOpening)
            {
                IntVec3 intVec = bands.Translate(sol.opening, num2);
                newOrigin = new Vector2((float)intVec.x + 0.5f, (float)intVec.z + 0.5f);
            }
            else
            {
                newOrigin = new Vector2(origin.x, origin.y + num3);
            }

            r.oldHeight = shotHeight;
            r.newHeight = shotHeight;
            float num4 = ((fRoundGravity != null) ? ((float)fRoundGravity.GetValue(proj)) : 0f);
            if (num4 <= 0f && pGravityPerWidth != null && proj.def?.projectile != null && tProjectilePropsCE != null && tProjectilePropsCE.IsInstanceOfType(proj.def.projectile))
            {
                num4 = (float)pGravityPerWidth.GetValue(proj.def.projectile, null);
            }

            bool flag = instant || num4 <= 0f;
            float num5 = ((fIntendedTargetHeight != null) ? ((float)fIntendedTargetHeight.GetValue(proj)) : 0f);
            float angle = 0f;
            if (r.viaOpening && num2 < num)
            {
                r.fromCeiling = TryCeilingEntry(intended, map, flag, num4, shotSpeed, shotAngle, FlightDir(shotRotation), num5, ref r, ref aimThere, ref newOrigin, out angle);
            }

            r.newDist = (aimThere - newOrigin).magnitude;
            if (r.viaOpening && r.newDist > 0.05f)
            {
                shotRotation = RotationOf(aimThere - newOrigin);
                float dh = num5 - r.oldHeight;
                float angle2;
                float angle3;
                if (r.fromCeiling)
                {
                    shotAngle = angle;
                }
                else if (TryBallisticAngle(flag, num4, shotSpeed, r.oldDist, dh, out angle2) && TryBallisticAngle(flag, num4, shotSpeed, r.newDist, dh, out angle3))
                {
                    shotAngle += angle3 - angle2;
                }
            }

            if (r.fromCeiling)
            {
                shotHeight = r.newHeight;
                ceCeilingEntries++;
                if (r.pulledBack)
                {
                    ceCeilingPullbacks++;
                }
            }

            origin = newOrigin;
            return true;
        }

        internal static float HeightAt(bool straight, float g, float speed, float h0, float angle, float x)
        {
            float num = h0 + x * Mathf.Tan(angle);
            if (!straight)
            {
                float num2 = speed * Mathf.Cos(angle);
                if (num2 > 0.001f)
                {
                    num -= g * x * x / (2f * num2 * num2);
                }
            }

            return num;
        }

        internal static bool TryTargetBounds(LocalTargetInfo t, Map map, out Bounds box)
        {
            box = default;
            if (mBoundsFor == null || !t.HasThing || t.Thing == null || !t.Thing.Spawned || t.Thing.Map != map)
            {
                return false;
            }

            try
            {
                box = (Bounds)mBoundsFor.Invoke(null, new object[1] { t.Thing });
            }
            catch (Exception ex)
            {
                mBoundsFor = null;
                boundsThrew = true;
                ABLog.Dev("CE bridge: GetBoundsFor threw (" + (ex.InnerException ?? ex).GetType().Name + "), ceiling entries continue without the target's collision box.");
                return false;
            }

            return box.size.sqrMagnitude > 0f;
        }

        internal static float MinCollisionFor(float targetDistance)
        {
            return (targetDistance <= 7.5f) ? Mathf.Min(1.5f, targetDistance * 0.75f) : (targetDistance * 0.2f);
        }

        internal static bool TryBallisticAngle(bool instant, float gravityPerWidth, float speed, float dist, float dh, out float angle)
        {
            angle = 0f;
            if (dist < 0.01f)
            {
                return false;
            }

            if (instant)
            {
                angle = Mathf.Atan2(dh, dist);
                return true;
            }

            if (gravityPerWidth <= 0f || speed <= 0f)
            {
                return false;
            }

            float num = speed * speed;
            float num2 = num * num - gravityPerWidth * (gravityPerWidth * dist * dist + 2f * dh * num);
            if (num2 < 0f || float.IsNaN(num2))
            {
                return false;
            }

            angle = Mathf.Atan((num - Mathf.Sqrt(num2)) / (gravityPerWidth * dist));
            return !float.IsNaN(angle);
        }

        internal static float RotationOf(Vector2 d)
        {
            return (-90f + 57.29578f * Mathf.Atan2(d.y, d.x)) % 360f;
        }

        internal static Vector2 FlightDir(float rotationDeg)
        {
            float f = (rotationDeg + 90f) * (Mathf.PI / 180f);
            return new Vector2(Mathf.Cos(f), Mathf.Sin(f));
        }

        internal static List<CodeInstruction> RewritePositionReads(IEnumerable<CodeInstruction> input, string where, int expected, string eyeName, int warnKey, out int swapped)
        {
            MethodInfo methodInfo = AccessTools.PropertyGetter(typeof(Thing), "Position");
            MethodInfo methodInfo2 = AccessTools.Method(typeof(ABCECompat), eyeName);
            List<CodeInstruction> list = new List<CodeInstruction>(input);
            swapped = 0;
            if (methodInfo == null || methodInfo2 == null)
            {
                return list;
            }

            for (int i = 0; i < list.Count; i++)
            {
                if ((list[i].opcode == OpCodes.Callvirt || list[i].opcode == OpCodes.Call) && list[i].operand as MethodInfo == methodInfo)
                {
                    list[i].opcode = OpCodes.Call;
                    list[i].operand = methodInfo2;
                    swapped++;
                }
            }

            if (swapped != expected)
            {
                Log.WarningOnce("[As above, So below] Combat Extended bridge redirected " + swapped + " of " + expected + " position reads in " + where + ((swapped == 0) ? "; cover across levels falls back to none." : "; CE's method changed shape, so check cover across levels."), warnKey);
            }
            else
            {
                ABLog.Dev("CE bridge redirected " + swapped + " of " + expected + " expected position reads in " + where + ".");
            }

            return list;
        }

        public static IntVec3 HeightEye(Thing t)
        {
            if (eyeCaster != null && t == eyeCaster)
            {
                ceCoverEyeReads++;
                return eyeColumn;
            }

            return t.Position;
        }

        public static IntVec3 CoverEye(Thing t)
        {
            if (eyeCaster != null && t == eyeCaster)
            {
                ceCoverEyeReads++;
                return eyeCell;
            }

            return t.Position;
        }

        internal static void CloseEye(EyeScope scope)
        {
            eyeCaster = scope.prevCaster;
            eyeCell = scope.prevCell;
            eyeColumn = scope.prevColumn;
        }

        internal static EyeScope OpenEye(Verb verb, LocalTargetInfo target)
        {
            EyeScope result = new EyeScope
            {
                prevCaster = eyeCaster,
                prevCell = eyeCell,
                prevColumn = eyeColumn
            };
            eyeCaster = null;
            try
            {
                Thing thing = verb?.caster;
                if (thing == null || !thing.Spawned || !target.IsValid || !ABCombatV2.Enabled)
                {
                    return result;
                }

                Map map = thing.Map;
                ABBandMap aBBandMap = ABBands.CompOf(map);
                if (aBBandMap == null || !aBBandMap.Banded || !target.Cell.InBounds(map))
                {
                    return result;
                }

                int num = aBBandMap.BandOf(thing.Position);
                int num2 = aBBandMap.BandOf(target.Cell);
                if (num == num2)
                {
                    return result;
                }

                result.relation = ((num > num2) ? 1 : (-1));
                if (!coverSeamArmed && !heightSeamArmed)
                {
                    return result;
                }

                if (!ABCombatV2.TrySolve(verb, thing.Position, target, out var sol, out var source, out var _))
                {
                    return result;
                }

                IntVec3 intVec = ((sol.overhead || !sol.opening.IsValid) ? aBBandMap.Translate(source, num2) : aBBandMap.Translate(sol.opening, num2));
                if (!intVec.InBounds(map))
                {
                    return result;
                }

                IntVec3 intVec2 = aBBandMap.Translate(thing.Position, num2);
                eyeCaster = thing;
                eyeCell = intVec;
                eyeColumn = (intVec2.InBounds(map) ? intVec2 : intVec);
                result.armed = true;
            }
            catch (Exception e)
            {
                eyeCaster = null;
                ABGuard.Disable(ABGuard.Combat, e, "CE cross-level cover eye");
            }

            return result;
        }

        internal static List<CodeInstruction> RewriteCellSubtraction(IEnumerable<CodeInstruction> input, string where, int expected)
        {
            MethodInfo methodInfo = AccessTools.Method(typeof(IntVec3), "op_Subtraction", new Type[2] { typeof(IntVec3), typeof(IntVec3) });
            MethodInfo methodInfo2 = AccessTools.Method(typeof(ABCECompat), "BandLocalDelta");
            List<CodeInstruction> list = new List<CodeInstruction>(input);
            if (methodInfo == null || methodInfo2 == null)
            {
                return list;
            }

            int num = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].opcode == OpCodes.Call && list[i].operand as MethodInfo == methodInfo)
                {
                    list[i].operand = methodInfo2;
                    num++;
                }
            }

            if (num == 0)
            {
                Log.WarningOnce("[As above, So below] Combat Extended bridge found no cell subtraction to correct in " + where + "; cross-level turret orders may be refused as out of range.", 2822352);
            }
            else
            {
                ABLog.Dev("CE bridge rewrote " + num + " of " + expected + " expected cell subtractions in " + where + ".");
            }

            return list;
        }

        public static IntVec3 BandLocalDelta(IntVec3 a, IntVec3 b)
        {
            Map map = orderScopeMap;
            if (map == null)
            {
                return a - b;
            }

            try
            {
                ABBandMap aBBandMap = ABBands.CompOf(map);
                if (aBBandMap == null || !aBBandMap.Banded)
                {
                    return a - b;
                }

                int num = aBBandMap.BandOf(b);
                if (aBBandMap.BandOf(a) == num)
                {
                    return a - b;
                }

                ceTurretOrdersLocalized++;
                return aBBandMap.Translate(a, num) - b;
            }
            catch (Exception e)
            {
                orderScopeMap = null;
                ABGuard.Disable(ABGuard.Combat, e, "CE turret band-local range");
                return a - b;
            }
        }

        internal static void PopOrderScope()
        {
            if (orderDepth != 0 && --orderDepth == 0)
            {
                orderScopeMap = null;
            }
        }

        internal static void PushOrderScope(Map map)
        {
            if (orderDepth++ <= 0)
            {
                orderScopeMap = (ABBands.Banded(map) ? map : null);
            }
        }

        private static FieldInfo Typed(FieldInfo f, Type expected)
        {
            return (f != null && f.FieldType == expected) ? f : null;
        }

        internal static Type FireArcCE
        {
            get
            {
                Resolve();
                return tFireArcCE;
            }
        }

        internal static Type TurretCE
        {
            get
            {
                Resolve();
                return tTurretCE;
            }
        }

        private static readonly Dictionary<Type, FieldInfo> fuseFields = new Dictionary<Type, FieldInfo>();

        internal const float MinCeilingRun = 0.5f;

        internal const float CeilingHeight = 2f;

        internal static FieldInfo fRoundGravity;

        internal static bool boundsThrew;

        internal static MethodInfo mBoundsFor;

        internal const float MaxCeilingMiss = 6f;

        private static IntVec3 eyeColumn;

        private static IntVec3 eyeCell;

        private static Thing eyeCaster;

        private static int orderDepth;

        private static Map orderScopeMap;

        private static Type tFireArcCE;

        private static Type tTurretCE;

        public static int ceCeilingPullbacks;

        public static int ceCeilingEntries;

        public static int ceFusesAdjusted;

        public static int ceRayCastsRemapped;

        public static int ceLaunchUnsolved;

        public static int ceOpeningEmergences;

        public static int ceCoverEyeReads;

        public static int ceLeanSolves;

        public static int ceFireArcsLocalized;

        public static int ceTurretOrdersLocalized;

        internal static bool launchSeamFound;

        internal static bool heightSeamArmed;

        internal static bool coverSeamArmed;

        private static Type tProjectilePropsCE;

        internal static FieldInfo fArmingDelay;

        internal static PropertyInfo pGravityPerWidth;

        internal static FieldInfo fShotSpeed;

        internal static FieldInfo fShotHeight;

        internal static FieldInfo fShotRotation;

        internal static FieldInfo fShotAngle;

        internal static FieldInfo fIgnoreRoof;

        internal static FieldInfo fMinCollision;

        internal static FieldInfo fIntendedTargetHeight;

        internal static FieldInfo fIntendedTarget;

        internal static FieldInfo fShotDist;

        public struct Remap
        {
            public bool viaOpening;
            public bool solved;
            public float oldDist;
            public float newDist;
            public int bandFrom;
            public int bandTo;
            public ABShotSolution sol;
            public bool fromCeiling;
            public bool pulledBack;
            public float oldHeight;
            public float newHeight;
            public float alongBearing;
            public float runToTarget;
        }

        public struct EyeScope
        {
            public Thing prevCaster;
            public IntVec3 prevCell;
            public IntVec3 prevColumn;
            public int relation;
            public bool armed;
        }

    }

    /// <summary>
    /// SEAM 1: shot legality. Same permit/deny logic as the vanilla bridge, at CE's own
    /// chokepoint. CE's CanHitTargetFrom (player targeting + AI) and TryCastShot both call
    /// this, so one prefix gates everything - and the DENY branch matters just as much as
    /// under vanilla, because CE's fallthrough also decides on raw distance (§41c).
    /// </summary>
    [HarmonyPatch]
    public static class Patch_CE_ShootLine_ABCrossBand
    {
        private static MethodBase TargetMethod()
        {
            Type t = ABCECompat.VerbCE;
            if (t == null)
            {
                return null;
            }
            // The 4-arg overload (root, targ, out line, out targetPos) is the real body;
            // the 3-arg one forwards to it.
            foreach (MethodInfo m in t.GetMethods(AccessTools.all))
            {
                if (m.Name == "TryFindCEShootLineFromTo" && m.GetParameters().Length == 4)
                {
                    return m;
                }
            }
            return null;
        }

        private static bool Prepare()
        {
            return TargetMethod() != null;
        }

        private static bool Prefix(Verb __instance, IntVec3 root, LocalTargetInfo targ, ref ShootLine resultingLine, ref Vector3 targetPos, ref bool __result)
        {
            try
            {
                if (!ABBands.Banded(__instance?.caster?.Map))
                {
                    return true;
                }

                if (ABCombatV2.TrySolve(__instance, root, targ, out var sol, out var source, out var dest))
                {
                    targetPos = (targ.HasThing ? targ.Thing.TrueCenter() : targ.Cell.ToVector3Shifted());
                    resultingLine = new ShootLine(source, dest);
                    ABCombatRelay.RecordSolution(__instance.caster, targ.Cell, sol);
                    ABCECompat.ceShotsSolved++;
                    if ((source != root && __instance.caster is Pawn) || (dest != targ.Cell && targ.Thing is Pawn))
                    {
                        ABCECompat.ceLeanSolves++;
                    }

                    if (ABV2Debug.LogCombat)
                    {
                        string[] array = new string[9]
                        {
                            "CE shootline OK ",
                            __instance.caster.LabelShortCap,
                            " ",
                            null,
                            null,
                            null,
                            null,
                            null,
                            null
                        };
                        IntVec3 intVec = root;
                        array[3] = intVec.ToString();
                        array[4] = " -> ";
                        array[5] = targ.Cell.ToString();
                        array[6] = " from ";
                        intVec = source;
                        array[7] = intVec.ToString();
                        string text;
                        if (!sol.overhead)
                        {
                            intVec = sol.opening;
                            text = " via " + intVec.ToString();
                        }
                        else
                        {
                            text = " (overhead)";
                        }

                        array[8] = text;
                        ABV2Debug.Combat(string.Concat(array));
                    }

                    __result = true;
                    return false;
                }

                if (ABCombatV2.OwnsPair(__instance, root, targ))
                {
                    resultingLine = new ShootLine(root, targ.Cell);
                    targetPos = targ.Cell.ToVector3Shifted();
                    __result = false;
                    ABCECompat.ceShotsDenied++;
                    return false;
                }
            }
            catch (Exception e)
            {
                ABGuard.Disable(ABGuard.Combat, e, "CE cross-band shoot line");
            }

            return true;
        }
    }

    /// <summary>
    /// SEAM 2a: the accuracy report. CE computes shotDist from the raw cell (a Slot across
    /// bands - every accuracy factor then reads a 128-cell shot) and walks cover/smoke along
    /// the raw line through the gutter. Localize the cell going in; going out, strip the
    /// fabricated cover and smoke (§41k's lesson verbatim: the localized walk samples the
    /// SHOOTER's band, and those covers are not decoration - CE aims around them) and apply
    /// the HIGH-GROUND accuracy bonus when the shooter stands above the target.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_CE_ShiftVecReport_ABCrossBand
    {
        private static MethodBase TargetMethod()
        {
            Type t = ABCECompat.VerbCE;
            if (t == null)
            {
                return null;
            }
            foreach (MethodInfo m in t.GetMethods(AccessTools.all))
            {
                ParameterInfo[] p = m.GetParameters();
                if (m.Name == "ShiftVecReportFor" && p.Length == 2
                    && p[0].ParameterType == typeof(LocalTargetInfo)
                    && p[1].ParameterType == typeof(IntVec3))
                {
                    return m;
                }
            }
            return null;
        }

        private static bool Prepare()
        {
            return TargetMethod() != null && ABCECompat.fCover != null && ABCECompat.fSmoke != null && ABCECompat.fSway != null && ABCECompat.fSpread != null && ABCECompat.fShotDist != null;
        }
        private static void Finalizer(ABCECompat.EyeScope __state)
        {
            ABCECompat.CloseEye(__state);
        }

        private static void Postfix(Verb __instance, IntVec3 __1, object __result, ABCECompat.EyeScope __state)
        {
            if (__state.relation == 0 || __result == null)
            {
                return;
            }

            try
            {
                Thing caster = __instance.caster;
                ABBandMap aBBandMap = ABBands.CompOf(caster.Map);
                int toBand = aBBandMap.BandOf(caster.Position);
                ABCECompat.fShotDist.SetValue(__result, (aBBandMap.Translate(__1, toBand) - caster.Position).LengthHorizontal);
                if (!__state.armed || !ABCECompat.coverSeamArmed)
                {
                    ABCECompat.fCover.SetValue(__result, null);
                    ABCECompat.fSmoke.SetValue(__result, 0f);
                }

                if (__state.relation > 0)
                {
                    ABCECompat.fSway.SetValue(__result, (float)ABCECompat.fSway.GetValue(__result) * 0.8f);
                    ABCECompat.fSpread.SetValue(__result, (float)ABCECompat.fSpread.GetValue(__result) * 0.8f);
                    ABCECompat.ceHighGroundShots++;
                }
            }
            catch (Exception e)
            {
                ABGuard.Disable(ABGuard.Combat, e, "CE cross-band report repair");
            }
        }

        private static void Prefix(Verb __instance, LocalTargetInfo __0, out ABCECompat.EyeScope __state)
        {
            __state = ABCECompat.OpenEye(__instance, __0);
        }

    }

    /// <summary>
    /// SEAM 2b: the aim vector. ShiftTarget receives the target's REAL centre and derives
    /// bearing, ballistic angle and lead from `v - sourceLoc` - across bands that vector is
    /// dominated by ±Slot, so the pawn would aim due north at a 60-degree lob. Localizing v
    /// into the caster's band makes every derived quantity band-local. Self-contained (no
    /// latch): the band comparison IS the trigger.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_CE_ShiftTarget_ABCrossBand
    {
        private static MethodBase TargetMethod()
        {
            Type t = ABCECompat.VerbCE;
            if (t == null)
            {
                return null;
            }
            foreach (MethodInfo m in t.GetMethods(AccessTools.all))
            {
                ParameterInfo[] p = m.GetParameters();
                if (m.Name == "ShiftTarget" && p.Length == 5
                    && p[1].ParameterType == typeof(Vector3))
                {
                    return m;
                }
            }
            return null;
        }

        private static bool Prepare()
        {
            return TargetMethod() != null;
        }

        private static void Prefix(Verb __instance, ref Vector3 __1)
        {
            try
            {
                Thing caster = __instance?.caster;
                if (caster == null || !caster.Spawned || !ABCombatV2.Enabled)
                {
                    return;
                }
                ABBandMap bands = ABBands.CompOf(caster.Map);
                if (bands == null || !bands.Banded)
                {
                    return;
                }
                int casterBand = bands.BandOf(caster.Position);
                int vBand = bands.BandOf(__1.ToIntVec3());
                if (vBand == casterBand)
                {
                    return;
                }
                __1 = new Vector3(__1.x, __1.y,
                    __1.z + (casterBand - vBand) * bands.Slot);
            }
            catch (Exception e)
            {
                ABGuard.Disable(ABGuard.Combat, e, "CE cross-band aim vector");
            }
        }
    }

    /// <summary>
    /// SEAM 3: the origin remap. The parked solution from seam 1 carries the band pair;
    /// when this launch belongs to it (same tick, origin on the solved root band), the 2D
    /// origin moves into the TARGET's band and the round flies band-locally where the
    /// target lives - collisions, suppression and impact all on the correct level. Also
    /// registers with the relay, which draws it through ceiling holes like any round.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_CE_ProjectileLaunch_ABCrossBand
    {
        private static MethodBase TargetMethod()
        {
            Type projectileCE = ABCECompat.ProjectileCE;
            if (projectileCE == null)
            {
                return null;
            }

            foreach (MethodInfo declaredMethod in AccessTools.GetDeclaredMethods(projectileCE))
            {
                ParameterInfo[] parameters = declaredMethod.GetParameters();
                if (declaredMethod.Name == "Launch" && parameters.Length == 3 && parameters[0].ParameterType == typeof(Thing) && parameters[1].ParameterType == typeof(Vector2) && parameters[2].ParameterType == typeof(Thing))
                {
                    return declaredMethod;
                }
            }

            return null;
        }

        private static bool Prepare()
        {
            ABCECompat.launchSeamFound = TargetMethod() != null && ABCECompat.fIntendedTarget != null && ABCECompat.fShotAngle != null && ABCECompat.fShotRotation != null && ABCECompat.fShotHeight != null && ABCECompat.fShotSpeed != null && ABCECompat.fMinCollision != null;
            return ABCECompat.launchSeamFound;
        }

        private static void Prefix(Thing __instance, Thing __0, ref Vector2 __1)
        {
            try
            {
                if (__instance == null || __0 == null || !__0.Spawned || !ABCombatV2.Enabled)
                {
                    return;
                }

                Map map = __0.Map;
                ABBandMap aBBandMap = ABBands.CompOf(map);
                if (aBBandMap == null || !aBBandMap.Banded)
                {
                    return;
                }

                Vector2 vector = __1;
                float shotAngle = (float)ABCECompat.fShotAngle.GetValue(__instance);
                float shotRotation = (float)ABCECompat.fShotRotation.GetValue(__instance);
                float shotHeight = (float)ABCECompat.fShotHeight.GetValue(__instance);
                float shotSpeed = (float)ABCECompat.fShotSpeed.GetValue(__instance);
                float num = shotAngle;
                if (!ABCECompat.TryRemap(__instance, map, aBBandMap, ref __1, ref shotAngle, ref shotRotation, ref shotHeight, shotSpeed, instant: false, out var r))
                {
                    return;
                }

                ABCECompat.fShotAngle.SetValue(__instance, shotAngle);
                ABCECompat.fShotRotation.SetValue(__instance, shotRotation);
                ABCECompat.fShotHeight.SetValue(__instance, shotHeight);
                ABCECompat.fMinCollision.SetValue(__instance, ABCECompat.MinCollisionFor(r.fromCeiling ? r.runToTarget : r.newDist));
                if (r.viaOpening)
                {
                    ABCECompat.fIgnoreRoof?.SetValue(__instance, true);
                    ABCECompat.AdjustFuse(__instance, r, shotSpeed, num, shotAngle);
                    ABCECompat.ceOpeningEmergences++;
                }

                if (!r.solved)
                {
                    ABCECompat.ceLaunchUnsolved++;
                }

                ABCECompat.ceOriginsRemapped++;
                ABCombatRelay.Register(__instance, __0, r.bandFrom, r.bandTo, r.sol);
                if (ABV2Debug.LogCombat)
                {
                    string[] array = new string[17]
                    {
                        "CE projectile ",
                        __instance.def.defName,
                        " origin ",
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null
                    };
                    Vector2 vector2 = vector;
                    array[3] = vector2.ToString();
                    array[4] = " (band ";
                    array[5] = r.bandFrom.ToString();
                    array[6] = ") -> ";
                    vector2 = __1;
                    array[7] = vector2.ToString();
                    array[8] = " (band ";
                    array[9] = r.bandTo.ToString();
                    array[10] = ") ";
                    string text;
                    if (!r.viaOpening)
                    {
                        text = (r.solved ? "shooter's column (shell)" : "shooter's column: NO PARKED SOLUTION");
                    }
                    else
                    {
                        IntVec3 opening = r.sol.opening;
                        text = "out of opening " + opening.ToString();
                    }

                    array[11] = text;
                    array[12] = ", aim dist ";
                    array[13] = r.oldDist.ToString("0.0");
                    array[14] = " -> ";
                    array[15] = r.newDist.ToString("0.0");
                    array[16] = (r.fromCeiling ? (", enters at the ceiling (height " + r.oldHeight.ToString("0.00") + " -> " + r.newHeight.ToString("0.00") + ", angle " + (num * 57.29578f).ToString("0.0") + " -> " + (shotAngle * 57.29578f).ToString("0.0") + " deg, vertical miss " + r.alongBearing.ToString("+0.00;-0.00;0.00") + " along the flight" + (r.pulledBack ? ", entry pulled back" : "") + ")") : "");
                    ABV2Debug.Combat(string.Concat(array));
                }
            }
            catch (Exception e)
            {
                ABGuard.Disable(ABGuard.Combat, e, "CE cross-band projectile origin");
            }
        }
    }

    /// <summary>
    /// SEAM 4: SUPPRESSION - the half of the high-ground model the user led with.
    ///
    /// CE applies flyover and impact suppression from the projectile, and by the time
    /// CompSuppressable.AddSuppression runs, the origin it receives is OUR REMAPPED cell -
    /// same band as the victim, band delta zero, a naive hook would never fire (our own
    /// remap would have neutralized it). The projectile's `launcher` still stands on the
    /// TRUE band, so the scale reads the launcher.
    ///
    /// Patched at ApplySuppression on the projectile (shape-validated: Pawn first, float
    /// multiplier second) so the multiplier scales BEFORE Suppressability stats - if CE
    /// renames it, Prepare() skips and the combat report flags the missing seam.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_CE_ApplySuppression_ABHighGround
    {
        private static MethodBase target;

        private static MethodBase TargetMethod()
        {
            if (target != null)
            {
                return target;
            }
            Type t = ABCECompat.ProjectileCE;
            if (t == null)
            {
                return null;
            }
            foreach (MethodInfo m in AccessTools.GetDeclaredMethods(t))
            {
                ParameterInfo[] p = m.GetParameters();
                if (m.Name == "ApplySuppression" && p.Length >= 1
                    && p[0].ParameterType == typeof(Pawn)
                    && (p.Length == 1 || p[1].ParameterType == typeof(float)))
                {
                    ABCECompat.suppressionSeamFound = p.Length >= 2;
                    target = ABCECompat.suppressionSeamFound ? m : null;
                    return target;
                }
            }
            return null;
        }

        private static bool Prepare()
        {
            return TargetMethod() != null;
        }

        private static void Prefix(Thing __instance, Pawn __0, ref float __1)
        {
            try
            {
                if (__0 == null || !__0.Spawned || !ABCombatV2.Enabled)
                {
                    return;
                }
                ABBandMap bands = ABBands.CompOf(__0.Map);
                if (bands == null || !bands.Banded)
                {
                    return;
                }
                Thing launcher = ABCECompat.fLauncher != null
                    ? ABCECompat.fLauncher.GetValue(__instance) as Thing
                    : null;
                if (launcher == null || !launcher.Spawned || launcher.Map != __0.Map)
                {
                    return;
                }
                int shooterBand = bands.BandOf(launcher.Position);
                int victimBand = bands.BandOf(__0.Position);
                float scale = ABCECompat.SuppressionScale(shooterBand, victimBand);
                if (scale != 1f)
                {
                    __1 *= scale;
                    ABCECompat.ceSuppressionScaled++;
                }
            }
            catch (Exception e)
            {
                ABGuard.Disable(ABGuard.Combat, e, "CE high-ground suppression");
            }
        }
    }
}
