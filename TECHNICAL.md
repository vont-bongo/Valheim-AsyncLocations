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
unloaded mid-copy (its placeholder gone, or its view already detached from its ZDO in the unload's frame: 1.0.1) discards its copy, releases the reference and unmarks the zone; the zone's sector is remembered at the start
(ZDOs are pooled and may be reused); leaving the world (`ZoneSystem.OnDestroy`) waits for pending copies and destroys them.

**Background loading (1.1.0, setting `Faster background loading`):** before a location is copied, `LocationProxy` loads its asset
bundle asynchronously, and dungeons load their rooms the same way. In play the game leaves `Application.backgroundLoadingPriority`
at `Low`: `BackgroundLoadingBudgetController.ApplyBudget` sets the highest request or `Low`, and only `SceneLoader` requests
(`Normal`, then `High`, during a loading screen). Unity uses the setting for its loading threads and as the main thread's time slice
per frame for integrating loaded assets (Low 2 ms, BelowNormal 4, Normal 10, High 50). The plugin adds one standing `BelowNormal`
request through `RequestLoadingBudget` while the setting is on (released when off and at shutdown); with nothing loading it costs
nothing. Measured on an i5-12600KF / RTX 5080 PC (120 fps cap, release build; each load released and unloaded before the next), until
loaded / worst frame during the load: the 6 largest locations Low 317 ms / 13-15 ms, BelowNormal 217 / 12-18, Normal 189 / 18-19,
High 185 / 40-59; 16 Morkhalla rooms at once Low 5.8 s / 34, BelowNormal 1.8 s / 32, Normal 1.7 s / 18.5, High 1.7 s / 58. A single
large asset can still take a 30-40 ms frame to integrate at any of these priorities.

## Measured

In the real game (release build, Valheim 1.0.20):
- Async copies are identical to the game's own (36 of 36 compared: every transform's name, active flag, local position,
  rotation, scale and components).
- Spawn frame on the main thread: see the table in the README (Winding Tunnels (TheHole01) ~155 -> ~28 ms).
- The copy's integration (Unity bringing it into the scene, `EarlyUpdate.UpdateAsyncInstantiate`) is one frame of its own when
  the copy is done, whatever `AsyncInstantiateOperation.SetIntegrationTimeMS` is set to (0.5-16 ms tried; default 2): about 40-48 ms
  for TheHole01 and 31-38 ms for Charred Fortress (release build, same PC). The 1.0.0 measurement missed that frame.
- Flying the same route over visited land, on vs off: location frames dropped from 22-24 ms to 5-6 ms.

## Build

`dotnet build -c Release -p:ValheimDir="<your Valheim folder with BepInEx>"` (the .NET SDK; the publicizer package comes from
NuGet). The DLL is in `bin/Release/netstandard2.1/`.
