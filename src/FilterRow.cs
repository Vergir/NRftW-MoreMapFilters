using System;
using System.Collections.Generic;
using Il2CppMoon.Forsaken;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MapResourceMarkers;

/// <summary>
/// The selector: a "Markers" panel above the game's Filters menu (mapScreen/mainPane/filterMarkerMenu). The game's row is a
/// panel ("bg ", trailing space in the prefab, 168 units high) with a title label at the top and markerItems
/// (HorizontalLayoutGroup, no spacing, 86 high, 40 above the bottom), one 56-unit icon button per MapIconType (Image +
/// Button + UISoundsHandler + MouseHoverSelect + EventTrigger + MapMarker, children selection and a hidden label). We
/// clone the panel, keep one button as a template and clone it per marker type with our own click and hover handlers. The game's menu never sees our buttons (its navigation runs over
/// MarkerFilters.FilterOptions), so "All" does not touch them.
///
/// Lines: the groups (MarkerGroup, a divider dot between them) fill the top line of icons in order; a group that does not
/// fit whole starts the next line, so groups are never split (Layout). Extra lines are clones of markerItems inside the
/// same panel, which grows by one markerItems height per line (room for the hover name under the upper line) and keeps
/// sitting directly on the game's panel. The width is the panel's (the canvas width: 1846 units on a 9:8 screen, where
/// all nine groups need ~2200; a 16:9 screen fits them in one line).
///
/// Gamepad / keyboard: two input paths reach the menu. D-pad / arrow keys: MapScreen.ProcessInput calls the open sub-menu's
/// OnUp/Down/Left/RightButtonPressed and OnSubmit; each direction handler reads FilterOptions[m_selectedOptionIndex].Button
/// .navigation.selectOnX and selects it via EventSystem.SetSelectedGameObject, OnSubmit toggles that option. Left stick: the
/// EventSystem's own move events, which follow the same explicit Selectable.navigation links. Selecting a game button runs
/// OnMarkerOptionSelected (hides the previous button's label, shows the new one).
/// So both paths are served by navigation links (LinkNavigation, on every menu open and row change, after a layout pass):
/// left / right within a row, up / down to the nearest button of the neighbouring row, down from the last row to the
/// nearest game button; the game buttons link up to the nearest button of our last row (their original navigation is
/// restored on unload). Our focus follows the EventSystem selection (Select / Deselect triggers on our buttons).
/// Patches/FilterMenuPatches routes the D-pad presses here first and moves the selection along the same links; Submit
/// toggles the focused type.
///
/// Types appear in the row once the story allows them in this world: a live point, or a point in an area variant whose
/// story conditions pass (MapOverlay.IsEligible; it can roll in later). That is remembered per world (Prefs.Unlocked,
/// keyed by the local realm guid, see World()), so story-locked kinds such as Winged Brood join the row as the world
/// progresses and never leave it. While no live variant has them (Harvesters rotate in and out within minutes) they are
/// only dimmed, so the row does not jump. Groups switched off in the settings (Prefs.GroupOn) are left out entirely.
/// </summary>
internal static class FilterRow
{
    private const string Title = "Markers";
    private const float Gap = 4f;
    /// <summary>Nine groups at 56 units a button fit in three lines on any screen the game supports.</summary>
    private const int MaxLines = 3;
    /// <summary>Share of the panel width the buttons may use (a little air at the screen edges).</summary>
    private const float UsableWidth = 0.96f;

    private static MapScreen? _screen;
    private static GameObject? _panel;
    private static RectTransform? _panelRt;
    /// <summary>The icon lines (markerItems and its clones), top first.</summary>
    private static readonly List<RectTransform> Lines = new();
    private static readonly List<RowButton> Buttons = new();
    /// <summary>Divider dots, each in front of the first button of Group.</summary>
    private static readonly List<Divider> Dividers = new();
    /// <summary>Index into Buttons of the gamepad focus, -1 = the game's own row has it.</summary>
    private static int _focus = -1;
    /// <summary>Game panel position / height, our panel's own height and its line's y with one line, the line step.</summary>
    private static Vector2 _gamePanelPos;
    private static float _gamePanelHeight, _panelHeight, _lineY, _lineStep, _buttonWidth, _dividerWidth;
    private static int _linesUsed;

    private sealed class Divider
    {
        public GameObject Go = null!;
        public MarkerGroup Group;
    }

