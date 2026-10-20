using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoreMapFilters;

/// <summary>
/// Map marker sprites baked once per type from the item icon (style "B" picked by the user from mockups): the item picture
/// cropped to its visible part, a dark outline around its shape and a soft drop shadow, so it reads as a map icon on both
/// the light and dark parts of the map instead of blending in. Baked into a texture (not uGUI Outline/Shadow components):
/// the soft shadow needs a blur, and other mods that mirror our markers (the Minimap mod copies Images under the icon
/// layer) get the same look from a plain sprite.
///
/// The item sprites are GPU-only, so they are read back through a RenderTexture (Graphics.Blit + ReadPixels), halving
/// the size on the way down to avoid aliasing; the outline / shadow are alpha dilate + box blur on the CPU (~ms per type).
///
/// Enemy kinds use the game's skull map glyph (SkullMapIcon: white with a soft dark glow) multiplied by the kind's tint,
/// baked twice: with outline + shadow for the map, plain for the row button (the button's MapMarker overwrites the Image
/// color with its enabled / disabled color, so the tint has to be in the texture).
/// </summary>
internal static class MarkerArt
{
    /// <summary>Baked sprite size; the item picture fills IconPx of it, the rest is room for outline and shadow.</summary>
    private const int Size = 128;
    private const int IconPx = 84;
    /// <summary>Visible icon relative to the sprite, for sizing markers like the old raw item pictures.</summary>
    public const float IconFraction = IconPx / (float)Size;

    private const int OutlineRadius = 4;
    private static readonly Color32 OutlineColor = new Color32(15, 12, 10, 255);
    private const int ShadowRadius = 6, ShadowBlur = 6, ShadowDx = 4, ShadowDy = -6;
    private const float ShadowOpacity = 200f / 255f;

    private static readonly Dictionary<string, Sprite> Cache = new();

    /// <summary>Badge glyphs: the game's QuestionMapIcon (white) and UICrossIcon (brushed X, tinted red), on a dark disc in
    /// the top-right corner; DoorwayMapIcon (the game's own map glyph for doorways) in the top-left one for points drawn at a
    /// cave's mouth.</summary>
    private static readonly Color32 CrossTint = new(255, 105, 92, 255);

    private static bool BadgeGlyph(Badge badge, out Sprite? glyph, out Color32? tint)
    {
        tint = null;
        glyph = null;
        if (badge == Badge.None) return true;
        glyph = GameSprites.Find(badge == Badge.Question ? "QuestionMapIcon" : "UICrossIcon");
        if (badge == Badge.Cross) tint = CrossTint;
        return glyph != null;
    }

    private static bool CaveGlyph(bool cave, out Sprite? glyph)
    {
        glyph = cave ? GameSprites.Find("DoorwayMapIcon") : null;
        return !cave || glyph != null;
    }

    /// <summary>Picture marker (item icon or a Split composite) with optional corner badges.</summary>
    public static Sprite? ForPicture(string id, Sprite? icon, Badge badge, bool cave)
    {
        string key = id + "_" + badge + (cave ? "_cave" : "");
        if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
        if (icon == null || !BadgeGlyph(badge, out var glyph, out var glyphTint) || !CaveGlyph(cave, out var caveGlyph)) return null;
        try
        {
            var sprite = Bake(icon, key, null, true, glyph, glyphTint, caveGlyph);
            if (sprite != null) Cache[key] = sprite;
            return sprite;
        }
        catch (Exception e)
        {
            MoreMapFiltersMod.Log.Warning($"Baking the {key} marker failed ({e.Message}); using the plain item picture.");
            return icon;
        }
    }

    public static Sprite? ForGlyph(EnemyKind kind, bool outlined, Badge badge, bool cave) =>
        ForGlyph(kind.Id, "SkullMapIcon", kind.Tint, outlined, badge, cave);

