using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using SmashTools;
using Verse;
using Vehicles;

namespace AASBVehicleRamps;

/// <summary>
/// Draws crossing animations through Vehicle Framework's own vehicle draw path.
/// <list type="bullet">
/// <item>A prefix on <c>VehiclePawn.DynamicDrawPhaseAt(DrawPhase, Vector3, bool)</c> adds the clip offset to the
/// draw location. VF passes that location to every parallel renderer of the vehicle (body, turrets, graphic
/// overlays, drawn crew), so they all move together; the body's shadow is drawn at the body position.</item>
/// <item>A transpiler on the same method replaces its single <c>FullRotation</c> read with <see cref="DrawRotation"/>,
/// so the drawn facing (and every renderer's orientation) turns into the ramp during entry without changing the
/// vehicle's logical rotation or footprint.</item>
/// <item>For the draw phase, the prefix opens a scale context about the drawn centre; transpilers replace the
/// <c>Graphics.DrawMesh(Mesh, Vector3, Quaternion, Material, int)</c> calls of the body, turret and overlay renderers
/// with <see cref="DrawMesh"/>, which scales positions about that centre and the meshes themselves.</item>
/// </list>
/// With no active clip the hooks cost one field read per vehicle draw and one thread-static read per mesh.
/// </summary>
public static class ClipRenderPatch
{
    // DrawPhase.Draw: the main-thread phase that issues the meshes.
    private const int DrawPhaseDraw = 2;

    [ThreadStatic] private static bool s_scaling;
    [ThreadStatic] private static Vector3 s_pivot;
    [ThreadStatic] private static float s_scale;

    public static MethodInfo NativeDrawMesh => AccessTools.Method(typeof(Graphics), nameof(Graphics.DrawMesh),
        new[] { typeof(Mesh), typeof(Vector3), typeof(Quaternion), typeof(Material), typeof(int) });

    public static MethodInfo FullRotationGetter =>
        AccessTools.PropertyGetter(typeof(VehiclePawn), nameof(VehiclePawn.FullRotation));

    public static void DrawPrefix(VehiclePawn __instance, DrawPhase phase, ref Vector3 drawLoc, out bool __state)
    {
        __state = false;
        if (!RampClips.AnyActive || !RampClips.TryGetPose(__instance, out var pose))
        {
            return;
        }

        drawLoc += pose.Offset;
        if ((int)phase == DrawPhaseDraw && pose.Scale < 0.999f)
        {
            s_pivot = drawLoc;
            s_scale = pose.Scale;
            s_scaling = true;
            __state = true;
        }
    }

    public static void DrawFinalizer(bool __state)
    {
        if (__state)
        {
            s_scaling = false;
        }
    }

    /// <summary>
    /// Drawn facing: the clip's facing during entry, otherwise the vehicle's own. Graphics that are not drawn rotated
    /// keep Vehicle Framework's fixed facing.
    /// </summary>
    public static Rot8 DrawRotation(VehiclePawn vehicle)
    {
        if (RampClips.AnyActive && RampClips.TryGetPose(vehicle, out var pose) && pose.Facing.IsValid
            && vehicle.VehicleDef.graphicData.drawRotated)
        {
            return pose.Facing;
        }

        return vehicle.FullRotation;
    }

    public static void DrawMesh(Mesh mesh, Vector3 position, Quaternion rotation, Material material, int layer)
    {
        if (!s_scaling)
        {
            Graphics.DrawMesh(mesh, position, rotation, material, layer);
            return;
        }

        var scaled = new Vector3(s_pivot.x + (position.x - s_pivot.x) * s_scale, position.y,
            s_pivot.z + (position.z - s_pivot.z) * s_scale);
        Graphics.DrawMesh(mesh, Matrix4x4.TRS(scaled, rotation, new Vector3(s_scale, 1f, s_scale)), material, layer);
    }

    /// <summary>Number of native DrawMesh calls in a method body; used by startup validation.</summary>
    public static int CountDrawMesh(List<CodeInstruction> codes)
    {
        var native = NativeDrawMesh;
        var count = 0;
        foreach (var code in codes)
        {
            if (code.Calls(native))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Number of FullRotation reads in a method body; used by startup validation.</summary>
    public static int CountFullRotation(List<CodeInstruction> codes)
    {
        var getter = FullRotationGetter;
        var count = 0;
        foreach (var code in codes)
        {
            if (code.Calls(getter))
            {
                count++;
            }
        }

        return count;
    }

    public static IEnumerable<CodeInstruction> DrawMeshTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var native = NativeDrawMesh;
        var replacement = AccessTools.Method(typeof(ClipRenderPatch), nameof(DrawMesh));
        foreach (var code in instructions)
        {
            if (code.Calls(native))
            {
                code.opcode = OpCodes.Call;
                code.operand = replacement;
            }

            yield return code;
        }
    }

    public static IEnumerable<CodeInstruction> RotationTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var getter = FullRotationGetter;
        var replacement = AccessTools.Method(typeof(ClipRenderPatch), nameof(DrawRotation));
        foreach (var code in instructions)
        {
            if (code.Calls(getter))
            {
                code.opcode = OpCodes.Call;
                code.operand = replacement;
            }

            yield return code;
        }
    }
}
