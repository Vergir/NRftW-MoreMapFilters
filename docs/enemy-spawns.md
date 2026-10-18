# How enemy and animal spawns work (and what the markers mean)

No Rest for the Wicked does not have fixed enemy camps everywhere. Most of the world is split into small areas, and each area
has several **variants**: the same courtyard can hold Risen in one variant, Nith or Harvesters in another. The game picks
the variant itself, and it changes over time. This page explains the rules as far as we could work them out from the game
data and code, and how Map Resource Markers shows them.

## The markers

| Marker | Meaning |
|---|---|
| Plain icon | They are there: a fixed spawn, or the current variant of an area that cannot change right now |
| Icon with **?** | They **can** be there when you arrive: a variant the area may roll, or an animal spawner that may roll that species |
| Icon with a red **x** | Cleared: harvested, killed or picked up, and not back yet |
| Icon with a doorway (top left) | The spot is inside a cave or other interior; the marker sits at its entrance |
| No icon | Not possible right now (see below) |

A button in the Markers row is **grey** when the story allows that type but no area can show it right now, and it is
**hidden** until the story allows it at all in your world.

The **?** button next to the panel's title hides every marker that is only a maybe: then you see only what is certainly
there (fixed spawns, areas that are locked, caves where every variant has it).

## Which variants are possible

- **Story progress.** Almost every variant is tied to a main quest: "after The Weeping Sisters", "after Roots That Bind"
  and so on. Once allowed, a variant stays allowed for the rest of that world. Only a few "before quest X" variants go
  away for good. Example: Lowland Meadows loses its Harvesters and Nith after The Weeping Sisters.
- **Quests and bounties can take over an area.** While a quest step or bounty is active, it can put its own variant into
  an area, and that variant always wins. Example: from "Hunt Down Deserters" until "Fight the Winged Brood" in Faith and
  Flame, the Mountain Pass wolf den holds deserter knights and wolves, and nothing else can appear there. The area goes
  back to normal the moment that quest step is done.
- **Story-locked animals.** Some animals only spawn after a quest: bears after The Weeping Sisters, Winged Brood after
  Faith and Flame.

## When an area changes

- **Areas you have not visited recently re-roll every 10 to 30 seconds.** The game picks which faction holds the area
  (all allowed factions equally likely), then one of that faction's variants. That is far faster than anyone can travel, so
  the map shows every allowed variant of such an area with a **?** instead of the current roll.
- **An area locks when you get there.** Once the area around you loads, or you have uncovered its fog, it keeps its variant.
  What you meet is whatever it rolled at the moment you arrived.
- **The lock lasts about 45 real minutes.** Uncovered fog creeps back about 45 minutes after you first uncovered the area
  (real time, and time with the game closed counts too). Then the area starts re-rolling again. Until then, quitting and
  reloading does not give you a new roll: the area's faction is saved with the world.
- **Fast travel does not lock your destination early.** The area keeps re-rolling during the loading screen, so a **?** at
  your destination is a chance, not a promise.

## Caves and other interiors

- A cave only loads, and locks, when you **walk in**. Standing at the entrance does not lock it, so the cave keeps
  re-rolling every 10 to 30 seconds while you wait outside.
- **Cave watch:** when an enemy or animal filter is on and you stand within 20 m of a cave entrance that can roll a type
  you selected, the mod checks that cave every 2 seconds. When the current roll has your type, the marker at the entrance
  loses its **?**. Walk in then, and the cave locks with that variant.
- A cave held by a quest (see above) never re-rolls until the quest step is done.

## Respawns ("cleared" markers)

- Harvested nodes, picked plants and killed enemies come back when the fog creeps back over them (about 45 real minutes
  after you uncovered that spot), not on a timer, and not by resting at a whisper.
- The game only respawns things when their area loads, so the save can say "harvested" for a place you left long ago. The
  mod applies the game's own respawn rule instead, so you only see a red **x** where the fog is still uncovered.
- Trees, ores, digging and fishing spots and plants near you update the moment you harvest them. Killed enemies may only
  show their **x** after you leave the area or the game autosaves.

## Finding a specific enemy type (for "Kill N X" challenges)

1. Select only that enemy type in the Markers row.
2. Plain icons are certain: go there. Areas you uncovered in the last ~45 minutes show their real variant.
3. **?** icons are chances. Pick an area or cave you have not been to recently.
4. For caves, wait at the entrance until the marker loses its **?**, then walk in.
5. If the button is grey, every place that type can appear is currently locked to something else. Wait for the locks to
   expire (~45 minutes), or progress the story.

Odds per area differ. Late in the story, for example, Nameless Pass rolls Harvesters about one time in five and Mariner's
Keep about one time in eight.
