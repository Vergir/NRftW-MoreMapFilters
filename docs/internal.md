# Map Markers: internals

Verified in game for 1.0.0 (2026-10-19). Build 29466 (Steam). RVAs from `reference/dump.cs` in the workspace root; the
`analysis/*.md` files named below are in the workspace root too (research notes with tools in `tools/`).

## Source files

| File | What |
|---|---|
| `MapMarkersMod.cs` | Entry point: prefs, Harmony (`HarmonyDontPatchAll` + own `PatchAll`), settings rows, per-frame `MapOverlay.Sync` / `LiveUpdates.Tick` / `MapOverlay.LateUpdate` / `FilterRow.Tick`, `MapScreen.Show` and teleport hooks, unload. `Debug()` logs only with `Debug = true`. |
| `MarkerTypes.cs` | `MarkerGroup` (row segments), `MarkerCategory` (settings), `MarkerType` and its kinds (`ResourceType`, `EnemyKind`, `AnimalKind`, `ChestKind`, `SpecialShinyKind`), the catalogue with all guids. |
| `NodeIndex.cs` | `MapPoint`, `PointSource`; resource nodes, ground pickups and static chests from the linked entities, with their content set. |
| `EnemyIndex.cs` | Enemy spawn points, chest and special-shiny spawners; area buckets, variants, eligibility, locks, quest pins: `State()`. |
| `AnimalIndex.cs` | Animal spawn points: exact placements and "possible" spawners from the rebuilt spawn pool, story gates. |
| `Entrances.cs` | Interior (cave) exits: where an off-map point is drawn. |
| `ClearedState.cs` | Harvested / killed / opened: save data, live components, the game's respawn rule. |
| `LiveUpdates.cs`, `Patches/LiveUpdatePatches.cs` | Crosses right after a kill / harvest / opened chest. |
| `MapOverlay.cs` | The markers on the map: build, place, refresh, availability, cave watch. |
| `MarkerArt.cs`, `GameSprites.cs`, `ItemIcons.cs` | Baked marker sprites and badges, game sprites by name, item icons. |
| `HeightArrows.cs` | The game's above / below arrows on markers near the hero. |
| `FilterRow.cs`, `Patches/FilterMenuPatches.cs` | The "Markers" panel in the map's Filters menu, the "?" toggle, gamepad navigation. |
| `Localization.cs` | Button names in the game's language. |
| `SettingsRows.cs`, `Prefs.cs`, `Heroes.cs`, `Tags.cs` | Options > Gameplay rows, preferences, local hero registry, raw tag arrays. |

## World data

Right after the hero is in the world, `Frame.InstanceDataStorage` holds the static instance data of the whole world,
loaded or not (verified by the probe mod, see `analysis/repeatable_quests.md` 3.2):

* `m_storageLinkedEntityInstanceData` (7318): `FarmableResourceStaticInstanceData` (778 trees / ores / dig / fishing
  spots), `StaticPickupStaticInstanceData` (782 herbs / food / crabs / nettles), `ContainerStaticInstanceData`
  (62; 19 chests), teleports and trigger zones (cave exits). Type = the first `LootSource.Sources[]` guid
  (`MarkerTypes.ByLootSource`); chests by `Settings` (chestData / smallChestData).
* `m_storageReadonlyContent` (2150 content packs): each names its content set (`ContainingNuggetId`: a chunk / area id,
  or an area variant / quest nugget) and lists its `LinkedEntityIndices`, `NpcIndices`, `DynamicSpawnerDataIndices`. A
  point's content set comes from here (`ListView<T>.m_list` needs the `Il2Cppmoon.common` reference).
* `m_storageDynamicSpawnerData` (9675): `SpawnerTags` All / Any / None. The arrays are il2cpp `MarkupTagData[]` with a
  reference field, read raw (`Tags.cs`: length @+0x18, 16-byte elements from @+0x20, Tag @0).
* `m_storageNpcInstanceData` (2243 fixed placements), `m_storageNpcData` (the spawn pool), `m_storageRuntimeStreamingNode`
  (308 nodes; 223 buckets = map sub-areas with alternative variants).