    private sealed class RowButton
    {
        public MarkerType Type = null!;
        public GameObject Go = null!;
        public Button Button = null!;
        public MapMarker Marker = null!;
        public GameObject Selection = null!;
        public TextMeshProUGUI? Label;
        public bool Hovered;
        /// <summary>In the row but nothing to show right now.</summary>
        public bool Empty;
        public CanvasGroup? Fade;
        public int Row;
    }

    private static bool Built => _panel != null && Lines.Count > 0;

    public static void Attach(MapScreen screen)
    {
        Localization.Reset();   // the language may have changed in the settings
        Localization.Label(MarkerTypes.All[0]);   // decides (and logs) the language
        if (_screen != null && _screen.Pointer == screen.Pointer && Built)
        {
            ApplySprites();
            return;
        }
        Destroy();
        _screen = screen;
        try { Build(screen); }
        catch (Exception e) { MapResourceMarkersMod.Log.Error("Building the Markers row: " + e); }
    }

    private static void Build(MapScreen screen)
    {
        var menu = screen.MarkerFilters.FilterMarkerMenu.transform;
        for (int i = menu.childCount - 1; i >= 0; i--)
            if (menu.GetChild(i).name.StartsWith(MapOverlay.Prefix)) Object.Destroy(menu.GetChild(i).gameObject);

        var panel = menu.GetChild(0).GetComponent<RectTransform>();
        var row = Object.Instantiate(panel.gameObject);
        row.name = MapOverlay.Prefix + "MarkersPanel";
        var rowRt = row.GetComponent<RectTransform>();
        rowRt.SetParent(menu, false, false);
        rowRt.anchorMin = panel.anchorMin;
        rowRt.anchorMax = panel.anchorMax;
        rowRt.pivot = panel.pivot;
        rowRt.sizeDelta = panel.sizeDelta;
        rowRt.localScale = panel.localScale;
        _panel = row;
        _panelRt = rowRt;

        _gamePanelPos = panel.anchoredPosition;
        _gamePanelHeight = panel.rect.height;
        _panelHeight = panel.sizeDelta.y;

        var titleText = row.transform.Find("label")?.GetComponent<TextMeshProUGUI>();
        if (titleText != null)
        {
            var tr = titleText.rectTransform;
            _titleY0 = tr.anchoredPosition.y;
            // Title centre below the panel top, as a share of the panel height (pivot at the top).
            _titleShare = (-tr.anchoredPosition.y + tr.rect.height * tr.pivot.y) / Mathf.Max(1f, _panelHeight);
            DestroyComponent(titleText.gameObject, "LocalizedText");
            titleText.text = Title;
        }

        var items = row.transform.Find("markerItems")?.GetComponent<RectTransform>();
        if (items == null || items.childCount == 0) throw new InvalidOperationException("markerItems not found in the cloned Filters panel");
        var template = items.GetChild(0).gameObject;
        for (int i = items.childCount - 1; i >= 1; i--) Object.DestroyImmediate(items.GetChild(i).gameObject);
        _lineY = items.anchoredPosition.y;
        _lineStep = items.rect.height > 0f ? items.rect.height : items.sizeDelta.y;
        Lines.Add(items);
        for (int l = 1; l < MaxLines; l++)
        {
            var line = new GameObject(MapOverlay.Prefix + "markerItems" + l).AddComponent<RectTransform>();
            line.SetParent(row.transform, false, false);
            line.anchorMin = items.anchorMin;
            line.anchorMax = items.anchorMax;
            line.pivot = items.pivot;
            line.sizeDelta = items.sizeDelta;
            line.anchoredPosition = items.anchoredPosition;
            var src = items.GetComponent<HorizontalLayoutGroup>();
            var hl = line.gameObject.AddComponent<HorizontalLayoutGroup>();
            if (src != null)
            {
                hl.spacing = src.spacing;
                hl.childAlignment = src.childAlignment;
                hl.childControlWidth = src.childControlWidth;
                hl.childControlHeight = src.childControlHeight;
                hl.childForceExpandWidth = src.childForceExpandWidth;
                hl.childForceExpandHeight = src.childForceExpandHeight;
                hl.padding = src.padding;
            }
            Lines.Add(line);
        }

        var font = titleText;
        _buttonWidth = template!.GetComponent<RectTransform>().sizeDelta.x;
        _dividerWidth = _buttonWidth * 0.5f;
        MarkerGroup? group = null;
        foreach (var type in MarkerTypes.All)
        {
            if (group != null && type.Group != group)
                Dividers.Add(new Divider { Go = AddDivider(Lines[0], template, font), Group = type.Group });
            group = type.Group;
            var go = Object.Instantiate(template);
            go.name = MapOverlay.Prefix + type.Id;
            go.transform.SetParent(Lines[0], false, false);
            Wire(go, type);
        }
        BuildPossibleToggle(template, rowRt, titleText);
        Object.DestroyImmediate(template);
        ApplySprites();
        UpdateButtons();
        UpdateVisibility();
        MapResourceMarkersMod.Debug($"Markers rows added under '{menu.name}'.");
    }

