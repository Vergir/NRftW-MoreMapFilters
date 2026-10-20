using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppQuantum;
using UnityEngine;

namespace MapMarkers;

/// <summary>
/// Which map points are cleared right now: harvested resource nodes, dead fixed enemies, spawners whose enemy was killed
/// (analysis/cleared_state.md; keys and offsets verified in game by probe ResourceMapProbe 0.4.0). They come back when the
/// fog creeps back over them (~45 real minutes after the area was first revealed), not on a timer.
///
/// Source: the save-data asset GameSaveData.Get(frame) (a frame-local dynamic asset, fine from the main thread).
/// RealmContents (+0x20) holds two Lists the game fills when an area unloads and on every autosave, so the area the hero
/// is in can lag by up to one autosave:
/// - SerializedStaticData (+0x110): 16-byte structs { long EntityId, ref EcsPersistentData }.
///     LinkedFarmableResourceData / StaticPickupData.EcsPersistentData (key = InstanceGuid): LastDespawnTime @0x18 != 0
///     = harvested / picked.
///     NpcSpawnerData.EcsPersistentData (key = NpcInstanceData.Guid): Spawned @0x18 and (NpcWasDead @0x38 or
///     LastTimeWithoutEntity @0x20 != 0) = dead.
/// - SerializedDynamicData (+0x118): 0x28-byte structs { EntityId, SourceEntityData, ref EcsPersistentData @0x10, ... }.
///     DynamicNpcSpawnerData.EcsPersistentData: SpawnerId @0x20 (= DynamicSpawnerData.Guid), Spawned @0x28; a spawner
///     whose records are all not spawned = cleared.
/// The structs hold a reference field, so the lists are read raw (List: _items @0x10, _size @0x18; array data @0x20).
/// The sim thread rewrites them in place during unloads / saves: read on map open / after fast travel, inside try/catch.
///
/// Loaded areas: the save lists only catch up on the next unload / autosave (a herb picked a minute ago is not in them
/// yet, and an old entry can be stale), so resource nodes and pickups that are loaded are read live and override the save:
/// IdentityComponentAPI.ResolveEntity(f, InstanceGuid) -> RespawnablesAPI.IsRespawnableCurrentlySpawned (the game's own
/// pattern, RootsThatBindQuestDirector.SetDireNettlesRespawnState). Enemies in loaded areas likewise (LiveEnemies).
///
/// The saved stamp alone is not enough: the game only re-checks it when the area loads (StaticSpawnerSystem /
/// DynamicSpawnerSystem), so an area the hero left long ago keeps its "harvested" / "dead" records although everything
/// there comes back on arrival. IsCleared therefore also runs the game's respawn test: a spot stays cleared only while a
/// revealed fog cell is still near it (RespawnablesAPI.GetEffectiveRespawnConfig: FogOfWarSystem.IsAnyRevealedInRange,
/// radius 1 for resources and placements; DynamicSpawnerSystem.CheckIfSpawnerIsFullyFogged: the spawner's own cell).
/// Revealed cells creep back ~45 real minutes after an area was first revealed. Where there is no fog cell a 1 game hour
/// timer decides instead; those keep the saved state.
/// </summary>
internal static class ClearedState
{
    private static readonly HashSet<long> Resources = new();
    private static readonly HashSet<long> Placements = new();
    private static readonly HashSet<long> Spawners = new();
    /// <summary>Opened chests: static ones by InstanceGuid, spawned ones by their spawner's DynamicSpawnerData.Guid.</summary>
    private static readonly HashSet<long> StaticChests = new(), ChestSpawners = new();
    private static readonly Dictionary<IntPtr, string> ClassNames = new();
    private static bool _errorLogged, _timingLogged;

    /// <summary>Activated whispers (per world), re-read with the cleared state and after an activation (LiveUpdates).</summary>
    private static readonly HashSet<long> ActivatedWhispers = new();

    public static bool IsActivated(long whisper) => ActivatedWhispers.Contains(whisper);

    /// <summary>For the log: whispers activated, keys collected, shortcuts opened.</summary>
    public static string GoneCounts() => $"whispers {ActivatedWhispers.Count}, keys {KeysGone.Count}, doors {KeyDoorsPending.Count}, shortcuts {ShortcutsOpen.Count}";

