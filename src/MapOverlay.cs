using System.Collections.Generic;
using Il2Cppframeworks.worldMap;
using Il2CppMoon.Forsaken;
using FPVector2 = Il2CppPhoton.Deterministic.FPVector2;
using Il2CppQuantum;
using UnityEngine;
using UnityEngine.UI;

namespace MapMarkers;

/// <summary>
/// The markers: one Image per point (resource node / enemy spawn point) under the map's own icon layer (mapScreen/.../chunks/icons, first sibling so
/// the game's icons draw on top). That layer is in map space: local (x, y) = world (x, z), no offset or scale (verified
/// against the player arrow, whose localPosition is the hero's world x/z). The game rescales the arrow on zoom to keep it
/// the same size on screen; the markers copy the arrow's scale.
///
/// Built as soon as the hero is in the world (Sync, once a second), not on the first map open: the Minimap mod mirrors
/// these markers onto the HUD while the map is closed. For the same reason visibility follows the hero's map (overworld /
/// sewer, from the hero position: the map screen's m_currentMapSlices is only updated when the map opens) and, with
/// "unexplored areas" off, re-checks whenever the explored area grows (FogOfWarSystem.FrameData.Version).
///
/// Markers of a type are created the first time the type is selected (enemy kinds have hundreds of spawn points each).
/// Enemy / animal points follow EnemyIndex.State: plain (fixed, or the current variant of a locked area), "?" (a variant
/// an unlocked area may roll when the hero gets there) or hidden. The variants, area locks and the
/// animals' story gates are re-read on map open, after a fast travel (RequestLiveCheck) and every 10 s while the map is
/// open (far areas re-roll each minute), never on a timer during play: the re-read visits all 223 areas (2-5 ms).
///
/// Cleared points (harvested nodes, killed enemies; ClearedState, read at the same moments) get a red cross badge baked into
/// their sprite (MarkerArt), like the "?" of possible animal spawns. Near the hero, markers above / below get the game's
/// height arrow (HeightArrows).
///
/// Points in caves and other interiors without a map are drawn at the interior's mouth (Entrances), one marker per type and
/// mouth for all of them, with the game's doorway glyph in the top-left corner. Its badge / liveness sum up the points: shown
/// while any is live, crossed when all live ones are cleared, "?" when all are only possible.
///
/// Types without a single point that could be shown right now (on the overworld / sewer map, live, and explored unless
/// "unexplored areas" is on) are hidden from the Filters row (UpdateAvailable, every Refresh): Harvesters while no area
/// holds a Harvester variant, Winged Brood before its story gate. Measured in game: ~1 ms over all 7780 points, the first
/// run 2-4 ms with the map lookups (Slices cache).
/// </summary>
internal static class MapOverlay
{
    public const string Prefix = "MRM_";

    private static MapScreen? _screen;
    private static RectTransform? _root, _topRoot;
    /// <summary>One map marker: a point on a map, or all points of one type behind one interior exit.</summary>
    private sealed class Marker
    {
        public MarkerType Type = null!;
        public readonly List<MapPoint> Points = new();
        public Place Place;
        public GameObject Go = null!;
    }

    /// <summary>Where a point is drawn: the map (TextureSliceData pointer; Zero = none) and position, its own or its
    /// interior's mouth (Exit set).</summary>
    private readonly record struct Place(System.IntPtr Slice, Vector3 Position, FPVector2 Sim, Exit? Exit);

    private static readonly List<Marker> Markers = new();
    /// <summary>Types whose markers exist. Sprites are set per marker in Refresh (badge state); a sprite that can't be resolved
    /// yet (item assets load on demand, see ItemIcons) sets _spritesMissing and Sync retries.</summary>
    private static readonly HashSet<MarkerType> Built = new();
    private static bool _spritesMissing;
    /// <summary>Per point: where it is drawn (see PlaceFor).</summary>
    private static readonly Dictionary<MapPoint, Place> PlaceOf = new();
    private static bool _liveCheckRequested;
    private static Vector3 _lastScale;
    private static float _lastSize;
    /// <summary>What the last Refresh saw: the hero's map and the fog version (only tracked with "unexplored" off).</summary>
    private static System.IntPtr _shownSlice;
    private static ulong _shownFogVersion;
    private static bool _shownUnexplored;
    private static float _nextSync, _nextOpenCheck;
    private const float OpenCheckInterval = 10f;
    private const float SyncInterval = 1f;
    /// <summary>Baked sprite size relative to the player arrow at MarkerScale 1: the picture (MarkerArt.IconFraction of the
    /// sprite) ends up ~1.4x the arrow. The user found the first baked size too small and needed 150..200% of it.</summary>
    private const float BaseScale = 1.4f / MarkerArt.IconFraction;

