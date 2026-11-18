#if RIMWORLD_17
#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AsAboveSoBelow;

public static class Showcase
{
    public sealed class Scene
    {
        public readonly string key;

        public readonly string caption;

        public readonly Func<IEnumerable<Wait?>> play;

        public Scene(string key, string caption, Func<IEnumerable<Wait?>> play)
        {
            this.key = key;
            this.caption = caption;
            this.play = play;
        }
    }

    public sealed class Wait
    {
        public float endTime;

        public Func<bool>? until;
    }

    public class CaptionWindow : Window
    {
        protected override float Margin => 0f;

        public override Vector2 InitialSize => new Vector2(Mathf.Max(200f, StageRect().width - 32f), 48f);

        public CaptionWindow()
        {
            layer = WindowLayer.Super;
            doWindowBackground = false;
            drawShadow = false;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = false;
            closeOnCancel = false;
            closeOnAccept = false;
            preventCameraMotion = false;
            forcePause = false;
            doCloseX = false;
            onlyOneOfTypeAllowed = true;
            soundAppear = null;
            soundClose = null;
        }

        protected override void SetInitialSizeAndPosition()
        {
            Rect rect = StageRect();
            windowRect = new Rect(rect.x + 16f, rect.y + 16f, InitialSize.x, InitialSize.y).Rounded();
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (Event.current.type == EventType.repaint)
            {
                Frame();
            }
            DrawCaption(inRect);
        }

        public override void PostClose()
        {
            base.PostClose();
            if (Running)
            {
                Stop("caption window closed");
            }
        }
    }

    public static bool CaptionsOn = true;

    public static bool HideMessages = true;

    public static bool CleanUI = false;

    private static bool active;

    private static bool started;

    private static bool startLogged;

    private static List<Scene> queue = new List<Scene>();

    private static int sceneIndex;

    private static Scene? scene;

    private static IEnumerator<Wait?>? cur;

    private static Wait? waiting;

    private static CaptionWindow? window;

    private static ShowcaseUnloadWatcher? watcher;

    private static string caption = "";

    private static float captionStart;

    private static readonly List<Window> sceneWindows = new List<Window>();

    private static readonly List<Pawn> madePawns = new List<Pawn>();

    private static readonly List<Thing> madeThings = new List<Thing>();

    private static readonly List<WorldObject> madeWorldObjects = new List<WorldObject>();

    private static bool savedDevMode;

    private static bool savedGodMode;

    private static bool savedLogVerbose;

    private static bool savedResetOnCrash;

    private static bool savedScreenshotMode;

    private static float camSmooth = 3f;

    private static WorldObject? followWorld;

    private static Vector3? worldLookAt;

    private static float altFrom;

    private static float altTo;

    private static float altStart;

    private static float altDur;