    /// <summary>Key pickups that are gone for good (see KeyKind), and the door puzzles' saved states.</summary>
    private static readonly HashSet<long> KeysGone = new();
    private static readonly Dictionary<long, long> PuzzleStates = new();

    public static bool IsKeyGone(long pickup) => KeysGone.Contains(pickup);

    /// <summary>Keys taken whose door is not open yet: their door gets the marker (NodeIndex.KeyDoors).</summary>
    private static readonly HashSet<long> KeyDoorsPending = new();

    public static bool IsKeyDoorPending(long pickup) => KeyDoorsPending.Contains(pickup);

    /// <summary>Opened shortcuts (ShortcutData) and broken walls from the save.</summary>
    private static readonly HashSet<long> ShortcutsOpen = new(), BrokenWalls = new();

    public static bool IsShortcutOpen(long guid) => ShortcutsOpen.Contains(guid);

    /// <summary>
    /// Opened = the record's own IsTriggered (live state while loaded, else its save entry: ladder KickedDown, door IsOpen,
    /// lever state != initial), a puzzle in one of its open states (live PuzzleAPI.GetStateId, else the saved state,
    /// else its initial one), a breakable wall despawned (live / saved LastDespawnTime). Remembered per world.
    /// </summary>
    private static void ReadShortcuts(Frame f)
    {
        ShortcutsOpen.Clear();
        if (NodeIndex.ShortcutRecords.Count == 0) return;
        string world = FilterRow.World();
        bool remembered = false;
        foreach (var kv in NodeIndex.ShortcutRecords)
        {
            long guid = kv.Key;
            var (record, entry) = kv.Value;
            bool open = Prefs.IsShortcutOpened(world, guid);
            if (!open)
            {
                switch (entry.Record)
                {
                    case ShortcutRecord.Ladder: open = record.TryCast<LadderStaticInstanceData>()?.IsTriggered(f) ?? false; break;
                    case ShortcutRecord.Door: open = record.TryCast<DoorStaticInstanceData>()?.IsTriggered(f) ?? false; break;
                    case ShortcutRecord.Lever: open = record.TryCast<LeverStaticInstanceData>()?.IsTriggered(f) ?? false; break;
                    case ShortcutRecord.Puzzle:
                    {
                        var e = IdentityComponentAPI.ResolveEntity(f, guid);
                        long state = f.Exists(e) ? PuzzleAPI.GetStateId(f, e)
                                                 : PuzzleStates.TryGetValue(guid, out long saved) ? saved : entry.InitialState;
                        open = state >= 0 && state < 31 && (entry.OpenMask & (1 << (int)state)) != 0;
                        break;
                    }
                    case ShortcutRecord.Destructible:
                    {
                        var e = IdentityComponentAPI.ResolveEntity(f, guid);
                        open = f.Exists(e) ? !RespawnablesAPI.IsRespawnableCurrentlySpawned(f, e) : BrokenWalls.Contains(guid);
                        break;
                    }
                }
                if (open) remembered |= Prefs.RememberShortcutOpened(world, guid);
            }
            if (open) ShortcutsOpen.Add(guid);
        }
        if (remembered) MelonLoader.MelonPreferences.Save();
        MapMarkersMod.Debug($"Shortcuts: {ShortcutsOpen.Count} of {NodeIndex.ShortcutRecords.Count} opened in this world");
    }

    private static readonly HashSet<string> GuardLogged = new();

    /// <summary>One read that must not take the others down (game APIs called from the main thread).</summary>
    private static void Guarded(string what, Action<Frame> read, Frame f)
    {
        try { read(f); }
        catch (Exception e)
        {
            if (GuardLogged.Add(what)) MapMarkersMod.Log.Warning($"Reading {what} failed ({e.GetType().Name}: {e.Message}).");
        }
    }