    public static void Attach(MapScreen screen)
    {
        if (_screen != null && _screen.Pointer == screen.Pointer && _root != null) return;
        Destroy();
        _screen = screen;
    }

    /// <summary>Build / catch up with world data, icons and visibility (map opened, or the background sync).</summary>
    public static void Ensure(MapScreen screen)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Attach(screen);
        bool changed = NodeIndex.Refresh();
        var f = NodeIndex.LocalFrame();
        if (f != null) changed |= EnemyIndex.Refresh(f) | AnimalIndex.Refresh(f);
        if (f != null) changed |= Entrances.Refresh(f, p => SliceAt(p) != System.IntPtr.Zero);
        if (changed || _root == null) Build();
        if (f != null) UpdateLive(f);
        if (f != null && changed) LogResourceStates();
        if (f != null) ClearedState.Refresh(f);
        HeightArrows.ReadFromGame(screen);
        Refresh();
        if (sw.ElapsedMilliseconds > 50) MapMarkersMod.Debug($"Markers built / refreshed in {sw.ElapsedMilliseconds} ms");
    }

    /// <summary>How many resource nodes the area variants hide or leave open (once per index build).</summary>
    private static void LogResourceStates()
    {
        int plain = 0, possible = 0, hidden = 0;
        var hiddenTypes = new SortedDictionary<string, int>();
        foreach (var n in NodeIndex.Nodes)
            switch (EnemyIndex.State(n.ContentSet))
            {
                case EnemyIndex.SpawnState.Plain: plain++; break;
                case EnemyIndex.SpawnState.Possible: possible++; break;
                default:
                    hidden++;
                    hiddenTypes[n.Type.Label] = hiddenTypes.TryGetValue(n.Type.Label, out int c) ? c + 1 : 1;
                    break;
            }
        var parts = new List<string>();
        foreach (var kv in hiddenTypes) parts.Add($"{kv.Key} {kv.Value}");
        MapMarkersMod.Debug($"Resource nodes: {plain} there, {possible} possible (area variant may roll), {hidden} not there now" +
                                      (hidden > 0 ? $" ({string.Join(", ", parts)})" : ""));
    }

    /// <summary>Once a second while in the world: build the markers if needed and refresh visibility when the hero's map
    /// or the explored area changed.</summary>
    public static void Sync()
    {
        if (Time.unscaledTime < _nextSync) return;
        _nextSync = Time.unscaledTime + SyncInterval;
        var hero = Heroes.Local();
        if (hero == null) return;
        var screen = _screen != null ? _screen : FindScreen();
        if (screen == null) return;
        if (_root == null || NodeIndex.IsStale())
        {
            FilterRow.Attach(screen);
            Ensure(screen);
            return;
        }
        bool liveChanged = false;
        // While the map is open: re-read the area variants every 10 s, the game's own re-evaluation step. Areas that are
        // neither loaded nor revealed re-roll each minute (analysis/world_states.md 5.2), so the map follows them.
        bool open = screen.LocalPlayerIndicator != null && screen.LocalPlayerIndicator.gameObject.activeInHierarchy;
        if (!open) _nextOpenCheck = Time.unscaledTime + OpenCheckInterval;
        else if (Time.unscaledTime >= _nextOpenCheck)
        {
            _nextOpenCheck = Time.unscaledTime + OpenCheckInterval;
            var f = NodeIndex.LocalFrame();
            if (f != null && UpdateLive(f))
            {
                liveChanged = true;
                MapMarkersMod.Debug("Area variants changed while the map is open; markers updated.");
            }
        }
        if (CaveGuard(hero.transform.position)) liveChanged = true;
        if (_liveCheckRequested)
        {
            _liveCheckRequested = false;
            var f = NodeIndex.LocalFrame();
            if (f != null) liveChanged = UpdateLive(f);
            if (f != null) { ClearedState.Refresh(f); liveChanged = true; }
        }
        bool unexplored = !Prefs.ShowInUnrevealed.Value;
        if (liveChanged || _spritesMissing || HeroSlice() != _shownSlice || unexplored != _shownUnexplored
            || (unexplored && FogVersion() != _shownFogVersion))
            Refresh();
    }

    private const float CaveGuardRadius = 20f, CaveGuardInterval = 2f;
    private static float _caveDistance;
    private static float _nextCaveCheck;
    private static long _cave, _lockedNoted;

    /// <summary>
    /// Waiting at a cave mouth for the right roll: a cave interior is only loaded (and locked) once the hero walks in, so
    /// until then it keeps re-rolling (verified: Orban Glades cave every 10-12 s at its mouth). While the hero stands within
    /// 20 m of the mouth of a cave that is not locked and can roll a selected enemy / animal type, its buckets are re-read
    /// every 2 s and their current roll is drawn plain (EnemyIndex.Watch), so the marker at the mouth turns from "?" to
    /// plain the moment the wanted variant rolls in (map and Minimap); each roll is logged. Costs a few node reads; off
    /// everywhere else. Known exception: the Mountain Pass wolf den never re-rolled, at its mouth or 150 m away (cause
    /// unknown; it is neither revealed nor loaded). True when the markers need a refresh.
    /// </summary>
    private static bool CaveGuard(Vector3 hero)
    {
        if (Time.unscaledTime < _nextCaveCheck) return false;
        _nextCaveCheck = Time.unscaledTime + CaveGuardInterval;
        long cave = 0, near = 0;
        if (AnySpawnFilter())
        {
            // The nearest cave mouth within the radius.
            float best = CaveGuardRadius * CaveGuardRadius;
            foreach (var exit in Entrances.All)
            {
                float d = (exit.Destination - hero).sqrMagnitude;
                if (d > best) continue;
                best = d;
                near = exit.Chunk;
                _caveDistance = Mathf.Sqrt(d);
            }
            if (near != 0 && !CanRollSelected(near)) near = 0;   // nothing the player looks for can roll in there
            if (near != 0 && EnemyIndex.HasUnlockedBucket(near)) cave = near;
        }
        // Near a cave that cannot re-roll: say why once (the markers already show its variant plain).
        if (near != 0 && cave == 0 && near != _lockedNoted)
        {
            var f0 = NodeIndex.LocalFrame();
            if (f0 != null) EnemyIndex.RefreshChunk(f0, near);
            string reason = EnemyIndex.LockReason(near);
            if (reason.Length > 0)
            {
                _lastRoll = "";
                LogRoll(near);
                MapMarkersMod.Debug($"Cave {Entrances.ChunkName(near)} cannot re-roll now: {reason}.");
            }
        }
        _lockedNoted = near != 0 && cave == 0 ? near : 0;
        bool changed = false;
        if (cave != _cave)
        {
            MapMarkersMod.Debug(cave != 0 ? $"At the mouth of {Entrances.ChunkName(cave)}: watching its variant every 2 s. " +
                                                      $"Variants the story allows: {DescribeEligible(cave)}. State: {StateOf(cave)}" : "Cave watch off.");
            _cave = cave;
            EnemyIndex.Watch(cave);
            changed = true;
        }
        var f = NodeIndex.LocalFrame();
        if (cave != 0 && f != null) changed |= EnemyIndex.RefreshChunk(f, cave);
        if (cave != 0) LogRoll(cave);
        return changed;
    }

    private static string StateOf(long cave)
    {
        var f = NodeIndex.LocalFrame();
        return f != null ? EnemyIndex.ChunkState(f, cave) : "?";
    }

    private static string DescribeEligible(long cave)
    {
        var parts = new List<string>();
        foreach (var bucket in EnemyIndex.EligibleIn(cave))
        {
            var variants = new List<string>();
            foreach (long set in bucket)
                variants.Add(KindsBySet().TryGetValue(set, out var k) && k.Count > 0 ? string.Join("+", k) : "(other)");
            parts.Add(variants.Count == 0 ? "none" : string.Join(" | ", variants));
        }
        return parts.Count == 0 ? "none" : string.Join("; ", parts);
    }

    private static string _lastRoll = "";
    private static float _lastRollAt, _lastStateLog;

    /// <summary>Who is in the watched cave right now (its current roll), logged when it changes.</summary>
    private static void LogRoll(long cave)
    {
        var kinds = new SortedSet<string>();
        foreach (long set in EnemyIndex.ActiveIn(cave))
            if (KindsBySet().TryGetValue(set, out var k)) kinds.UnionWith(k);
        string roll = kinds.Count == 0 ? "no enemies or animals of the marker types" : string.Join(", ", kinds);
        if (roll == _lastRoll)
        {
            if (Time.unscaledTime - _lastStateLog >= 60f)
            {
                _lastStateLog = Time.unscaledTime;
                MapMarkersMod.Debug($"Cave {Entrances.ChunkName(cave)} still {roll} after {Time.unscaledTime - _lastRollAt:0} s, you are {_caveDistance:0} m from its mouth. State: {StateOf(cave)}");
            }
            return;
        }
        string since = _lastRoll.Length > 0 ? $" ({Time.unscaledTime - _lastRollAt:0} s after the last roll)" : "";
        MapMarkersMod.Debug($"Cave {Entrances.ChunkName(cave)} rolled: {roll}{since}, you are {_caveDistance:0} m from its mouth");
        _lastRoll = roll;
        _lastRollAt = Time.unscaledTime;
    }

    private static Dictionary<long, SortedSet<string>>? _kindsBySet;

    /// <summary>Enemy kinds / animals with spawn points per content set (built once per index).</summary>
    private static Dictionary<long, SortedSet<string>> KindsBySet()
    {
        if (_kindsBySet != null) return _kindsBySet;
        _kindsBySet = new Dictionary<long, SortedSet<string>>();
        void Add(List<MapPoint> points)
        {
            foreach (var p in points)
            {
                if (!_kindsBySet!.TryGetValue(p.ContentSet, out var set)) _kindsBySet[p.ContentSet] = set = new SortedSet<string>();
                set.Add(p.Possible ? p.Type.Label + "?" : p.Type.Label);
            }
        }
        Add(EnemyIndex.Points);
        Add(AnimalIndex.Points);
        Add(EnemyIndex.Chests);
        Add(EnemyIndex.SpecialShinies);
        return _kindsBySet;
    }

    /// <summary>A variant the story allows in the cave has spawn points of a selected enemy / animal type.</summary>
    private static bool CanRollSelected(long cave)
    {
        foreach (var bucket in EnemyIndex.EligibleIn(cave))
            foreach (long set in bucket)
                if (TypesBySet().TryGetValue(set, out var types))
                    foreach (var t in types)
                        if (Prefs.IsSelected(t) && Prefs.GroupOn(t.Group)) return true;
        return false;
    }

    private static Dictionary<long, HashSet<MarkerType>>? _typesBySet, _certainBySet;

    /// <summary>
    /// Whatever an unlocked cave rolls, it has this type: some bucket of it has the type for certain (not just "may roll")
    /// in every variant the story allows. Then the marker at the mouth needs no "?" (e.g. caves whose every variant has
    /// its own chest spawners).
    /// </summary>
    private static bool GuaranteedInCave(MarkerType type, long cave)
    {
        TypesBySet();
        foreach (var variants in EnemyIndex.EligibleIn(cave, unlockedOnly: true))
        {
            if (variants.Count == 0) continue;
            bool all = true;
            foreach (long set in variants)
                if (!_certainBySet!.TryGetValue(set, out var types) || !types.Contains(type)) { all = false; break; }
            if (all) return true;
        }
        return false;
    }

    private static Dictionary<long, HashSet<MarkerType>> TypesBySet()
    {
        if (_typesBySet != null) return _typesBySet;
        _typesBySet = new Dictionary<long, HashSet<MarkerType>>();
        _certainBySet = new Dictionary<long, HashSet<MarkerType>>();
        foreach (var points in new[] { EnemyIndex.Points, AnimalIndex.Points, EnemyIndex.Chests, EnemyIndex.SpecialShinies })
            foreach (var p in points)
            {
                if (!_typesBySet.TryGetValue(p.ContentSet, out var set)) _typesBySet[p.ContentSet] = set = new HashSet<MarkerType>();
                set.Add(p.Type);
                if (p.Possible) continue;
                if (!_certainBySet.TryGetValue(p.ContentSet, out var certain)) _certainBySet[p.ContentSet] = certain = new HashSet<MarkerType>();
                certain.Add(p.Type);
            }
        return _typesBySet;
    }

    /// <summary>An enemy or animal type is selected (in a group that is switched on).</summary>
    private static bool AnySpawnFilter()
    {
        foreach (var t in MarkerTypes.All)
            if ((t is EnemyKind || t is AnimalKind || t is ChestKind || t is SpecialShinyKind) && Prefs.IsSelected(t)
                && Prefs.GroupOn(t.Group)) return true;
        return false;
    }

    /// <summary>Fast travel finished (HeroView.OnTeleportComplete): re-read area variants and story gates on the next sync.</summary>
    public static void RequestLiveCheck() => _liveCheckRequested = true;

    /// <summary>Active area variants + animal story gates. True when visible points may have changed.</summary>
    private static bool UpdateLive(Frame f)
    {
        bool changed = EnemyIndex.UpdateLive(f);
        if (AnimalIndex.UpdateGates(f))
        {
            DropMarkers<AnimalKind>();
            changed = true;
        }
        return changed;
    }

    private static float _nextScreenSearch;

    private static MapScreen? FindScreen()
    {
        if (Time.unscaledTime < _nextScreenSearch) return null;
        _nextScreenSearch = Time.unscaledTime + 5f;
        foreach (var s in Resources.FindObjectsOfTypeAll<MapScreen>())
            if (s != null && s.gameObject.scene.IsValid() && s.LocalPlayerIndicator != null) return s;
        return null;
    }

    /// <summary>A type was just switched on: make sure its markers exist and enemy liveness is current.</summary>
    public static void OnSelectionChanged(MarkerType type)
    {
        if ((type is EnemyKind || type is AnimalKind) && Prefs.IsSelected(type))
        {
            var f = NodeIndex.LocalFrame();
            if (f != null) UpdateLive(f);
        }
        Refresh();
    }

    /// <summary>Re-evaluate which markers are visible (selection, the hero's map, fog, live area variants).</summary>
    /// <summary>After a kill / harvest / opened chest (LiveUpdates): only the markers around the hero.</summary>
    public static void RefreshNearHero()
    {
        var hero = Heroes.Local();
        if (hero != null) Refresh(hero.transform.position);
    }

    public static void Refresh() => Refresh(null);

    /// <param name="near">Only markers within the hook ranges of this position (ClearedState.HookRange / HookEnemyRange),
    /// and no row update (LiveUpdates).</param>
    private static void Refresh(Vector3? near)
    {
        if (_screen == null || _root == null) return;
        foreach (var type in MarkerTypes.All)
            if (Prefs.IsSelected(type) && Prefs.GroupOn(type.Group) && !Built.Contains(type)) BuildType(type);
        _spritesMissing = false;
        if (_topRoot != null && _topRoot.GetSiblingIndex() != _topRoot.parent.childCount - 1) _topRoot.SetAsLastSibling();
        var hero = Heroes.Local();
        Vector3? heroPos = hero != null ? hero.transform.position : null;
        var currentPtr = HeroSlice();
        bool showUnrevealed = Prefs.ShowInUnrevealed.Value;

        // Explored state from the simulation (FogOfWarSystem bitsets, what the map paints). The map screen's own
        // MapFogOfWarData is no use: its updater only visits the cell under the player, so explored areas read "never".
        var frame = NodeIndex.LocalFrame();
        var f = showUnrevealed ? null : frame;
        foreach (var m in Markers)
        {
            // Keys always: a taken key's marker moves to its door, which can be far from the pickup (a dozen markers).
            if (near is Vector3 n && m.Type is not KeyKind)
            {
                float dx = m.Place.Position.x - n.x, dz = m.Place.Position.z - n.z;   // plain floats: Vector3 ops go through interop
                float r = m.Type is EnemyKind or AnimalKind ? ClearedState.HookEnemyRange : ClearedState.HookRange;
                if (dx * dx + dz * dz > r * r) continue;
            }
            bool visible = Prefs.IsSelected(m.Type) && Prefs.GroupOn(m.Type.Group) && currentPtr != System.IntPtr.Zero
                           && m.Place.Slice == currentPtr;
            // Plain points decide the badge: cross when all of them are cleared, "?" when none is certain (only possible
            // variants, or animal spawners that may roll the species).
            int shown = 0, plain = 0, certain = 0, cleared = 0;
            if (visible)
                foreach (var p in m.Points)
                {
                    var state = EnemyIndex.State(p.ContentSet);
                    if (state == EnemyIndex.SpawnState.Hidden) continue;
                    if (p.Source == PointSource.Whisper && ClearedState.IsActivated(p.EntityId)) continue;   // found: gone
                    if (p.Source == PointSource.Key && ClearedState.IsKeyGone(p.EntityId)) continue;        // taken: gone
                    if (p.Source == PointSource.KeyDoor && !ClearedState.IsKeyDoorPending(p.EntityId)) continue;   // key not taken / door open
                    if (p.Source == PointSource.Shortcut && ClearedState.IsShortcutOpen(p.EntityId)) continue;   // opened: gone
                    // Chests in a plague safe haven (Sacrament) never come back once opened (seen in game): hide them.
                    if (p.Source is PointSource.Chest or PointSource.ChestSpawner && EnemyIndex.InSafeHaven(p.ContentSet)
                        && ClearedState.IsCleared(p, frame)) continue;
                    shown++;
                    if (state != EnemyIndex.SpawnState.Plain) continue;
                    plain++;
                    if (!p.Possible) certain++;
                    if (ClearedState.IsCleared(p, frame)) cleared++;
                }
            visible &= shown > 0;
            // Undiscovered whispers ignore "unexplored areas": they would hardly ever show otherwise.
            if (visible && f != null && m.Type is not WhisperKind && !FogOfWarSystem.IsExplored(f, FogOfWarSystem.GetCellRef(f, m.Place.Sim)))
                visible = false;
            if (visible && certain == 0 && m.Place.Exit is { } caveExit && GuaranteedInCave(m.Type, caveExit.Chunk)) certain = 1;
            // "?" toggle off (FilterRow): only markers with something certain.
            if (visible && certain == 0 && !Prefs.ShowPossible.Value) visible = false;
            var go = m.Go;
            if (go.activeSelf != visible) go.SetActive(visible);
            if (!visible) continue;
            var badge = plain > 0 && cleared == plain ? Badge.Cross : certain > 0 ? Badge.None : Badge.Question;
            var sprite = m.Type.MarkerSprite(badge, m.Place.Exit != null);
            var img = go.GetComponent<Image>();
            if (sprite == null) _spritesMissing = true;
            else if (img.sprite == null || img.sprite.Pointer != sprite.Pointer) img.sprite = sprite;
            // Ladders / trapdoors are reached from the top: their arrow shows from 1.5 m (the game's 5 m misses short ones).
            if (heroPos is Vector3 hp)
                HeightArrows.Apply(go, m.Place.Position, hp, m.Place.Exit == null && NodeIndex.ShortcutsAbove.Contains(m.Points[0].EntityId) ? 1.5f : -1f);
        }
        if (near != null) return;
        _shownSlice = currentPtr;
        _shownUnexplored = !showUnrevealed;
        _shownFogVersion = f != null ? FogVersion() : 0;
        UpdateAvailable(showUnrevealed);
    }


    /// <summary>Types with at least one point that could be shown now (see class remarks); null = not computed yet.</summary>
    private static HashSet<MarkerType>? _available;
    /// <summary>Types with a point in a live or story-eligible variant (EnemyIndex.IsEligible): may roll in later.</summary>
    private static HashSet<MarkerType> _eligible = new();
    private static readonly List<MapPoint> AllPoints = new();

    public static bool IsAvailable(MarkerType type) => _available == null || _available.Contains(type);
    public static bool AvailabilityKnown => _available != null;
    public static bool IsEligible(MarkerType type) => _eligible.Contains(type);

    private static void UpdateAvailable(bool showUnrevealed)
    {
        var f = NodeIndex.LocalFrame();
        if (f == null) return;
        AllPoints.Clear();
        AllPoints.AddRange(NodeIndex.Nodes);
        AllPoints.AddRange(EnemyIndex.Points);
        AllPoints.AddRange(AnimalIndex.Points);
        AllPoints.AddRange(NodeIndex.Chests);
        AllPoints.AddRange(EnemyIndex.Chests);
        AllPoints.AddRange(EnemyIndex.SpecialShinies);
        AllPoints.AddRange(NodeIndex.Whispers);
        AllPoints.AddRange(NodeIndex.Keys);
        AllPoints.AddRange(NodeIndex.KeyDoors);
        AllPoints.AddRange(NodeIndex.Shortcuts);
        var available = new HashSet<MarkerType>();
        var eligible = new HashSet<MarkerType>();
        foreach (var p in AllPoints)
        {
            if (available.Contains(p.Type)) continue;
            var state = EnemyIndex.State(p.ContentSet);
            if (p.Source == PointSource.Whisper && ClearedState.IsActivated(p.EntityId)) state = EnemyIndex.SpawnState.Hidden;
            if (p.Source == PointSource.Key && ClearedState.IsKeyGone(p.EntityId)) state = EnemyIndex.SpawnState.Hidden;
            if (p.Source == PointSource.KeyDoor && !ClearedState.IsKeyDoorPending(p.EntityId)) state = EnemyIndex.SpawnState.Hidden;
            if (p.Source == PointSource.Shortcut && ClearedState.IsShortcutOpen(p.EntityId)) state = EnemyIndex.SpawnState.Hidden;
            bool live = Prefs.ShowPossible.Value ? state != EnemyIndex.SpawnState.Hidden
                                                 : state == EnemyIndex.SpawnState.Plain && !p.Possible;
            if (!live && (eligible.Contains(p.Type) || !EnemyIndex.IsEligible(p.ContentSet))) continue;
            var place = PlaceFor(p);
            if (place.Slice == System.IntPtr.Zero) continue;
            if (!showUnrevealed && p.Type is not WhisperKind && !FogOfWarSystem.IsExplored(f, FogOfWarSystem.GetCellRef(f, place.Sim))) continue;
            eligible.Add(p.Type);
            if (live) available.Add(p.Type);
        }
        AllPoints.Clear();
        if (_available != null && _available.SetEquals(available) && _eligible.SetEquals(eligible)) return;
        _available = available;
        _eligible = eligible;
        FilterRow.UpdateVisibility();
    }

    private static ulong FogVersion()
    {
        var f = NodeIndex.LocalFrame();
        return f?.FogOfWarData != null ? f.FogOfWarData.Version : 0;
    }

    /// <summary>The map the hero is on (the map screen always shows that one): same bounds rule as the nodes.</summary>
    private static System.IntPtr HeroSlice()
    {
        var hero = Heroes.Local();
        return hero != null && _screen != null ? SliceAt(hero.transform.position) : System.IntPtr.Zero;
    }

    /// <summary>Every frame: follow the zoom.</summary>
    public static void LateUpdate()
    {
        if (_screen == null || _root == null) return;
        var arrow = _screen.LocalPlayerIndicator;
        if (arrow == null) return;
        var scale = arrow.localScale;
        // The baked sprite has room for outline and shadow around the picture; scale so the picture itself is about as big as
        // the plain item pictures were (those had ~20% transparent margin of their own).
        float size = arrow.sizeDelta.x * Prefs.MarkerScale.Value * BaseScale;
        if (scale == _lastScale && size == _lastSize) return;
        _lastScale = scale;
        _lastSize = size;
        var sizeDelta = new Vector2(size, size);
        foreach (var m in Markers) Resize(m.Go);
    }

    private static void Resize(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.localScale = _lastScale;
        rt.sizeDelta = new Vector2(_lastSize, _lastSize);
    }

    /// <summary>
    /// Where a point is drawn. On a map: the smallest map texture (overworld or an interior such as the sewers) whose bounds
    /// contain it. The game's own GetSliceData is no use here: it answered the overworld for every node, sewer nodes and the
    /// small unmapped dungeons (placed far outside both maps in world space) included. Build 29466: overworld 1732x1732
    /// around (422, 614) holds 699 resource nodes, sewer 400x400 around (-38, -1166) 31, and 48 are in caves without a map;
    /// those go to their cave's mouth (Entrances), or nowhere (crucible, prologue ship).
    /// </summary>
    private static Place PlaceFor(MapPoint p)
    {
        if (PlaceOf.TryGetValue(p, out var place)) return place;
        var slice = SliceAt(p.Position);
        if (slice != System.IntPtr.Zero) place = new Place(slice, p.Position, p.SimPosition, null);
        else if (Entrances.For(p.Position) is { } exit)
            place = new Place(SliceAt(exit.Destination), exit.Destination, exit.SimDestination, exit);
        else place = new Place(System.IntPtr.Zero, p.Position, p.SimPosition, null);
        PlaceOf[p] = place;
        return place;
    }

    /// <summary>Map bounds (overworld + interiors), read once per map screen: reading them through interop for every point
    /// cost more than the rest of a refresh.</summary>
    private static readonly List<(System.IntPtr Ptr, float MinX, float MaxX, float MinZ, float MaxZ, float Area)> Slices = new();

    private static System.IntPtr SliceAt(Vector3 p)
    {
        if (Slices.Count == 0) ReadSlices();
        var ptr = System.IntPtr.Zero;
        float bestArea = float.MaxValue;
        foreach (var s in Slices)
        {
            if (p.x < s.MinX || p.x > s.MaxX || p.z < s.MinZ || p.z > s.MaxZ || s.Area >= bestArea) continue;
            bestArea = s.Area;
            ptr = s.Ptr;
        }
        return ptr;
    }

    private static void ReadSlices()
    {
        void Add(TextureSliceData? slice)
        {
            if (slice == null) return;
            var c = slice.BoundsCenter;
            var size = slice.BoundsSize;
            Slices.Add((slice.Pointer, c.x - size.x * 0.5f, c.x + size.x * 0.5f, c.z - size.y * 0.5f, c.z + size.y * 0.5f, size.x * size.y));
        }
        Add(_screen!.OverworldMapSlices);
        var interiors = _screen.InteriorMapSlices;
        if (interiors != null)
            foreach (var slice in interiors) Add(slice);
    }

    private static void Build()
    {
        DestroyMarkers();
        if (_screen == null) return;
        var layer = _screen.IconsCanvas.transform;
        RemoveLeftovers(layer);

        var rootGo = new GameObject(Prefix + "Markers");
        _root = rootGo.AddComponent<RectTransform>();
        // Interop SetParent(Transform, bool) throws "Method unstripping failed"; Unity 6 has the 3-argument native one.
        _root.SetParent(layer, false, false);
        _root.anchorMin = _root.anchorMax = _screen.LocalPlayerIndicator.anchorMin;
        _root.pivot = new Vector2(0.5f, 0.5f);
        _root.sizeDelta = Vector2.zero;
        _root.localPosition = Vector3.zero;
        _root.SetAsFirstSibling();
        // A second root drawn after the game's icons: undiscovered whispers sit on the game's own whisper icon.
        var topGo = new GameObject(Prefix + "MarkersTop");
        _topRoot = topGo.AddComponent<RectTransform>();
        _topRoot.SetParent(layer, false, false);
        _topRoot.anchorMin = _topRoot.anchorMax = _root.anchorMin;
        _topRoot.pivot = new Vector2(0.5f, 0.5f);
        _topRoot.sizeDelta = Vector2.zero;
        _topRoot.localPosition = Vector3.zero;
        _topRoot.SetAsLastSibling();
        Built.Clear();
        _lastScale = Vector3.zero;
        _lastSize = 0f;
    }

    private static void BuildType(MarkerType type)
    {
        Built.Add(type);
        var points = type is EnemyKind ? EnemyIndex.Points : type is AnimalKind ? AnimalIndex.Points
            : type is ChestKind ? ChestPoints() : type is SpecialShinyKind ? EnemyIndex.SpecialShinies
            : type is WhisperKind ? NodeIndex.Whispers : type is KeyKind ? KeyPoints()
            : type is ShortcutType ? NodeIndex.Shortcuts : NodeIndex.Nodes;
        var caves = new Dictionary<Exit, Marker>();
        int n = 0, inCaves = 0;
        foreach (var point in points)
        {
            if (point.Type != type) continue;
            var place = PlaceFor(point);
            if (place.Slice == System.IntPtr.Zero) continue;
            if (place.Exit is { } exit)
            {
                inCaves++;
                if (caves.TryGetValue(exit, out var shared)) { shared.Points.Add(point); continue; }
            }
            var m = new Marker { Type = type, Place = place, Go = NewMarker(point.Name, place.Position, type is WhisperKind) };
            m.Points.Add(point);
            Markers.Add(m);
            if (place.Exit != null) caves[place.Exit] = m;
            n++;
        }
        if (n > 0 && type is not ResourceType || caves.Count > 0)
            MapMarkersMod.Debug($"{type.Label}: {n} markers created ({inCaves} points in {caves.Count} caves at their entrances).");
    }

    private static List<MapPoint> KeyPoints()
    {
        var list = new List<MapPoint>(NodeIndex.Keys);
        list.AddRange(NodeIndex.KeyDoors);
        return list;
    }

    private static List<MapPoint> ChestPoints()
    {
        var list = new List<MapPoint>(NodeIndex.Chests);
        list.AddRange(EnemyIndex.Chests);
        return list;
    }

    private static GameObject NewMarker(string name, Vector3 position, bool onTop = false)
    {
        var go = new GameObject(Prefix + name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(onTop && _topRoot != null ? _topRoot : _root, false, false);
        rt.localPosition = new Vector3(position.x, position.z, 0f);
        var img = go.AddComponent<Image>();
        img.preserveAspect = true;
        img.raycastTarget = false;
        go.SetActive(false);
        if (_lastSize > 0f) Resize(go);
        return go;
    }

    /// <summary>Destroy the markers of one kind of type so the next Refresh rebuilds them from new points.</summary>
    private static void DropMarkers<T>() where T : MarkerType
    {
        for (int i = Markers.Count - 1; i >= 0; i--)
        {
            if (Markers[i].Type is not T) continue;
            Object.Destroy(Markers[i].Go);
            Markers.RemoveAt(i);
        }
        var stale = new List<MapPoint>();
        foreach (var p in PlaceOf.Keys) if (p.Type is T) stale.Add(p);
        foreach (var p in stale) PlaceOf.Remove(p);
        Built.RemoveWhere(t => t is T);
        _kindsBySet = null;
        _typesBySet = null;
    }

    private static void RemoveLeftovers(Transform layer)
    {
        for (int i = layer.childCount - 1; i >= 0; i--)
        {
            var child = layer.GetChild(i);
            if (child.name.StartsWith(Prefix)) Object.Destroy(child.gameObject);
        }
    }

    private static void DestroyMarkers()
    {
        if (_root != null) Object.Destroy(_root.gameObject);
        if (_topRoot != null) Object.Destroy(_topRoot.gameObject);
        _topRoot = null;
        _root = null;
        Markers.Clear();
        PlaceOf.Clear();
        Slices.Clear();
        _kindsBySet = null;
        _typesBySet = null;
        Built.Clear();
        _available = null;
        _eligible.Clear();
    }

    public static void Destroy()
    {
        DestroyMarkers();
        _screen = null;
    }
}