    /// <summary>
    /// The current world (realm): the local realm guid of the session's save handler (MoonQuantumGame.SaveInfo
    /// .SaveDataHandler.m_localRealmId, Guid @0x70); "" when unknown. Not MoonQuantumGame.RealmId (empty offline) nor
    /// SeenItemsRegistry.GetCurrentRealmId (returns the type name "frameworks.session.SessionDefinition" in build 29466).
    /// </summary>
    private static string World()
    {
        try
        {
            var handler = NodeIndex.LocalFrame()?.Game?.TryCast<Il2CppQuantum.MoonQuantumGame>()?.SaveInfo?.SaveDataHandler;
            if (handler == null) return "";
            var bytes = new byte[16];
            System.Runtime.InteropServices.Marshal.Copy(handler.Pointer + 0x70, bytes, 0, 16);
            var guid = new Guid(bytes);
            return guid == Guid.Empty ? "" : guid.ToString("N");
        }
        catch { return ""; }
    }

    /// <summary>A dot between groups: a text element half as wide as a button, ignored by navigation.</summary>
    private static GameObject AddDivider(Transform items, GameObject template, TextMeshProUGUI? font)
    {
        var go = new GameObject(MapOverlay.Prefix + "divider");
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(items, false, false);
        var size = template.GetComponent<RectTransform>().sizeDelta;
        rt.sizeDelta = new Vector2(size.x * 0.5f, size.y);
        var layout = go.AddComponent<LayoutElement>();
        layout.preferredWidth = layout.minWidth = size.x * 0.5f;
        layout.preferredHeight = size.y;
        var text = go.AddComponent<TextMeshProUGUI>();
        if (font != null)
        {
            text.font = font.font;
            text.fontSize = font.fontSize;
        }
        text.text = "•";
        text.alignment = TextAlignmentOptions.Center;
        text.color = new Color(1f, 1f, 1f, 0.45f);
        text.raycastTarget = false;
        return go;
    }

    /// <summary>Types with nothing to show right now: grey picture (MarkerArt.Grey), slightly faded. A fade alone was lost
    /// on top of the game's own "off" colour of unselected buttons.</summary>
    private const float EmptyAlpha = 0.7f;

    /// <summary>Row contents from what MapOverlay can show and the group switches (see class remarks), then the row
    /// layout; relinks the navigation if the menu is open.</summary>
    public static void UpdateVisibility()
    {
        if (!Built) return;
        string world = World();
        bool known = MapOverlay.AvailabilityKnown;
        var added = new List<string>();
        var empty = new List<string>();
        for (int b = 0; b < Buttons.Count; b++)
        {
            var rb = Buttons[b];
            bool available = known && MapOverlay.IsAvailable(rb.Type);
            bool eligible = available || (known && MapOverlay.IsEligible(rb.Type));
            if (eligible && Prefs.Unlock(world, rb.Type)) added.Add(rb.Type.Label);
            bool on = Prefs.GroupOn(rb.Type.Group) && (eligible || Prefs.IsUnlocked(world, rb.Type));
            bool isEmpty = on && known && !available;
            if (isEmpty) empty.Add(rb.Type.Label);
            if (rb.Go.activeSelf == on && rb.Empty == isEmpty) continue;
            if (!on && _focus == b) ResetFocus();
            rb.Empty = isEmpty;
            if (rb.Fade != null) rb.Fade.alpha = isEmpty ? EmptyAlpha : 1f;
            ApplySprite(rb);
            rb.Hovered = false;
            UpdateHighlight(rb);
            rb.Go.SetActive(on);
        }
        Layout();
        if (added.Count > 0) Prefs.SaveUnlocked();
        if (added.Count > 0) MapResourceMarkersMod.Log.Msg($"New in the Markers row for this world: {string.Join(", ", added)}");
        if (known) MapResourceMarkersMod.Debug(empty.Count == 0 ? "Every type in the Markers row has something to show."
                                                                 : $"Dimmed in the Markers row (nothing to show right now): {string.Join(", ", empty)}");
        var menu = _screen?.MarkerFilters;
        if (_menuWasOpen && menu != null)
        {
            try { LinkNavigation(menu); }
            catch (Exception e) { MapResourceMarkersMod.Log.Warning("Linking filter navigation: " + e.Message); }
        }
    }

