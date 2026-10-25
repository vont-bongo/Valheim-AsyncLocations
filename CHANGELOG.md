# Changelog

## 1.0.1
- Fix: a location whose zone unloaded in the exact frame its copy finished (most likely right at a portal, a respawn or another
  loading screen, or when moving fast along the edge of the loaded area) threw an error and left that zone marked as still loading
  until the next login: its objects didn't appear and a teleport into the area never finished. The copy is now dropped cleanly.

## 1.0.0
- First release: locations (ruins, towers, fortresses, camps, dungeon entrances) are copied on Unity's worker threads with
  `Object.InstantiateAsync` instead of in one frame on the main thread. Tested with Valheim 1.0.20 (Unity 6000.0.75f1).
