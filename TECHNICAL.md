# AsyncLocations: how it works

**The cost.** `LocationProxy.SpawnLocation` -> `ZoneSystem.SpawnProxyLocation` -> `ZoneSystem.SpawnLocation(..., SpawnMode.Client, ...)`
-> `SoftReferenceableAssets.Utils.Instantiate(location.m_prefab, pos, rot)`: a plain `Object.Instantiate` of the whole
hierarchy. Unity's profiler markers show it is almost all the engine's native copy (`Instantiate.Copy`/`Produce`), not scripts.

Valheim 1.0 runs on Unity 6, which has `Object.InstantiateAsync` (it survives the game's DLL stripping). It
captures the source at the call: changes to the source right after the call are not in the copy. So the game's prepared
prefab can be restored immediately, exactly as vanilla does. Async copies are identical to synchronous ones (transforms, names,
active flags, local transforms, components).

**The patches** (`src/Plugin.cs`, about 300 lines):
1. `LocationProxy.SpawnLocation`: prefix and finalizer remember the proxy, so only proxy spawns are changed.
2. `ZoneSystem.SpawnLocation` (private, 7 arguments): a transpiler replaces its one call to
   `Utils.Instantiate(SoftReference<GameObject>, Vector3, Quaternion)` with `Spawn` (unchanged if it doesn't match exactly once).
3. `Spawn`: `original.Load()`; `asset.SetActive(false)`; `InstantiateAsync(asset, pos, rot)`; `asset.SetActive(wasActive)` (the copy
   arrives inactive, no `Awake` runs early). It returns an empty placeholder, so the rest of the game's code runs unchanged, and calls
   `ZoneSystem.SetLoadingInZone(proxyZdo)`.
4. Every frame, for a finished copy: `SetActive(true)`, `TransferReference(original)` (the game's asset bundle holder),
   `SnapToGround.SnappAll()`, parent to the proxy, `proxy.m_instance = copy`, destroy the placeholder, `proxy.m_nview.LoadFields()`
   again (ZDO-stored field values reach the copy), and remove the zone's loading entry.

**Edge cases handled:** behind a loading screen the game's own path is used and pending copies finish at once; a proxy
unloaded mid-copy discards its copy, releases the reference and unmarks the zone; the zone's sector is remembered at the start
(ZDOs are pooled and may be reused); leaving the world (`ZoneSystem.OnDestroy`) waits for pending copies and destroys them.

## Measured

In the real game (release build, Valheim 1.0.20):
- Async copies are identical to the game's own (36 of 36 compared: every transform's name, active flag, local position,
  rotation, scale and components).
- Spawn frame on the main thread: see the table in the README (Winding Tunnels (TheHole01) ~155 -> ~28 ms).
- Flying the same route over visited land, on vs off: location frames dropped from 22-24 ms to 5-6 ms.

## Build

`dotnet build -c Release -p:ValheimDir="<your Valheim folder with BepInEx>"` (the .NET SDK; the publicizer package comes from
NuGet). The DLL is in `bin/Release/netstandard2.1/`.