    /// <summary>Spread the groups over the rows (see class remarks) and stack the rows above the game's panel.</summary>
    private static void Layout()
    {
        float capacity = _panelRt!.rect.width * UsableWidth;
        if (capacity <= _buttonWidth) capacity = float.MaxValue;   // not laid out yet: one row until the next update
        int row = 0;
        float used = 0f;
        // Units = what a dot separates: the categories, with Fish joining the one category of the two that is on.
        var units = new SortedDictionary<int, List<MarkerGroup>>();
        foreach (var group in MarkerGroups.All)
        {
            int unit = UnitOf(group);
            if (!units.TryGetValue(unit, out var list)) units[unit] = list = new List<MarkerGroup>();
            list.Add(group);
        }
        foreach (var segments in units.Values)
        {
            int visible = 0;
            foreach (var rb in Buttons) if (segments.Contains(rb.Type.Group) && rb.Go.activeSelf) visible++;
            float width = visible * _buttonWidth;
            if (visible > 0 && used > 0f && used + _dividerWidth + width > capacity && row < MaxLines - 1)
            {
                row++;
                used = 0f;
            }
            bool dotted = false;
            foreach (var group in segments)
            {
                var divider = Dividers.Find(d => d.Group == group);
                int segmentVisible = 0;
                foreach (var rb in Buttons) if (rb.Type.Group == group && rb.Go.activeSelf) segmentVisible++;
                if (divider != null)
                {
                    // Only in front of the unit's first visible segment, and not at the start of a line.
                    divider.Go.SetActive(segmentVisible > 0 && !dotted && used > 0f);
                    divider.Go.transform.SetParent(Lines[row], false, false);
                    divider.Go.transform.SetAsLastSibling();
                }
                if (segmentVisible > 0) dotted = true;
                foreach (var rb in Buttons)
                {
                    if (rb.Type.Group != group) continue;
                    rb.Row = row;
                    rb.Go.transform.SetParent(Lines[row], false, false);
                    rb.Go.transform.SetAsLastSibling();
                }
            }
            if (visible > 0) used += (used > 0f ? _dividerWidth : 0f) + width;
        }

        // Lines in use: those up to the last visible button (switched-off groups can leave trailing lines empty).
        _linesUsed = 1;
        foreach (var rb in Buttons) if (rb.Go.activeSelf) _linesUsed = System.Math.Max(_linesUsed, rb.Row + 1);
        // The panel grows upwards per extra line. Its background (uiMenuBack1Texture) is a sliced sprite whose top + bottom
        // borders are taller than the panel, so Unity scales them with the panel height and the top border line moves
        // down: the title moves with it (same share of the height). The growth per extra line is tuned in game (9:8,
        // 2 lines) so the title keeps the one-line gap to the top line of icons: 1 / (1 - share) of a line was ~40 units
        // too much, 0.85 too little; 1.09 interpolated from the two screenshots.
        float height = _panelHeight + (_linesUsed - 1) * _lineStep * LineGrowth;
        if (_title != null)
            _title.rectTransform.anchoredPosition = new Vector2(_title.rectTransform.anchoredPosition.x, _titleY0 * height / _panelHeight);
        PlacePossibleToggle();
        _panelRt!.sizeDelta = new Vector2(_panelRt.sizeDelta.x, height);
        _panelRt.anchoredPosition = _gamePanelPos + new Vector2(0f, _gamePanelHeight / 2f + Gap + height / 2f);
        for (int l = 0; l < Lines.Count; l++)
        {
            bool on = l < _linesUsed;
            Lines[l].gameObject.SetActive(on);
            if (on) Lines[l].anchoredPosition = new Vector2(Lines[l].anchoredPosition.x, _lineY + (_linesUsed - 1 - l) * _lineStep);
        }
    }

    /// <summary>Dot-separated unit of a row segment: Fish stands alone between Harvesting and Cooking when both are on,
    /// otherwise it joins the one that is on.</summary>
    private static int UnitOf(MarkerGroup g)
    {
        if (g != MarkerGroup.Fish) return (int)g * 2;
        bool harvesting = Prefs.CategoryOn(MarkerCategory.Harvesting), cooking = Prefs.CategoryOn(MarkerCategory.Cooking);
        return harvesting && cooking ? (int)g * 2 : harvesting ? (int)MarkerGroup.Harvesting * 2 : (int)MarkerGroup.Cooking * 2;
    }

