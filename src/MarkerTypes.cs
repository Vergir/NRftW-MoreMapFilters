using System.Collections.Generic;
using UnityEngine;

namespace MapResourceMarkers;

/// <summary>
/// Segments of the Markers row, in row order. Each belongs to one or two settings categories (MarkerCategory); dots
/// separate categories, not segments: Fish belongs to Harvesting and Cooking and stands between them with a dot on each
/// side when both are on, or joins whichever one is on (FilterRow.Layout).
/// </summary>
internal enum MarkerGroup
{
    Harvesting,   // trees, ores, digging spots
    Fish,         // carp, trout, bass
    Cooking,      // herbs, raw food
    Animals,
    Enemies,
    Valuables,    // treasure chests, special shinies, Dire Nettle
}

/// <summary>What a settings checkbox switches (Options > Gameplay).</summary>
internal enum MarkerCategory
{
    Harvesting,
    Cooking,
    Animals,
    Enemies,
    Valuables,
}

internal static class MarkerGroups
{
    public static readonly MarkerCategory[] Categories = (MarkerCategory[])System.Enum.GetValues(typeof(MarkerCategory));
    public static readonly MarkerGroup[] All = (MarkerGroup[])System.Enum.GetValues(typeof(MarkerGroup));

    public static bool In(MarkerGroup g, MarkerCategory c) => g switch
    {
        MarkerGroup.Harvesting => c == MarkerCategory.Harvesting,
        MarkerGroup.Fish => c is MarkerCategory.Harvesting or MarkerCategory.Cooking,
        MarkerGroup.Cooking => c == MarkerCategory.Cooking,
        MarkerGroup.Animals => c == MarkerCategory.Animals,
        MarkerGroup.Enemies => c == MarkerCategory.Enemies,
        _ => c == MarkerCategory.Valuables,
    };

    public static string Label(MarkerCategory c) => c switch
    {
        MarkerCategory.Harvesting => "Harvesting",
        MarkerCategory.Cooking => "Cooking",
        MarkerCategory.Animals => "Wild Animals",
        MarkerCategory.Enemies => "Enemies",
        _ => "Valuables",
    };

    /// <summary>Settings row tooltip: what the category shows, with examples.</summary>
    public static string Description(MarkerCategory c) => c switch
    {
        MarkerCategory.Harvesting =>
            "Map marker buttons for trees, ore veins, digging spots and fishing spots, e.g. Birch, Iron Ore, Bass.",
        MarkerCategory.Cooking =>
            "Map marker buttons for cooking ingredients: fish, herbs and food you can pick, e.g. Carp, Artemisia Herb, Mushroom, Tomato.",
        MarkerCategory.Animals =>
            "Map marker buttons for animals whose parts upgrade gear, e.g. Boar, Wolf, Bear.",
        MarkerCategory.Enemies =>
            "Map marker buttons for enemy factions, handy for \"Kill N\" challenges, e.g. The Risen, Nith, Harvesters.",
        _ => "Map marker buttons for treasure chests, special (orange) shinies and Dire Nettles. Shows where loot is: " +
             "this might feel like cheating or spoil the fun of exploring.",
    };

    /// <summary>Off until the player opts in: Valuables (shows where loot is) and Cooking (a long row of ingredients).</summary>
    public static bool DefaultOn(MarkerCategory c) => c is not (MarkerCategory.Valuables or MarkerCategory.Cooking);
}

/// <summary>Corner badge on a marker: "?" = the point only might have it (animal spawners), cross = cleared (harvested /
/// killed, waiting for the fog to creep back; ClearedState). Cleared wins over "?".</summary>
internal enum Badge
{
    None,
    Question,
    Cross,
}

/// <summary>One selectable kind of marker (a button in the row). Id is what Prefs.Selected stores.</summary>
internal abstract class MarkerType
{
    public readonly string Id;
    public readonly string Label;
    public readonly MarkerGroup Group;

    protected MarkerType(string id, string label, MarkerGroup group)
    {
        Id = id;
        Label = label;
        Group = group;
    }

    /// <summary>Sprite for the row button (the MapMarker tints it enabled / disabled).</summary>
    public abstract Sprite? ButtonSprite();
    /// <summary>Sprite for the map markers, with a corner badge (top right) and, for points drawn at a cave's mouth, the
    /// doorway glyph (top left).</summary>
    public abstract Sprite? MarkerSprite(Badge badge, bool cave);
}

