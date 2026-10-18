# Map Resource Markers

A [MelonLoader](https://github.com/LavaGang/MelonLoader) mod for **No Rest for the Wicked** that adds a Markers panel to the
map's Filters menu: pick trees, ores, fishing and digging spots, herbs and food, the animals whose parts upgrade gear,
the enemy factions of the "Kill N" challenges, or treasure chests, and the map shows where they are, with the world's
current state (area variants, harvested / killed / looted spots, caves). Display only; the Quantum simulation is untouched.

Download: [GitHub releases](https://github.com/vergir/NRftW-MapResourceMarkers/releases/latest)

What players see is described on the Nexus page ([docs/nexus-description.bbcode](docs/nexus-description.bbcode)); how enemy
spawns work, for players, in [docs/enemy-spawns.md](docs/enemy-spawns.md); the traced game code, data sources and dead
ends in [docs/internal.md](docs/internal.md).

## Build

* `dotnet build -c Release` builds and copies `MapResourceMarkers.dll` to `<game>/Mods` (`-p:DeployToGame=false` to skip,
  `-p:GameDir=...` for another install). With HotReload in `<game>/Plugins` the running game picks up new builds.
* `pwsh ./package.ps1` builds the release zip into `dist/MapResourceMarkers.zip` (`Mods/MapResourceMarkers.dll`, README,
  LICENSE, CHANGELOG).
* Needs MelonLoader 0.7.3's generated interop assemblies in the game folder (start the game once with MelonLoader).
* `src/Shared/SettingsRowsKit.cs` is a vendored copy of the workspace's `../Shared/SettingsRowsKit.cs` (a build warning
  says when they differ).

## Layout

| Path | What |
|---|---|
| `src/MapResourceMarkersMod.cs` | Entry point, per-frame ticks, map / teleport / settings hooks, unload. |
| `src/MarkerTypes.cs` | Groups, settings categories, the marker types and their game asset guids. |
| `src/NodeIndex.cs`, `src/EnemyIndex.cs`, `src/AnimalIndex.cs`, `src/Entrances.cs` | Where things are: world data, area variants and their state, caves. |
| `src/ClearedState.cs`, `src/LiveUpdates.cs` | Harvested / killed / looted, from the save and the live world. |
| `src/MapOverlay.cs`, `src/MarkerArt.cs`, `src/HeightArrows.cs` | The markers on the map. |
| `src/FilterRow.cs`, `src/Patches/` | The Markers panel, gamepad navigation, game hooks. |
| `src/Localization.cs`, `src/SettingsRows.cs`, `src/Prefs.cs` | Names, Options rows, preferences. |

## Preferences

`UserData/MelonPreferences.cfg`, section `[MapResourceMarkers]`:

| Key | Default | |
|---|---|---|
| `Enabled` | `true` | Master switch. |
| `Selected` | `""` | Selected types (comma-separated ids); set from the panel. |
| `ShowInUnrevealed` | `true` | Markers in unexplored parts of the map. |
| `MarkerScale` | `1` | Marker size (0.5..2). |
| `ShowHarvesting`, `ShowAnimals`, `ShowEnemies` | `true` | Button groups in the panel. |
| `ShowCooking`, `ShowValuables` | `false` | Button groups in the panel. |
| `ShowPossible` | `true` | "?" markers (the panel's "?" button). |
| `Unlocked` | `""` | Per world: types that joined the panel (written by the mod). |
| `AddSettingsRows` | `true` | The rows in Options > Gameplay. |
| `Debug` | `false` | Log details (indexing, area variants, cave rolls, live reads, timings). |

## License

MIT, see [LICENSE](LICENSE).