    private static void Wire(GameObject go, MarkerType type)
    {
        // The game's hover-select would move its own menu selection onto a button it does not know.
        DestroyComponent(go, "MouseHoverSelect");

        var marker = go.GetComponent<MapMarker>();
        // Until the item icon resolves the button keeps the template's sprite ("All"); ApplySprites retries per map open.
        if (marker.Icon != null) marker.Icon.preserveAspect = true;
        var selection = go.transform.Find("selection")?.gameObject ?? new GameObject();
        selection.SetActive(false);
        // Like the game's buttons: the name shows under the hovered button.
        var label = go.transform.Find("label")?.GetComponent<TextMeshProUGUI>();
        if (label != null)
        {
            DestroyComponent(label.gameObject, "LocalizedText");
            label.gameObject.SetActive(false);
        }

        var button = go.GetComponent<Button>();
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener((Action)(() => Toggle(type)));

        var trigger = go.GetComponent<EventTrigger>();
        trigger.triggers.Clear();
        var rb = new RowButton { Type = type, Go = go, Button = button, Marker = marker, Selection = selection, Label = label };
        rb.Fade = go.GetComponent<CanvasGroup>() ?? go.AddComponent<CanvasGroup>();
        int index = Buttons.Count;
        AddTrigger(trigger, EventTriggerType.PointerEnter, _ => { rb.Hovered = true; UpdateHighlight(rb); });
        AddTrigger(trigger, EventTriggerType.PointerExit, _ => { rb.Hovered = false; UpdateHighlight(rb); });
        AddTrigger(trigger, EventTriggerType.Select, _ => OnSelected(index));
        AddTrigger(trigger, EventTriggerType.Deselect, _ => OnDeselected(index));

        Buttons.Add(rb);
    }

    private static void AddTrigger(EventTrigger trigger, EventTriggerType id, Action<BaseEventData> action)
    {
        var entry = new EventTrigger.Entry { eventID = id };
        entry.callback.AddListener(action);
        trigger.triggers.Add(entry);
    }

    private static void Toggle(MarkerType type)
    {
        Prefs.SetSelected(type, !Prefs.IsSelected(type));
        UpdateButtons();
        MapOverlay.OnSelectionChanged(type);
    }

    /// <summary>Highlight + name under the button while it is hovered or has the gamepad focus.</summary>
    private static void UpdateHighlight(RowButton rb)
    {
        bool on = rb.Hovered || (_focus >= 0 && _focus < Buttons.Count && Buttons[_focus] == rb);
        rb.Selection.SetActive(on);
        if (rb.Label == null) return;
        string name = Localization.Label(rb.Type);
        rb.Label.text = rb.Empty ? name + " (none right now)" : name;
        rb.Label.gameObject.SetActive(on);
    }

    // ------------------------------------------------------------------ gamepad / keyboard (see class remarks)

    public static bool HasFocus => (_focus >= 0 || _possibleFocus) && Built;

    // ------------------------------------------------------------------ "?" toggle next to the title

    private static GameObject? _possible;
    private static Button? _possibleButton;
    private static MapMarker? _possibleMarker;
    private static GameObject? _possibleSelection;
    private static TextMeshProUGUI? _possibleLabel;
    private static bool _possibleHovered, _possibleFocus;