/// <summary>A resource type: farmable nodes (trees, ore veins, digging and fishing spots) or ground pickups (herbs, food,
/// nettles). A node belongs to it when its first loot source is one of <see cref="LootSources"/> (LootSourceDataAsset
/// guids, build 29466; names in analysis/qdb_asset_names.json).</summary>
internal sealed class ResourceType : MarkerType
{
    /// <summary>HeroItemDataAsset name and asset guid whose ItemIcon is the button and marker picture.</summary>
    public readonly string IconItem;
    public readonly long IconItemGuid;
    public readonly long[] LootSources;
    /// <summary>A second item sharing the picture (MarkerArt.Split): the IconItem in the top-left, this one in the
    /// bottom-right, cut along the diagonal between them.</summary>
    public (string Name, long Guid)? SecondItem { get; init; }

    public ResourceType(MarkerGroup group, string id, string label, string iconItem, long iconItemGuid, params long[] lootSources)
        : base(id, label, group)
    {
        IconItem = iconItem;
        IconItemGuid = iconItemGuid;
        LootSources = lootSources;
    }

    private Sprite? Picture() => SecondItem is { } second
        ? MarkerArt.Split(Id, ItemIcons.Get(IconItem, IconItemGuid), ItemIcons.Get(second.Name, second.Guid))
        : ItemIcons.Get(IconItem, IconItemGuid);

    public override Sprite? ButtonSprite() => Picture();
    public override Sprite? MarkerSprite(Badge badge, bool cave) => MarkerArt.ForPicture(Id, Picture(), badge, cave);
}

/// <summary>
/// An enemy group as the daily "Kill N X" challenges count it: an NpcData tag (MarkupTag, analysis/repeatable_quests.md 3.1).
/// Faction tags come from EnemyFaction bits (FactionBit); Giants are EnemyType.Giant (tag 7). Markers are spawn points (see
/// EnemyIndex), drawn as the game's skull map glyph tinted per group.
/// </summary>
internal sealed class EnemyKind : MarkerType
{
    public readonly int Tag;
    /// <summary>EnemyFaction flag for placements / faction-less spawners; 0 = not a faction (Giants).</summary>
    public readonly int FactionBit;
    public readonly bool Giant;
    public readonly Color32 Tint;

    public EnemyKind(string id, string label, int tag, int factionBit, Color32 tint, bool giant = false)
        : base(id, label, MarkerGroup.Enemies)
    {
        Tag = tag;
        FactionBit = factionBit;
        Tint = tint;
        Giant = giant;
    }

    public override Sprite? ButtonSprite() => MarkerArt.ForGlyph(this, outlined: false, Badge.None, false);
    public override Sprite? MarkerSprite(Badge badge, bool cave) => MarkerArt.ForGlyph(this, outlined: true, badge, cave);
}

/// <summary>
/// An animal whose kills drop upgrade parts (analysis/animal_parts.md): every kill rolls 1-2 of its three parts, so one
/// button per animal, drawn with its skull part. Species = NpcData guids that roll the parts (Torn Canid counts as a wolf).
/// </summary>
internal sealed class AnimalKind : MarkerType
{
    public readonly string IconItem;
    public readonly long IconItemGuid;
    public readonly long[] Species;

    public AnimalKind(string id, string label, string iconItem, long iconItemGuid, params long[] species)
        : base(id, label, MarkerGroup.Animals)
    {
        IconItem = iconItem;
        IconItemGuid = iconItemGuid;
        Species = species;
    }

    public override Sprite? ButtonSprite() => ItemIcons.Get(IconItem, IconItemGuid);
    public override Sprite? MarkerSprite(Badge badge, bool cave) =>
        MarkerArt.ForPicture(Id, ItemIcons.Get(IconItem, IconItemGuid), badge, cave);
}

/// <summary>
/// Treasure chests (analysis/chest_state.md): ~2,100 chest spawners (DynamicSpawnerData tagged SmallChest 20004 ..
/// SpecialChest 20007; the size is rolled per spawn) and the static chests (ContainerStaticInstanceData with chestData /
/// smallChestData). One button for all sizes. Spawners that may roll a ground shiny instead (tags 20008 / 20009) are
/// "possible". Drawn with the game's chest map glyph, tinted gold.
/// </summary>
internal sealed class ChestKind : MarkerType
{
    public static readonly Color32 Tint = new(255, 205, 95, 255);
    public static readonly long[] StaticSettings = { 1677010924608272942, 1419899727325947293 };   // chestData, smallChestData

