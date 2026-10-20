using System;
using System.Collections.Generic;
using Il2CppMoon.Forsaken;
using Il2CppQuantum;

namespace MoreMapFilters;

/// <summary>
/// Enemy spawn points per EnemyKind, from the world's static data (analysis/repeatable_quests.md section 3; verified in game
/// by the Kill-X probe, tools/kill_probe_ProbeMod.cs.txt):
/// - Frame.InstanceDataStorage holds every content pack (ReadonlyContent, 2150), every DynamicSpawnerData (9675) and fixed
///   NpcInstanceData placement (2243) of the world, loaded or not. Each content pack names its content set
///   (ContainingNuggetId): a chunk / area (always there) or a nugget = one of a bucket's alternative variants (story state,
///   weights, reshuffles), of which only the active one is live.
/// - StreamingAPI.GetActiveNuggetIn(f, bucketId) is set for all 223 buckets, visited or not. So: a point is live when its
///   content set is a non-bucket streaming node id, or a bucket's active nugget. Quest / bounty content (their own nuggets)
///   is live only while active.
/// - Spawners: SpawnerTags.AllTags / AnyTags hold MarkupTags; the spawned NpcData is picked at spawn time by tags. A spawner
///   with the kind's tag (and not in NoneTags) is a spawn point of that kind. Enemy spawners without any faction tag take
///   the occupying faction of their nugget (NuggetData.OccupyingFaction).
/// - Placements: NpcData.EnemyFaction bits / EnemyType.Giant; co-op extras (MinimalNumberOfPlayers > 1) skipped.
/// Spawn points mean "this kind spawns here", not "one is standing here now" (miss chance, respawn timers).
///
/// What to show (State, analysis/world_states.md 5-6): every bucket lists its alternative variants as ambience payloads
/// (StreamingAPI.GetAmbiencePayloadCollectionForStreamingNode); one whose story conditions pass (AmbiencePayload
/// .ConditionsPassed, Weight > 0) can be rolled. A bucket with revealed fog (FogOfWarSystem.IsAnyRevealedInNode; ~45 real
/// minutes after the area was uncovered) or streamed in keeps its variant: "locked". Any other bucket re-rolls every
/// 10-30 s (the chunk re-picks its faction every 10 s), far faster than anyone can travel there, so its current roll is
/// meaningless and every eligible variant is shown as possible instead. So a spawn point is:
/// - Plain: area content (no variant), the current variant of a locked bucket, a live quest / bounty variant, or the
///   current roll of a watched cave (MapOverlay's cave guard: the hero waits at its mouth and walks in on the right roll);
/// - Possible ("?"): an eligible variant of an unlocked bucket;
/// - Hidden: a variant of a locked bucket that is not its current one, or a variant the story does not allow (yet).
/// </summary>
internal static class EnemyIndex
{
    public static readonly List<MapPoint> Points = new();
    /// <summary>Chest spawners (MarkerTypes.Chest), found in the same pass.</summary>
    public static readonly List<MapPoint> Chests = new();
    /// <summary>Special shiny spawners (MarkerTypes.SpecialShiny).</summary>
    public static readonly List<MapPoint> SpecialShinies = new();
    /// <summary>Content sets inside a plague safe haven (Sacrament, ...): their chests never come back once opened.</summary>
    private static readonly HashSet<long> SafeHavenSets = new();
    private static int _builtFrom = -1;

    /// <summary>Non-bucket streaming node ids (root / world / area / chunk content is always loaded).</summary>
    private static readonly HashSet<long> AlwaysSets = new();
    private static readonly HashSet<long> ActiveNuggets = new();
    private static readonly HashSet<long> EligibleNuggets = new();

    private sealed class Bucket
    {
        public AssetGuid Id;
        public Il2CppQuantum.StreamingGraph.RuntimeStreamingNode Node = null!;
        /// <summary>The chunk (Kind Chunk) above it: the area / cave interior.</summary>
        public long Chunk;
        public long Active;
        public bool Locked;
        /// <summary>Why it is locked: revealed fog (~45 min) and / or streamed in (only while the hero is near).</summary>
        public bool Revealed, Loaded;
        /// <summary>A quest / activity variant holds it (analysis/wolf_den_variant.md): the quest payload beats ambience
        /// (NuggetSystem.ResolveNuggetConflict), so it cannot re-roll until that quest step ends.</summary>
        public bool Pinned;
        /// <summary>RuntimeStreamingNodeRef value: 1-based index into InstanceDataStorage's node storage
        /// (InstanceDataStorageSystem.Get(RuntimeStreamingNodeRef) does index - 1).</summary>
        public int Ref;
    }
    private static readonly List<Bucket> BucketList = new();
    /// <summary>Chunk id -> (node, ref): the chunk's own reveal / load state decides whether its faction is re-picked.</summary>
    private static readonly Dictionary<long, (Il2CppQuantum.StreamingGraph.RuntimeStreamingNode Node, int Ref)> ChunkNodes = new();
    /// <summary>Every ambience variant (eligible or not) -> its bucket.</summary>
    private static readonly Dictionary<long, Bucket> BucketOfVariant = new();
    /// <summary>Chunk the cave guard watches (0 = none): its current roll counts as plain although it is not locked.</summary>
    private static long _watchedChunk;
    private static bool _eligibilityLogged;
    private static string? _pinnedLogged;
    private static readonly List<AssetGuid> Buckets = new();


