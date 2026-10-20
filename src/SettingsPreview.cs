using System.Collections.Generic;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MapMarkers;

/// <summary>
/// A live picture under the description of our Options rows, so the change is visible before the map is opened:
/// a group's markers (shown while the group is on), markers at the chosen size, and a strip of explored land and fog
/// with markers on both sides (the fogged ones only while "unexplored areas" is on). The settings screen shows the
/// hovered / selected row's name and description in two texts (SettingsScreenControls.m_settingNameText /
/// m_specificationText); the row is recognized by its name, the picture hangs under the description text.
/// </summary>
internal static class SettingsPreview
{
    private enum Kind { None, Category, Size, Unexplored }

    private sealed class Screen
    {
        public TMP_Text Name = null!, Spec = null!;
        public RectTransform? Root;
        public string Shown = "";
        public float ShownScale = -1f;
        public int ShownOn = -1;
        public float ShownHeight = -1f;
        public float RetryAt;
    }

    private static readonly List<Screen> Screens = new();
    private static readonly Dictionary<string, (Kind Kind, MarkerCategory Category)> Rows = new();

    private const float Gap = 14f, IconPx = 34f, Spacing = 4f, StripHeight = 76f;

    public static void Register(string label, MarkerCategory c) => Rows[label] = (Kind.Category, c);
    public static void RegisterSize(string label) => Rows[label] = (Kind.Size, default);
    public static void RegisterUnexplored(string label) => Rows[label] = (Kind.Unexplored, default);

    /// <summary>The description texts of a settings screen (called when our rows are added).</summary>
    public static void Track(TMP_Text? name, TMP_Text? spec)
    {
        if (name == null || spec == null) return;
        Screens.RemoveAll(s => s.Name == null || s.Spec == null);
        foreach (var s in Screens) if (s.Spec.Pointer == spec.Pointer) return;
        Screens.Add(new Screen { Name = name, Spec = spec });
    }

    /// <summary>Every frame: only reads two texts while a settings screen is open.</summary>
    public static void Tick()
    {
        for (int i = Screens.Count - 1; i >= 0; i--)
        {
            var s = Screens[i];
            if (s.Name == null || s.Spec == null) { Destroy(s); Screens.RemoveAt(i); continue; }
            if (!s.Spec.gameObject.activeInHierarchy) continue;
            string name = s.Name.text ?? "";
            if (!Rows.TryGetValue(name, out var row)) { Hide(s); continue; }
            Show(s, name, row.Kind, row.Category);
        }
    }

    private static void Hide(Screen s)
    {
        if (s.Root != null && s.Root.gameObject.activeSelf) s.Root.gameObject.SetActive(false);
        s.Shown = "";
    }

    private static void Show(Screen s, string name, Kind kind, MarkerCategory c)
    {
        float scale = Prefs.MarkerScale.Value;
        int on = kind switch
        {
            Kind.Category => Prefs.CategoryOn(c) ? 1 : 0,
            Kind.Unexplored => Prefs.ShowInUnrevealed.Value ? 1 : 0,
            _ => 0,
        };
        float height = s.Spec.preferredHeight;
        bool same = s.Shown == name && s.ShownOn == on && Mathf.Abs(s.ShownHeight - height) < 0.5f
                    && (kind != Kind.Size && kind != Kind.Unexplored || Mathf.Abs(s.ShownScale - scale) < 0.001f);
        if (same && s.Root != null) return;
        if (s.Shown == "" && s.Root != null && Time.unscaledTime < s.RetryAt) return;
        if (s.Root == null) s.Root = NewRoot(s.Spec);
        var root = s.Root;
        for (int i = root.childCount - 1; i >= 0; i--) Object.Destroy(root.GetChild(i).gameObject);
        bool complete = kind switch
        {
            Kind.Category => on == 0 || Icons(root, Types(c), IconPx, s.Spec.rectTransform.rect.width),
            Kind.Size => Icons(root, SizeSamples(), IconPx * scale, s.Spec.rectTransform.rect.width),
            Kind.Unexplored => Strip(root, s.Spec.rectTransform.rect.width, on == 1, scale),
            _ => true,
        };
        // Under the description text (top aligned); the settings screen's own layout does not move it.
        var r = s.Spec.rectTransform.rect;
        root.localPosition = new Vector3(r.xMin, r.yMax - height - Gap, 0f);
        root.gameObject.SetActive(true);
        // Sprites not loaded yet (map art comes with the player menu): try again next frame.
        s.Shown = complete ? name : "";
        s.RetryAt = Time.unscaledTime + 0.5f;
        s.ShownOn = on;
        s.ShownScale = scale;
        s.ShownHeight = height;
    }