    /// <summary>
    /// Per key (KeyKind): taken = its door puzzle left the first state (0 = key there, 1 = key taken, 2 = door open; live
    /// while loaded, else saved, -2 = no record: not touched) or the key is in the world's key items; open = puzzle state 2,
    /// or for the two keys without a puzzle their door / lever record's IsTriggered. Taken and not open: the marker moves
    /// to the door. Open: nothing left, remembered per world. A pickup that fails the game's spawn conditions is not there
    /// now (the Western Bridge Key outside its quest).
    /// </summary>
    private static void ReadKeys(Frame f)
    {
        KeysGone.Clear();
        KeyDoorsPending.Clear();
        if (NodeIndex.KeyData.Count == 0) return;
        string world = FilterRow.World();
        var owner = f.InventorySystem.RealmInventoryOwner;
        bool remembered = false;
        foreach (var kv in NodeIndex.KeyData)
        {
            long pickup = kv.Key;
            if (Prefs.IsKeyUsed(world, pickup)) { KeysGone.Add(pickup); continue; }
            var (item, puzzle) = KeyKind.Pickups[pickup];
            long state = -1;
            if (puzzle != 0)
            {
                var e = IdentityComponentAPI.ResolveEntity(f, puzzle);
                state = f.Exists(e) ? PuzzleAPI.GetStateId(f, e) : PuzzleStates.TryGetValue(puzzle, out long saved) ? saved : -2;
            }
            var keyRef = new AssetRefHeroItemData { Id = new AssetGuid(item) };
            bool taken = state >= 1 || InventoryAPI.GetItemMatchingCount(f, owner, keyRef, false) >= 1;
            bool open = puzzle != 0 ? state >= 2
                      : KeyKind.Doors.TryGetValue(pickup, out var door) && LockOpen(f, door.Lock);
            if (open)
            {
                KeysGone.Add(pickup);
                remembered |= Prefs.RememberKeyUsed(world, pickup);
            }
            else if (taken)
            {
                KeysGone.Add(pickup);
                KeyDoorsPending.Add(pickup);
            }
            else if (!RespawnablesAPI.DoesPassSpawnRequirements(f, new IRespawnableEntity(kv.Value.Pointer))) KeysGone.Add(pickup);
        }
        MapMarkersMod.Debug($"Keys: {KeysGone.Count} of {NodeIndex.KeyData.Count} taken, {KeyDoorsPending.Count} doors still to open");
        if (remembered) MelonLoader.MelonPreferences.Save();
    }

    private static bool LockOpen(Frame f, long guid)
    {
        if (!NodeIndex.KeyLocks.TryGetValue(guid, out var record)) return false;
        return record.TryCast<DoorStaticInstanceData>()?.IsTriggered(f)
               ?? record.TryCast<LeverStaticInstanceData>()?.IsTriggered(f) ?? false;
    }

    private static void ReadWhispers(Frame f)
    {
        ActivatedWhispers.Clear();
        foreach (var kv in NodeIndex.WhisperData)
            if (Il2Cpp.CerimWhisperViewAPI.IsActivated(f, kv.Value)) ActivatedWhispers.Add(kv.Key);
    }

    public static bool IsCleared(MapPoint p, Frame? f)
    {
        bool saved = p.Source switch
        {
            PointSource.Resource => Resources.Contains(p.EntityId),
            PointSource.Placement => Placements.Contains(p.EntityId),
            PointSource.Spawner => Spawners.Contains(p.EntityId),
            PointSource.Chest => StaticChests.Contains(p.EntityId),
            PointSource.ChestSpawner => ChestSpawners.Contains(p.EntityId),
            _ => false,
        };
        if (!saved || f == null) return saved;
        var cell = FogOfWarSystem.GetCellRef(f, p.SimPosition);
        if (!cell.IsValid) return true;
        // Spawners check their own cell (DynamicSpawnerSystem.CheckIfSpawnerIsFullyFogged), static entities a 1-cell ring.
        bool spawner = p.Source is PointSource.Spawner or PointSource.ChestSpawner;
        return FogOfWarSystem.IsAnyRevealedInRange(f, cell, spawner ? 0 : 1);
    }