    private const int TagNormal = 12, TagGiant = 7, TagElite = 13, TagBoss = 14;
    private const int TagSmallChest = 20004, TagSpecialChest = 20007, TagShiny = 20008, TagSpecialShiny = 20009;
    /// <summary>Tags that name a faction: Plagued 5, Beast 6, Humanoid 9, Risen 16 .. Blob 30.</summary>
    private static bool IsFactionTag(int t) => t == 5 || t == 6 || t == 9 || (t >= 16 && t <= 30);
    private static bool IsEnemyTypeTag(int t) => t == TagNormal || t == TagGiant || t == TagElite || t == TagBoss;

    public enum SpawnState { Hidden, Plain, Possible }

    /// <summary>The content set belongs to a plague safe haven (the node or a bucket under one).</summary>
    public static bool InSafeHaven(long contentSet) =>
        SafeHavenSets.Contains(contentSet) || (BucketOfVariant.TryGetValue(contentSet, out var b) && SafeHavenSets.Contains(b.Id.Value));

    /// <summary>How a point of this content set is shown (see class remarks).</summary>
    public static SpawnState State(long contentSet)
    {
        if (contentSet == 0 || AlwaysSets.Contains(contentSet)) return SpawnState.Plain;
        if (BucketOfVariant.TryGetValue(contentSet, out var b))
        {
            if (b.Active == contentSet && (b.Locked || (b.Chunk == _watchedChunk && _watchedChunk != 0))) return SpawnState.Plain;
            if (b.Locked) return SpawnState.Hidden;
            return EligibleNuggets.Contains(contentSet) ? SpawnState.Possible : SpawnState.Hidden;
        }
        // Quest / bounty content (not an ambience variant): there while its nugget is active.
        return ActiveNuggets.Contains(contentSet) ? SpawnState.Plain : SpawnState.Hidden;
    }

    /// <summary>Shown now, or a variant the story allows (it can roll in once its area unlocks).</summary>
    public static bool IsEligible(long contentSet) => State(contentSet) != SpawnState.Hidden || EligibleNuggets.Contains(contentSet);

    /// <summary>The chunk has a bucket whose variant can still change (worth watching from the cave mouth).</summary>
    public static bool HasUnlockedBucket(long chunk)
    {
        foreach (var b in BucketList) if (b.Chunk == chunk && !b.Locked) return true;
        return false;
    }

    public static void Watch(long chunk) => _watchedChunk = chunk;

    /// <summary>Why a chunk cannot re-roll right now ("" = it can): for the cave guard's log.</summary>
    public static string LockReason(long chunk)
    {
        bool any = false, loaded = false, revealed = false, pinned = false;
        foreach (var b in BucketList)
        {
            if (b.Chunk != chunk) continue;
            any = true;
            if (!b.Locked) return "";
            loaded |= b.Loaded;
            revealed |= b.Revealed;
            pinned |= b.Pinned;
        }
        if (!any) return "it has no variants";
        return pinned ? "a quest or bounty holds it on its own variant until that quest step is done"
             : loaded ? "it is loaded (preloaded near its mouth), which freezes its variant while you are close"
                      : revealed ? "you revealed it recently; it keeps its variant until the fog creeps back (~45 min)" : "locked";
    }

    /// <summary>The chunk's own state (its faction is only re-picked while it is neither revealed nor loaded).</summary>
    public static string ChunkState(Frame f, long chunk)
    {
        if (!ChunkNodes.TryGetValue(chunk, out var c)) return "chunk not found";
        bool revealed = false, loaded = false;
        try { revealed = FogOfWarSystem.IsAnyRevealedInNode(f, c.Node); } catch { }
        try { loaded = StreamingAPI.IsIntegrated(f, NodeRef(c.Ref)); } catch { }
        var parts = new List<string>();
        foreach (var b in BucketList)
            if (b.Chunk == chunk) parts.Add($"{b.Node.Name}: revealed {b.Revealed}, loaded {b.Loaded}");
        return $"chunk revealed {revealed}, loaded {loaded}; buckets {string.Join("; ", parts)}";
    }