    /// <summary>
    /// "Show '?' markers" switch (Prefs.ShowPossible), a button like the filters (the template) with the game's "?" map
    /// glyph, right of the "Markers" title: visibly not one of the filters, and one Up from the top line on a gamepad.
    /// Bright = "?" markers shown, dimmed = hidden.
    /// </summary>
    private static void BuildPossibleToggle(GameObject template, RectTransform panel, TextMeshProUGUI? title)
    {
        var go = Object.Instantiate(template);
        go.name = MapOverlay.Prefix + "possibleToggle";
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(panel, false, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        // About the title's cap height, so it sits in the title line like a badge rather than a filter icon.
        float size = title != null ? title.fontSize * 1.15f : 40f;
        rt.sizeDelta = new Vector2(size, size);
        _possibleRt = rt;
        _possibleSize = size;
        _title = title;
        PlacePossibleToggle();

        DestroyComponent(go, "MouseHoverSelect");
        _possible = go;
        _possibleMarker = go.GetComponent<MapMarker>();
        if (_possibleMarker?.Icon != null)
        {
            _possibleMarker.Icon.preserveAspect = true;
            // The badge the "?" markers carry (dark disc, light rim); falls back to the bare glyph.
            var glyph = MarkerArt.BadgeIcon(Badge.Question) ?? GameSprites.Find("QuestionMapIcon");
            if (glyph != null) _possibleMarker.Icon.sprite = glyph;
            // The filter buttons' icon is the button's own Image; only a child image needs stretching.
            if (_possibleMarker.Icon.transform.Pointer != go.transform.Pointer) Fill(_possibleMarker.Icon.rectTransform);
        }
        _possibleSelection = go.transform.Find("selection")?.gameObject;
        if (_possibleSelection != null) Fill(_possibleSelection.GetComponent<RectTransform>(), -size * 0.25f);
        _possibleSelection?.SetActive(false);
        _possibleLabel = go.transform.Find("label")?.GetComponent<TextMeshProUGUI>();
        if (_possibleLabel != null)
        {
            DestroyComponent(_possibleLabel.gameObject, "LocalizedText");
            _possibleLabel.gameObject.SetActive(false);
        }
        _possibleButton = go.GetComponent<Button>();
        _possibleButton.onClick.RemoveAllListeners();
        _possibleButton.onClick.AddListener((Action)TogglePossible);
        var trigger = go.GetComponent<EventTrigger>();
        trigger.triggers.Clear();
        AddTrigger(trigger, EventTriggerType.PointerEnter, _ => { _possibleHovered = true; UpdatePossibleToggle(); });
        AddTrigger(trigger, EventTriggerType.PointerExit, _ => { _possibleHovered = false; UpdatePossibleToggle(); });
        AddTrigger(trigger, EventTriggerType.Select, _ =>
        {
            ResetFocus();
            _possibleFocus = true;
            UpdatePossibleToggle();
            var menu = _screen?.MarkerFilters;
            if (menu != null) SetGameLabel(menu, false);
        });
        AddTrigger(trigger, EventTriggerType.Deselect, _ => { _possibleFocus = false; UpdatePossibleToggle(); });
        UpdatePossibleToggle();
    }

    /// <summary>Stretch a child over its parent (the template's icon / highlight have fixed filter-button sizes).</summary>
    private static void Fill(RectTransform? rt, float inset = 0f)
    {
        if (rt == null) return;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
    }

    private static float _titleY0, _titleShare;
    private const float LineGrowth = 1.09f;
    private static RectTransform? _possibleRt;
    private static float _possibleSize;
    private static TextMeshProUGUI? _title;

    /// <summary>
    /// Right after the rendered title text, centred on its letters (TMP textBounds, in the title's local space; the title
    /// rect is anchored at the panel's top centre like the toggle, so its pivot is the toggle's origin too). Re-run when the
    /// panel is laid out (the bounds need a generated mesh).
    /// </summary>
    private static void PlacePossibleToggle()
    {
        if (_possibleRt == null) return;
        float x = 90f, y = -40f;
        if (_title != null)
        {
            _title.ForceMeshUpdate();
            var b = _title.textBounds;
            var t = _title.rectTransform;
            if (b.size.x > 0f)
            {
                x = t.anchoredPosition.x + b.max.x + _possibleSize * 0.85f;
                y = t.anchoredPosition.y + b.center.y;
            }
        }
        _possibleRt.anchoredPosition = new Vector2(x, y);
    }

    private static void TogglePossible()
    {
        Prefs.ShowPossible.Value = !Prefs.ShowPossible.Value;
        MelonLoader.MelonPreferences.Save();
        UpdatePossibleToggle();
        MapOverlay.Refresh();
    }

    private static void UpdatePossibleToggle()
    {
        if (_possible == null) return;
        bool on = Prefs.ShowPossible.Value;
        if (_possibleMarker != null)
        {
            if (on) _possibleMarker.SetEnabledColor();
            else _possibleMarker.SetDisabledColor();
        }
        bool highlight = _possibleHovered || _possibleFocus;
        _possibleSelection?.SetActive(highlight);
        if (_possibleLabel == null) return;
        _possibleLabel.text = on ? "Possible spots (?): shown" : "Possible spots (?): hidden";
        _possibleLabel.gameObject.SetActive(highlight);
    }

    /// <summary>EventSystem selected one of our buttons (stick, D-pad via Select, or code).</summary>
    private static void OnSelected(int index)
    {
        int old = _focus;
        _focus = index;
        if (old >= 0 && old < Buttons.Count && old != index) UpdateHighlight(Buttons[old]);
        UpdateHighlight(Buttons[index]);
        var menu = _screen?.MarkerFilters;
        if (menu != null) SetGameLabel(menu, false);
    }

    private static void OnDeselected(int index)
    {
        if (_focus != index) return;
        _focus = -1;
        UpdateHighlight(Buttons[index]);
    }

    /// <summary>Up pressed in the Filters menu (D-pad / keys). True = handled (skip the game's handler).</summary>
    public static bool OnUp(MapMarkerFilterMenu menu)
    {
        if (!Built) return false;
        if (_possibleFocus) return true;   // the toggle is the top
        if (HasFocus) return Follow(Buttons[_focus].Button.navigation.selectOnUp);
        // From the game's row: the link LinkNavigation set on its button (our last row), if any.
        var options = menu.FilterOptions;
        int i = menu.m_selectedOptionIndex;
        if (options == null || i < 0 || i >= options.Length || options[i]?.Button == null) return false;
        var target = options[i].Button.navigation.selectOnUp;
        if (target == null || !IsOurs(target)) return false;
        Select(target.gameObject);
        return true;
    }

    public static bool OnDown(MapMarkerFilterMenu menu) =>
        _possibleFocus ? Follow(_possibleButton?.navigation.selectOnDown)
                       : HasFocus && Follow(Buttons[_focus].Button.navigation.selectOnDown);

    public static bool OnLeftRight(int step) =>
        _possibleFocus || (HasFocus && Follow(step < 0 ? Buttons[_focus].Button.navigation.selectOnLeft
                                                         : Buttons[_focus].Button.navigation.selectOnRight));

    /// <summary>Move the selection along a link (selecting a game button runs the game's OnMarkerOptionSelected). Always
    /// handled while our row has the focus, also at its edges.</summary>
    private static bool Follow(Selectable? target)
    {
        if (target != null) Select(target.gameObject);
        return true;
    }

    private static bool IsOurs(Selectable s)
    {
        foreach (var rb in Buttons) if (rb.Button.Pointer == s.Pointer) return true;
        return _possibleButton != null && _possibleButton.Pointer == s.Pointer;
    }

    public static bool OnSubmit()
    {
        if (_possibleFocus) { TogglePossible(); return true; }
        if (!HasFocus) return false;
        Toggle(Buttons[_focus].Type);
        return true;
    }

    /// <summary>Menu opened / closed, or a game button got selected (mouse hover): the game row owns the focus again.</summary>
    public static void ResetFocus()
    {
        if (_possibleFocus)
        {
            _possibleFocus = false;
            UpdatePossibleToggle();
        }
        if (_focus < 0) return;
        int old = _focus;
        _focus = -1;
        if (old < Buttons.Count) UpdateHighlight(Buttons[old]);
    }

    private static void Select(GameObject go) => EventSystem.current?.SetSelectedGameObject(go);

    private static bool _menuWasOpen;

    /// <summary>Every frame: the Filters panel just appeared -> layout + navigation (OpenMarkerFilterMenu is inlined into
    /// MapScreen.ProcessInput, so a hook on it never fires).</summary>
    public static void Tick()
    {
        var menu = _screen?.MarkerFilters;
        var panel = menu?.FilterMarkerMenu;
        bool open = panel != null && panel.activeInHierarchy;
        if (open == _menuWasOpen) return;
        _menuWasOpen = open;
        ResetFocus();
        if (!open || !Built) return;
        Layout();   // the panel width is known for sure now
        try { LinkNavigation(menu!); }
        catch (Exception e) { MapResourceMarkersMod.Log.Warning("Linking filter navigation: " + e.Message); }
    }

    /// <summary>Original navigation of the game's filter buttons (we change their selectOnUp), restored on unload.</summary>
    private static readonly List<(Button Button, Navigation Nav)> GameNav = new();

    /// <summary>Explicit links within and between our rows and the game's row (see class remarks).</summary>
    public static void LinkNavigation(MapMarkerFilterMenu menu)
    {
        if (!Built) return;
        var options = menu.FilterOptions;
        if (options == null) return;
        // Positions are only valid after a layout pass (the menu was inactive until now).
        foreach (var line in Lines)
            if (line.gameObject.activeSelf) LayoutRebuilder.ForceRebuildLayoutImmediate(line);
        var gameButtons = new List<Button>();
        foreach (var o in options)
            if (o?.Button != null) gameButtons.Add(o.Button);
        if (gameButtons.Count > 0) LayoutRebuilder.ForceRebuildLayoutImmediate(gameButtons[0].transform.parent.GetComponent<RectTransform>());

        var rows = new List<List<Button>>();
        for (int r = 0; r < _linesUsed; r++) rows.Add(new List<Button>());
        foreach (var rb in Buttons)
            if (rb.Go.activeSelf && rb.Row < _linesUsed) rows[rb.Row].Add(rb.Button);
        rows.RemoveAll(r => r.Count == 0);
        if (rows.Count == 0)
        {
            RestoreGameNavigation();
            return;
        }
        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            for (int b = 0; b < row.Count; b++)
            {
                var nav = row[b].navigation;
                float x = row[b].transform.position.x;
                nav.mode = Navigation.Mode.Explicit;
                nav.selectOnLeft = b > 0 ? row[b - 1] : null;
                nav.selectOnRight = b < row.Count - 1 ? row[b + 1] : null;
                nav.selectOnUp = r > 0 ? Nearest(rows[r - 1], x) : _possibleButton;
                nav.selectOnDown = r < rows.Count - 1 ? Nearest(rows[r + 1], x) : Nearest(gameButtons, x);
                row[b].navigation = nav;
            }
        }
        if (_possibleButton != null)
        {
            var nav = _possibleButton.navigation;
            nav.mode = Navigation.Mode.Explicit;
            nav.selectOnUp = nav.selectOnLeft = nav.selectOnRight = null;
            nav.selectOnDown = Nearest(rows[0], _possibleButton.transform.position.x);
            _possibleButton.navigation = nav;
        }
        bool saveOriginals = GameNav.Count == 0;
        foreach (var gb in gameButtons)
        {
            var nav = gb.navigation;
            if (saveOriginals) GameNav.Add((gb, nav));
            nav.mode = Navigation.Mode.Explicit;
            nav.selectOnUp = Nearest(rows[rows.Count - 1], gb.transform.position.x);
            gb.navigation = nav;
        }
    }

