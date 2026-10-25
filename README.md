# AsyncLocations

100% Vibe coded for personal use, I couldn't find any other mod that had a fix for this so I investigated it using Claude. I had stutters when running through unexplored areas that annoyed me. Since this fixed the issue for me I decided to upload it. 

Valheim runs on Unity 6 since February 2026, which can copy objects on worker threads (`Object.InstantiateAsync`). This BepInEx mod uses it so ruins, towers, fortresses and dungeon entrances load in with noticeably less stutter: the game copies a location you come near
in a single frame on the main thread; this mod has Unity copy it on its worker threads instead.

Flying past a location on fresh land (release build, Ryzen 7 9800X3D, RTX 5080, 1920×1080, 60 fps cap, so a normal frame is
16.7 ms; mean of 3 runs per setup): the worst frame at the location, and how long from the game asking for the location's files
until it shows.

| Location | Vanilla | AsyncLocations 1.0.0 | AsyncLocations 1.1.0 |
|---|---|---|---|
| Winding Tunnels, Deep North (33,587 objects) | 160 ms · 1.3 s | 69 ms · 1.3 s | 53 ms · 0.7 s |
| Charred Fortress, Ashlands | 133 ms · 2.0 s | 48 ms · 2.0 s | 42 ms · 2.0 s |
| Mistlands guard tower | 64 ms · 0.5 s | 21 ms · 0.5 s | 20 ms · 0.3 s |

Graphs of every run: `AsyncLocations-1.1.0-*.png` on the mod page.

How these numbers are measured (new in 1.1.0): up to 1.0.1 the table came from copying each location on its own in a test and timing the copy call and its activation. Since 1.1.0 every number comes from play instead: the test script flies past each location at 8 m/s on undiscovered land and every frame is recorded.

With the async copy a location shows a few frames (under 0.1 s) after the game's spawn call, at the edge of the loaded area
where nobody sees it. Objects in its zone wait for it with the game's own "location still loading" mechanism, so nothing lands before its floor.

## Install

Copy `AsyncLocations.dll` to `Valheim/BepInEx/plugins/`. Nothing is sent over the network, other players
don't need it. Settings in `BepInEx/config/AsyncLocations.cfg` (both default on; off = the game's own behaviour, live):
`Enabled` (the async copy) and `Faster background loading` (since 1.1.0: location and dungeon room assets load in the
background at Unity's `BelowNormal` instead of the game's `Low`, so they appear sooner, a dungeon's rooms in ~1.8 s instead of ~5.8 at 120 fps).

Tested with Valheim 1.0.20 (Unity 6000.0.75f1), BepInEx 5.4.23. A game update that changes the patched method makes the plugin
log one warning and leave the game alone.

## Compatibility

- Background loading uses the game's own request list (`BackgroundLoadingBudgetController`), which applies the highest request:
  loading screens and other mods using it combine with it.
- Other mods: it patches only `LocationProxy.SpawnLocation`, `ZoneSystem.SpawnLocation` and `ZoneSystem.OnDestroy`. A mod that reads a `LocationProxy`'s instance in the same frame as the
  spawn would see the empty placeholder for a few frames.

How it works, the patches and the measurements: [TECHNICAL.md](https://github.com/vont-bongo/Valheim-AsyncLocations/blob/main/TECHNICAL.md).

## License

MIT (see `LICENSE`): use it, copy it into your own mods, change it.