    private static unsafe RuntimeStreamingNodeRef NodeRef(int value)
    {
        var r = default(RuntimeStreamingNodeRef);
        *(int*)&r = value;
        return r;
    }

    /// <summary>Cave guard: re-read only this chunk's buckets (current roll and lock). True when anything changed.</summary>
    public static bool RefreshChunk(Frame f, long chunk)
    {
        bool changed = false;
        foreach (var b in BucketList)
        {
            if (b.Chunk != chunk) continue;
            changed |= ReadBucket(f, b);
        }
        return changed;
    }

    /// <summary>Per bucket of the chunk: its eligible variants (the cave guard's log).</summary>
    public static List<List<long>> EligibleIn(long chunk, bool unlockedOnly = false)
    {
        var result = new List<List<long>>();
        foreach (var b in BucketList)
        {
            if (b.Chunk != chunk || (unlockedOnly && (b.Locked || b.Chunk == _watchedChunk))) continue;
            var list = new List<long>();
            foreach (var kv in BucketOfVariant)
                if (kv.Value == b && EligibleNuggets.Contains(kv.Key)) list.Add(kv.Key);
            result.Add(list);
        }
        return result;
    }

    /// <summary>Current variants of a chunk's buckets (the cave guard's roll log).</summary>
    public static List<long> ActiveIn(long chunk)
    {
        var list = new List<long>();
        foreach (var b in BucketList) if (b.Chunk == chunk && b.Active != 0) list.Add(b.Active);
        return list;
    }

    /// <summary>Current variant and lock of one bucket. True when that changes what is drawn: the lock, or the variant of
    /// a locked / watched bucket or a quest one (an unlocked bucket's own roll is not drawn, see State).</summary>
    private static bool ReadBucket(Frame f, Bucket b)
    {
        long active = 0;
        bool locked = false;
        try { active = StreamingAPI.GetActiveNuggetIn(f, b.Id)?.Guid.Value ?? 0; } catch { }
        bool revealed = false, loaded = false;
        try { revealed = FogOfWarSystem.IsAnyRevealedInNode(f, b.Node); } catch { }
        // Streamed in (AmbienceStateSystem.EvaluateNode skips loaded buckets): a cave interior is preloaded while the hero
        // stands near its mouth, so it cannot re-roll then.
        try { loaded = StreamingAPI.IsIntegrated(f, NodeRef(b.Ref)); } catch { }
        // Its current variant is not one of its ambience variants: a quest / bounty one (e.g. faithAndFlameWolfDen).
        bool pinned = active != 0 && BucketOfVariant.Count > 0 && !BucketOfVariant.ContainsKey(active);
        b.Revealed = revealed;
        b.Loaded = loaded;
        b.Pinned = pinned;
        locked = revealed || loaded || pinned;
        bool shown = locked || (b.Chunk == _watchedChunk && _watchedChunk != 0)
                     || !BucketOfVariant.ContainsKey(active) || !BucketOfVariant.ContainsKey(b.Active);
        bool changed = locked != b.Locked || (active != b.Active && shown);
        if (b.Active != 0) ActiveNuggets.Remove(b.Active);
        b.Active = active;
        b.Locked = locked;
        if (active != 0) ActiveNuggets.Add(active);
        return changed;
    }

