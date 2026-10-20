using System;
using System.Collections.Generic;
using Il2CppMoon.Forsaken;
using Il2CppQuantum;

namespace MoreMapFilters;

/// <summary>
/// Button names in the game's language. In English the mod's own labels are kept ("Pine", "Giants", "Digging Spots"); in
/// any other language the game's own texts are used:
/// - enemy kinds: the challenge's enemy type names (Core.Instance.EnemyTypesLocalization, EnemyTypeLocalization.Localize);
/// - resources: the item the node gives (HeroItemDataAsset.ItemNameMsg); trout spots "Trout / Gangrenous Eel";
/// - animals: their skull part (the button picture; the NPC names are inconsistent: the boar has none, the bear is
///   "Rust Bear");
/// - digging spots: the dig spot's short interaction prompt "Dig" (digSpotResourceData
///   .LocalizedShortenedHasToolHint, key ui.hints.inLineHints.DigSpotToolHint).
/// The row title, group names, "(none right now)" and the settings rows stay English (the game has no text for them).
/// "English" = a game text resolves to its English string (LocalizedMessageExtensions.GetText vs LocalizedMessage.English),
/// re-checked whenever the row is rebuilt or the map opens (Reset).
/// </summary>
internal static class Localization
{
    private static readonly Dictionary<MarkerType, string> Cache = new();
    private static bool? _english;
    private static bool _languageLogged;

    public static void Reset()
    {
        Cache.Clear();
        _english = null;
    }

    public static string Label(MarkerType type)
    {
        if (IsEnglish()) return type.Label;
        if (Cache.TryGetValue(type, out var cached)) return cached;
        string? text = null;
        try
        {
            text = type switch
            {
                EnemyKind k => EnemyName(k.Tag),
                ResourceType r when r.Id == "dig" => DigText(),
                ResourceType r when r.SecondItem is { } second => Join(ItemName(r.IconItemGuid), ItemName(second.Guid)),
                ResourceType r => ItemName(r.IconItemGuid),
                AnimalKind a => ItemName(a.IconItemGuid),
                _ => null,
            };
        }
        catch (Exception e)
        {
            MoreMapFiltersMod.Log.Warning($"Localized name of {type.Id}: {e.Message}");
        }
        if (string.IsNullOrWhiteSpace(text)) return type.Label;   // not resolvable (yet): retried next time
        Cache[type] = text!;
        return text!;
    }

    private static bool IsEnglish()
    {
        if (_english is bool known) return known;
        try
        {
            var mappings = Il2CppMoon.Forsaken.Core.Instance?.EnemyTypesLocalization?.EnemyTypeMappings;
            var msg = mappings != null && mappings.Length > 0 ? mappings[0].LocalizedEnemyTypeName : null;
            if (msg == null) return true;   // not loaded yet: English for now, decided later
            _english = LocalizedMessageExtensions.GetText(msg, "") == msg.English;
            if (!_languageLogged)
            {
                _languageLogged = true;
                string Sample(MarkerType t) => t switch
                {
                    EnemyKind k => EnemyName(k.Tag) ?? "?",
                    ResourceType r when r.Id == "dig" => DigText() ?? "?",
                    ResourceType r => ItemName(r.IconItemGuid) ?? "?",
                    AnimalKind a => ItemName(a.IconItemGuid) ?? "?",
                    _ => "?",
                };
                MoreMapFiltersMod.Debug($"Game language {(_english.Value ? "English: the mod's own button names" : "not English: the game's names")}" +
                                              $" (game texts e.g. {Sample(MarkerTypes.Enemies[0])}, {Sample(MarkerTypes.Resources[0])}, {Sample(MarkerTypes.Animals[0])}, {Sample(Array.Find(MarkerTypes.Resources, t => t.Id == "dig")!)})");
            }
            return _english.Value;
        }
        catch { return true; }
    }

    private static string? EnemyName(int tag) =>
        Il2CppMoon.Forsaken.Core.Instance?.EnemyTypesLocalization?.Localize((MarkupTag)tag);

    private static string? ItemName(long guid)
    {
        var game = NodeIndex.LocalFrame()?.Game?.TryCast<MoonQuantumGame>();
        var item = game?.FindWrapper<Il2Cpp.HeroItemDataAsset>(new AssetGuid(guid));
        var msg = item?.ItemNameMsg;
        return msg != null ? LocalizedMessageExtensions.GetText(msg, "") : null;
    }

    /// <summary>LinkedFarmableResourceDataAsset digSpotResourceData (guid 964127749868152500).</summary>
    private static string? DigText()
    {
        var game = NodeIndex.LocalFrame()?.Game?.TryCast<MoonQuantumGame>();
        var spot = game?.FindWrapper<Il2Cpp.LinkedFarmableResourceDataAsset>(new AssetGuid(964127749868152500));
        var msg = spot?.LocalizedShortenedHasToolHint;
        return msg != null ? LocalizedMessageExtensions.GetText(msg, "") : null;
    }

    private static string? Join(string? a, string? b) => a != null && b != null ? a + " / " + b : a ?? b;
}