* Quantum assets from the main thread: `IResourceManagerExtensions.GetAsset(game.ResourceManager, guid, true)?.TryCast<T>()`.
  `MoonQuantumGame.FindAsset<T>` asserts "Code must run on sim thread". Unity-side wrappers (item icons / names):
  `MoonQuantumGame.FindWrapper<T>(guid)`.

### Marker kinds

* **Resources**: 778 farmable nodes + 782 pickups, one type per loot source (`analysis/resource_loot.md`). Fish = the
  fishing tier (carp / trout +30% Gangrenous Eel / bass); digging spots share one loot table (one button).
* **Enemies** (`EnemyIndex`): the 10 tags the "Kill N X" challenges roll (`analysis/repeatable_quests.md` 3).
  Spawners with the kind's tag (minus NoneTags); faction-less enemy spawners take their nugget's `OccupyingFaction`;
  placements by `NpcData.EnemyFaction` / `EnemyType.Giant`, co-op extras (`MinimalNumberOfPlayers > 1`) skipped. Names =
  the game's `EnemyTypeLocalization` (tag 25 "False Ones", 26 "Harvesters").
* **Animals** (`AnimalIndex`, `analysis/animal_parts.md`): exact = placements of the species; possible = spawners whose
  rebuilt candidate pool gives the kind a share >= 0.2 (`DynamicSpawnerSystem.GetSpawnableMatchingTags` rebuilt: NpcData
  tags from EnemyType / faction bits / combat style / NativeRegions 30001+r / affix flags / AdditionalTags, the node's
  region tag, the node chain's blacklist, `SpawnRate` weights). Story gates: `NpcData.DynamicSpawningUnlocksAfter
  .ConditionPassed(f)` (works on the main thread).
* **Treasure chests** (`analysis/chest_state.md`): ~2,100 spawners tagged SmallChest 20004 .. SpecialChest 20007 (size
  rolled per spawn) + the static chests. A spawner also tagged Shiny / SpecialShiny is "possible".
* **Special shinies** (`analysis/shinies.md`): spawners tagged SpecialShiny 20009 (63 spots); mixed ones "possible".
  Normal (blue) shinies are not shown.
* **Keys** (`analysis/keys.md`): the 12 key / handle pickups (static genericPickupData containers, `KeyKind.Pickups`:
  container -> key item, door puzzle; `KeyKind.Doors`: what it opens). Taken = the door puzzle left state 0 (1 key taken,
  2 door opened; save `PuzzleData.EcsPersistentData` State @0x18, live `PuzzleAPI.GetStateId`) or the key is in the
  world's key inventory (`InventoryAPI.GetItemMatchingCount(f, f.InventorySystem.RealmInventoryOwner, key, false)`).
  A taken key's marker moves to its door (`NodeIndex.KeyDoors`, PointSource.KeyDoor) until it is open: puzzle state 2,
  or for the two keys without a puzzle the door / lever record's `IsTriggered(f)` (Rusted Barnacle Key: the tower
  cellar door; Sewer Lever Handle: only its single-use broken wall lever, the other three are pulled again and again).
  Open = remembered per world (`KeysUsed`; the earlier `KeysCollected` recorded taken keys and is no longer read). A key
  not taken whose pickup fails `RespawnablesAPI.DoesPassSpawnRequirements` is not there now (Western Bridge Key outside
  its quest). After a hook the keys are redrawn whatever their distance (the door can be ~100 m from the pickup).
* **Undiscovered whispers** (`analysis/whispers.md`): `CerimWhisperStaticInstanceData` (27; Sacrament's always counts as
  activated). Activated = `Il2Cpp.CerimWhisperViewAPI.IsActivated(f, data)` (per world); hook
  `CerimWhisperAPI.SetActivationState`. Ignore fog; drawn on a second root after the game's icons (`MRM_MarkersTop`):
  the game draws its own whisper icon for every explored whisper, activated or not.
* **Shortcuts** (`analysis/shortcuts.md`, `ShortcutData.cs` generated by `python tools/shortcuts.py --json`): 105
  kick-down ladders, trapdoors, one-way doors, drawbridges, lever gates, breakable walls (key doors left out, and the 8
  in caves / cellars / houses, too small for a shortcut to matter; the sewer's 5 stay, it is a map of its own). Marker where the player
  must go (ladder top, the door's open side, ...); ladders / trapdoors get the height arrow from 1.5 m. The fog check
  uses that spot too: a puzzle's own record sits at (0, 0, 0) and a one-way door's on its closed side, so checking the
  record showed some shortcuts in unexplored land and hid others.
  Opened = the record's `IsTriggered(f)` (ladder / door / lever: live, else save), a puzzle in an open state, a wall
  despawned (`DestructibleData.EcsPersistentData` LastDespawnTime); remembered per world (`ShortcutsOpened`; Faith and
  Flame raises some Mountain Pass ladders again). Hooks: `LadderData.KickDown`, `DoorAPI.Open`, `PuzzleData.Trigger`.

## Which map, and caves

* Map space: under `mapScreen/.../chunks/icons` (`MapScreen.IconsCanvas`) local (x, y) = world (x, z). Markers go into a
  `MRM_Markers` root as the layer's first child (game icons draw on top) and copy the player arrow's `localScale` (zoom).
* A point belongs to the smallest map texture whose bounds contain it (`TextureSliceData.BoundsCenter / BoundsSize` of
  `OverworldMapSlices` and `InteriorMapSlices`, cached per map screen). The game's `GetSliceData` answers "overworld" for
  everything. Overworld 1732^2 around (422, 614), sewer 400^2 around (-38, -1166).
* Points outside every map are in interiors: drawn at the interior's mouth (`Entrances`, `analysis/interior_entrances.md`).
  Interior = the smallest chunk (`RuntimeStreamingNode.PhysicalBounds`, Kind Chunk) containing the point; exits = the
  links the game's map draws as Entrances (`InteractableTeleportStaticInstanceData`, `TriggerZoneStaticInstanceData` Type
  Teleport) standing in that chunk with a destination `WorldSpawnPointData` on a map; the point goes to the exit whose
  source is nearest. One marker per (type, exit). Excluded: `infiniteDungeon` (crucible), prologue ship, phalenChurch,
  hillsCaveC, global.

## Area variants (`analysis/world_states.md`, `analysis/wolf_den_variant.md`)

Every bucket lists its alternative variants as ambience payloads (`StreamingAPI.GetAmbiencePayloadCollectionForStreamingNode`;
the nugget guid is raw @+0x20 of the payload). One is eligible when `Weight > 0 && ConditionsPassed(f, default)` (story).

* **Re-roll**: a bucket that is neither loaded nor revealed re-rolls every 10-30 s (the chunk re-picks its faction every
  10 s, `AmbienceStateSystem`), so its current roll is useless on a map. A cave only loads when the hero walks in.
* **Locked** = revealed (`FogOfWarSystem.IsAnyRevealedInNode(f, node)`, ~45 real minutes after the area was uncovered,
  survives a reload: the faction is saved, `PersistentStreamingNode`), loaded (`StreamingAPI.IsIntegrated(f, ref)`, ref =
  node storage index + 1) or **pinned**: its current variant (`GetActiveNuggetIn`) is not one of its ambience variants =
  a quest / bounty variant, which beats ambience until the quest step ends (e.g. Faith and Flame's deserters in the
  Mountain Pass wolf den).
* `EnemyIndex.State(contentSet)`: **Plain** = area content / current variant of a locked bucket / live quest content /
  the watched cave's roll; **Possible** = eligible variant of an unlocked bucket; **Hidden** = other.
* Re-read (`UpdateLive`, ~3 ms for 223 buckets) on map open, after fast travel and every 10 s while the map is open;
  never on a timer otherwise (an early timer version hitched).
* **Cave watch** (`MapOverlay.CaveGuard`): hero within 20 m of an unlocked cave's mouth that can roll a selected type ->
  that chunk is re-read every 2 s and its current roll drawn plain; each roll is logged (Debug).
* A cave marker drops its "?" when some unlocked bucket has the type for certain in every eligible variant
  (`GuaranteedInCave`; e.g. caves whose every variant has its own chest spawners).

## Cleared state (`analysis/cleared_state.md`, `analysis/chest_state.md`)

* **Save** (unloaded areas): `GameSaveData.Get(f).RealmContents` (+0x20). `SerializedStaticData` (+0x110, 16-byte
  {long EntityId, ref Ecs}) and `SerializedDynamicData` (+0x118, 0x28-byte entries, Ecs @0x10), read raw, classes by
  il2cpp class name:
  `LinkedFarmableResourceData` / `StaticPickupData` .EcsPersistentData LastDespawnTime @0x18;
  `NpcSpawnerData` Spawned @0x18 && (NpcWasDead @0x38 || LastTimeWithoutEntity @0x20);
  `DynamicNpcSpawnerData` SpawnerId @0x20, Spawned @0x28 (cleared when no record is alive);
  `LinkedContainerData` LastDespawnTime @0x18 || IsOpen @0x28 (static chests);
  `ContainerData` IsOpen @0x18, DynamicData {HasValue @0x30, SpawnerId @0x40} (spawned chests and shinies).
* **Live** (loaded areas, override the save; the save only catches up on unload / autosave):
  nodes: `IdentityComponentAPI.ResolveEntity(f, InstanceGuid)` + `RespawnablesAPI.IsRespawnableCurrentlySpawned`;
  placements: `NpcSpawnerComponent` {Spawned @0x14, MainNpc ref @0x60} + `AiUtils.ExistsAlive`;
  dynamic spawners: walk the `DynamicSpawnerComponent` buffer {data ref @0, SpawnedEntityGuid @0x18}, find the per-spawn
  object by `DynamicallySpawnedObjectComponent.Guid` (@0x8; the game scans the same way), then NPC
  (`DynamicNpcSpawnerComponent`, `DynamicSpawningAPI.GetSpawnedEntityInstanceRef`) or container
  (`ContainerComponent.IsOpen` @0x4; a looted chest or shiny stays as an opened object).
  Components through non-generic interop: `ComponentTypeId.GetComponentIndex("Name")`, `f.Has(e, i)`,
  `f.Unsafe.GetPointer(e, i)`, and the raw `ComponentDataBuffer` from `f.GetDataBuffer(i)` (count @0x4, stride @0xC,
  blocks @0x18 {EntityRef* handles, byte* data}, block capacity @0x28; dense index 0 unused).
* **Respawn rule** (`IsCleared`): a cleared spot comes back on its next load once no revealed fog cell is near it
  (`FogOfWarSystem.IsAnyRevealedInRange`, radius 0 for spawners, 1 for static entities). So a stamp counts only while
  revealed fog is still around it; without a fog cell the saved state stands.
* Chests in a plague safe haven (`RuntimeStreamingNode.IsPlagueSafeHaven`, Sacrament) never come back: hidden once opened.
* **LiveUpdates**: postfixes on `GameSystem.OnNpcDied`, `RespawnablesAPI.MarkRespawnableAsDespawnedNow`,
  `ContainerData.Interact`, `LinkedContainerData.Interact` set a flag (simulation thread, possibly predicted frames);
  2 s after the last event (max 4 s after the first) the live part is re-read within 25 m (60 m for enemies, which die
  away from their spawn point) and only those markers are redrawn.

## Marker art

* `MarkerArt.Bake`: 128 px sprite; the picture (item icon read back through a RenderTexture, halved on the way, cropped
  to its alpha bounds) fits 84 px (`IconFraction`), scaled to the same opaque area (`TargetArea` 0.42) so a skull and an
  ore chunk look equally big; dark outline (dilate r4) and soft shadow (dilate r6 + blur, offset). Enemy, chest and
  shiny markers use the game's map glyphs (`SkullMapIcon`, `ChestMapIcon`, `StarMapIcon`) tinted.
* Badges: top right "?" (`QuestionMapIcon`) or red cross (`UICrossIcon` tinted 255,105,92) on a dark disc; top left the
  game's `DoorwayMapIcon` for cave markers. Trout spots: `Split` (trout top-left, eel bottom-right, cut on the diagonal).
* Empty types in the row: `Grey` (luminance) button picture.
* Size: `sizeDelta` = player arrow width x `MarkerScale` x `1.4 / IconFraction`, `localScale` = the arrow's.
* `HeightArrows`: sprites, rule and placement copied from a live `MapIcon.m_heightIndicator` (game: within 50 m, from 5 m
  height difference, full opacity at 30 m; up rotated z 90, down z 270). Child named `...NoMinimap`.
* Minimap mod: it mirrors every active Image under the icon layer every 0.5 s (sprite swaps included); its own drawing
  size for other mods' markers is its business (`mods/Minimap/docs/foreign-marker-size.md`).
  Marker names end in `@y=<world height>` (invariant culture): the Minimap reads it for its above / below arrows.

## The Markers panel (`FilterRow`)

* A clone of the Filters menu's panel (`filterMarkerMenu`, "bg ") above it, title "Markers", one clone of the first filter
  button per type (`MouseHoverSelect` removed, own click / hover / select handlers; the game's "All" never sees them).
* Lines: `markerItems` plus clones inside the one panel; units (dot-separated) fill the top line in order, a unit that does
  not fit starts the next line (`UsableWidth` 0.96 of the panel; 1846 units on a 9:8 screen, ~2200 needed for all).
  Units = categories; Fish (in Harvesting and Cooking) stands alone between them when both are on, else joins the one on.
* The panel background (`uiMenuBack1Texture`) is sliced with borders larger than the panel, so it scales with the height:
  the title moves with it (same share of the height) and the panel grows by `LineGrowth` 1.09 lines per extra line
  (tuned from screenshots).
* Row contents: a type shows once the story allows it in this world (live or eligible variant; remembered per world in
  `Unlocked`, keyed by `MoonQuantumGame.SaveInfo.SaveDataHandler.m_localRealmId`, Guid raw @0x70;
  `MoonQuantumGame.RealmId` is empty offline and `SeenItemsRegistry.GetCurrentRealmId` returns a type name). Grey
  picture while nothing can show; hidden until then.
* "?" toggle: the badge sprite (`MarkerArt.BadgeIcon`) right of the rendered title (TMP `textBounds`); off hides markers
  with nothing certain (`ShowPossible`).
* Gamepad: the D-pad goes through `MapScreen.ProcessInput` -> the sub-menu's `OnUp/Down/Left/RightButtonPressed` /
  `OnSubmit` (our prefixes); the left stick through `UIScreen.TryNavigate` -> EventSystem move events -> explicit
  `Selectable.navigation`. Both follow the links `LinkNavigation` sets when the panel appears (`OpenMarkerFilterMenu` is
  inlined, so `FilterRow.Tick` watches the panel): left / right within a line, up / down between lines, top line up to
  the "?" toggle, last line down to the game's row, the game's buttons up to our last line (restored on unload).

## Settings and language

* Options > Gameplay rows via the shared kit (`src/Shared/SettingsRowsKit.cs`, vendored): unexplored areas, marker size,
  one checkbox per category. `SettingsPreview` draws a live picture under the hovered row's description (the
  controls' `m_settingNameText` / `m_specificationText`; the row is recognized by its name): the category's markers
  while it is on, markers at the chosen size, a strip of explored land and fog with markers on both sides (the fogged
  ones only while "unexplored areas" is on). The test builds were "Map Resource Markers": `Prefs.CarryOver` copies a
  lone `[MapResourceMarkers]` section to `[MapMarkers]` once.
* `Localization`: in English the mod's labels; otherwise the game's texts (enemy type names, item names via
  `HeroItemDataAsset.ItemNameMsg`, animals = their skull item, digging spots = the dig prompt "Dig",
  `LinkedFarmableResourceDataAsset` 964127749868152500 `.LocalizedShortenedHasToolHint`).
  English test: a game text equals its `.English`.

## Performance (9:8, user's world)

Index build ~200 ms once per world (enemies 35 ms, animals 90 ms). Map open: variants + locks ~3 ms, cleared state ~2 ms,
availability ~1 ms. Hook update ~0.5 ms. A log line costs ~1 ms: per-event lines are Debug-only. Vector3 operators can go
through interop: hot loops use plain floats.

## Pitfalls and dead ends

* `MapScreen.m_isShowing` reads false while the map is open; map open = `MapScreen.Show` postfix, "map is open" =
  `LocalPlayerIndicator.gameObject.activeInHierarchy`.
* `MapScreen.m_fogOfWarData` (`MapFogOfWarData`) only updates the player's cell: use `FogOfWarSystem.IsExplored`.
* Interop `Transform.SetParent(Transform, bool)` throws "Method unstripping failed": use the 3-argument version.
* The vanilla map has no mouse drag (WASD / stick pan only).
* The filter button's icon is the button's own Image (stretching "the icon" stretches the button).
* `GameSprites.Find` caches all sprite names per scan (a rate-limited scan once hid the second lookup).