    /// <summary>Build the spawn point list once per world (content pack count changes on another realm). True = rebuilt.</summary>
    public static bool Refresh(Frame f)
    {
        var st = f.InstanceDataStorage;
        var contents = st.m_storageReadonlyContent.m_storage;
        if (contents.Count == _builtFrom) return false;
        _builtFrom = contents.Count;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        Points.Clear();
        Chests.Clear();
        SpecialShinies.Clear();
        SafeHavenSets.Clear();
        AlwaysSets.Clear();
        Buckets.Clear();
        BucketList.Clear();
        ChunkNodes.Clear();
        BucketOfVariant.Clear();

        var nodes = st.m_storageRuntimeStreamingNode.m_storage;
        for (int i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            if (n == null) continue;
            if (n.Kind == StreamingNodeKind.Chunk) ChunkNodes[n.Id.Value] = (n, i + 1);
            if (n.Kind == StreamingNodeKind.Bucket)
            {
                Buckets.Add(n.Id);
                long chunk = 0;
                for (var p = n.Parent; p != null; p = p.Parent)
                    if (p.Kind == StreamingNodeKind.Chunk) { chunk = p.Id.Value; break; }
                BucketList.Add(new Bucket { Id = n.Id, Node = n, Chunk = chunk, Ref = i + 1 });
            }
            else AlwaysSets.Add(n.Id.Value);
            for (var s = n; s != null; s = s.Parent)
                if (s.IsPlagueSafeHaven) { SafeHavenSets.Add(n.Id.Value); break; }
        }

        var game = f.Game?.TryCast<MoonQuantumGame>();
        var occupying = new Dictionary<long, int>();
        var npcData = new Dictionary<long, NpcData?>();
        var all = new List<int>();
        var none = new List<int>();
        int spawnerPoints = 0, placementPoints = 0;

        for (int c = 0; c < contents.Count; c++)
        {
            var content = contents[c];
            if (content == null) continue;
            long set = content.ContainingNuggetId.Value;

            var spawners = content.DynamicSpawnerDataIndices.m_list;
            if (spawners != null)
                for (int i = 0; i < spawners.Count; i++)
                {
                    var sp = st.Get(spawners[i]);
                    if (sp == null) continue;
                    all.Clear();
                    none.Clear();
                    Tags.Read(sp.SpawnerTags.AllTags, all);
                    Tags.Read(sp.SpawnerTags.AnyTags, all);
                    Tags.Read(sp.SpawnerTags.NoneTags, none);
                    bool hasFaction = false, isEnemy = false;
                    foreach (int t in all)
                    {
                        hasFaction |= IsFactionTag(t);
                        isEnemy |= IsEnemyTypeTag(t) || IsFactionTag(t);
                    }
                    bool chest = false, shiny = false;
                    foreach (int t in all)
                    {
                        chest |= t >= TagSmallChest && t <= TagSpecialChest;
                        shiny |= t == TagShiny || t == TagSpecialShiny;
                    }
                    // A spawner that may roll a ground shiny instead of a chest is only "possible".
                    if (chest) Chests.Add(MapPoint.At(MarkerTypes.Chest, sp.DebugName, sp.Position, PointSource.ChestSpawner, sp.Guid, set, shiny));
                    bool special = all.Contains(TagSpecialShiny);
                    if (special)
                    {
                        bool mixed = chest || all.Contains(TagShiny);   // may roll something else (even odds)
                        SpecialShinies.Add(MapPoint.At(MarkerTypes.SpecialShiny, sp.DebugName, sp.Position, PointSource.ChestSpawner, sp.Guid, set, mixed));
                    }
                    if (!isEnemy) continue;   // loot / shiny / critter spawners
                    int nuggetFaction = !hasFaction && game != null ? Occupying(game, content.ContainingNuggetId, occupying) : 0;
                    foreach (var kind in MarkerTypes.Enemies)
                    {
                        if (none.Contains(kind.Tag)) continue;
                        bool match = all.Contains(kind.Tag) || (kind.FactionBit != 0 && (nuggetFaction & kind.FactionBit) != 0);
                        if (!match) continue;
                        Points.Add(MapPoint.At(kind, sp.DebugName, sp.Position, PointSource.Spawner, sp.Guid, set));
                        spawnerPoints++;
                    }
                }

            var npcs = content.NpcIndices.m_list;
            if (npcs != null && game != null)
                for (int i = 0; i < npcs.Count; i++)
                {
                    var npc = st.Get(npcs[i]);
                    if (npc == null || npc.MinimalNumberOfPlayers > 1) continue;
                    long guid = npc.NpcData.Id.Value;
                    if (!npcData.TryGetValue(guid, out var data))
                    {
                        try { data = Asset<NpcData>(game, npc.NpcData.Id); }
                        catch (Exception e)
                        {
                            data = null;
                            if (npcData.Count < 3) MoreMapFiltersMod.Log.Warning($"FindAsset<NpcData>({guid}): {e.GetType().Name}: {e.Message}");
                        }
                        if (data == null && npcData.Count < 3) MoreMapFiltersMod.Log.Warning($"NpcData {guid} for '{npc.DebugName}' not found");
                        npcData[guid] = data;
                    }
                    if (data == null) continue;
                    int faction = (int)data.EnemyFaction;
                    bool giant = data.EnemyType == EnemyType.Giant;
                    foreach (var kind in MarkerTypes.Enemies)
                    {
                        if ((kind.FactionBit == 0 || (faction & kind.FactionBit) == 0) && !(kind.Giant && giant)) continue;
                        Points.Add(MapPoint.At(kind, npc.DebugName, npc.Position, PointSource.Placement, npc.Guid, set));
                        placementPoints++;
                    }
                }
        }
        MoreMapFiltersMod.Debug($"Indexed enemy spawn points: {spawnerPoints} from spawners, {placementPoints} placements " +
                                      $"({contents.Count} content packs, {Buckets.Count} areas) in {sw.ElapsedMilliseconds} ms");
        return true;
    }

