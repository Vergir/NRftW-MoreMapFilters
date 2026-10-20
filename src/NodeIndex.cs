using System.Collections.Generic;
using Il2CppMoon.Forsaken;
using Il2CppPhoton.Deterministic;
using Il2CppQuantum;
using UnityEngine;

namespace MoreMapFilters;

/// <summary>What a point's EntityId refers to (for the cleared state, see ClearedState).</summary>
internal enum PointSource
{
    Resource,    // LinkedEntityInstanceData.InstanceGuid (farmable nodes and ground pickups)
    Placement,   // NpcInstanceData.Guid
    Spawner,     // DynamicSpawnerData.Guid
    Chest,       // ContainerStaticInstanceData.InstanceGuid (static chest)
    ChestSpawner,// DynamicSpawnerData.Guid of a chest spawner
    Whisper,     // CerimWhisperStaticInstanceData.InstanceGuid
    Key,         // the key pickup container's InstanceGuid
    KeyDoor,     // what a taken key opens (KeyKind.Doors); EntityId = the key pickup's InstanceGuid
    Shortcut,    // the shortcut record's InstanceGuid (ShortcutData)
}

/// <summary>One marker position: a resource node or an enemy spawn point.</summary>
internal sealed class MapPoint
{
    public readonly MarkerType Type;
    public readonly string Name;
    /// <summary>World position. The map draws world (x, z) as its local (x, y).</summary>
    public readonly Vector3 Position;
    /// <summary>World (x, z) in simulation units, for the fog of war lookup.</summary>
    public readonly FPVector2 SimPosition;
    /// <summary>Content set (nugget / chunk) the point belongs to; 0 = always there (resource nodes). See EnemyIndex.</summary>
    public readonly long ContentSet;
    /// <summary>Only a chance (an animal spawner that may roll this species): drawn with a "?" badge.</summary>
    public readonly bool Possible;
    public readonly PointSource Source;
    public readonly long EntityId;

    public MapPoint(MarkerType type, string name, Vector3 position, FPVector2 simPosition, PointSource source, long entityId,
        long contentSet = 0, bool possible = false)
    {
        Source = source;
        EntityId = entityId;
        Type = type;
        Name = name;
        Position = position;
        SimPosition = simPosition;
        ContentSet = contentSet;
        Possible = possible;
    }

    public static MapPoint At(MarkerType type, string? name, FPVector3 p, PointSource source, long entityId, long contentSet = 0,
        bool possible = false) =>
        new(type, name ?? "", new Vector3(p.X.AsFloat, p.Y.AsFloat, p.Z.AsFloat), new FPVector2(p.X, p.Z), source, entityId,
            contentSet, possible);
}

/// <summary>
/// Every resource node in the world. The client keeps the static instance data of the whole world (all streaming nodes,
/// loaded or not) in InstanceDataStorage from the moment the hero is in the world, so one pass finds them all.
/// </summary>
internal static class NodeIndex
{
    public static readonly List<MapPoint> Nodes = new();
    /// <summary>The static chests (ChestKind.StaticSettings); the spawned ones are in EnemyIndex.Chests.</summary>
    public static readonly List<MapPoint> Chests = new();
    /// <summary>The whispers, with their instance data (CerimWhisperViewAPI.IsActivated takes it).</summary>
    public static readonly List<MapPoint> Whispers = new();
    public static readonly Dictionary<long, CerimWhisperStaticInstanceData> WhisperData = new();
    /// <summary>Key pickups, with their container data (RespawnablesAPI.DoesPassSpawnRequirements takes it).</summary>
    public static readonly List<MapPoint> Keys = new();
    public static readonly Dictionary<long, ContainerStaticInstanceData> KeyData = new();
    /// <summary>The doors of taken keys (KeyKind.Doors), and the door / lever records of the keys without a puzzle.</summary>
    public static readonly List<MapPoint> KeyDoors = new();
    public static readonly Dictionary<long, LinkedEntityInstanceData> KeyLocks = new();
    /// <summary>Shortcuts (ShortcutData) with their records; Above = reached from the top (ladders, trapdoors).</summary>
    public static readonly List<MapPoint> Shortcuts = new();
    public static readonly Dictionary<long, (LinkedEntityInstanceData Record, ShortcutData.Entry Entry)> ShortcutRecords = new();
    public static readonly HashSet<long> ShortcutsAbove = new();

    /// <summary>Count of LinkedEntityInstanceData the index was built from; -1 = not built.</summary>
    private static int _builtFrom = -1;

