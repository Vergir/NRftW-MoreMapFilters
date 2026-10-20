namespace MoreMapFilters;

/// <summary>What a shortcut is (analysis/shortcuts.md); ladders and trapdoors are reached from above.</summary>
internal enum ShortcutKind { Ladder, Trapdoor, OneWayDoor, Drawbridge, LeverGate, Wall }

/// <summary>Which instance data record holds its state.</summary>
internal enum ShortcutRecord { Ladder, Door, Lever, Puzzle, Destructible }

/// <summary>
/// The world's one-way shortcuts, generated from `python tools/shortcuts.py --json` (build 29466; key doors left out, the
/// Keys filter covers them; so are the 8 in caves, cellars and houses: too small for a shortcut to matter. The sewer's stay). Position = where the player must go: a ladder's / trapdoor's top, a one-way door's open
/// side, a drawbridge's hinge, a gate's lever, a wall itself. Puzzles: OpenMask = bit per state id that means open.
/// </summary>
internal static class ShortcutData
{
    public readonly record struct Entry(long Guid, ShortcutKind Kind, ShortcutRecord Record, float X, float Y, float Z,
        int OpenMask, int InitialState);

    public static readonly Entry[] All =
    {
        new(-1536685384, ShortcutKind.Ladder, ShortcutRecord.Ladder, 546.74f, 103.62f, 345.79f, 0, 0),   // kickdownLadder, mountainPassA
        new(-1396181459, ShortcutKind.Ladder, ShortcutRecord.Ladder, 504.54f, 98.40f, 286.78f, 0, 0),   // kickdownLadder, mountainPassA
        new(-317342137, ShortcutKind.Ladder, ShortcutRecord.Ladder, 519.82f, 142.21f, 237.53f, 0, 0),   // kickdownLadder, mountainPassA
        new(-252379505, ShortcutKind.Ladder, ShortcutRecord.Ladder, 410.84f, 113.62f, 304.84f, 0, 0),   // kickdownLadder, mountainPassA
        new(288397716, ShortcutKind.Ladder, ShortcutRecord.Ladder, 474.99f, 136.86f, 236.88f, 0, 0),   // kickdownLadder, mountainPassA
        new(423814282, ShortcutKind.Ladder, ShortcutRecord.Ladder, 456.06f, 92.48f, 291.60f, 0, 0),   // kickdownLadder, mountainPassA
        new(1218799478, ShortcutKind.Ladder, ShortcutRecord.Ladder, 463.99f, 116.70f, 321.84f, 0, 0),   // kickdownLadder, mountainPassA
        new(1503429053, ShortcutKind.Ladder, ShortcutRecord.Ladder, 431.78f, 123.25f, 323.67f, 0, 0),   // kickdownLadder, mountainPassA
        new(2145572608, ShortcutKind.Ladder, ShortcutRecord.Ladder, 508.87f, 119.05f, 389.63f, 0, 0),   // kickdownLadder, mountainPassA
        new(-42759271, ShortcutKind.Ladder, ShortcutRecord.Ladder, 51.00f, 23.41f, 122.92f, 0, 0),   // kickdownLadder, outskirtsA
        new(169844264, ShortcutKind.Ladder, ShortcutRecord.Ladder, 104.41f, 65.83f, 156.45f, 0, 0),   // kickdownLadder, outskirtsA
        new(811967372, ShortcutKind.Ladder, ShortcutRecord.Ladder, 126.64f, 51.82f, 89.41f, 0, 0),   // kickdownLadder, outskirtsA
        new(1251096477, ShortcutKind.Ladder, ShortcutRecord.Ladder, 107.18f, 71.97f, 161.34f, 0, 0),   // kickdownLadder, outskirtsA
        new(-2072040654, ShortcutKind.Ladder, ShortcutRecord.Ladder, 47.73f, 22.09f, -21.07f, 0, 0),   // kickdownLadder, hillsA
        new(-1498085234, ShortcutKind.Ladder, ShortcutRecord.Ladder, 181.54f, 77.35f, -121.02f, 0, 0),   // kickdownLadder, hillsA
        new(1533094806, ShortcutKind.Ladder, ShortcutRecord.Ladder, 146.71f, 79.20f, -88.84f, 0, 0),   // kickdownLadder, hillsA
        new(-1668869200, ShortcutKind.Ladder, ShortcutRecord.Ladder, 246.81f, 75.63f, 405.27f, 0, 0),   // kickdownLadder, marinWoodsA
        new(-1471902894, ShortcutKind.Ladder, ShortcutRecord.Ladder, 263.20f, 90.88f, 385.16f, 0, 0),   // kickdownLadderBossLoot, marinWoodsA
        new(-1276726078, ShortcutKind.Ladder, ShortcutRecord.Ladder, 279.79f, 45.84f, 319.83f, 0, 0),   // kickdownLadder, marinWoodsA
        new(-1135986809, ShortcutKind.Ladder, ShortcutRecord.Ladder, 185.14f, 55.89f, 398.93f, 0, 0),   // kickdownLadder (4), marinWoodsA
        new(-725409390, ShortcutKind.Ladder, ShortcutRecord.Ladder, 195.34f, 73.68f, 392.54f, 0, 0),   // kickdownLadder, marinWoodsA
        new(-303590809, ShortcutKind.Ladder, ShortcutRecord.Ladder, 221.46f, 44.62f, 333.88f, 0, 0),   // kickdownLadder, marinWoodsA
        new(-196001910, ShortcutKind.Ladder, ShortcutRecord.Ladder, 302.88f, 65.03f, 346.96f, 0, 0),   // kickdownLadder (3), marinWoodsA
        new(17158463, ShortcutKind.Ladder, ShortcutRecord.Ladder, 174.78f, 92.02f, 401.95f, 0, 0),   // kickdownLadder (2), marinWoodsA
        new(269888460, ShortcutKind.Ladder, ShortcutRecord.Ladder, 185.52f, 65.40f, 427.65f, 0, 0),   // kickdownLadder, marinWoodsA
        new(910650555, ShortcutKind.Ladder, ShortcutRecord.Ladder, 183.92f, 94.79f, 463.82f, 0, 0),   // kickdownLadder (1), marinWoodsA
        new(-29623864, ShortcutKind.Ladder, ShortcutRecord.Ladder, 252.62f, 69.52f, 381.98f, 0, 0),   // kickdownLadder, marinPathStateChanges
        new(-1704241553, ShortcutKind.Ladder, ShortcutRecord.Ladder, -95.42f, 40.66f, -1070.28f, 0, 0),   // kickdownLadder (1), sewerDungeonA
        new(-872485866, ShortcutKind.Ladder, ShortcutRecord.Ladder, -49.31f, 51.22f, -1102.17f, 0, 0),   // kickdownLadder (2), sewerDungeonA
        new(512578885, ShortcutKind.Ladder, ShortcutRecord.Ladder, -6.82f, 45.15f, -1174.67f, 0, 0),   // kickdownLadder, sewerDungeonA
        new(-470177741, ShortcutKind.Ladder, ShortcutRecord.Ladder, 266.20f, 62.47f, 470.93f, 0, 0),   // kickdownLadder (2), villageArtState
        new(222316991, ShortcutKind.Ladder, ShortcutRecord.Ladder, 233.19f, 58.95f, 546.17f, 0, 0),   // kickdownLadder (3), villageArtState
        new(512016601, ShortcutKind.Ladder, ShortcutRecord.Ladder, 208.57f, 63.35f, 515.71f, 0, 0),   // kickdownLadder (1), villageArtState
        new(-2042505619, ShortcutKind.Ladder, ShortcutRecord.Ladder, -65.76f, 22.79f, 62.42f, 0, 0),   // kickdownLadder, fortressA
        new(-1465662833, ShortcutKind.Ladder, ShortcutRecord.Ladder, -99.18f, 37.05f, 47.64f, 0, 0),   // kickdownLadder, fortressA
        new(79935613, ShortcutKind.Ladder, ShortcutRecord.Ladder, 60.81f, 48.30f, 115.99f, 0, 0),   // kickdownLadder, fortressA
        new(1975383240, ShortcutKind.Ladder, ShortcutRecord.Ladder, 71.51f, 17.96f, 88.11f, 0, 0),   // kickdownLadder, fortressA
        new(-2079722045, ShortcutKind.LeverGate, ShortcutRecord.Puzzle, 431.47f, 88.95f, 234.45f, 10, 0),   // gate2, mountainPassA
        new(-1777186622, ShortcutKind.Ladder, ShortcutRecord.Puzzle, 378.90f, 86.66f, 217.50f, 10, 0),   // ladder1, mountainPassA
        new(-963510430, ShortcutKind.LeverGate, ShortcutRecord.Puzzle, 489.89f, 97.98f, 211.16f, 10, 0),   // gate4, mountainPassA
        new(-868383741, ShortcutKind.Ladder, ShortcutRecord.Puzzle, 450.07f, 89.82f, 204.30f, 10, 1),   // ladder3, mountainPassA
        new(1293928370, ShortcutKind.Ladder, ShortcutRecord.Puzzle, 446.09f, 94.28f, 247.42f, 10, 0),   // ladder5, mountainPassA
        new(1350448505, ShortcutKind.Trapdoor, ShortcutRecord.Puzzle, 543.40f, 105.35f, 394.29f, 2, 0),   // toSewers, mountainPassA
        new(1442988754, ShortcutKind.LeverGate, ShortcutRecord.Puzzle, 449.89f, 93.32f, 291.92f, 10, 0),   // gate6, mountainPassA
        new(2044553039, ShortcutKind.Trapdoor, ShortcutRecord.Puzzle, 73.67f, 64.44f, 179.42f, 2, 0),   // toSewers, outskirtsA
        new(-256937477, ShortcutKind.Trapdoor, ShortcutRecord.Puzzle, -66.89f, 18.37f, 23.42f, 2, 0),   // toSewer, coastA
        new(-2058181893, ShortcutKind.OneWayDoor, ShortcutRecord.Door, 485.07f, 105.92f, 250.63f, 0, 0),   // doorOneHinge, mountainPassA
        new(-1550803699, ShortcutKind.Drawbridge, ShortcutRecord.Door, 500.99f, 99.88f, 341.85f, 0, 0),   // kickDownBridge, mountainPassA
        new(-1420882845, ShortcutKind.Drawbridge, ShortcutRecord.Door, 449.63f, 119.67f, 292.60f, 0, 0),   // mountainDrawBridge, mountainPassA
        new(-1223072169, ShortcutKind.Drawbridge, ShortcutRecord.Door, 438.67f, 121.12f, 448.71f, 0, 0),   // fortressOutskirtsDrawBridge, mountainPassA
        new(-666861947, ShortcutKind.Drawbridge, ShortcutRecord.Door, 489.18f, 124.17f, 215.34f, 0, 0),   // kickDownBridge, mountainPassA
        new(1253880646, ShortcutKind.Drawbridge, ShortcutRecord.Door, 458.64f, 122.94f, 482.39f, 0, 0),   // fortressOutskirtsDrawBridge, mountainPassA
        new(1407543143, ShortcutKind.Drawbridge, ShortcutRecord.Door, 404.79f, 81.90f, 249.74f, 0, 0),   // fortressOutskirtsDrawBridge, mountainPassA
        new(1651122447, ShortcutKind.OneWayDoor, ShortcutRecord.Door, 479.14f, 96.58f, 310.52f, 0, 0),   // doorOneHinge, mountainPassA
        new(1843121758, ShortcutKind.OneWayDoor, ShortcutRecord.Door, 460.37f, 83.97f, 207.09f, 0, 0),   // doorOneHinge, mountainPassA
        new(2065801727, ShortcutKind.OneWayDoor, ShortcutRecord.Door, 436.45f, 110.26f, 337.23f, 0, 0),   // doorOneHinge, mountainPassA
        new(-14039622, ShortcutKind.OneWayDoor, ShortcutRecord.Door, 45.90f, 45.70f, -1058.57f, 0, 0),   // doorOneHinge (1), sewerDungeonDarakBossArena
        new(-1847285199, ShortcutKind.Drawbridge, ShortcutRecord.Door, 214.75f, 53.51f, 3.82f, 0, 0),   // fortressOutskirtsDrawBridge, hillsA
        new(-1573027842, ShortcutKind.OneWayDoor, ShortcutRecord.Door, 102.25f, 63.18f, -70.59f, 0, 0),   // doorLeftHingeWoodA, hillsA
        new(-620216410, ShortcutKind.OneWayDoor, ShortcutRecord.Door, 133.38f, 73.17f, -113.49f, 0, 0),   // doorOneHinge, hillsA
        new(-308850634, ShortcutKind.OneWayDoor, ShortcutRecord.Door, 234.77f, 80.37f, -64.05f, 0, 0),   // doorLeftHingeWoodA (1), hillsA
        new(-122376743, ShortcutKind.OneWayDoor, ShortcutRecord.Door, 107.31f, 63.20f, -79.45f, 0, 0),   // doorOneHinge (1), hillsA
        new(332391321, ShortcutKind.Drawbridge, ShortcutRecord.Door, 116.73f, 76.19f, -128.96f, 0, 0),   // drawBridgeWest, hillsA
        new(1055909879, ShortcutKind.Drawbridge, ShortcutRecord.Door, 176.04f, 69.17f, -84.99f, 0, 0),   // drawBridge, hillsA
        new(1414447602, ShortcutKind.Drawbridge, ShortcutRecord.Door, 65.17f, 58.68f, -63.21f, 0, 0),   // fortressOutskirtsDrawBridge, hillsA
        new(-1685985262, ShortcutKind.OneWayDoor, ShortcutRecord.Door, 301.07f, 51.20f, 347.64f, 0, 0),   // doorOneHinge, marinWoodsA
        new(-271786723, ShortcutKind.OneWayDoor, ShortcutRecord.Door, 181.68f, 55.58f, 403.57f, 0, 0),   // doorOneHinge, marinWoodsA
        new(-1975488085, ShortcutKind.Drawbridge, ShortcutRecord.Door, 200.07f, 49.16f, 320.76f, 0, 0),   // drawBridge, marinPathStateChanges
        new(1959039875, ShortcutKind.Drawbridge, ShortcutRecord.Door, 233.10f, 53.92f, 354.15f, 0, 0),   // fortressOutskirtsDrawBridge (1), marinPathStateChanges
        new(-1650038306, ShortcutKind.OneWayDoor, ShortcutRecord.Door, 239.78f, 51.14f, 515.86f, 0, 0),   // doorOneHinge, villageArtState
        new(-958418458, ShortcutKind.Drawbridge, ShortcutRecord.Door, 200.40f, 65.95f, 479.32f, 0, 0),   // fortressOutskirtsDrawBridge, villageArtState
        new(-327031131, ShortcutKind.OneWayDoor, ShortcutRecord.Door, 185.75f, 64.71f, 483.71f, 0, 0),   // doorOneHinge, villageArtState
        new(-438643494, ShortcutKind.Drawbridge, ShortcutRecord.Door, 74.18f, 32.80f, 96.79f, 0, 0),   // fortressOutskirtsDrawBridge, fortressA
        new(-2050340882, ShortcutKind.OneWayDoor, ShortcutRecord.Door, 379.21f, 45.19f, 152.23f, 0, 0),   // shipGraveyardDoorA (1), sacramentA
        new(-2022292912, ShortcutKind.OneWayDoor, ShortcutRecord.Door, 363.36f, 48.53f, 75.66f, 0, 0),   // doorOneHinge (3), sacramentA
        new(-1992692394, ShortcutKind.OneWayDoor, ShortcutRecord.Door, 376.82f, 46.43f, 145.03f, 0, 0),   // doorOneHinge (2), sacramentA
        new(-1884138961, ShortcutKind.OneWayDoor, ShortcutRecord.Door, 374.10f, 38.79f, 70.22f, 0, 0),   // doorOneHinge (2), sacramentA
        new(-1252965130, ShortcutKind.OneWayDoor, ShortcutRecord.Door, 168.55f, 42.59f, 245.45f, 0, 0),   // churchStorageDoorB, sacramentA
        new(2724845, ShortcutKind.OneWayDoor, ShortcutRecord.Door, 171.29f, 42.59f, 247.69f, 0, 0),   // churchStorageDoorA, sacramentA
        new(1648015010, ShortcutKind.OneWayDoor, ShortcutRecord.Door, 377.12f, 40.06f, 120.87f, 0, 0),   // shipGraveyardDoorA, sacramentA
        new(1375053548, ShortcutKind.LeverGate, ShortcutRecord.Lever, 505.11f, 101.06f, 321.39f, 0, 1),   // mountainWallLeverView, mountainPassA
        new(1163983178, ShortcutKind.LeverGate, ShortcutRecord.Lever, -0.60f, 9.67f, 146.34f, 0, 1),   // mountainWallLeverView, outskirtsA
        new(-1805565161, ShortcutKind.LeverGate, ShortcutRecord.Lever, 169.09f, 55.41f, -102.33f, 0, 1),   // fortressWallLeverView, hillsA
        new(-1338187345, ShortcutKind.LeverGate, ShortcutRecord.Lever, 158.63f, 65.97f, -33.15f, 0, 1),   // leverView, hillsA
        new(-928337022, ShortcutKind.LeverGate, ShortcutRecord.Lever, 168.46f, 71.92f, -85.54f, 0, 1),   // fortressWallLeverView, hillsA
        new(511435124, ShortcutKind.LeverGate, ShortcutRecord.Lever, 112.39f, 77.03f, -138.16f, 0, 0),   // fortressWallLeverView, hillsA
        new(-1075386827, ShortcutKind.LeverGate, ShortcutRecord.Lever, 210.08f, 73.10f, 436.22f, 0, 1),   // kestrelEggAltarLeverView, marinWoodsA
        new(-836637124, ShortcutKind.LeverGate, ShortcutRecord.Lever, 187.04f, 87.47f, 408.88f, 0, 1),   // leverView, marinWoodsA
        new(1600557213, ShortcutKind.LeverGate, ShortcutRecord.Lever, 186.33f, 48.20f, 355.79f, 0, 1),   // mountainWallLeverView, marinWoodsA
        new(-1309900188, ShortcutKind.LeverGate, ShortcutRecord.Lever, 17.16f, 35.42f, -1203.27f, 0, 1),   // sewerBrokenWallLeverView, sewerDungeonA
        new(-1391356595, ShortcutKind.LeverGate, ShortcutRecord.Lever, 19.75f, 32.56f, 96.32f, 0, 1),   // fortressBrokenWallLeverView, fortressA
        new(378851690, ShortcutKind.Wall, ShortcutRecord.Destructible, 529.04f, 98.23f, 359.99f, 0, 0),   // destructiblePrisonDoorView, prisonCells
        new(-542045663, ShortcutKind.Wall, ShortcutRecord.Destructible, 529.04f, 98.23f, 359.99f, 0, 0),   // destructiblePrisonDoorView, prisonCells
        new(141015179, ShortcutKind.Wall, ShortcutRecord.Destructible, 200.47f, 64.16f, 50.20f, 0, 0),   // destructibleWoodenWallAOutskirtsView, hillsA
        new(176525282, ShortcutKind.Wall, ShortcutRecord.Destructible, 130.00f, 61.98f, -7.51f, 0, 0),   // destructibleWoodenWallAOutskirtsView, hillsA
        new(1469507785, ShortcutKind.Wall, ShortcutRecord.Destructible, 224.51f, 13.21f, -4.82f, 0, 0),   // destructibleWoodenWallACoastView, hillsA
        new(1827129203, ShortcutKind.Wall, ShortcutRecord.Destructible, 107.50f, 62.42f, -106.26f, 0, 0),   // destructibleWoodenWallAOutskirtsView, hillsA
        new(-1141098166, ShortcutKind.Wall, ShortcutRecord.Destructible, 208.55f, 61.40f, 347.60f, 0, 0),   // destructibleWoodenWallAOutskirtsView, marinWoodsA
        new(1771034386, ShortcutKind.Wall, ShortcutRecord.Destructible, 273.58f, 32.11f, 345.50f, 0, 0),   // destructibleWoodenWallAOutskirtsView (3), marinWoodsA
        new(-430924219, ShortcutKind.Wall, ShortcutRecord.Destructible, 499.65f, 87.78f, 266.67f, 0, 0),   // destructiblePlankWall, quarryBottom
        new(-1624694607, ShortcutKind.Wall, ShortcutRecord.Destructible, 199.32f, 59.37f, 479.78f, 0, 0),   // destructibleWoodenWallAOutskirtsView, villageArtState
        new(347660190, ShortcutKind.Wall, ShortcutRecord.Destructible, 260.21f, 56.66f, 478.05f, 0, 0),   // destructibleWoodenWallAOutskirtsView, villageArtState
        new(1393427671, ShortcutKind.Wall, ShortcutRecord.Destructible, -110.74f, 24.04f, 50.13f, 0, 0),   // destructibleWoodenWallCFortressView, fortressA
        new(-164233508, ShortcutKind.Wall, ShortcutRecord.Destructible, 0.94f, 12.28f, 41.39f, 0, 0),   // destructibleWoodenWallACoastView, coastA
        new(693668428, ShortcutKind.Wall, ShortcutRecord.Destructible, 111.27f, 35.30f, 119.35f, 0, 0),   // destructibleWoodenWallAOutskirtsView, northCave
    };
}