    private static Button? Nearest(List<Button> buttons, float x)
    {
        Button? best = null;
        float bestDist = float.MaxValue;
        foreach (var b in buttons)
        {
            float d = System.Math.Abs(b.transform.position.x - x);
            if (d < bestDist) { bestDist = d; best = b; }
        }
        return best;
    }

    private static void RestoreGameNavigation()
    {
        foreach (var (button, nav) in GameNav)
            if (button != null) button.navigation = nav;
        GameNav.Clear();
    }

    private static void SetGameLabel(MapMarkerFilterMenu menu, bool on)
    {
        var labels = menu.m_filterLabels;
        int i = menu.m_selectedOptionIndex;
        if (labels != null && i >= 0 && i < labels.Length && labels[i] != null) labels[i].gameObject.SetActive(on);
    }

    /// <summary>Button pictures (item icons load on demand: retried on every map open), grey while a type is empty.</summary>
    private static void ApplySprites()
    {
        foreach (var b in Buttons) ApplySprite(b);
    }

    private static void ApplySprite(RowButton b)
    {
        if (b.Marker == null || b.Marker.Icon == null) return;
        var sprite = b.Type.ButtonSprite();
        if (b.Empty) sprite = MarkerArt.Grey(b.Type.Id, sprite);
        if (sprite == null) return;
        var icon = b.Marker.Icon;
        if (icon.sprite == null || icon.sprite.Pointer != sprite.Pointer) icon.sprite = sprite;
    }

    private static void UpdateButtons()
    {
        foreach (var b in Buttons)
        {
            if (b.Marker == null) continue;
            if (Prefs.IsSelected(b.Type)) b.Marker.SetEnabledColor();
            else b.Marker.SetDisabledColor();
        }
    }

    private static void DestroyComponent(GameObject go, string typeName)
    {
        foreach (var c in go.GetComponents<Component>())
            if (c != null && c.GetIl2CppType().Name == typeName) Object.DestroyImmediate(c);
    }

    public static void Destroy()
    {
        RestoreGameNavigation();
        if (_panel != null) Object.Destroy(_panel);
        _panel = null;
        _possible = null;
        _possibleButton = null;
        _possibleMarker = null;
        _possibleSelection = null;
        _possibleLabel = null;
        _possibleHovered = _possibleFocus = false;
        _panelRt = null;
        Lines.Clear();
        _screen = null;
        _focus = -1;
        _menuWasOpen = false;
        _linesUsed = 0;
        Buttons.Clear();
        Dividers.Clear();
    }
}
