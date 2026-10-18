using System;
using System.Collections.Generic;
using Il2CppMoon.Forsaken;
using Il2CppPhoton.Deterministic;
using Il2CppQuantum;
using UnityEngine;

namespace MapResourceMarkers;

/// <summary>A way out of an interior: where it leads on a mapped area (the cave mouth / door outside).</summary>
internal sealed class Exit
{
    public long Chunk;
    /// <summary>The teleport itself, inside the interior.</summary>
    public Vector3 Source;
    /// <summary>Its destination spawn point, on the overworld / sewer map, 1-5 m from the entrance there.</summary>
    public Vector3 Destination;
    public FPVector2 SimDestination;
}

/// <summary>
/// Caves, cellars and other interiors have no map; points inside them are drawn at the interior's mouth instead
/// (analysis/interior_entrances.md):
/// - Interior = the streaming chunk whose bounds contain the point (equals its content chunk for all 2812 off-map points).
/// - Exits = the level links the game's map draws as Entrances (MapScreen.TryParseMapIconFor): teleport doors
///   (InteractableTeleportStaticInstanceData) and walk-in teleport zones (TriggerZoneStaticInstanceData, Type Teleport),
///   standing in that chunk, whose destination spawn point (WorldSpawnPointData asset) lies on a map.
/// - A point goes to the exit whose source is nearest (caves with two mouths).
/// Exits are used rather than the outside entrances: three interiors with content (the two mountain pass caves, Irmgard's
/// house) have no entrance in the Quantum data, but every interior has an exit. The crucible (infiniteDungeon world), the
/// prologue ship and two unused chunks are excluded; their points stay off the map.
/// </summary>
internal static class Entrances
{
    private sealed class ChunkBounds
    {
        public long Id;
        public float MinX, MaxX, MinZ, MaxZ, Area;
        public string Name = "";
        public bool Excluded;
    }

    private static readonly string[] ExcludedNames = { "infiniteDungeon", "shipPrologue", "phalenChurch", "hillsCaveC" };

    private static readonly List<ChunkBounds> Chunks = new();
    private static readonly List<Exit> Exits = new();
    public static IReadOnlyList<Exit> All => Exits;
    private static int _builtFrom = -1;
    /// <summary>Spawn points that did not resolve yet (they may only be resident while their area is loaded): retry.</summary>
    private static bool _unresolved;

    /// <summary>Teleports standing in an interior (not excluded): chunk, own position, destination spawn point.</summary>
    private static readonly List<(long Chunk, Vector3 Source, AssetGuid Target)> Candidates = new();

    /// <summary>Rebuild for a new world, or retry missing spawn points. True when the exits changed.</summary>
    public static bool Refresh(Frame f, Func<Vector3, bool> onMap)
    {
        var st = f.InstanceDataStorage;
        var all = st.m_storageLinkedEntityInstanceData.m_storage;
        bool rebuild = all.Count != _builtFrom;
        if (!rebuild && !_unresolved) return false;
        var game = f.Game?.TryCast<MoonQuantumGame>();
        if (game == null) return false;
        if (rebuild)
        {
            _builtFrom = all.Count;
            ReadChunks(f);
            ReadCandidates(all, onMap);
        }

        int before = Exits.Count, missing = 0;
        Exits.Clear();
        var seen = new HashSet<long>();
        foreach (var (chunk, source, target) in Candidates)
        {
            WorldSpawnPointData? spawn = null;
            try { spawn = IResourceManagerExtensions.GetAsset(game.ResourceManager, target, true)?.TryCast<WorldSpawnPointData>(); }
            catch { }
            if (spawn == null) { missing++; continue; }
            var dest = ToVector(spawn.Position);
            if (!onMap(dest) || !seen.Add(chunk ^ target.Value)) continue;   // the tavern door exists once per Sacrament level
            Exits.Add(new Exit
            {
                Chunk = chunk, Source = source, Destination = dest,
                SimDestination = new FPVector2(spawn.Position.X, spawn.Position.Z),
            });
        }
        // A few teleports point at spawn points cut from the game; more missing = probably not loaded yet, retry.
        _unresolved = missing > 4;
        MapResourceMarkersMod.Debug($"Interior exits: {Exits.Count} to the map from {Candidates.Count} teleports in interiors" +
                                      (missing > 0 ? $" ({missing} destinations not found{(_unresolved ? ", retrying on the next map open" : "")})" : ""));
        return rebuild || Exits.Count != before;
    }