    /// <summary>Rebuild when the world data changed (first load, other realm). True when the list changed.</summary>
    public static bool Refresh()
    {
        var f = LocalFrame();
        if (f == null) return false;
        var all = f.InstanceDataStorage.m_storageLinkedEntityInstanceData.m_storage;
        if (all.Count == _builtFrom) return false;
        _builtFrom = all.Count;

        Nodes.Clear();
        Chests.Clear();
        Whispers.Clear();
        WhisperData.Clear();
        Keys.Clear();
        KeyData.Clear();
        KeyDoors.Clear();
        KeyLocks.Clear();
        var lockGuids = new HashSet<long>();
        foreach (var door in KeyKind.Doors.Values) if (door.Lock != 0) lockGuids.Add(door.Lock);
        Shortcuts.Clear();
        ShortcutRecords.Clear();
        ShortcutsAbove.Clear();
        var shortcutEntries = new Dictionary<long, ShortcutData.Entry>();
        foreach (var entry in ShortcutData.All) shortcutEntries[entry.Guid] = entry;
        // Content set of every linked entity: the chunk (always there) or an area variant / quest nugget it belongs to
        // (analysis/world_states.md 7: e.g. 28 spruce at the Nameless Pass battering ram exist only until Faith and Flame
        // is done). Drawn by the same rules as enemy spawns (EnemyIndex.State).
        var setOf = new Dictionary<long, long>();
        var st = f.InstanceDataStorage;
        var contents = st.m_storageReadonlyContent.m_storage;
        for (int c = 0; c < contents.Count; c++)
        {
            var content = contents[c];
            var refs = content?.LinkedEntityIndices.m_list;
            if (refs == null) continue;
            long set = content!.ContainingNuggetId.Value;
            for (int i = 0; i < refs.Count; i++)
            {
                var e = st.Get(refs[i]);
                if (e != null) setOf[e.InstanceGuid] = set;
            }
        }
        for (int i = 0; i < all.Count; i++)
        {
            var e = all[i];
            if (e == null) continue;
            if (lockGuids.Contains(e.InstanceGuid)) KeyLocks[e.InstanceGuid] = e;   // may be a shortcut too
            if (shortcutEntries.TryGetValue(e.InstanceGuid, out var sc))
            {
                setOf.TryGetValue(e.InstanceGuid, out long scSet);
                // Fog is checked where the marker is drawn: puzzle records sit at (0, 0, 0), one-way doors on the closed side.
                var at = new Vector3(sc.X, sc.Y, sc.Z);
                Shortcuts.Add(new MapPoint(MarkerTypes.Shortcut, e.Name ?? "", at, new FPVector2(FP.FromFloat_UNSAFE(sc.X), FP.FromFloat_UNSAFE(sc.Z)),
                    PointSource.Shortcut, e.InstanceGuid, scSet));
                ShortcutRecords[e.InstanceGuid] = (e, sc);
                if (sc.Kind is ShortcutKind.Ladder or ShortcutKind.Trapdoor) ShortcutsAbove.Add(e.InstanceGuid);
                continue;
            }
            var whisper = e.TryCast<CerimWhisperStaticInstanceData>();
            if (whisper != null)
            {
                setOf.TryGetValue(e.InstanceGuid, out long whisperSet);
                Whispers.Add(MapPoint.At(MarkerTypes.Whisper, e.Name, e.Position, PointSource.Whisper, e.InstanceGuid, whisperSet));
                WhisperData[e.InstanceGuid] = whisper;
                continue;
            }
            var container = e.TryCast<ContainerStaticInstanceData>();
            if (container != null)
            {
                if (KeyKind.Pickups.ContainsKey(e.InstanceGuid))
                {
                    setOf.TryGetValue(e.InstanceGuid, out long keySet);
                    Keys.Add(MapPoint.At(MarkerTypes.Key, e.Name, e.Position, PointSource.Key, e.InstanceGuid, keySet));
                    KeyData[e.InstanceGuid] = container;
                    if (KeyKind.Doors.TryGetValue(e.InstanceGuid, out var door))
                        KeyDoors.Add(new MapPoint(MarkerTypes.Key, e.Name ?? "", new Vector3(door.X, door.Y, door.Z),
                            new FPVector2(FP.FromFloat_UNSAFE(door.X), FP.FromFloat_UNSAFE(door.Z)), PointSource.KeyDoor,
                            e.InstanceGuid, keySet));
                    continue;
                }
                if (System.Array.IndexOf(ChestKind.StaticSettings, container.Settings.Id.Value) >= 0)
                {
                    setOf.TryGetValue(e.InstanceGuid, out long chestSet);
                    Chests.Add(MapPoint.At(MarkerTypes.Chest, e.Name, e.Position, PointSource.Chest, e.InstanceGuid, chestSet));
                }
                continue;
            }
            // Farmable nodes (trees, ore, digging / fishing spots) and ground pickups (herbs, food, nettles).
            var sources = e.TryCast<FarmableResourceStaticInstanceData>()?.LootSource.Sources
                          ?? e.TryCast<StaticPickupStaticInstanceData>()?.LootSource.Sources;
            if (sources == null || sources.Length == 0) continue;
            var type = MarkerTypes.ByLootSource(sources[0].Id.Value);
            if (type == null) continue;
            setOf.TryGetValue(e.InstanceGuid, out long contentSet);
            Nodes.Add(MapPoint.At(type, e.Name, e.Position, PointSource.Resource, e.InstanceGuid, contentSet));
        }
        MoreMapFiltersMod.Debug($"Indexed {Nodes.Count} resource nodes ({setOf.Count} linked entities with a content set).");
        return true;
    }

    /// <summary>The world data no longer matches the index (another realm / reload).</summary>
    public static bool IsStale()
    {
        var f = LocalFrame();
        return f != null && f.InstanceDataStorage.m_storageLinkedEntityInstanceData.m_storage.Count != _builtFrom;
    }

    internal static Frame? LocalFrame() => Heroes.Local()?.VerifiedFrame;
}