    private static readonly FieldInfo? DesiredAltitudeField = typeof(WorldCameraDriver).GetField("desiredAltitude", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? PrefsDataField = typeof(Prefs).GetField("data", BindingFlags.Static | BindingFlags.NonPublic);

    private static Thing? followThing;

    private static Vector3? mapLookAt;

    private static Vector3 mapCam;

    private static bool mapCamLive;

    private static float sizeFrom;

    private static float sizeTo;

    private static float sizeStart;

    private static float sizeDur;

    private const string Category = "As above, So below II";

    private const string Tag = "AsAboveSoBelowII";

    private static readonly List<Scene> Scenes = new List<Scene>
    {
        new Scene("levels", "One map, many levels: the sky above, the ground, the rock below", Levels),
        new Scene("seebelow", "From a sky level you look down on the colony as it lives", SeeBelow),
        new Scene("stairs", "Build stairs and pawns walk between levels on their own", Stairs),
        new Scene("underground", "Dig down: the level below is solid rock and ore", Underground),
        new Scene("rooftop", "Roof a room and its rooftop becomes ground on the level above", Rooftop),
        new Scene("combat", "Fight across levels: shoot down through the openings", Combat)
    };

    private static Map? takeMap;

    private static int savedViewBand = -1;

    private static CellRect? room;

    private static Pawn? wolf;

    private static Pawn? shooter;

    private static IntVec3 shooterHome = IntVec3.Invalid;

    private static ThingWithComps? shooterOwnWeapon;

    public static float Now => Time.realtimeSinceStartup;

    public static bool Running => active;

    private static string Clock => DateTime.UtcNow.ToString("HH:mm:ss.fff");

    private static Map? Home => Find.Maps.FirstOrDefault((Map m) => m.IsPlayerHome);

    private static void OnBegin()
    {
        if (PrefsDataField?.GetValue(null) is PrefsData prefsData)
        {
            prefsData.logVerbose = false;
        }
        Log.ResetMessageCount();
        takeMap = Home;
        ABBandMap aBBandMap = ((takeMap == null) ? null : ABBands.CompOf(takeMap));
        savedViewBand = ((aBBandMap != null && aBBandMap.Banded) ? ABBandView.CurrentBand(takeMap) : (-1));
        room = null;
        wolf = null;
        shooter = null;
        shooterHome = IntVec3.Invalid;
        shooterOwnWeapon = null;
    }

    private static void OnStop()
    {
        Map map = takeMap;
        takeMap = null;
        if (map == null || Current.Game == null || !Find.Maps.Contains(map))
        {
            wolf = null;
            shooter = null;
            shooterOwnWeapon = null;
            return;
        }
        RemoveWolf();
        RestoreShooter(map);
        if (savedViewBand >= 0)
        {
            ABBandView.SetBand(map, savedViewBand);
        }
    }

    public static Wait For(float seconds)
    {
        return new Wait
        {
            endTime = Now + seconds
        };
    }

    public static Wait Until(Func<bool> done, float timeoutSeconds)
    {
        return new Wait
        {
            endTime = Now + timeoutSeconds,
            until = done
        };
    }

    private static void Say(string text)
    {
        Log.Message($"[{"AsAboveSoBelowII"} showcase] {Clock} {text}");
    }

    private static void Guard(string what, Action a)
    {
        try
        {
            a();
        }
        catch (Exception ex)
        {
            Log.Warning($"[{"AsAboveSoBelowII"} showcase] {what} failed: {ex.Message}");
        }
    }

    public static void Play(IEnumerable<Scene> scenes)
    {
        if (active && (window == null || !Find.WindowStack.IsOpen(window)))
        {
            Abandon();
        }
        if (active)
        {
            Stop("restarted");
        }
        queue = scenes.ToList();
        sceneIndex = 0;
        scene = null;
        cur = null;
        started = false;
        startLogged = false;
        caption = "";
        active = true;
        waiting = For(2f);
        window = new CaptionWindow();
        Find.WindowStack.Add(window);
        watcher = new GameObject("ShowcaseKit unload watcher").AddComponent<ShowcaseUnloadWatcher>();
        Messages.Message($"Showcase starts in 2 seconds: {queue.Count} scene(s). Press End to stop.", MessageTypeDefOf.SilentInput, historical: false);
    }

    private static void Begin()
    {
        started = true;
        savedDevMode = Prefs.DevMode;
        savedGodMode = DebugSettings.godMode;
        savedLogVerbose = Prefs.LogVerbose;
        savedResetOnCrash = Prefs.ResetModsConfigOnCrash;
        foreach (Window item in Find.WindowStack.Windows.ToList())
        {
            if (item is Window_Dev)
            {
                Find.WindowStack.TryRemove(item, doCloseSound: false);
            }
        }
        SetDevMode(on: false);
        DebugSettings.godMode = false;
        savedScreenshotMode = Find.UIRoot.screenshotMode.Active;
        if (CleanUI)
        {
            Find.UIRoot.screenshotMode.Active = true;
        }
        Guard("OnBegin", () =>
        {
            OnBegin();
        });
    }

    private static void LogStart()
    {
        startLogged = true;
        Rect rect = StageRect();
        float s = Prefs.UIScale;
        Say($"start: {queue.Count} scene(s); screen {Screen.width}x{Screen.height} px, stage crop (ffmpeg crop=w:h:x:y) {Even(rect.width)}:{Even(rect.height)}:{Even(rect.x)}:{Even(rect.y)}");
        int Even(float v)
        {
            return Mathf.RoundToInt(v * s / 2f) * 2;
        }
    }

    public static void Stop(string why)
    {
        if (!active)
        {
            return;
        }
        active = false;
        Guard("ending the scene", EndScene);
        ResetCamera();
        foreach (WorldObject o in madeWorldObjects)
        {
            Guard("removing " + o, () =>
            {
                if (!o.Destroyed)
                {
                    if (o is Caravan caravan)
                    {
                        caravan.RemoveAllPawns();
                    }
                    if (!o.Destroyed)
                    {
                        o.Destroy();
                    }
                }
            });
        }
        foreach (Thing t in madeThings)
        {
            Guard("removing " + t, () =>
            {
                if (!t.Destroyed)
                {
                    t.Destroy();
                }
            });
        }
        foreach (Pawn p in madePawns)
        {
            Guard("discarding " + p, () =>
            {
                DiscardMade(p);
            });
        }
        madeWorldObjects.Clear();
        madeThings.Clear();
        madePawns.Clear();
        if (started)
        {
            Guard("restoring screenshot mode", () =>
            {
                Find.UIRoot.screenshotMode.Active = savedScreenshotMode;
            });
        }
        RestorePrefs();
        if (Current.ProgramState == ProgramState.Playing)
        {
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
        }
        CaptionWindow captionWindow = window;
        window = null;
        captionWindow?.Close(doCloseSound: false);
        DropWatcher();
        cur = null;
        scene = null;
        waiting = null;
        Say("stop: " + why);
        if (Current.ProgramState == ProgramState.Playing)
        {
            Messages.Message("Showcase finished (" + why + "). Scene times are in Player.log.", MessageTypeDefOf.SilentInput, historical: false);
        }
    }

    public static void Abandon()
    {
        if (active)
        {
            active = false;
            RestorePrefs();
            madePawns.Clear();
            madeThings.Clear();
            madeWorldObjects.Clear();
            sceneWindows.Clear();
            window = null;
            cur = null;
            scene = null;
            waiting = null;
            ResetCamera();
            DropWatcher();
            Say("stop: the game it was playing in was unloaded");
        }
    }

    internal static void LateFrame()
    {
        if (active && started && HideMessages)
        {
            Messages.Clear();
        }
    }

    internal static void WatcherDestroyed(ShowcaseUnloadWatcher w)
    {
        if (!(watcher != w))
        {
            watcher = null;
            Abandon();
        }
    }

    private static void DropWatcher()
    {
        ShowcaseUnloadWatcher showcaseUnloadWatcher = watcher;
        watcher = null;
        if (showcaseUnloadWatcher != null)
        {
            UnityEngine.Object.Destroy(showcaseUnloadWatcher.gameObject);
        }
    }

    private static void RestorePrefs()
    {
        if (started)
        {
            started = false;
            SetDevMode(savedDevMode);
            Prefs.LogVerbose = savedLogVerbose;
            Prefs.ResetModsConfigOnCrash = savedResetOnCrash;
            DebugSettings.godMode = savedGodMode;
            Guard("OnStop", () =>
            {
                OnStop();
            });
        }
    }

    private static void DiscardMade(Pawn p)
    {
        if (p == null || p.Destroyed || p.Spawned)
        {
            return;
        }
        foreach (Faction item in Find.FactionManager.AllFactionsListForReading)
        {
            if (item.kidnapped != null && item.kidnapped.KidnappedPawnsListForReading.Contains(p))
            {
                item.kidnapped.RemoveKidnappedPawn(p);
            }
        }
        Caravan caravan = p.GetCaravan();
        if (caravan != null && !caravan.Destroyed)
        {
            caravan.RemovePawn(p);
        }
        if (Find.WorldPawns.Contains(p))
        {
            Find.WorldPawns.RemoveAndDiscardPawnViaGC(p);
        }
    }

    internal static void Frame()
    {
        if (!active)
        {
            return;
        }
        if (Current.ProgramState != ProgramState.Playing)
        {
            Stop("left the game");
            return;
        }
        if (Input.GetKeyDown(KeyCode.End))
        {
            Stop("stopped with End");
            return;
        }
        try
        {
            CameraFrame(Mathf.Min(Time.unscaledDeltaTime, 0.1f));
        }
        catch (Exception ex)
        {
            Log.Warning("[AsAboveSoBelowII showcase] camera: " + ex.Message);
            ResetCamera();
        }
        if (waiting != null)
        {
            bool flag = Now >= waiting.endTime;
            if (!flag && waiting.until != null)
            {
                try
                {
                    flag = waiting.until();
                }
                catch
                {
                    flag = true;
                }
            }
            if (!flag)
            {
                return;
            }
            waiting = null;
        }
        if (!started)
        {
            Begin();
            waiting = For(1f);
            return;
        }
        if (!startLogged)
        {
            LogStart();
        }
        if (cur == null)
        {
            if (sceneIndex >= queue.Count)
            {
                Stop("all scenes played");
                return;
            }
            BeginScene(queue[sceneIndex++]);
        }
        bool flag2;
        try
        {
            flag2 = cur != null && cur.MoveNext();
        }
        catch (Exception value)
        {
            Log.Warning($"[{"AsAboveSoBelowII"} showcase] scene {scene?.key} failed: {value}");
            flag2 = false;
        }
        if (HideMessages)
        {
            Messages.Clear();
        }
        if (flag2)
        {
            waiting = cur.Current;
            return;
        }
        EndScene();
        waiting = For(0.8f);
    }

    private static void BeginScene(Scene s)
    {
        scene = s;
        ResetCamera();
        Find.WorldSelector.ClearSelection();
        Find.Selector.ClearSelection();
        SetCaption(s.caption);
        Say($"begin {sceneIndex}/{queue.Count} {s.key}: {s.caption}");
        try
        {
            cur = s.play().GetEnumerator();
        }
        catch (Exception value)
        {
            Log.Warning($"[{"AsAboveSoBelowII"} showcase] scene {s.key} failed to start: {value}");
            cur = Enumerable.Empty<Wait>().GetEnumerator();
        }
    }

    private static void EndScene()
    {
        if (scene == null)
        {
            return;
        }
        foreach (Window sceneWindow in sceneWindows)
        {
            if (sceneWindow != null && Find.WindowStack.IsOpen(sceneWindow))
            {
                Find.WindowStack.TryRemove(sceneWindow, doCloseSound: false);
            }
        }
        sceneWindows.Clear();
        Say("end " + scene.key);
        scene = null;
        cur = null;
        SetCaption("");
        ResetCamera();
    }

    private static void Skip(string why)
    {
        Say("skipped " + scene?.key + ": " + why);
        SetCaption("");
    }

    private static void SetCaption(string text)
    {
        caption = text;
        captionStart = Now;
    }

    private static void SetDevMode(bool on)
    {
        if (PrefsDataField?.GetValue(null) is PrefsData prefsData)
        {
            prefsData.devMode = on;
        }
        else
        {
            Prefs.DevMode = on;
        }
    }

    private static void ResetCamera()
    {
        followWorld = null;
        worldLookAt = null;
        altDur = 0f;
        followThing = null;
        mapLookAt = null;
        mapCamLive = false;
        sizeDur = 0f;
        camSmooth = 3f;
    }

    private static float Ease(float start, float dur)
    {
        float num = Mathf.Clamp01((Now - start) / dur);
        return num * num * (3f - 2f * num);
    }

    private static void CameraFrame(float dt)
    {
        float t = 1f - Mathf.Exp((0f - camSmooth) * dt);
        if (WorldRendererUtility.WorldSelected)
        {
            WorldCameraDriver worldCameraDriver = Find.WorldCameraDriver;
            Vector3? vector = ((followWorld != null && followWorld.Spawned) ? new Vector3?(followWorld.DrawPos) : worldLookAt);
            if (vector.HasValue && vector.Value.sqrMagnitude > 1E-06f)
            {
                worldCameraDriver.sphereRotation = Quaternion.Slerp(worldCameraDriver.sphereRotation, WorldRotationFor(vector.Value), t);
            }
            if (altDur > 0f)
            {
                float num = Ease(altStart, altDur);
                SetAltitude(Mathf.Lerp(altFrom, altTo, num));
                if (num >= 1f)
                {
                    altDur = 0f;
                }
            }
        }
        else
        {
            if (Find.CurrentMap == null)
            {
                return;
            }
            CameraDriver cameraDriver = Find.CameraDriver;
            Vector3? vector2 = ((followThing != null && followThing.Spawned && followThing.Map == Find.CurrentMap) ? new Vector3?(followThing.DrawPos) : mapLookAt);
            if (vector2.HasValue)
            {
                if (!mapCamLive)
                {
                    mapCam = cameraDriver.MapPosition.ToVector3Shifted();
                    mapCamLive = true;
                }
                mapCam = Vector3.Lerp(mapCam, vector2.Value, t);
                cameraDriver.JumpToCurrentMapLoc(mapCam);
            }
            if (sizeDur > 0f)
            {
                float num2 = Ease(sizeStart, sizeDur);
                cameraDriver.SetRootSize(Mathf.Lerp(sizeFrom, sizeTo, num2));
                if (num2 >= 1f)
                {
                    sizeDur = 0f;
                }
            }
        }
    }

    private static Quaternion WorldRotationFor(Vector3 point)
    {
        return Quaternion.Inverse(Quaternion.LookRotation(-point.normalized));
    }

    private static void SetAltitude(float a)
    {
        WorldCameraDriver worldCameraDriver = Find.WorldCameraDriver;
        a = Mathf.Clamp(a, WorldCameraDriver.MinAltitude, 1100f);
        worldCameraDriver.altitude = a;
        DesiredAltitudeField?.SetValue(worldCameraDriver, a);
    }

    private static void ShowWorld(PlanetLayer? layer = null)
    {
        CameraJumper.TryShowWorld();
        PlanetLayer planetLayer = layer ?? Find.WorldGrid.Surface;
        if (PlanetLayer.Selected != planetLayer)
        {
            PlanetLayer.Selected = planetLayer;
        }
    }

    private static void ZoomWorld(float altitude, float seconds)
    {
        if (seconds <= 0f)
        {
            altDur = 0f;
            SetAltitude(altitude);
            return;
        }
        altFrom = Find.WorldCameraDriver.altitude;
        altTo = altitude;
        altStart = Now;
        altDur = seconds;
    }

    private static void SnapWorld(Vector3 point)
    {
        if (point.sqrMagnitude > 1E-06f)
        {
            Find.WorldCameraDriver.sphereRotation = WorldRotationFor(point);
        }
    }

    private static void FollowWorld(WorldObject? o)
    {
        followWorld = o;
        worldLookAt = null;
    }

    private static void LookAtWorld(Vector3 point)
    {
        worldLookAt = point;
        followWorld = null;
    }

    private static void SelectWorld(WorldObject o)
    {
        Find.WorldSelector.ClearSelection();
        if (o != null && o.Spawned && o.Tile.Valid && o.Tile.Layer == PlanetLayer.Selected)
        {
            Find.WorldSelector.Select(o, playSound: false);
        }
    }

    private static void ShowMap(Map map)
    {
        if (Find.CurrentMap != map)
        {
            Current.Game.CurrentMap = map;
        }
        CameraJumper.TryHideWorld();
    }

    private static void ZoomMap(float size, float seconds)
    {
        if (seconds <= 0f)
        {
            sizeDur = 0f;
            Find.CameraDriver.SetRootSize(size);
            return;
        }
        sizeFrom = Find.CameraDriver.ZoomRootSize;
        sizeTo = size;
        sizeStart = Now;
        sizeDur = seconds;
    }

    private static void SnapMap(Vector3 pos)
    {
        Find.CameraDriver.JumpToCurrentMapLoc(pos);
        mapCam = pos;
        mapCamLive = true;
    }

    private static void FrameMap(IntVec3 cell, float size)
    {
        SnapMap(cell.ToVector3Shifted());
        ZoomMap(size, 0f);
    }

    private static void Follow(Thing? t)
    {
        followThing = t;
        mapLookAt = null;
    }

    private static void LookAt(Vector3 pos)
    {
        mapLookAt = pos;
        followThing = null;
    }

    private static void SelectThing(Thing t)
    {
        Find.Selector.ClearSelection();
        if (t != null && t.Spawned)
        {
            Find.Selector.Select(t, playSound: false, forceDesignatorDeselect: false);
        }
    }

    private static void Speed(TimeSpeed s)
    {
        Find.TickManager.CurTimeSpeed = s;
    }

    private static bool Fire(IncidentDef def, IIncidentTarget target, Action<IncidentParms>? tweak = null)
    {
        IncidentParms incidentParms = StorytellerUtility.DefaultParmsNow(def.category, target);
        incidentParms.forced = true;
        tweak?.Invoke(incidentParms);
        return def.Worker.TryExecute(incidentParms);
    }

    private static HashSet<int> PawnIds(Map map)
    {
        return new HashSet<int>(map.mapPawns.AllPawnsSpawned.Select((Pawn p) => p.thingIDNumber));
    }

    private static List<Pawn> NewPawns(Map map, HashSet<int> before, Func<Pawn, bool>? pred = null)
    {
        return map.mapPawns.AllPawnsSpawned.Where((Pawn p) => !before.Contains(p.thingIDNumber) && (pred == null || pred(p))).ToList();
    }

    private static HashSet<int> WorldObjectIds()
    {
        return new HashSet<int>(Find.WorldObjects.AllWorldObjects.Select((WorldObject o) => o.ID));
    }

    private static List<T> NewWorldObjects<T>(HashSet<int> before) where T : WorldObject
    {
        return (from o in Find.WorldObjects.AllWorldObjects.OfType<T>()
            where !before.Contains(o.ID)
            select o).ToList();
    }

    private static void Track(Window w)
    {
        sceneWindows.Add(w);
    }

    private static T Open<T>(T w) where T : Window
    {
        Find.WindowStack.Add(w);
        Track(w);
        return w;
    }

    private static HashSet<Window> OpenNow()
    {
        return new HashSet<Window>(Find.WindowStack.Windows);
    }

    private static T? NewWindow<T>(HashSet<Window> before) where T : Window
    {
        return Find.WindowStack.Windows.OfType<T>().FirstOrDefault((T w) => !before.Contains(w));
    }

    private static void OpenMenuAt(FloatMenu menu, Vector2 uiPos)
    {
        menu.vanishIfMouseDistant = false;
        Find.WindowStack.Add(menu);
        menu.windowRect.x = Mathf.Clamp(uiPos.x + 18f, 0f, (float)UI.screenWidth - menu.windowRect.width);
        menu.windowRect.y = Mathf.Clamp(uiPos.y - 14f, 0f, (float)UI.screenHeight - menu.windowRect.height);
        Track(menu);
    }

    private static Vector2 UIPosOf(Thing t)
    {
        return t.DrawPos.MapToUIPosition();
    }

    private static Vector2 UIPosOf(WorldObject o)
    {
        return o.ScreenPos();
    }

    private static T Made<T>(T t) where T : Thing
    {
        if (t is Pawn item)
        {
            madePawns.Add(item);
        }
        else
        {
            madeThings.Add(t);
        }
        return t;
    }

    private static T MadeWorld<T>(T o) where T : WorldObject
    {
        madeWorldObjects.Add(o);
        return o;
    }

    public static Rect StageRect()
    {
        float num = UI.screenWidth;
        float num2 = UI.screenHeight;
        float num3 = Mathf.Min(num, num2 * 16f / 9f);
        float num4 = num3 * 9f / 16f;
        return new Rect((num - num3) / 2f, (num2 - num4) / 2f, num3, num4);
    }

    internal static void DrawCaption(Rect inRect)
    {
        if (CaptionsOn && !caption.NullOrEmpty())
        {
            float num = Mathf.Clamp01((Now - captionStart) / 0.35f);
            GameFont font = Text.Font;
            Text.Font = GameFont.Medium;
            Vector2 vector = Text.CalcSize(caption);
            Rect rect = new Rect(0f, 0f, Mathf.Min(inRect.width, vector.x + 28f), vector.y + 12f);
            Widgets.DrawBoxSolid(rect, new Color(0.05f, 0.06f, 0.07f, 0.82f * num));
            GUI.color = new Color(0.4f, 0.44f, 0.5f, num);
            Widgets.DrawBox(rect);
            GUI.color = new Color(1f, 1f, 1f, num);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(rect.x + 14f, rect.y, rect.width - 20f, rect.height), caption);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            Text.Font = font;
        }
    }