    private static RectTransform NewRoot(TMP_Text spec)
    {
        var go = new GameObject("MRM_SettingsPreview", Il2CppInterop.Runtime.Il2CppType.Of<RectTransform>());
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(spec.transform, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = Vector2.zero;
        return rt;
    }

    private static void Destroy(Screen s)
    {
        if (s.Root != null) Object.Destroy(s.Root.gameObject);
        s.Root = null;
    }

    /// <summary>Hot reload / unload.</summary>
    public static void RemoveAll()
    {
        foreach (var s in Screens) Destroy(s);
        Screens.Clear();
        Rows.Clear();
    }

    private static List<MarkerType> Types(MarkerCategory c)
    {
        var list = new List<MarkerType>();
        foreach (var t in MarkerTypes.All) if (MarkerGroups.In(t.Group, c)) list.Add(t);
        return list;
    }

    private static List<MarkerType> SizeSamples()
    {
        var list = new List<MarkerType>();
        foreach (var id in new[] { "pine", "copper", "dig", "wolf", "risen", "chests" })
            foreach (var t in MarkerTypes.All)
                if (t.Id == id) { list.Add(t); break; }
        return list;
    }

    /// <summary>Map markers in rows across the description's width; false when a sprite is missing.</summary>
    private static bool Icons(RectTransform root, List<MarkerType> types, float iconPx, float width)
    {
        // The baked sprite has room for outline and shadow around the picture (MarkerArt.IconFraction).
        float size = iconPx / MarkerArt.IconFraction, step = iconPx + Spacing;
        int perRow = Mathf.Max(1, Mathf.FloorToInt((width + Spacing) / step));
        bool complete = true;
        for (int i = 0; i < types.Count; i++)
        {
            var sprite = types[i].MarkerSprite(Badge.None, false);
            if (sprite == null) { complete = false; continue; }
            float x = (i % perRow) * step + iconPx * 0.5f, y = -(i / perRow) * (iconPx + Spacing) - iconPx * 0.5f;
            Image(root, sprite, new Vector2(x, y), size);
        }
        return complete;
    }

    private static Texture2D? _fog;
    private static Sprite? _fogSprite;

    /// <summary>Explored land on the left, fog on the right; markers on both, the fogged ones only when shown.</summary>
    private static bool Strip(RectTransform root, float width, bool showFogged, float scale)
    {
        var bg = Image(root, FogSprite(), new Vector2(width * 0.5f, -StripHeight * 0.5f), 0f);
        bg.rectTransform.sizeDelta = new Vector2(width, StripHeight);
        var samples = SizeSamples();
        // (fraction of the width, of the height, fogged)
        var spots = new (float X, float Y, bool Fogged)[]
            { (0.10f, 0.55f, false), (0.24f, 0.30f, false), (0.36f, 0.68f, false),
              (0.60f, 0.36f, true), (0.74f, 0.66f, true), (0.89f, 0.40f, true) };
        float iconPx = IconPx * scale, size = iconPx / MarkerArt.IconFraction;
        bool complete = true;
        for (int i = 0; i < spots.Length && i < samples.Count; i++)
        {
            if (spots[i].Fogged && !showFogged) continue;
            var sprite = samples[i].MarkerSprite(Badge.None, false);
            if (sprite == null) { complete = false; continue; }
            Image(root, sprite, new Vector2(width * spots[i].X, -StripHeight * spots[i].Y), size);
        }
        return complete;
    }

    /// <summary>A painted-map look: warm land with some texture, then a ragged edge into dark, cloudy fog.</summary>
    private static Sprite FogSprite()
    {
        if (_fogSprite != null) return _fogSprite;
        const int w = 512, h = 96;
        _fog = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        var px = new Color32[w * h];
        var land = new Color(0.47f, 0.42f, 0.30f);
        var land2 = new Color(0.36f, 0.38f, 0.25f);
        var fog = new Color(0.13f, 0.12f, 0.11f);
        var fog2 = new Color(0.24f, 0.23f, 0.21f);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float n = Mathf.PerlinNoise(x * 0.025f, y * 0.025f), n2 = Mathf.PerlinNoise(x * 0.09f + 40f, y * 0.09f + 13f);
                var l = Color.Lerp(land, land2, n) * (0.9f + 0.2f * n2);
                var f = Color.Lerp(fog, fog2, Mathf.PerlinNoise(x * 0.018f + 7f, y * 0.03f + 3f));
                float edge = w * 0.5f + (Mathf.PerlinNoise(3f, y * 0.06f) - 0.5f) * 70f + (n2 - 0.5f) * 24f;
                float t = Mathf.Clamp01((x - edge) / 26f + 0.5f);
                var c = Color.Lerp(l, f, t);
                // Soft rounded ends, so the strip does not look like a pasted box.
                float a = Mathf.Clamp01(Mathf.Min(x, w - 1 - x) / 10f) * Mathf.Clamp01(Mathf.Min(y, h - 1 - y) / 6f);
                px[y * w + x] = new Color(c.r, c.g, c.b, a);
            }
        _fog.SetPixels32(px);
        _fog.Apply();
        _fogSprite = Sprite.Create(_fog, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
        _fogSprite.hideFlags = HideFlags.HideAndDontSave;
        return _fogSprite;
    }

    private static Image Image(RectTransform root, Sprite sprite, Vector2 center, float size)
    {
        var go = new GameObject("MRM_PreviewIcon", Il2CppInterop.Runtime.Il2CppType.Of<RectTransform>());
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(root, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = center;
        rt.sizeDelta = new Vector2(size, size);
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = true;
        img.raycastTarget = false;
        return img;
    }
}
