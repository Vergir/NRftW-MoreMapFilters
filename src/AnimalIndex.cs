using System;
using System.Collections.Generic;
using Il2CppMoon.Forsaken;
using Il2CppPhoton.Deterministic;
using Il2CppQuantum;
using Il2CppQuantum.StreamingGraph;

namespace MapResourceMarkers;

/// <summary>
/// Where to hunt the animals that drop upgrade parts (analysis/animal_parts.md). Two kinds of points per AnimalKind:
/// - exact: fixed NpcInstanceData placements of one of the kind's species (drawn plain);
/// - possible: DynamicSpawnerData whose spawn roll gives the kind a share >= MinShare (drawn with a "?" badge).
/// Spawners name no species: DynamicSpawnerSystem.GetSpawnableMatchingTags picks one NpcData from the pool
/// (InstanceDataStorage NpcData storage) whose tags contain all AllTags (plus the node's region tag), one of AnyTags and
/// none of NoneTags, minus the node chain's DynamicSpawnBlacklist, weighted by SpawnRate. NpcData tags are rebuilt like
/// NpcData.GetTags: EnemyType, EnemyFaction bits, combat style, NativeRegions (30001 + region; none = any region), affix
/// tags of AdditionalAffixes, AdditionalTags. Story gates: an NpcData whose DynamicSpawningUnlocksAfter conditions are not
/// passed (ConditionCollection.ConditionPassed: e.g. bears only after Weeping Sisters) is left out of the pool; UpdateGates
/// re-checks them with the area variants and recomputes the shares when one opens up. Liveness (active area variant) is the
/// same as for enemy points (EnemyIndex.State).
/// </summary>
internal static class AnimalIndex
{
    public static readonly List<MapPoint> Points = new();
    public const float MinShare = 0.2f;
    private static int _builtFrom = -1;

    private sealed class Npc
    {
        public long Guid;
        public HashSet<int> Tags = new();
        public bool HasRegion;
        public float Rate;
        public ConditionCollection? Unlock;
        public bool Unlocked = true;
    }

    /// <summary>A spawner that can roll at least one animal species (before story gates).</summary>
    private sealed class Spawner
    {
        public string Name = "";
        public long Guid;
        public FPVector3 Position;
        public long Set;
        public List<Npc> Candidates = new();
    }

    private static readonly List<Npc> Pool = new();
    private static readonly List<Spawner> Spawners = new();
    private static readonly List<MapPoint> Exact = new();
    private static readonly Dictionary<long, AnimalKind> SpeciesKind = new();
    private static bool _gateErrorLogged;

    private static readonly int[] TypeTag = { 12, 7, 13, 14, 8 };   // Normal, Giant, Elite, Boss, Critters
    private static readonly (int Bit, int Tag)[] FactionTags =
    {
        (1, 9), (2, 6), (4, 5), (8, 16), (16, 17), (32, 18), (64, 19), (128, 20), (256, 21), (512, 22), (1024, 23),
        (2048, 24), (4096, 25), (8192, 26), (16384, 27), (32768, 28), (65536, 29), (131072, 30),
    };
    private static readonly (int Bit, int Tag)[] AffixTags = { (1, 10), (2, 11), (4, 15), (8, 4) };  // Shield, Armor, Swimming, Magic
    private static bool IsRegionTag(int t) => t > 30001 && t < 31000;