    public ChestKind() : base("chests", "Treasure Chests", MarkerGroup.Valuables) { }

    public override Sprite? ButtonSprite() => MarkerArt.ForGlyph(Id, "ChestMapIcon", Tint, outlined: false, Badge.None, false);
    public override Sprite? MarkerSprite(Badge badge, bool cave) => MarkerArt.ForGlyph(Id, "ChestMapIcon", Tint, outlined: true, badge, cave);
}

/// <summary>
/// Special (orange) shinies (analysis/shinies.md): spawners tagged SpecialShiny (20009) spawn shinySpecialDynamicData, a
/// ground sparkle with an off-hand / gem / ember / research paper plus 1-2 Blue or Purple gear pieces. 63 spots; a spawner
/// mixed with other loot tags is only "possible". Normal (blue) shinies are not shown: ~630 spots of mostly materials.
/// Drawn with the game's star map glyph tinted orange.
/// </summary>
internal sealed class SpecialShinyKind : MarkerType
{
    public static readonly Color32 Tint = new(255, 140, 40, 255);

    public SpecialShinyKind() : base("specialshinies", "Special Shinies", MarkerGroup.Valuables) { }

    public override Sprite? ButtonSprite() => MarkerArt.ForGlyph(Id, "StarMapIcon", Tint, outlined: false, Badge.None, false);
    public override Sprite? MarkerSprite(Badge badge, bool cave) => MarkerArt.ForGlyph(Id, "StarMapIcon", Tint, outlined: true, badge, cave);
}

internal static class MarkerTypes
{
    public static readonly ChestKind Chest = new();
    public static readonly SpecialShinyKind SpecialShiny = new();

    /// <summary>Order = order in the row: tiered groups weakest to strongest (item DropLevel 1 / 11 / 21), the others
    /// alphabetical.</summary>
    public static readonly ResourceType[] Resources =
    {
        new(MarkerGroup.Harvesting, "pine", "Pine", "pineWood", 3191183561878509088, 2507069714705311797),
        new(MarkerGroup.Harvesting, "spruce", "Spruce", "spruceWood", 2466785325384271848, 1755435428372031607),
        new(MarkerGroup.Harvesting, "birch", "Birch", "birchWood", 3703354541582648915, 3827233106795457381),
        new(MarkerGroup.Harvesting, "copper", "Copper Ore", "copperOre", 1876157270097675752, 4555293537027968619),
        new(MarkerGroup.Harvesting, "iron", "Iron Ore", "ironOre", 996809264675242694, 3291196766757105210),
        new(MarkerGroup.Harvesting, "silver", "Silver Ore", "silverOre", 3172688301877486470, 3337855123421323678),
        // Tiers 1..3 share one loot table (only the gold amount differs; items depend on the area's loot level), so one
        // button (analysis/resource_loot.md).
        new(MarkerGroup.Harvesting, "dig", "Digging Spots", "copperShovel", 4035307286508899593,
            3645522471762548633, 1783696367591900281, 130891197561177058),
        // One fish species per fishing tier (each spot gives one fish of it); trout spots also have a 30% Gangrenous Eel.
        new(MarkerGroup.Fish, "carp", "Carp", "rawCarp", 3974289298266048918, 1100972428867542908),
        new(MarkerGroup.Fish, "trout", "Trout and Gangrenous Eel", "rawTrout", 402846414938053789, 630363913923906831)
            { SecondItem = ("gangrenousEel", 163932043211063097) },
        new(MarkerGroup.Fish, "bass", "Bass", "rawBass", 2894787350020426640, 689802455546116992),
        // Ground pickups (StaticPickupStaticInstanceData, 782 in the world): picture = the item the loot source gives.
        new(MarkerGroup.Cooking, "artemisia", "Artemisia Herb", "artemisiaHerb", 2271648021907577248, 4061078049747029281),
        new(MarkerGroup.Cooking, "dracaena", "Dracaena Herb", "dracaenaHerb", 3492249412788768703, 4283188503883380655),
        new(MarkerGroup.Cooking, "lithops", "Lithops Herb", "lithopsHerb", 2460116408179348514, 3413751789013475074),
        new(MarkerGroup.Cooking, "blueberries", "Blueberries", "blueberries", 529511889418667429, 2463191737202403129),
        new(MarkerGroup.Cooking, "firebrand", "Firebrand Berries", "firebrandBerries", 180833899404673680, 2485832729852243706),
        new(MarkerGroup.Cooking, "crab", "Horseshoe Crab", "horseshoeCrab", 3471571811583308745, 1147639769189290294),
        new(MarkerGroup.Cooking, "mushroom", "Mushroom", "mushroom", 334358031176021315, 823578215697629511),
        new(MarkerGroup.Cooking, "potato", "Potato", "potato", 2021943158973951263, 858938746346541843),
        new(MarkerGroup.Cooking, "tomato", "Tomato", "tomato", 4122645235203468911, 2699393012938231103),
        new(MarkerGroup.Valuables, "nettle", "Dire Nettle", "direNettleSpores", 4148592465844323776, 2496325793267758538),
    };