    public static void Refresh(Frame f)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Resources.Clear();
        Placements.Clear();
        Spawners.Clear();
        StaticChests.Clear();
        ChestSpawners.Clear();
        PuzzleStates.Clear();
        BrokenWalls.Clear();
        try
        {
            var realm = GameSaveData.Get(f)?.RealmContents;
            if (realm == null) return;
            IntPtr realmPtr = realm.Pointer;

            IntPtr list = Marshal.ReadIntPtr(realmPtr, 0x110);
            if (list != IntPtr.Zero)
            {
                IntPtr items = Marshal.ReadIntPtr(list, 0x10);
                int n = Marshal.ReadInt32(list, 0x18);
                for (int i = 0; i < n; i++)
                {
                    long id = Marshal.ReadInt64(items, 0x20 + i * 16);
                    IntPtr ecs = Marshal.ReadIntPtr(items, 0x20 + i * 16 + 8);
                    if (ecs == IntPtr.Zero) continue;
                    switch (ClassOf(ecs))
                    {
                        case "LinkedFarmableResourceData.EcsPersistentData":
                        case "DestructibleData.EcsPersistentData":   // breakable walls: LastDespawnTime @0x18
                            if (Marshal.ReadInt64(ecs, 0x18) != 0) BrokenWalls.Add(id);
                            break;
                        case "PuzzleData.EcsPersistentData":   // door puzzle (keys): long State @0x18
                            PuzzleStates[id] = Marshal.ReadInt64(ecs, 0x18);
                            break;
                        case "LinkedContainerData.EcsPersistentData":   // static chest: LastDespawnTime @0x18, IsOpen @0x28
                            if (Marshal.ReadInt64(ecs, 0x18) != 0 || Marshal.ReadInt32(ecs, 0x28) != 0) StaticChests.Add(id);
                            break;
                        case "StaticPickupData.EcsPersistentData":
                            if (Marshal.ReadInt64(ecs, 0x18) != 0) Resources.Add(id);
                            break;
                        case "NpcSpawnerData.EcsPersistentData":
                            bool spawned = Marshal.ReadByte(ecs, 0x18) != 0;
                            bool dead = Marshal.ReadByte(ecs, 0x38) != 0 || Marshal.ReadInt64(ecs, 0x20) != 0;
                            if (spawned && dead) Placements.Add(id);
                            break;
                    }
                }
            }

            list = Marshal.ReadIntPtr(realmPtr, 0x118);
            if (list != IntPtr.Zero)
            {
                IntPtr items = Marshal.ReadIntPtr(list, 0x10);
                int n = Marshal.ReadInt32(list, 0x18);
                var alive = new HashSet<long>();
                var closedChests = new HashSet<long>();
                for (int i = 0; i < n; i++)
                {
                    IntPtr ecs = Marshal.ReadIntPtr(items, 0x20 + i * 0x28 + 0x10);
                    if (ecs == IntPtr.Zero) continue;
                    string cls = ClassOf(ecs);
                    if (cls == "ContainerData.EcsPersistentData")
                    {
                        // Spawned chest: IsOpen @0x18; DynamicData @0x30 {HasValue, Guid @0x38, SpawnerId @0x40}.
                        if (Marshal.ReadByte(ecs, 0x30) == 0) continue;
                        long chestSpawner = Marshal.ReadInt64(ecs, 0x40);
                        if (Marshal.ReadInt32(ecs, 0x18) != 0) ChestSpawners.Add(chestSpawner);
                        else closedChests.Add(chestSpawner);
                        continue;
                    }
                    if (cls != "DynamicNpcSpawnerData.EcsPersistentData") continue;
                    long spawner = Marshal.ReadInt64(ecs, 0x20);
                    if (Marshal.ReadByte(ecs, 0x28) != 0) alive.Add(spawner);
                    else Spawners.Add(spawner);
                }
                Spawners.ExceptWith(alive);   // cleared only when none of its records has a living enemy
                ChestSpawners.ExceptWith(closedChests);
            }

            double saved = sw.Elapsed.TotalMilliseconds;
            Guarded("whispers", ReadWhispers, f);
            Guarded("keys", ReadKeys, f);
            Guarded("shortcuts", ReadShortcuts, f);
            int loaded = LiveNodes(f, LoadedRange);
            double nodesDone = sw.Elapsed.TotalMilliseconds;
            LiveEnemies(f, LoadedRange);
            double enemiesDone = sw.Elapsed.TotalMilliseconds;
            if (!_timingLogged)
            {
                _timingLogged = true;
                MapMarkersMod.Debug($"Cleared state: save read in {saved:F1} ms, {loaded} loaded nodes read live, nodes done at {nodesDone:F1}, enemies done at {enemiesDone:F1}, total {sw.Elapsed.TotalMilliseconds:F1} ms");
            }
        }
        catch (Exception e)
        {
            if (_errorLogged) return;
            _errorLogged = true;
            MapMarkersMod.Log.Warning($"Reading cleared spawns failed ({e.GetType().Name}: {e.Message}); markers stay uncrossed.");
        }
    }

    /// <summary>Only the live part (loaded areas around the hero), on top of the last full Refresh: LiveUpdates after a
    /// kill / harvest / opened chest.</summary>
    public static void RefreshLive(Frame f)
    {
        try
        {
            LiveNodes(f, HookRange);
            LiveEnemies(f, HookEnemyRange);
            Guarded("whispers", ReadWhispers, f);
            Guarded("keys", ReadKeys, f);
            Guarded("shortcuts", ReadShortcuts, f);
        }
        catch (Exception e)
        {
            if (_errorLogged) return;
            _errorLogged = true;
            MapMarkersMod.Log.Warning($"Reading cleared spawns failed ({e.GetType().Name}: {e.Message}); markers stay uncrossed.");
        }
    }

    /// <summary>Resource nodes and pickups near the hero, read live. Returns how many were loaded.</summary>
    private static int LiveNodes(Frame f, float range)
    {
        int loaded = 0;
        var heroForNodes = Heroes.Local();
        var heroNodePos = heroForNodes != null ? heroForNodes.transform.position : Vector3.zero;
        foreach (var node in NodeIndex.Nodes)
        {
            if (heroForNodes == null || !Near(node.Position, heroNodePos, range)) continue;
            var e = IdentityComponentAPI.ResolveEntity(f, node.EntityId);
            if (!f.Exists(e)) continue;   // not loaded: the save decides
            loaded++;
            if (RespawnablesAPI.IsRespawnableCurrentlySpawned(f, e)) Resources.Remove(node.EntityId);
            else Resources.Add(node.EntityId);
        }
        return loaded;
    }

    private static int _npcSpawnerIndex = -1, _dynamicSpawnerIndex = -1, _spawnedObjectIndex = -1, _npcObjectIndex = -1,
        _containerIndex = -1;
    private static (int, int, int, int) _lastCounts = (-1, 0, 0, 0);
    /// <summary>Placements farther than this from the hero are never loaded (they are looked up one by one).</summary>
    private const float LoadedRange = 250f;
    /// <summary>After a hook (LiveUpdates): harvesting / pickups / chests happen where the hero stands; an enemy dies away
    /// from its spawn point (chases, ranged kills), so enemies get a wider ring.</summary>
    public const float HookRange = 25f, HookEnemyRange = 60f;

    /// <summary>
    /// Loaded enemy spawn points, read live (analysis/cleared_state.md 1.2 / 1.3), so a kill shows its cross on the next map
    /// open instead of after the area unloads or the game autosaves:
    /// - placement: IdentityComponentAPI.ResolveEntity(NpcInstanceData.Guid) = the spawner entity; NpcSpawnerComponent
    ///   {Spawned @0x14, MainNpc.EntityInstanceRef @0x60}; cleared = spawned and the NPC is gone or dead (AiUtils.ExistsAlive);
    /// - chests and special shinies (both ContainerData objects; a looted one stays with ContainerComponent.IsOpen @0x4 = 1,