    /// <summary>A game map glyph (SkullMapIcon, ChestMapIcon) tinted, outlined for the map or plain for the button.</summary>
    public static Sprite? ForGlyph(string id, string glyphName, Color32 tint, bool outlined, Badge badge, bool cave)
    {
        string key = id + (outlined ? "_" + badge + (cave ? "_cave" : "") : "_button");
        if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
        var glyph = GameSprites.Find(glyphName);
        if (glyph == null || !BadgeGlyph(badge, out var badgeGlyph, out var badgeTint) || !CaveGlyph(cave, out var caveGlyph)) return null;
        try
        {
            var sprite = Bake(glyph, key, tint, outlined, badgeGlyph, badgeTint, caveGlyph);
            if (sprite != null) Cache[key] = sprite;
            return sprite;
        }
        catch (Exception e)
        {
            MoreMapFiltersMod.Log.Warning($"Baking the {key} marker failed ({e.Message}); using the plain glyph.");
            return glyph;
        }
    }

    private const int BadgePx = 50, BadgeGlyphPx = 32;
    /// <summary>Visible area of a map marker picture as a share of IconPx^2, and the largest side it may take.</summary>
    private const float TargetArea = 0.42f;
    private const int MaxIconPx = 96;

    /// <summary>
    /// Two item pictures in one (user's design for Trout + Gangrenous Eel, both from trout spots): <paramref name="a"/> in the
    /// top-left, <paramref name="b"/> in the bottom-right, each in a Box-sized square pushed into its corner, so they overlap
    /// the bottom-left -> top-right diagonal a little; each keeps only its side of that diagonal (~90% visible: a long fish
    /// lies parallel to the cut), with a thin light line with a dark edge on the cut. Readable 256 px texture so Bake can
    /// read it like any sprite; also the row button picture.
    /// </summary>
    public static Sprite? Split(string id, Sprite? a, Sprite? b)
    {
        string key = id + "_split";
        if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
        if (a == null || b == null) return null;
        const int N = 256, Box = 192;
        var px = new Color32[N * N];
        // Texture rows go bottom-up: top-left = y > x.
        void Place(Sprite s, bool topLeft)
        {
            var full = Read(s, 256, 256, null);
            if (full == null) return;
            var bb = AlphaBounds(full, 256, 256);
            if (bb.width <= 0) return;
            float k = Box / Mathf.Max(bb.width, bb.height);
            int w = Mathf.Max(1, Mathf.RoundToInt(bb.width * k)), h = Mathf.Max(1, Mathf.RoundToInt(bb.height * k));
            var pic = Read(s, w, h, new Rect(bb.x / 256f, bb.y / 256f, bb.width / 256f, bb.height / 256f));
            if (pic == null) return;
            int x0 = topLeft ? 0 : N - Box, y0 = topLeft ? N - Box : 0;
            int ox = x0 + (Box - w) / 2, oy = y0 + (Box - h) / 2;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int tx = x + ox, ty = y + oy;
                    if ((ty > tx) != topLeft) continue;
                    int i = ty * N + tx;
                    px[i] = Over(pic[y * w + x], px[i]);
                }
        }
        Place(a, topLeft: true);
        Place(b, topLeft: false);
        // Thin divider on the cut (y = x), a little short of the corners.
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                if (x < 28 || x > N - 28) continue;
                float d = Mathf.Abs(y - x) / 1.41421f;
                if (d > 3f) continue;
                var c = d <= 1.25f ? new Color(0.92f, 0.88f, 0.78f, 0.9f) : new Color(0.06f, 0.05f, 0.04f, Mathf.Clamp01(3.5f - d) * 0.8f);
                px[y * N + x] = Over(c, px[y * N + x]);
            }
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "MRM_" + key, wrapMode = TextureWrapMode.Clamp };
        tex.SetPixels32(px);
        tex.Apply(false, false);   // stays readable: Bake blits it
        tex.hideFlags = HideFlags.HideAndDontSave;
        var sprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        sprite.name = "MRM_" + key;
        sprite.hideFlags = HideFlags.HideAndDontSave;
        Cache[key] = sprite;
        return sprite;
    }

    /// <summary>Greyscale copy of a button picture (types with nothing to show right now, FilterRow): same aspect, up to
    /// 256 px, luminance only.</summary>
    public static Sprite? Grey(string id, Sprite? source)
    {
        string key = id + "_grey";
        if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
        if (source == null) return null;
        var rect = source.rect;
        float k = Mathf.Min(1f, 256f / Mathf.Max(rect.width, rect.height));
        int w = Mathf.Max(1, Mathf.RoundToInt(rect.width * k)), h = Mathf.Max(1, Mathf.RoundToInt(rect.height * k));
        var px = Read(source, w, h, null);
        if (px == null) return null;
        for (int i = 0; i < px.Length; i++)
        {
            var c = px[i];
            byte g = (byte)Mathf.Clamp(Mathf.RoundToInt((0.3f * c.r + 0.59f * c.g + 0.11f * c.b) * 0.85f), 0, 255);
            px[i] = new Color32(g, g, g, c.a);
        }
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "MRM_" + key, wrapMode = TextureWrapMode.Clamp };
        tex.SetPixels32(px);
        tex.Apply(false, true);
        tex.hideFlags = HideFlags.HideAndDontSave;
        var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        sprite.name = "MRM_" + key;
        sprite.hideFlags = HideFlags.HideAndDontSave;
        Cache[key] = sprite;
        return sprite;
    }

    private static Sprite? Bake(Sprite icon, string name, Color32? tint, bool outlined, Sprite? badge = null, Color32? badgeTint = null,
        Sprite? caveBadge = null)
    {
        // 1. The visible part of the item picture.
        var full = Read(icon, 256, 256, null);
        if (full == null) return null;
        var bbox = AlphaBounds(full, 256, 256);
        if (bbox.width <= 0 || bbox.height <= 0) return null;

        // 2. That part, fitted into IconPx, read again from the source for full quality. Map markers are then scaled to the
        //    same visible area (opaque pixels), so a compact boar skull and a thin ore chunk look equally big; capped at
        //    MaxIconPx so long items keep room for outline and shadow.
        float scale = IconPx / Mathf.Max(bbox.width, bbox.height);
        if (outlined)
        {
            int opaque = 0;
            for (int y = (int)bbox.y; y < (int)(bbox.y + bbox.height); y++)
                for (int x = (int)bbox.x; x < (int)(bbox.x + bbox.width); x++)
                    if (full[y * 256 + x].a > 127) opaque++;
            float area = opaque * scale * scale;
            if (area > 0f) scale *= Mathf.Sqrt(TargetArea * IconPx * IconPx / area);
            scale = Mathf.Min(scale, MaxIconPx / Mathf.Max(bbox.width, bbox.height));
        }
        int w = Mathf.Max(1, Mathf.RoundToInt(bbox.width * scale)), h = Mathf.Max(1, Mathf.RoundToInt(bbox.height * scale));
        var crop = new Rect(bbox.x / 256f, bbox.y / 256f, bbox.width / 256f, bbox.height / 256f);
        var pic = Read(icon, w, h, crop);
        if (pic == null) return null;
        if (tint is Color32 t)
            for (int i = 0; i < pic.Length; i++)
                pic[i] = new Color32((byte)(pic[i].r * t.r / 255), (byte)(pic[i].g * t.g / 255), (byte)(pic[i].b * t.b / 255), pic[i].a);

        // 3. Compose: shadow, outline, picture.
        int ox = (Size - w) / 2, oy = (Size - h) / 2;
        var alpha = new float[Size * Size];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                alpha[(y + oy) * Size + x + ox] = pic[y * w + x].a / 255f;

        var outline = outlined ? Dilate(alpha, OutlineRadius) : new float[alpha.Length];
        var shadow = outlined ? BoxBlur(BoxBlur(Dilate(alpha, ShadowRadius), ShadowBlur / 2), ShadowBlur / 2) : new float[alpha.Length];

        var outPx = new Color32[Size * Size];
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                int i = y * Size + x;
                // Shadow (black, offset), then outline, then the picture, each "over" the previous.
                int sx = x - ShadowDx, sy = y - ShadowDy;
                float sa = sx >= 0 && sx < Size && sy >= 0 && sy < Size ? shadow[sy * Size + sx] * ShadowOpacity : 0f;
                Color c = new Color(0f, 0f, 0f, sa);
                c = Over(new Color(OutlineColor.r / 255f, OutlineColor.g / 255f, OutlineColor.b / 255f, outline[i]), c);
                int px = x - ox, py = y - oy;
                if (px >= 0 && px < w && py >= 0 && py < h) c = Over(pic[py * w + px], c);
                outPx[i] = c;
            }
        if (badge != null) DrawBadge(outPx, badge, badgeTint, right: true);
        if (caveBadge != null) DrawBadge(outPx, caveBadge, null, right: false);

        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "MRM_" + name, wrapMode = TextureWrapMode.Clamp };
        tex.SetPixels32(outPx);
        tex.Apply(false, true);   // no longer readable: frees the CPU copy
        tex.hideFlags = HideFlags.HideAndDontSave;
        var sprite = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
        sprite.name = "MRM_" + name;
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    /// <summary>Dark disc with a light rim in the top-right / top-left corner, the glyph on top (texture rows go bottom-up).</summary>
    private static void DrawBadge(Color32[] px, Sprite glyph, Color32? tint, bool right) =>
        DrawDisc(px, glyph, tint, right ? Size - BadgePx / 2f - 2f : BadgePx / 2f + 2f, Size - BadgePx / 2f - 2f, BadgePx / 2f,
            BadgeGlyphPx, 3f);

    /// <summary>
    /// The marker badge on its own (dark disc, light rim, glyph): the "?" toggle next to the Markers title, so it reads as
    /// the same "?" the possible markers carry.
    /// </summary>
    public static Sprite? BadgeIcon(Badge badge)
    {
        string key = "badgeIcon_" + badge;
        if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
        if (!BadgeGlyph(badge, out var glyph, out var tint) || glyph == null) return null;
        var px = new Color32[Size * Size];
        DrawDisc(px, glyph, tint, Size / 2f, Size / 2f, Size / 2f - 4f, Size * 0.62f, 7f);
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "MRM_" + key, wrapMode = TextureWrapMode.Clamp };
        tex.SetPixels32(px);
        tex.Apply(false, true);
        tex.hideFlags = HideFlags.HideAndDontSave;
        var sprite = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
        sprite.name = "MRM_" + key;
        sprite.hideFlags = HideFlags.HideAndDontSave;
        Cache[key] = sprite;
        return sprite;
    }

    private static void DrawDisc(Color32[] px, Sprite glyph, Color32? tint, float cx, float cy, float r, float glyphPx, float rim)
    {
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                if (d > r + 1f) continue;
                float a = Mathf.Clamp01(r + 0.5f - d);                     // anti-aliased edge
                var fill = d > r - rim ? new Color(0.92f, 0.88f, 0.78f, a) : new Color(0.08f, 0.07f, 0.06f, a * 0.95f);
                int i = y * Size + x;
                px[i] = Over(fill, px[i]);
            }
        var g = Read(glyph, 256, 256, null);
        if (g == null) return;
        var bbox = AlphaBounds(g, 256, 256);
        if (bbox.width <= 0) return;
        float s = glyphPx / Mathf.Max(bbox.width, bbox.height);
        int w = Mathf.Max(1, Mathf.RoundToInt(bbox.width * s)), h = Mathf.Max(1, Mathf.RoundToInt(bbox.height * s));
        var pic = Read(glyph, w, h, new Rect(bbox.x / 256f, bbox.y / 256f, bbox.width / 256f, bbox.height / 256f));
        if (pic == null) return;
        if (tint is Color32 t)
            for (int i = 0; i < pic.Length; i++)
                pic[i] = new Color32((byte)(pic[i].r * t.r / 255), (byte)(pic[i].g * t.g / 255), (byte)(pic[i].b * t.b / 255), pic[i].a);
        int ox = Mathf.RoundToInt(cx - w / 2f), oy = Mathf.RoundToInt(cy - h / 2f);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int tx = x + ox, ty = y + oy;
                if (tx < 0 || tx >= Size || ty < 0 || ty >= Size) continue;
                int i = ty * Size + tx;
                px[i] = Over(pic[y * w + x], px[i]);
            }
    }

    private static Color Over(Color top, Color under)
    {
        float a = top.a + under.a * (1f - top.a);
        if (a <= 0f) return new Color(0f, 0f, 0f, 0f);
        float r = (top.r * top.a + under.r * under.a * (1f - top.a)) / a;
        float g = (top.g * top.a + under.g * under.a * (1f - top.a)) / a;
        float b = (top.b * top.a + under.b * under.a * (1f - top.a)) / a;
        return new Color(r, g, b, a);
    }

    /// <summary>Pixels of the sprite (or a 0..1 sub-rect of it) scaled to w x h, bottom row first.</summary>
    private static Color32[]? Read(Sprite s, int w, int h, Rect? sub)
    {
        var tex = s.texture;
        if (tex == null) return null;
        var r = s.packed ? s.textureRect : s.rect;
        if (sub is Rect u)
            r = new Rect(r.x + u.x * r.width, r.y + u.y * r.height, u.width * r.width, u.height * r.height);
        var scale = new Vector2(r.width / tex.width, r.height / tex.height);
        var offset = new Vector2(r.x / tex.width, r.y / tex.height);

        // Halve step by step (bilinear Blit) so a 512 px picture shrinks to ~80 px without aliasing.
        int cw = Mathf.Max(w, Mathf.RoundToInt(r.width)), ch = Mathf.Max(h, Mathf.RoundToInt(r.height));
        var rt = RenderTexture.GetTemporary(cw, ch, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(tex, rt, scale, offset);
        while (cw / 2 >= w && ch / 2 >= h)
        {
            cw /= 2; ch /= 2;
            var next = RenderTexture.GetTemporary(cw, ch, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(rt, next);
            RenderTexture.ReleaseTemporary(rt);
            rt = next;
        }
        if (cw != w || ch != h)
        {
            var next = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(rt, next);
            RenderTexture.ReleaseTemporary(rt);
            rt = next;
        }

        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
        copy.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        copy.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        var px = (Color32[])copy.GetPixels32();
        UnityEngine.Object.Destroy(copy);
        return px;
    }

    private static Rect AlphaBounds(Color32[] px, int w, int h)
    {
        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (px[y * w + x].a > 8)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
        return maxX < 0 ? new Rect(0, 0, 0, 0) : new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    /// <summary>Max over a disc of the given radius.</summary>
    private static float[] Dilate(float[] a, int radius)
    {
        var o = new float[a.Length];
        int r2 = radius * radius;
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float m = 0f;
                for (int dy = -radius; dy <= radius && m < 1f; dy++)
                {
                    int yy = y + dy;
                    if (yy < 0 || yy >= Size) continue;
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (dx * dx + dy * dy > r2) continue;
                        int xx = x + dx;
                        if (xx < 0 || xx >= Size) continue;
                        float v = a[yy * Size + xx];
                        if (v > m) m = v;
                    }
                }
                o[y * Size + x] = m;
            }
        return o;
    }

    /// <summary>Separable box blur; two passes approximate a gaussian.</summary>
    private static float[] BoxBlur(float[] a, int radius)
    {
        if (radius <= 0) return a;
        var tmp = new float[a.Length];
        var o = new float[a.Length];
        float n = 2 * radius + 1;
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float s = 0f;
                for (int d = -radius; d <= radius; d++) { int xx = x + d; if (xx >= 0 && xx < Size) s += a[y * Size + xx]; }
                tmp[y * Size + x] = s / n;
            }
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float s = 0f;
                for (int d = -radius; d <= radius; d++) { int yy = y + d; if (yy >= 0 && yy < Size) s += tmp[yy * Size + x]; }
                o[y * Size + x] = s / n;
            }
        return o;
    }
}
