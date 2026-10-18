using System;
using System.Collections.Generic;
using Il2CppQuantum;
using UnityEngine;

namespace MapResourceMarkers;

/// <summary>
/// Item icon sprites (HeroItemDataAsset.ItemIcon) for the resource types. Item assets are loaded on demand: right after
/// a game start only the items the player owns are in memory (a scan of loaded assets found the fishing rod and nothing
/// else), so the game's own lookup is used first, the one the inventory uses (ItemViewUtils.GetItemAsset ->
/// MoonQuantumGame.FindWrapper&lt;T&gt;(item guid)); a scan of loaded assets is the fallback. Misses are retried on every
/// map open (MapOverlay/FilterRow re-apply sprites then).
/// </summary>
internal static class ItemIcons
{
    private static readonly Dictionary<string, Sprite> Cache = new();

    public static Sprite? Get(string itemName, long itemGuid)
    {
        if (Cache.TryGetValue(itemName, out var cached) && cached != null) return cached;

        var fromGame = FromGame(itemName, itemGuid);
        var sprite = fromGame ?? FromLoadedAssets(itemName);
        if (sprite == null) MapResourceMarkersMod.Debug($"Item icon '{itemName}' not available yet; retrying on the next map open.");
        if (sprite != null) Cache[itemName] = sprite;
        return sprite;
    }

    private static Sprite? FromGame(string itemName, long itemGuid)
    {
        try
        {
            var game = NodeIndex.LocalFrame()?.Game?.TryCast<MoonQuantumGame>();
            var asset = game?.FindWrapper<Il2Cpp.HeroItemDataAsset>(new AssetGuid(itemGuid));
            return asset != null ? asset.ItemIcon : null;
        }
        catch (Exception e)
        {
            MapResourceMarkersMod.Log.Warning($"FindWrapper({itemName}): {e.Message}");
            return null;
        }
    }

    private static Sprite? FromLoadedAssets(string itemName)
    {
        foreach (var item in Resources.FindObjectsOfTypeAll<Il2Cpp.HeroItemDataAsset>())
        {
            if (item == null || item.ItemIcon == null) continue;
            if (item.name == itemName) return item.ItemIcon;
        }
        return null;
    }
}