    /// <summary>The kill challenges' target choices (KillEnemiesRandomized...AllowedEnemyTagChoices), build 29466;
    /// alphabetical.</summary>
    public static readonly EnemyKind[] Enemies =
    {
        new("balaktaw", "Balak Taw", 17, 16, new Color32(245, 210, 80, 255)),
        new("boarskin", "Boarskin", 19, 64, new Color32(175, 115, 70, 255)),
        new("falseones", "False Ones", 25, 4096, new Color32(60, 210, 195, 255)),
        new("giant", "Giants", 7, 0, new Color32(255, 125, 175, 255), giant: true),
        new("harvesters", "Harvesters", 26, 8192, new Color32(215, 185, 140, 255)),
        new("tanth", "Knights of Tanth", 18, 32, new Color32(235, 70, 60, 255)),
        new("nith", "Nith", 23, 1024, new Color32(185, 110, 235, 255)),
        new("plagued", "Plagued", 5, 4, new Color32(150, 210, 60, 255)),
        new("prisoner", "Prisoners", 22, 512, new Color32(240, 150, 60, 255)),
        new("risen", "The Risen", 16, 8, new Color32(170, 215, 255, 255)),
    };

    /// <summary>NpcData guids (NpcGenericDataAsset), build 29466. Siren Offspring (npcSirenSmall) is left out: ~1% parts.
    /// Ordered by part tier (skull DropLevel 11: wolf, stag, siren; 21: boar, brood, bear), then part value.</summary>
    public static readonly AnimalKind[] Animals =
    {
        new("wolf", "Wolf", "wolfSkull", 1824559763003114189,
            857226759835938393, 802912525283026355, 1953168382649975962),
        new("stag", "Stag", "stagSkull", 753759426240132740, 2151685912851605104),
        new("siren", "Siren", "sirenSkull", 4543695367318167682, 1218440316423504502, 326349724471319267),
        new("boar", "Boar", "boarSkull", 2430793436759048744, 2724047205208832934),
        new("brood", "Winged Brood", "wingedBroodSkull", 3873599242578289189, 2363163903645806430),
        new("bear", "Bear", "bearSkull", 3007632164041505884, 220661299822949939, 252520450036965259),
    };

    public static readonly MarkerType[] All = Build();

    private static MarkerType[] Build()
    {
        var all = new List<MarkerType>();
        all.AddRange(Resources);
        all.AddRange(Enemies);
        all.AddRange(Animals);
        all.Add(Chest);
        all.Add(SpecialShiny);
        // Row order (stable sort); in Valuables the chests and shinies before Dire Nettle.
        return System.Linq.Enumerable.ToArray(System.Linq.Enumerable.ThenBy(
            System.Linq.Enumerable.OrderBy(all, t => t.Group), t => t is ResourceType ? 1 : 0));
    }

    private static Dictionary<long, ResourceType>? _byLoot;

    public static ResourceType? ByLootSource(long guid)
    {
        if (_byLoot == null)
        {
            _byLoot = new Dictionary<long, ResourceType>();
            foreach (var t in Resources)
                foreach (var g in t.LootSources) _byLoot[g] = t;
        }
        return _byLoot.TryGetValue(guid, out var type) ? type : null;
    }
}