    public static bool Refresh(Frame f)
    {
        var st = f.InstanceDataStorage;
        var contents = st.m_storageReadonlyContent.m_storage;
        if (contents.Count == _builtFrom) return false;
        _builtFrom = contents.Count;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Points.Clear();
        Pool.Clear();
        Spawners.Clear();
        Exact.Clear();
        SpeciesKind.Clear();
        var game = f.Game?.TryCast<MoonQuantumGame>();
        if (game == null) return true;

        foreach (var k in MarkerTypes.Animals)
            foreach (var g in k.Species) SpeciesKind[g] = k;
        var speciesKind = SpeciesKind;

        // 1. The spawn pool with rebuilt tags.
        var pool = Pool;
        var affixTag = new Dictionary<long, int>();
        var npcStorage = st.m_storageNpcData.m_storage;
        for (int i = 0; i < npcStorage.Count; i++)
        {
            var d = npcStorage[i];
            if (d == null || d.SpawnRate.AsFloat <= 0f) continue;
            var npc = new Npc { Guid = d.Guid.Value, Rate = d.SpawnRate.AsFloat };
            var unlock = d.DynamicSpawningUnlocksAfter;
            if (unlock != null && unlock.HasAnyCondition) npc.Unlock = unlock;
            int type = (int)d.EnemyType;
            if (type >= 0 && type < TypeTag.Length) npc.Tags.Add(TypeTag[type]);
            int faction = (int)d.EnemyFaction;
            foreach (var (bit, tag) in FactionTags) if ((faction & bit) != 0) npc.Tags.Add(tag);
            if ((int)d.EnemyCombatStyle == 1) npc.Tags.Add(2);
            if ((int)d.EnemyCombatStyle == 2) npc.Tags.Add(3);
            if (d.NativeRegions != null)
                for (int r = 0; r < d.NativeRegions.Count; r++) { npc.Tags.Add(30001 + (int)d.NativeRegions[r]); npc.HasRegion = true; }
            if (d.AdditionalAffixes != null)
                foreach (var a in d.AdditionalAffixes)
                {
                    if (!affixTag.TryGetValue(a.Id.Value, out int bits))
                    {
                        try { bits = (int)(Asset<AffixData>(game, a.Id)?.Tag ?? 0); } catch { bits = 0; }
                        affixTag[a.Id.Value] = bits;
                    }
                    foreach (var (bit, tag) in AffixTags) if ((bits & bit) != 0) npc.Tags.Add(tag);
                }
            if (d.AdditionalTags != null)
                for (int t = 0; t < d.AdditionalTags.Count; t++) npc.Tags.Add((int)d.AdditionalTags[t]);
            pool.Add(npc);
        }

        // 2. Content set -> streaming node (its region and blacklists).
        var nodeById = new Dictionary<long, RuntimeStreamingNode>();
        var nodes = st.m_storageRuntimeStreamingNode.m_storage;
        for (int i = 0; i < nodes.Count; i++) if (nodes[i] != null) nodeById[nodes[i].Id.Value] = nodes[i];
        var nodeOfSet = new Dictionary<long, RuntimeStreamingNode>();
        var collections = st.m_storageContentCollection.m_storage;
        for (int i = 0; i < collections.Count; i++)
        {
            var cc = collections[i];
            var sets = cc?.ContentSets.m_list;
            if (sets == null || !nodeById.TryGetValue(cc!.StreamingNodeId.Value, out var node)) continue;
            for (int s = 0; s < sets.Count; s++) nodeOfSet[sets[s].Id.Value] = node;
        }

        // 3. Points.
        var all = new List<int>();
        var any = new List<int>();
        var none = new List<int>();
        int exact = 0;
        for (int c = 0; c < contents.Count; c++)
        {
            var content = contents[c];
            if (content == null) continue;
            long set = content.ContainingNuggetId.Value;
            nodeOfSet.TryGetValue(set, out var node);
            if (node == null) nodeById.TryGetValue(set, out node);

            var npcs = content.NpcIndices.m_list;
            if (npcs != null)
                for (int i = 0; i < npcs.Count; i++)
                {
                    var npc = st.Get(npcs[i]);
                    if (npc == null || npc.MinimalNumberOfPlayers > 1) continue;
                    if (!speciesKind.TryGetValue(npc.NpcData.Id.Value, out var kind)) continue;
                    Exact.Add(MapPoint.At(kind, npc.DebugName, npc.Position, PointSource.Placement, npc.Guid, set));
                    exact++;
                }

            var spawners = content.DynamicSpawnerDataIndices.m_list;
            if (spawners == null) continue;
            int region = Region(node);
            var blacklist = Blacklist(node);
            for (int i = 0; i < spawners.Count; i++)
            {
                var sp = st.Get(spawners[i]);
                if (sp == null) continue;
                all.Clear(); any.Clear(); none.Clear();
                Tags.Read(sp.SpawnerTags.AllTags, all);
                Tags.Read(sp.SpawnerTags.AnyTags, any);
                Tags.Read(sp.SpawnerTags.NoneTags, none);
                if (all.Count == 0 && any.Count == 0) continue;
                bool loot = false;
                foreach (int t in all) loot |= t > 20000 && t < 21000;
                foreach (int t in any) loot |= t > 20000 && t < 21000;
                if (loot) continue;
                bool hasRegion = false;
                foreach (int t in all) hasRegion |= IsRegionTag(t);
                if (!hasRegion && region > 0) all.Add(30001 + region);

                var rec = new Spawner { Name = sp.DebugName ?? "", Guid = sp.Guid, Position = sp.Position, Set = set };
                bool animal = false;
                foreach (var npc in pool)
                {
                    if (!Matches(npc, all, any, none) || blacklist.Contains(npc.Guid)) continue;
                    rec.Candidates.Add(npc);
                    animal |= speciesKind.ContainsKey(npc.Guid);
                }
                if (animal) Spawners.Add(rec);
            }
        }
        UpdateGates(f, force: true);
        MapResourceMarkersMod.Debug($"Indexed animal points: {exact} exact, {Points.Count - exact} possible from {Spawners.Count} spawners " +
                                      $"(pool {pool.Count} NpcData) in {sw.ElapsedMilliseconds} ms");
        return true;
    }

