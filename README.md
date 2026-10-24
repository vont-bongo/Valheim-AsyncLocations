# AsyncLocations

100% Vibe coded.

Valheim runs on Unity 6 since February 2026, which can copy objects on worker threads (`Object.InstantiateAsync`). This BepInEx mod uses it so ruins, towers, fortresses and dungeon entrances load in with noticeably less stutter: the game copies a location you come near
in a single frame on the main thread; this mod has Unity copy it on its worker threads instead.

Main-thread time of a location's spawn frame (release build, measured in-game):

| Location | Vanilla | With AsyncLocations |
|---|---|---|
| Winding Tunnels, Deep North (33,587 objects) | ~155 ms | ~28 ms, plus ~3 ms a few frames later |
| Charred Fortress, Ashlands | ~127 ms | ~25 ms, plus ~2 ms |
| Mistlands towers and lighthouses | 60-68 ms | ~12 ms, plus ~1 ms |
| Small ruins and huts | 1.5-3 ms | 0.3-0.5 ms |

A location now appears 10-20 frames (0.1-0.3 s) after the game asks for it, at the edge of the loaded area where nobody sees
it. Objects in its zone wait for it with the game's own "location still loading" mechanism, so nothing lands before its floor.

## Install

Copy `AsyncLocations.dll` to `Valheim/BepInEx/plugins/`. Nothing is sent over the network, other players
don't need it. Setting: `BepInEx/config/AsyncLocations.cfg`, `Enabled` (default on; off = the
game's own behaviour, live).

Tested with Valheim 1.0.20 (Unity 6000.0.75f1), BepInEx 5.4.23. A game update that changes the patched method makes the plugin
log one warning and leave the game alone.

## Compatibility

- Other mods: it patches only `LocationProxy.SpawnLocation`, `ZoneSystem.SpawnLocation` and `ZoneSystem.OnDestroy`. A mod that reads a `LocationProxy`'s instance in the same frame as the
  spawn would see the empty placeholder for a few frames.

How it works, the patches and the measurements: [TECHNICAL.md](https://github.com/vont-bongo/Valheim-AsyncLocations/blob/main/TECHNICAL.md).

## License

MIT (see `LICENSE`): use it, copy it into your own mods, change it.