    private static void ReadCandidates(Il2CppSystem.Collections.Generic.List<LinkedEntityInstanceData> all, Func<Vector3, bool> onMap)
    {
        Candidates.Clear();
        for (int i = 0; i < all.Count; i++)
        {
            var e = all[i];
            if (e == null) continue;
            AssetGuid target;
            var door = e.TryCast<InteractableTeleportStaticInstanceData>();
            if (door != null) target = door.SpawnPoint.Id;
            else
            {
                var zone = e.TryCast<TriggerZoneStaticInstanceData>();
                if (zone == null || zone.Type != TriggerZoneType.Teleport) continue;
                target = zone.Spawnpoint.Id;
            }
            if (!target.IsValid) continue;
            var source = ToVector(e.Position);
            if (onMap(source)) continue;                     // an entrance, not an exit
            var chunk = ChunkAt(source);
            if (chunk == null || chunk.Excluded) continue;
            Candidates.Add((chunk.Id, source, target));
        }
    }

    /// <summary>Where an off-map point shows: the nearest exit of its interior, or null (no map for it at all).</summary>
    public static Exit? For(Vector3 p)
    {
        var chunk = ChunkAt(p);
        if (chunk == null || chunk.Excluded) return null;
        Exit? best = null;
        float bestDist = float.MaxValue;
        foreach (var e in Exits)
        {
            if (e.Chunk != chunk.Id) continue;
            float d = (e.Source - p).sqrMagnitude;
            if (d < bestDist) { bestDist = d; best = e; }
        }
        return best;
    }

    private static void ReadChunks(Frame f)
    {
        Chunks.Clear();
        var nodes = f.InstanceDataStorage.m_storageRuntimeStreamingNode.m_storage;
        for (int i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            if (n == null || n.Kind != StreamingNodeKind.Chunk) continue;
            var b = n.PhysicalBounds;
            float cx = b.Center.X.AsFloat, cz = b.Center.Z.AsFloat, ex = b.Extents.X.AsFloat, ez = b.Extents.Z.AsFloat;
            bool excluded = string.Equals(n.Name, "global", StringComparison.OrdinalIgnoreCase);
            for (var p = n; p != null && !excluded; p = p.Parent)
                foreach (var name in ExcludedNames)
                    if (p.Name != null && p.Name.Contains(name, StringComparison.OrdinalIgnoreCase)) { excluded = true; break; }
            Chunks.Add(new ChunkBounds
            {
                Id = n.Id.Value, Name = (n.Parent?.Name is { } area ? area + "/" : "") + n.Name, MinX = cx - ex, MaxX = cx + ex, MinZ = cz - ez, MaxZ = cz + ez, Area = 4f * ex * ez, Excluded = excluded,
            });
        }
    }

    public static string ChunkName(long id)
    {
        foreach (var c in Chunks) if (c.Id == id) return c.Name;
        return id.ToString();
    }

    /// <summary>The smallest chunk whose bounds contain the position (x / z).</summary>
    private static ChunkBounds? ChunkAt(Vector3 p)
    {
        ChunkBounds? best = null;
        foreach (var c in Chunks)
            if (p.x >= c.MinX && p.x <= c.MaxX && p.z >= c.MinZ && p.z <= c.MaxZ && (best == null || c.Area < best.Area))
                best = c;
        return best;
    }

    private static Vector3 ToVector(FPVector3 v) => new(v.X.AsFloat, v.Y.AsFloat, v.Z.AsFloat);

    public static void Reset()
    {
        _builtFrom = -1;
        Chunks.Clear();
        Candidates.Clear();
        Exits.Clear();
    }
}