    /// <summary>Re-check the story gates; recompute the possible points when one changed. True = points changed.</summary>
    public static bool UpdateGates(Frame f, bool force = false)
    {
        bool changed = force;
        foreach (var npc in Pool)
        {
            if (npc.Unlock == null) continue;
            bool open;
            try { open = npc.Unlock.ConditionPassed(f); }
            catch (Exception e)
            {
                open = true;   // can't tell: don't hide
                if (!_gateErrorLogged)
                {
                    _gateErrorLogged = true;
                    MapResourceMarkersMod.Log.Warning($"Story gate check failed ({e.GetType().Name}: {e.Message}); treating spawns as unlocked.");
                }
            }
            if (open != npc.Unlocked) { npc.Unlocked = open; changed = true; }
        }
        if (!changed) return false;
        Compute();
        return true;
    }

    private static void Compute()
    {
        Points.Clear();
        Points.AddRange(Exact);
        var share = new Dictionary<AnimalKind, float>();
        int locked = 0;
        foreach (var npc in Pool) if (!npc.Unlocked) locked++;
        foreach (var sp in Spawners)
        {
            float total = 0f;
            share.Clear();
            foreach (var npc in sp.Candidates)
            {
                if (!npc.Unlocked) continue;
                total += npc.Rate;
                if (SpeciesKind.TryGetValue(npc.Guid, out var kind))
                    share[kind] = (share.TryGetValue(kind, out var v) ? v : 0f) + npc.Rate;
            }
            if (total <= 0f) continue;
            foreach (var (kind, rate) in share)
                if (rate / total >= MinShare) Points.Add(MapPoint.At(kind, sp.Name, sp.Position, PointSource.Spawner, sp.Guid, sp.Set, possible: true));
        }
        MapResourceMarkersMod.Debug($"Animal points: {Points.Count} ({Exact.Count} exact); {locked} enemy types still story-locked");
    }

    private static bool Matches(Npc npc, List<int> all, List<int> any, List<int> none)
    {
        foreach (int t in all)
            if (!npc.Tags.Contains(t) && !(IsRegionTag(t) && !npc.HasRegion)) return false;
        if (any.Count > 0)
        {
            bool hit = false;
            foreach (int t in any) if (npc.Tags.Contains(t) || (IsRegionTag(t) && !npc.HasRegion)) { hit = true; break; }
            if (!hit) return false;
        }
        foreach (int t in none) if (npc.Tags.Contains(t)) return false;
        return true;
    }

    /// <summary>Region of the nearest node up the chain that has one (GameplayBalanceRegions; 0 = none).</summary>
    private static int Region(RuntimeStreamingNode? node)
    {
        for (var n = node; n != null; n = n.Parent)
        {
            int r = (int)n.Region;
            if (r > 0) return r;
        }
        return 0;
    }

    private static HashSet<long> Blacklist(RuntimeStreamingNode? node)
    {
        var set = new HashSet<long>();
        for (var n = node; n != null; n = n.Parent)
        {
            var list = n.DynamicSpawnBlacklist;
            if (list != null) foreach (var r in list) set.Add(r.Id.Value);
        }
        return set;
    }

    private static T? Asset<T>(MoonQuantumGame game, AssetGuid guid) where T : AssetObject =>
        IResourceManagerExtensions.GetAsset(game.ResourceManager, guid, true)?.TryCast<T>();

    public static void Reset()
    {
        _builtFrom = -1;
        Points.Clear();
        Pool.Clear();
        Spawners.Clear();
        Exact.Clear();
    }
}
