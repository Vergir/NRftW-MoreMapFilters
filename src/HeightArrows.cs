using Il2CppMoon.Forsaken;
using UnityEngine;
using UnityEngine.UI;

namespace MapResourceMarkers;

/// <summary>
/// The game's "above / below you" arrow on map icons (MapIcon.HeightIndicatorIcon, e.g. the up arrow next to a fire pit),
/// for our markers. Everything is copied from a live game icon on map open (MapScreen.m_allMapIcons only exist while the map
/// is open): the up / down arrow sprites, the rule (shown when the hero is within m_maxDistanceFromPlayerToShowIn,
/// height-nullified, and the height difference is at least m_heightDiffDisplayThreshold; opacity runs from
/// m_iconOpacityRange.x to .y as the difference grows to m_heightDiffForMaxOpacity) and where the arrow sits relative to the
/// icon. The game draws "down" with a rotated / flipped copy of the arrow, so each arrow's local rotation and scale are copied
/// too (without them the down arrow pointed sideways). The game's rule is used as is (bounty icon: within 50 m, from 5 m
/// height difference, full opacity at 30 m). Until the map has been open once there are no arrows. The arrow child is named "...NoMinimap" so the Minimap mod
/// does not mirror it (it is only updated when markers refresh).
/// </summary>
internal static class HeightArrows
{
    private static Sprite? _up, _down;
    private static Quaternion _upRot = Quaternion.identity, _downRot = Quaternion.identity;
    private static Vector3 _upScale = Vector3.one, _downScale = Vector3.one;
    private static float _maxDistance, _threshold, _fullOpacityDiff;
    private static Vector2 _opacity = new(0.3f, 1f);
    /// <summary>Arrow centre and size relative to the icon's visible picture (1 = picture width).</summary>
    private static Vector2 _offset = new(0.6f, 0f), _size = new(0.4f, 0.4f);
    private static bool _ready;
    private const string ChildName = MapOverlay.Prefix + "heightNoMinimap";

    public static void ReadFromGame(MapScreen screen)
    {
        if (_ready) return;
        try
        {
            var icons = screen.m_allMapIcons;
            if (icons == null) return;
            for (int i = 0; i < icons.Count; i++)
            {
                var icon = icons[i];
                var hi = icon?.m_heightIndicator;
                if (hi == null || !hi.m_isSupported || hi.m_upArrowIcon == null || hi.m_downArrowIcon == null) continue;
                _up = hi.m_upArrowIcon.sprite;
                _down = hi.m_downArrowIcon.sprite;
                _maxDistance = hi.m_maxDistanceFromPlayerToShowIn;
                _threshold = hi.m_heightDiffDisplayThreshold;
                _fullOpacityDiff = hi.m_heightDiffForMaxOpacity;
                _upRot = hi.m_upArrowIcon.rectTransform.localRotation;
                _downRot = hi.m_downArrowIcon.rectTransform.localRotation;
                _upScale = hi.m_upArrowIcon.rectTransform.localScale;
                _downScale = hi.m_downArrowIcon.rectTransform.localScale;
                _opacity = hi.m_iconOpacityRange;

                // Arrow placement relative to the icon's picture (m_graphicContainer, else the icon root).
                var pic = icon!.m_graphicContainer != null ? icon.m_graphicContainer : icon.GetComponent<RectTransform>();
                var arrow = hi.m_upArrowIcon.rectTransform;
                float picW = pic.rect.width * pic.lossyScale.x;
                if (picW > 0f)
                {
                    Vector3 d = arrow.position - pic.TransformPoint(pic.rect.center);
                    _offset = new Vector2(d.x, d.y) / picW;
                    _size = new Vector2(arrow.rect.width * arrow.lossyScale.x, arrow.rect.height * arrow.lossyScale.y) / picW;
                }
                _ready = _up != null && _down != null;
                MapResourceMarkersMod.Debug($"Height arrows from '{icon.name}': within {_maxDistance:0} m, from {_threshold:0.#} m height difference " +
                                              $"(full at {_fullOpacityDiff:0.#}), offset {_offset}, size {_size}; up rot {_upRot.eulerAngles} scale {_upScale}, down rot {_downRot.eulerAngles} scale {_downScale}");
                return;
            }
        }
        catch (System.Exception e)
        {
            MapResourceMarkersMod.Log.Warning("Reading the game's height indicator: " + e.Message);
        }
    }

    /// <summary>Show / hide / orient the arrow of one visible marker for the hero's position.</summary>
    public static void Apply(GameObject marker, Vector3 point, Vector3 hero)
    {
        var existing = marker.transform.Find(ChildName);
        bool show = false;
        float dy = point.y - hero.y;
        if (_ready)
        {
            float dx = point.x - hero.x, dz = point.z - hero.z;
            show = dx * dx + dz * dz <= _maxDistance * _maxDistance && Mathf.Abs(dy) >= _threshold;
        }
        if (!show)
        {
            if (existing != null && existing.gameObject.activeSelf) existing.gameObject.SetActive(false);
            return;
        }

        Image img;
        if (existing == null)
        {
            var go = new GameObject(ChildName);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(marker.transform, false, false);
            // The marker sprite's picture is MarkerArt.IconFraction of the rect, centred.
            float k = MarkerArt.IconFraction;
            var c = new Vector2(0.5f, 0.5f) + _offset * k;
            rt.anchorMin = c - _size * k * 0.5f;
            rt.anchorMax = c + _size * k * 0.5f;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            img = go.AddComponent<Image>();
            img.preserveAspect = true;
            img.raycastTarget = false;
        }
        else
        {
            img = existing.GetComponent<Image>();
            if (!existing.gameObject.activeSelf) existing.gameObject.SetActive(true);
        }
        img.sprite = dy > 0 ? _up : _down;
        img.rectTransform.localRotation = dy > 0 ? _upRot : _downRot;
        img.rectTransform.localScale = dy > 0 ? _upScale : _downScale;
        float t = _fullOpacityDiff > _threshold ? Mathf.InverseLerp(_threshold, _fullOpacityDiff, Mathf.Abs(dy)) : 1f;
        img.color = new Color(1f, 1f, 1f, Mathf.Lerp(_opacity.x, _opacity.y, t));
    }
}