    /// <summary>Re-read every bucket: current variant, lock (revealed fog) and the eligible variants. True when anything
    /// changed (unlocked areas re-roll, areas lock / unlock, story progress opens new variants).</summary>
    public static bool UpdateLive(Frame f)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var eligible = new HashSet<long>();
        int payloads = 0, locked = 0;
        bool changed = false;
        var hero = new EntityRef();
        foreach (var b in BucketList)
        {
            ReadVariants(b);
            changed |= ReadBucket(f, b);
            if (b.Locked) locked++;
        }
        void ReadVariants(Bucket b)
        {
            var bucket = b.Id;
            try
            {
                var list = StreamingAPI.GetAmbiencePayloadCollectionForStreamingNode(f, bucket)?.Payloads.m_list;
                if (list == null) return;
                for (int i = 0; i < list.Count; i++)
                {
                    var p = list[i];
                    if (p == null) continue;
                    payloads++;
                    // PayloadData (BasePayload @0x10) = { Nullable<ulong> ContentMask, AssetGuid @0x10 }: the nugget guid.
                    long guid = System.Runtime.InteropServices.Marshal.ReadInt64(p.Pointer, 0x20);
                    BucketOfVariant[guid] = b;
                    if (p.Weight > 0 && p.ConditionsPassed(f, hero)) eligible.Add(guid);
                }
            }
            catch (Exception e)
            {
                if (!_eligibilityLogged) MoreMapFiltersMod.Log.Warning($"Reading area variants failed: {e.GetType().Name}: {e.Message}");
                _eligibilityLogged = true;
            }
        }
        if (!_eligibilityLogged)
        {
            _eligibilityLogged = true;
            MoreMapFiltersMod.Debug($"Area variants: {BucketList.Count} areas ({locked} locked: revealed, loaded or held by a quest), {eligible.Count} " +
                                          $"of {payloads} variants eligible, read in {sw.Elapsed.TotalMilliseconds:F1} ms");
        }
        changed |= !eligible.SetEquals(EligibleNuggets);
        var pinned = new List<string>();
        foreach (var b in BucketList) if (b.Pinned) pinned.Add((b.Node.Parent?.Name ?? "?") + "/" + b.Node.Name);
        string pinnedText = string.Join(", ", pinned);
        if (pinnedText != _pinnedLogged)
        {
            _pinnedLogged = pinnedText;
            MoreMapFiltersMod.Debug(pinned.Count == 0 ? "No area is held by a quest or bounty variant."
                                                            : $"Held by a quest or bounty variant ({pinned.Count}): {pinnedText}");
        }
        EligibleNuggets.Clear();
        EligibleNuggets.UnionWith(eligible);
        return changed;
    }

    private static int Occupying(MoonQuantumGame game, AssetGuid nugget, Dictionary<long, int> cache)
    {
        if (cache.TryGetValue(nugget.Value, out int f)) return f;
        try { f = (int)(Asset<NuggetData>(game, nugget)?.OccupyingFaction ?? 0); }
        catch (Exception e)
        {
            f = 0;
            if (cache.Count < 3) MoreMapFiltersMod.Log.Warning($"NuggetData {nugget.Value}: {e.GetType().Name}: {e.Message}");
        }
        cache[nugget.Value] = f;
        return f;
    }

    /// <summary>A Quantum asset by guid from the main thread. MoonQuantumGame.FindAsset asserts "Code must run on sim thread";
    /// the resource manager's GetAsset(..., mainThread: true) is the main-thread path.</summary>
    private static T? Asset<T>(MoonQuantumGame game, AssetGuid guid) where T : AssetObject =>
        IResourceManagerExtensions.GetAsset(game.ResourceManager, guid, true)?.TryCast<T>();


    public static void Reset()
    {
        _builtFrom = -1;
        Points.Clear();
        ActiveNuggets.Clear();
        EligibleNuggets.Clear();
        BucketList.Clear();
        ChunkNodes.Clear();
        BucketOfVariant.Clear();
        _watchedChunk = 0;
    }
}