///   verified in game for chests and a special shiny) by the same root walk;
/// - dynamic spawner: every loaded one is in the DynamicSpawnerComponent buffer {DynamicSpawnerData ref @0x0,
    ///   SpawnedEntityGuid @0x18}; the per-spawn object is found by its DynamicallySpawnedObjectComponent.Guid (@0x8; the game
    ///   scans for it the same way, DynamicSpawnerSystem.TryFindDynamicObject) -> DynamicSpawningAPI
    ///   .GetSpawnedEntityInstanceRef -> alive or not. No spawn yet, or no object found, leaves the save's verdict.
    /// Components are read through the non-generic FrameBaseUnsafe.GetPointer(entity, index) and FrameBase.GetDataBuffer(index)
    /// (ComponentTypeId.GetComponentIndex(name)); ~1-2 ms for what is loaded around the hero.
    /// </summary>
    private static unsafe void LiveEnemies(Frame f, float range)
    {
        var swIndex = System.Diagnostics.Stopwatch.StartNew();
        if (_npcSpawnerIndex < 0) _npcSpawnerIndex = ComponentTypeId.GetComponentIndex("NpcSpawnerComponent");
        if (_dynamicSpawnerIndex < 0) _dynamicSpawnerIndex = ComponentTypeId.GetComponentIndex("DynamicSpawnerComponent");
        if (_spawnedObjectIndex < 0) _spawnedObjectIndex = ComponentTypeId.GetComponentIndex("DynamicallySpawnedObjectComponent");
        if (_npcObjectIndex < 0) _npcObjectIndex = ComponentTypeId.GetComponentIndex("DynamicNpcSpawnerComponent");
        if (_containerIndex < 0) _containerIndex = ComponentTypeId.GetComponentIndex("ContainerComponent");
        double tIndex = swIndex.Elapsed.TotalMilliseconds;
        var hero = Heroes.Local();
        if (hero == null) return;
        var heroPos = hero.transform.position;
        int placements = 0, deadPlacements = 0, spawners = 0, deadSpawners = 0, looked = 0, chests = 0, openChests = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var seen = new HashSet<long>();
        foreach (var list in new[] { EnemyIndex.Points, AnimalIndex.Points })
            foreach (var p in list)
            {
                if (p.Source != PointSource.Placement || !Near(p.Position, heroPos, range)
                    || !seen.Add(p.EntityId)) continue;
                looked++;
                var e = IdentityComponentAPI.ResolveEntity(f, p.EntityId);
                if (!f.Exists(e) || !f.Has(e, _npcSpawnerIndex)) continue;   // not loaded: the save decides
                byte* c = (byte*)f.Unsafe.GetPointer(e, _npcSpawnerIndex);
                if (c == null || c[0x14] == 0) continue;   // not spawned (yet)
                placements++;
                if (Alive(f, *(EntityRef*)(c + 0x60))) Placements.Remove(p.EntityId);
                else { Placements.Add(p.EntityId); deadPlacements++; }
            }

        double tPlacements = sw.Elapsed.TotalMilliseconds;
        var objects = new Dictionary<long, EntityRef>();
        foreach (var (e, data) in Components(f, _spawnedObjectIndex)) objects[*(long*)(data + 0x8)] = e;
        double tObjects = sw.Elapsed.TotalMilliseconds - tPlacements;
        var st = f.InstanceDataStorage;
        foreach (var (_, data) in Components(f, _dynamicSpawnerIndex))
        {
            long spawned = *(long*)(data + 0x18);
            if (spawned == 0 || !objects.TryGetValue(spawned, out var obj)) continue;
            bool isChest = f.Has(obj, _containerIndex);
            // Shiny / other loot objects have neither; only NPC spawns have DynamicNpcSpawnerComponent.
            if (!isChest && !f.Has(obj, _npcObjectIndex)) continue;
            var spawner = st.Get(Ref<DynamicSpawnerDataRef>(*(int*)data));
            if (spawner == null) continue;
            if (isChest)
            {
                // ContainerComponent.IsOpen @0x4 (set by ContainerData.Interact; the entity stays).
                byte* cc = (byte*)f.Unsafe.GetPointer(obj, _containerIndex);
                chests++;
                if (cc != null && *(int*)(cc + 0x4) != 0) { ChestSpawners.Add(spawner.Guid); openChests++; }
                else ChestSpawners.Remove(spawner.Guid);
                continue;
            }
            spawners++;
            if (Alive(f, DynamicSpawningAPI.GetSpawnedEntityInstanceRef(f, obj))) Spawners.Remove(spawner.Guid);
            else { Spawners.Add(spawner.Guid); deadSpawners++; }
        }
        // Logging costs more than the reads (~ms per line): only when the picture changes.
        // Static chests near the hero: same ContainerComponent.IsOpen.
        foreach (var p in NodeIndex.Chests)
        {
            if (!Near(p.Position, heroPos, range)) continue;
            var e = IdentityComponentAPI.ResolveEntity(f, p.EntityId);
            if (!f.Exists(e) || !f.Has(e, _containerIndex)) continue;
            byte* cc = (byte*)f.Unsafe.GetPointer(e, _containerIndex);
            chests++;
            if (cc != null && *(int*)(cc + 0x4) != 0) { StaticChests.Add(p.EntityId); openChests++; }
            else StaticChests.Remove(p.EntityId);
        }
        var counts = (placements, deadPlacements, spawners, deadSpawners + 1000 * openChests + 100000 * chests);
        if (counts != _lastCounts)
        {
            _lastCounts = counts;
            MapMarkersMod.Debug($"Loaded enemy spawns read live: {placements} placements ({deadPlacements} dead), " +
                                          $"{spawners} spawners ({deadSpawners} cleared), {chests} chests ({openChests} open); {looked} placements looked up in {tPlacements:F1} ms, " +
                                          $"{objects.Count} spawned objects in {tObjects:F1} ms, total {sw.Elapsed.TotalMilliseconds:F1} ms (component lookup {tIndex:F1} ms)");
        }
    }

    /// <summary>
    /// Every (entity, component data) of one component type, straight from its ComponentDataBuffer (Quantum, internal):
    /// _count @0x4, _stride @0xC, _blocksList @0x18 (Block {EntityRef* PackedHandles, byte* PackedData}), _blockCapacity
    /// @0x28; dense index i -> block i / capacity, slot i % capacity (ComponentDataBuffer.GetEntityRefByIndex). Index 0 is
    /// the unused "null" slot.
    /// </summary>
    private static unsafe List<(EntityRef, System.IntPtr)> Components(Frame f, int componentIndex)
    {
        var result = new List<(EntityRef, System.IntPtr)>();
        byte* buffer = (byte*)f.GetDataBuffer(componentIndex);
        if (buffer == null) return result;
        int count = *(int*)(buffer + 0x4), stride = *(int*)(buffer + 0xC), blockCapacity = *(int*)(buffer + 0x28);
        byte* blocks = *(byte**)(buffer + 0x18);
        if (blocks == null || blockCapacity <= 0) return result;
        for (int i = 1; i < count; i++)
        {
            byte* block = blocks + (i / blockCapacity) * 16;
            int slot = i % blockCapacity;
            var e = (*(EntityRef**)block)[slot];
            if (f.Exists(e)) result.Add((e, (System.IntPtr)(*(byte**)(block + 8) + slot * stride)));
        }
        return result;
    }

    private static unsafe T Ref<T>(int value) where T : unmanaged
    {
        var r = default(T);
        *(int*)&r = value;
        return r;
    }

    /// <summary>Within range on x / z. Plain float math: Vector3 operators can go through interop.</summary>
    private static bool Near(Vector3 a, Vector3 b, float range)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return dx * dx + dz * dz <= range * range;
    }

    private static bool Alive(Frame f, EntityRef npc) => f.Exists(npc) && AiUtils.ExistsAlive(f, npc);

    /// <summary>"Outer.Inner" il2cpp class name of an object, cached per class pointer.</summary>
    private static string ClassOf(IntPtr obj)
    {
        IntPtr k = IL2CPP.il2cpp_object_get_class(obj);
        if (ClassNames.TryGetValue(k, out var name)) return name;
        name = Marshal.PtrToStringAnsi(IL2CPP.il2cpp_class_get_name(k)) ?? "";
        IntPtr outer = IL2CPP.il2cpp_class_get_declaring_type(k);
        if (outer != IntPtr.Zero) name = Marshal.PtrToStringAnsi(IL2CPP.il2cpp_class_get_name(outer)) + "." + name;
        ClassNames[k] = name;
        return name;
    }

    public static int Count => Resources.Count + Placements.Count + Spawners.Count;
}