    [DebugAction("As above, So below II", "AsAboveSoBelowII showcase: play all scenes", false, false, false, false, false, 0, false, actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
    public static void PlayAllAction()
    {
        Play(Scenes);
    }

    [DebugAction("As above, So below II", "AsAboveSoBelowII showcase: play one scene", false, false, false, false, false, 0, false, actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
    public static void PlayOneAction()
    {
        List<FloatMenuOption> list = new List<FloatMenuOption>();
        foreach (Scene scene in Scenes)
        {
            Scene pick = scene;
            list.Add(new FloatMenuOption(scene.key + ": " + scene.caption, () =>
            {
                Play(new Scene[1] { pick });
            }));
        }
        Find.WindowStack.Add(new FloatMenu(list));
    }

    [DebugAction("As above, So below II", "AsAboveSoBelowII showcase: captions on/off", false, false, false, false, false, 0, false, actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
    public static void ToggleCaptionsAction()
    {
        CaptionsOn = !CaptionsOn;
        Messages.Message("Showcase captions " + (CaptionsOn ? "on" : "off") + ".", MessageTypeDefOf.SilentInput, historical: false);
    }

    private static void RestoreShooter(Map map)
    {
        Pawn pawn = shooter;
        shooter = null;
        if (pawn == null || !pawn.Spawned || pawn.Map != map)
        {
            return;
        }
        ABBandMap bands = ABBands.CompOf(map);
        if (shooterHome.IsValid && bands != null && bands.BandOf(pawn.Position) != bands.BandOf(shooterHome))
        {
            int homeBand = bands.BandOf(shooterHome);
            IntVec3 result = shooterHome;
            if (result.Standable(map) || CellFinder.TryFindRandomCellNear(shooterHome, map, 6, (IntVec3 c) => c.Standable(map) && bands.BandOf(c) == homeBand, out result))
            {
                pawn.Position = result;
                pawn.Notify_Teleported();
            }
            else
            {
                string[] array = new string[5] { "[AsAboveSoBelowII showcase] no ground cell near ", null, null, null, null };
                IntVec3 intVec = shooterHome;
                array[1] = intVec.ToString();
                array[2] = " to bring ";
                array[3] = pawn.LabelShort;
                array[4] = " back to; left on the rooftop.";
                Log.Warning(string.Concat(array));
            }
        }
        shooterHome = IntVec3.Invalid;
        if (pawn.drafter != null)
        {
            pawn.drafter.Drafted = false;
        }
        ThingWithComps thingWithComps = shooterOwnWeapon;
        shooterOwnWeapon = null;
        if (thingWithComps != null && pawn.equipment != null && pawn.equipment.Primary == null && pawn.inventory.innerContainer.Contains(thingWithComps))
        {
            pawn.inventory.innerContainer.Remove(thingWithComps);
            pawn.equipment.AddEquipment(thingWithComps);
        }
    }

    private static bool CanShoot(Pawn p)
    {
        return !p.WorkTagIsDisabled(WorkTags.Violent) && (p.apparel == null || !p.apparel.WornApparel.Any((Apparel a) => a.TryGetComp<CompShield>()?.Props.blocksRangedWeapons ?? false));
    }

    private static void RemoveWolf()
    {
        if (wolf != null)
        {
            if (wolf.Spawned)
            {
                wolf.Destroy();
            }
            else if (wolf.Corpse != null && wolf.Corpse.Spawned)
            {
                wolf.Corpse.Destroy();
            }
            wolf = null;
        }
    }

    private static bool TryStack(out Map map, out ABBandMap bands)
    {
        map = Home;
        bands = ((map == null) ? null : ABBands.CompOf(map));
        if (map == null || bands == null || !bands.Banded)
        {
            Skip((map == null) ? "no home map" : "the home map has only one level");
            return false;
        }
        ShowMap(map);
        return true;
    }

    private static bool View(Map map, int band, IntVec3 cell, float size)
    {
        if (!ABBandView.SetBand(map, band, preserveXZ: false))
        {
            return false;
        }
        FrameMap(cell, size);
        return true;
    }

    private static Pawn? SurfaceColonist(Map map, ABBandMap bands, Func<Pawn, bool>? pred = null)
    {
        return (from p in map.mapPawns.FreeColonistsSpawned
            where !p.Downed && !p.Drafted && bands.BandOf(p.Position) == bands.surfaceBand && (pred == null || pred(p))
            orderby p.thingIDNumber
            select p).FirstOrDefault();
    }

    private static void KeepLevelWith(Map map, ABBandMap bands, Pawn pawn)
    {
        if (pawn.Spawned)
        {
            int num = bands.BandOf(pawn.Position);
            if (num >= 0 && num != ABBandView.CurrentBand(map))
            {
                ABBandView.SetBand(map, num, preserveXZ: false);
                SnapMap(pawn.DrawPos);
            }
        }
    }

    private static bool Clear(Map map, IntVec3 c)
    {
        return c.InBounds(map) && c.Standable(map) && c.GetEdifice(map) == null && c.GetFirstPawn(map) == null && c.GetFirstItem(map) == null;
    }

    private static bool OpenAbove(Map map, ABBandMap bands, IntVec3 c, int levelsUp = 1)
    {
        IntVec3 c2 = bands.Translate(c, bands.BandOf(c) + levelsUp);
        return c2.InBounds(map) && !bands.InGutter(c2) && map.terrainGrid.TerrainAt(c2) == ABDefOf.AB_OpenAir;
    }

    private static bool WellInside(ABBandMap bands, CellRect r, int margin)
    {
        CellRect cellRect = bands.RectOfBand(bands.surfaceBand).ContractedBy(margin);
        return cellRect.Contains(new IntVec3(r.minX, 0, r.minZ)) && cellRect.Contains(new IntVec3(r.maxX, 0, r.maxZ));
    }

    private static IntVec3? FindSpot(Map map, ABBandMap bands, IntVec3 near, int w, int h, int minDist, int openMargin)
    {
        foreach (IntVec3 item in GenRadial.RadialCellsAround(near, 30f, useCenter: true))
        {
            if (!(item.DistanceTo(near) < (float)minDist))
            {
                CellRect cellRect = new CellRect(item.x, item.z, w, h);
                if (WellInside(bands, cellRect.ExpandedBy(openMargin + 2), 2) && cellRect.Cells.All((IntVec3 x) => Clear(map, x)) && (openMargin <= 0 || cellRect.ExpandedBy(openMargin).Cells.All((IntVec3 x) => x.InBounds(map) && OpenAbove(map, bands, x))))
                {
                    return item;
                }
            }
        }
        return null;
    }

    private static List<Thing> PlaceLink(Map map, string defName, IntVec3 cell)
    {
        List<Thing> list = new List<Thing>();
        ThingDef namedSilentFail = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
        if (namedSilentFail == null)
        {
            return list;
        }
        Thing thing = ThingMaker.MakeThing(namedSilentFail, GenStuff.DefaultStuffFor(namedSilentFail));
        thing.SetFactionDirect(Faction.OfPlayer);
        Thing thing2 = GenSpawn.Spawn(thing, cell, map, Rot4.North);
        list.Add(thing2);
        if (thing2 is Building_ABStairs2 building_ABStairs)
        {
            list.AddRange(building_ABStairs.Counterparts);
        }
        return list;
    }

    private static IntVec3? LandingSpot(Map map, Thing far)
    {
        CellRect foot = far.OccupiedRect();
        return (from c in foot.ExpandedBy(2).Cells
            where !foot.Contains(c) && Clear(map, c)
            orderby c.DistanceToSquared(foot.CenterCell) descending
            select c).Cast<IntVec3?>().FirstOrDefault();
    }

    private static IEnumerable<Wait?> Levels()
    {
        if (!TryStack(out Map map, out ABBandMap bands))
        {
            yield break;
        }
        Pawn lead = SurfaceColonist(map, bands);
        IntVec3 anchor;
        if (lead != null)
        {
            anchor = lead.Position;
        }
        else if (!ABBandView.TryColonyAnchor(map, out anchor))
        {
            anchor = bands.RectOfBand(bands.surfaceBand).CenterCell;
        }
        Speed(TimeSpeed.Normal);
        View(map, bands.surfaceBand, anchor, 44f);
        ZoomMap(52f, 3f);
        yield return For(3f);
        for (int b = bands.surfaceBand + 1; b < bands.bandCount; b++)
        {
            View(map, b, bands.Translate(anchor, b), 52f);
            yield return For(3f);
        }
        for (int b2 = bands.surfaceBand - 1; b2 >= 0; b2--)
        {
            if (bands.IsOpen(b2))
            {
                View(map, b2, bands.Translate(anchor, b2), 52f);
                yield return For(3f);
            }
        }
        View(map, bands.surfaceBand, anchor, 52f);
        yield return For(1.5f);
    }

    private static IEnumerable<Wait?> SeeBelow()
    {
        if (!TryStack(out Map map, out ABBandMap bands))
        {
            yield break;
        }
        int sky = bands.surfaceBand + 1;
        if (!bands.BandExists(sky))
        {
            Skip("no level above the surface");
            yield break;
        }
        Pawn lead = SurfaceColonist(map, bands, (Pawn p) => ABBands.TryResolveVisibleBelow(map, bands, bands.Translate(p.Position, sky), out var below, out var _) && below == p.Position);
        if (lead == null)
        {
            Skip("no colonist under open sky");
            yield break;
        }
        View(map, sky, bands.Translate(lead.Position, sky), 30f);
        SelectThing(lead);
        ZoomMap(20f, 9f);
        Speed(TimeSpeed.Normal);
        Pawn watched = lead;
        yield return Until(() =>
        {
            if (watched.Spawned && bands.BandOf(watched.Position) == bands.surfaceBand)
            {
                LookAt(bands.Translate(watched.Position, sky).ToVector3Shifted());
            }
            return false;
        }, 11f);
    }

    private static IEnumerable<Wait?> Stairs()
    {
        if (!TryStack(out Map map, out ABBandMap bands))
        {
            yield break;
        }
        int sky = bands.surfaceBand + 1;
        if (!bands.BandExists(sky))
        {
            Skip("no level above the surface");
            yield break;
        }
        Pawn walker = SurfaceColonist(map, bands);
        if (walker == null)
        {
            Skip("no free colonist on the surface");
            yield break;
        }
        IntVec3? spot = FindSpot(map, bands, walker.Position, 1, 2, 5, 2);
        if (!spot.HasValue)
        {
            Skip("no clear spot for stairs near the colonist");
            yield break;
        }
        View(map, bands.surfaceBand, walker.Position, 22f);
        Speed(TimeSpeed.Normal);
        yield return For(1f);
        List<Thing> link = PlaceLink(map, "AB2_StairsUp", spot.Value);
        Thing far = link.Skip(1).FirstOrDefault();
        if (far == null)
        {
            Skip("the stairs found no level to land on");
            yield break;
        }
        IntVec3? dest = LandingSpot(map, far);
        if (!dest.HasValue)
        {
            Skip("no standable cell on the landing");
            yield break;
        }
        yield return For(1.5f);
        walker.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto, dest.Value), JobTag.Misc);
        SelectThing(walker);
        Follow(walker);
        Speed(TimeSpeed.Fast);
        float arrivedAt = -1f;
        yield return Until(() =>
        {
            KeepLevelWith(map, bands, walker);
            if (arrivedAt < 0f && walker.Spawned && walker.Position == dest.Value)
            {
                arrivedAt = Now;
            }
            return arrivedAt >= 0f && Now > arrivedAt + 2.5f;
        }, 25f);
    }

    private static IEnumerable<Wait?> Underground()
    {
        if (!TryStack(out Map map, out ABBandMap bands))
        {
            yield break;
        }
        int below = bands.surfaceBand - 1;
        if (!bands.BandExists(below))
        {
            Skip("this colony has no level below");
            yield break;
        }
        IntVec3? spot = FindSpot(near: SurfaceColonist(map, bands)?.Position ?? bands.RectOfBand(bands.surfaceBand).CenterCell, map: map, bands: bands, w: 1, h: 2, minDist: 8, openMargin: 0);
        if (!spot.HasValue)
        {
            Skip("no clear spot for stairs down");
            yield break;
        }
        View(map, bands.surfaceBand, spot.Value, 22f);
        Speed(TimeSpeed.Normal);
        yield return For(1f);
        List<Thing> link = PlaceLink(map, "AB2_StairsDown", spot.Value);
        Thing far = link.Skip(1).FirstOrDefault();
        if (far == null)
        {
            Skip("the stairs found no level to land on");
            yield break;
        }
        yield return For(2f);
        if (!View(map, below, far.Position, 18f))
        {
            Skip("the level below did not open");
            yield break;
        }
        ZoomMap(34f, 9f);
        yield return For(10f);
    }

    private static IEnumerable<Wait?> BuildRoom(Map map, ABBandMap bands, IntVec3 near, float stepSeconds, IntVec3? framed = null)
    {
        IntVec3? corner = ((framed.HasValue && new CellRect(framed.Value.x, framed.Value.z, 7, 7).Cells.All((IntVec3 c3) => Clear(map, c3))) ? framed : FindSpot(map, bands, near, 7, 7, 6, 3));
        if (!corner.HasValue)
        {
            yield break;
        }
        CellRect r = new CellRect(corner.Value.x, corner.Value.z, 7, 7);
        ThingDef wallStuff = GenStuff.DefaultStuffFor(ThingDefOf.Wall);
        IntVec3 doorCell = new IntVec3(r.CenterCell.x, 0, r.minZ);
        List<IntVec3> edge = (from intVec in r.EdgeCells
            orderby intVec.z, intVec.x
            select intVec).ToList();
        for (int i = 0; i < edge.Count; i++)
        {
            IntVec3 c = edge[i];
            bool isDoor = c == doorCell;
            ThingDef def = (isDoor ? ThingDefOf.Door : ThingDefOf.Wall);
            Thing t = ThingMaker.MakeThing(def, isDoor ? GenStuff.DefaultStuffFor(def) : wallStuff);
            t.SetFactionDirect(Faction.OfPlayer);
            GenSpawn.Spawn(t, c, map);
            if (stepSeconds > 0f && i % 6 == 5)
            {
                yield return For(stepSeconds);
            }
        }
        foreach (IntVec3 c2 in r)
        {
            map.roofGrid.SetRoof(c2, RoofDefOf.RoofConstructed);
        }
        room = r;
    }

    private static IEnumerable<Wait?> Rooftop()
    {
        if (!TryStack(out Map map, out ABBandMap bands))
        {
            yield break;
        }
        int sky = bands.surfaceBand + 1;
        if (!bands.BandExists(sky))
        {
            Skip("no level above the surface");
            yield break;
        }
        IntVec3 near = SurfaceColonist(map, bands)?.Position ?? bands.RectOfBand(bands.surfaceBand).CenterCell;
        IntVec3? corner = FindSpot(map, bands, near, 7, 7, 6, 3);
        if (!corner.HasValue)
        {
            Skip("no clear 7x7 spot under open sky");
            yield break;
        }
        View(cell: new CellRect(corner.Value.x, corner.Value.z, 7, 7).CenterCell, map: map, band: bands.surfaceBand, size: 20f);
        Speed(TimeSpeed.Normal);
        yield return For(1f);
        foreach (Wait item in BuildRoom(map, bands, near, 0.35f, corner))
        {
            yield return item;
        }
        if (!room.HasValue)
        {
            Skip("the room could not be built");
            yield break;
        }
        yield return For(1.5f);
        View(map, sky, bands.Translate(room.Value.CenterCell, sky), 20f);
        ZoomMap(16f, 6f);
        yield return For(6f);
    }

    private static IEnumerable<Wait?> Combat()
    {
        if (!TryStack(out Map map, out ABBandMap bands))
        {
            yield break;
        }
        int sky = bands.surfaceBand + 1;
        if (!bands.BandExists(sky))
        {
            Skip("no level above the surface");
            yield break;
        }
        Pawn gunner = SurfaceColonist(map, bands, (Pawn p) =>
        {
            int result;
            if (CanShoot(p))
            {
                Pawn_EquipmentTracker equipment = p.equipment;
                result = ((equipment != null && equipment.Primary?.def.IsRangedWeapon == true) ? 1 : 0);
            }
            else
            {
                result = 0;
            }
            return (byte)result != 0;
        }) ?? SurfaceColonist(map, bands, CanShoot);
        if (gunner == null)
        {
            Skip("no free colonist on the surface");
            yield break;
        }
        shooter = gunner;
        if (!room.HasValue)
        {
            foreach (Wait item in BuildRoom(map, bands, gunner.Position, 0f))
            {
                _ = item;
            }
        }
        if (!room.HasValue)
        {
            Skip("no room to stand on");
            yield break;
        }
        CellRect r = room.Value;
        IntVec3 perch = bands.Translate(new IntVec3(r.CenterCell.x, 0, r.minZ + 1), sky);
        if (!perch.Standable(map))
        {
            Skip("the rooftop is not standable yet");
            yield break;
        }
        IntVec3 wolfCell = new IntVec3(r.CenterCell.x, 0, r.minZ - 6);
        if (!Clear(map, wolfCell) || !OpenAbove(map, bands, wolfCell))
        {
            wolfCell = (from c in GenRadial.RadialCellsAround(wolfCell, 4f, useCenter: true)
                where Clear(map, c) && !r.Contains(c) && bands.BandOf(c) == bands.surfaceBand && OpenAbove(map, bands, c)
                select c).DefaultIfEmpty(IntVec3.Invalid).First();
        }
        PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Wolf_Timber");
        if (kind == null || !wolfCell.IsValid)
        {
            Skip("no timber wolf or no cell for it");
            yield break;
        }
        ThingWithComps primary = gunner.equipment.Primary;
        if (primary == null || !primary.def.IsRangedWeapon)
        {
            ThingDef rifle = DefDatabase<ThingDef>.GetNamedSilentFail("Gun_BoltActionRifle");
            if (rifle == null)
            {
                Skip("no rifle def");
                yield break;
            }
            ThingWithComps own = gunner.equipment.Primary;
            if (own != null)
            {
                if (!gunner.equipment.TryTransferEquipmentToContainer(own, gunner.inventory.innerContainer))
                {
                    Skip("could not stow the colonist's own weapon");
                    yield break;
                }
                shooterOwnWeapon = own;
            }
            gunner.equipment.AddEquipment((ThingWithComps)Made(ThingMaker.MakeThing(rifle)));
        }
        shooterHome = gunner.Position;
        gunner.Position = perch;
        gunner.Notify_Teleported();
        View(map, sky, perch + new IntVec3(0, 0, -3), 20f);
        SelectThing(gunner);
        Speed(TimeSpeed.Normal);
        yield return For(1.5f);
        Pawn target = PawnGenerator.GeneratePawn(kind);
        wolf = target;
        GenSpawn.Spawn(target, wolfCell, map);
        target.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Manhunter, null, forced: true);
        gunner.drafter.Drafted = true;
        gunner.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.AttackStatic, target), JobTag.Misc);
        yield return Until(() =>
        {
            if (target.Spawned && bands.BandOf(target.Position) == bands.surfaceBand)
            {
                Vector3 vector = bands.Translate(target.Position, sky).ToVector3Shifted();
                LookAt((vector + gunner.DrawPos) * 0.5f);
            }
            return target.Dead || target.Downed || !target.Spawned;
        }, 16f);
        yield return For(2f);
        RemoveWolf();
    }
}
#endif
