# Changelog

## 1.0.0
- First release: locations (ruins, towers, fortresses, camps, dungeon entrances) are copied on Unity's worker threads with
  `Object.InstantiateAsync` instead of in one frame on the main thread. Tested with Valheim 1.0.20 (Unity 6000.0.75f1).
