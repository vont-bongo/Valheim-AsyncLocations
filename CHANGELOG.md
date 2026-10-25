# Changelog

## 1.1.0
- New setting `Faster background loading` (default on): while you play, the game loads location and dungeon room assets in the
  background at Unity's lowest priority (`Low`; it asks for more only behind loading screens). The mod adds one standing request
  for `BelowNormal` through the game's own loading-budget API. Measured at 120 fps: large locations load about a third sooner
  (317 -> 217 ms), the 16 Morkhalla rooms of a dungeon in about 1.8 s instead of 5.8, with frames as smooth as at `Low`. Loading
  screens keep the game's own higher setting; other mods that use the same API combine with it. Off = the game's own behaviour.
- New measurements: the README's table and the graphs now come from distance along the test route instead of frame timing, for better accuracy.

## 1.0.1
- Fix: a location whose zone unloaded in the exact frame its copy finished (most likely right at a portal, a respawn or another
  loading screen, or when moving fast along the edge of the loaded area) threw an error and left that zone marked as still loading
  until the next login: its objects didn't appear and a teleport into the area never finished. The copy is now dropped cleanly.

## 1.0.0
- First release: locations (ruins, towers, fortresses, camps, dungeon entrances) are copied on Unity's worker threads with
  `Object.InstantiateAsync` instead of in one frame on the main thread. Tested with Valheim 1.0.20 (Unity 6000.0.75f1).
