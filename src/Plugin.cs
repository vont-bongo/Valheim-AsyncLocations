using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using SoftReferenceableAssets;
using UnityEngine;

namespace AsyncLocations;

// Locations copied off the main thread with Unity 6's Object.InstantiateAsync (Valheim 1.0.20, Unity 6000.0.75).
//
// Cause: every location you come near (ruins, towers, fortresses, dungeon entrances; visited or new land) is copied from its
// prefab in one frame: LocationProxy.SpawnLocation -> ZoneSystem.SpawnProxyLocation -> ZoneSystem.SpawnLocation (SpawnMode.Client)
// -> SoftReferenceableAssets.Utils.Instantiate(location.m_prefab, pos, rot), a plain Object.Instantiate of the whole hierarchy:
// ~155 ms for TheHole01 (33,587 transforms), ~125 ms for fortresses, ~65 ms for Mistlands towers, nearly all Unity's native copy.
//
// Fix: InstantiateAsync takes its source at the call and copies on worker threads, so the game's prepared prefab can be restored
// at once. A transpiler swaps that one Utils.Instantiate call for Spawn: inside LocationProxy.SpawnLocation, outside a loading
// screen, Spawn starts the async copy (prefab root inactive for the call, so the copy arrives inactive and no Awake runs early),
// returns an empty placeholder so the rest of the game's code runs unchanged, and marks the zone loading with the game's own
// SetLoadingInZone (objects of that zone wait for the location, as for a location whose bundle is still loading). Update finishes
// done copies: activate, TransferReference (the game's bundle holder), SnapToGround.SnappAll, parent to the proxy, set m_instance,
// destroy the placeholder, LoadFields again, unmark the zone. A proxy unloaded meanwhile discards its copy; a loading screen
// finishes everything at once; leaving the world waits for the workers and destroys what they made.
[BepInPlugin(Guid, Name, Version)]
public sealed class Plugin : BaseUnityPlugin
{
	public const string Guid = "AsyncLocations";

	public const string Name = "AsyncLocations";

	public const string Version = "1.0.1";

	internal static ManualLogSource Log;

	internal static ConfigEntry<bool> Enabled;

	internal static bool On;

	private static bool active;

	private void Awake()
	{
		Log = Logger;
		Enabled = Config.Bind("General", "Enabled", true, "Locations you come near are copied on worker threads and appear a few frames later, instead of being copied in one frame (up to ~155 ms for the largest). Off: the game's own behaviour.");
		On = Enabled.Value;
		Enabled.SettingChanged += (_, __) => On = Enabled.Value;
	}

	// Patched in Start, when every plugin's Awake has run. If another plugin already transpiles ZoneSystem.SpawnLocation (another
	// copy of this fix, or a mod that changes location spawning), this one stands down rather than patch the same call twice.
	private void Start()
	{
		var patches = Harmony.GetPatchInfo(Locations.SpawnLocationMethod);
		if (patches != null && patches.Transpilers.Count > 0)
		{
			Log.LogInfo($"ZoneSystem.SpawnLocation is already transpiled by {string.Join(", ", patches.Transpilers.Select(p => p.owner))}: standing down.");
			return;
		}
		new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
		active = true;
	}

	private void Update()
	{
		if (active)
		{
			Locations.Drain();
		}
	}

	private void OnDestroy()
	{
		if (active)
		{
			Locations.Shutdown();
		}
	}
}

internal static class Locations
{
	internal static readonly MethodBase SpawnLocationMethod = AccessTools.Method(typeof(ZoneSystem), nameof(ZoneSystem.SpawnLocation),
		new[] { typeof(ZoneSystem.ZoneLocation), typeof(int), typeof(Vector3), typeof(Quaternion), typeof(ZoneSystem.SpawnMode), typeof(List<GameObject>), typeof(bool) });

	private sealed class Item
	{
		internal AsyncInstantiateOperation<GameObject> Op;

		internal SoftReference<GameObject> Original;

		internal GameObject Placeholder;

		internal LocationProxy Proxy;

		internal ZDO Zdo;

		// Remembered at the start: ZDOs are pooled, so the proxy's ZDO may be reused by the time the copy is done.
		internal Vector2s Sector;
	}

	private static readonly List<Item> pending = new List<Item>();

	private static readonly List<Item> discard = new List<Item>();

	private static readonly Stopwatch watch = new Stopwatch();

	// At most this much main-thread time per frame for finishing copies (at least one per frame).
	private static readonly long budgetTicks = (long)(2.0 * Stopwatch.Frequency / 1000.0);

	private static LocationProxy proxy;

	private static bool warned;

	private static bool LoadingScreen => ZNetScene.instance == null || ZNetScene.instance.InLoadingScreen();

	private static bool Eligible()
	{
		return Plugin.On && !LoadingScreen && proxy != null && proxy.m_nview != null && proxy.m_nview.IsValid() && ZoneSystem.instance != null;
	}

	// Replaces SoftReferenceableAssets.Utils.Instantiate(original, position, rotation) in ZoneSystem.SpawnLocation.
	internal static GameObject Spawn(SoftReference<GameObject> original, Vector3 position, Quaternion rotation)
	{
		if (!Eligible())
		{
			return SoftReferenceableAssets.Utils.Instantiate(original, position, rotation);
		}
		if (original.Load() != LoadResult.Succeeded)
		{
			original.Release();
			return SoftReferenceableAssets.Utils.Instantiate(original, position, rotation);
		}
		GameObject asset = original.Asset;
		bool was = asset.activeSelf;
		asset.SetActive(false);
		AsyncInstantiateOperation<GameObject> op = Object.InstantiateAsync(asset, position, rotation);
		asset.SetActive(was);
		var placeholder = new GameObject("AsyncLocations_Pending");
		placeholder.transform.SetPositionAndRotation(position, rotation);
		ZDO zdo = proxy.m_nview.GetZDO();
		ZoneSystem.instance.SetLoadingInZone(zdo);
		pending.Add(new Item { Op = op, Original = original, Placeholder = placeholder, Proxy = proxy, Zdo = zdo, Sector = zdo.GetSector() });
		return placeholder;
	}

	// ZoneSystem.UnsetLoadingInZone without asking the (possibly reused) ZDO for its sector.
	private static void Unmark(Item item)
	{
		ZoneSystem zs = ZoneSystem.instance;
		if (zs == null || !zs.m_loadingObjectsInZones.TryGetValue(item.Sector, out List<ZDO> list))
		{
			return;
		}
		list.Remove(item.Zdo);
		if (list.Count == 0)
		{
			zs.m_loadingObjectsInZones.Remove(item.Sector);
		}
	}

	// The proxy is gone: its placeholder destroyed, the proxy destroyed, or the proxy still there with its view already detached from
	// its ZDO (the game's unload resets the view first and destroys the object at the end of the frame). 1.0.0 missed the last case:
	// a copy finishing in the unload's frame (or a loading screen's) threw in LoadFields, and the zone stayed marked loading until
	// the next login (its objects never appeared; a teleport into it never ended).
	private static bool Gone(Item item) => item.Placeholder == null || item.Proxy == null || item.Proxy.m_nview == null || !item.Proxy.m_nview.IsValid();

	private static void Finish(Item item)
	{
		GameObject[] result = item.Op.Result;
		GameObject copy = result != null && result.Length > 0 ? result[0] : null;
		if (copy != null && Gone(item))
		{
			Object.Destroy(copy);
			item.Original.Release();
			Unmark(item);
			if (item.Placeholder != null)
			{
				Object.Destroy(item.Placeholder);
			}
			return;
		}
		if (copy == null)
		{
			if (!warned)
			{
				warned = true;
				Plugin.Log.LogWarning("An async location copy came back empty; that location is missing until the zone reloads.");
			}
			item.Original.Release();
			Unmark(item);
			Object.Destroy(item.Placeholder);
			return;
		}
		// The order of the game's own code: activate at the world root, snap, then the proxy parents it.
		copy.SetActive(true);
		copy.TransferReference(item.Original);
		SnapToGround.SnappAll();
		copy.transform.SetParent(item.Proxy.transform, true);
		item.Proxy.m_instance = copy;
		Object.Destroy(item.Placeholder);
		item.Proxy.m_nview.LoadFields();
		Unmark(item);
	}

	private static void Dispose(Item item)
	{
		GameObject[] result = item.Op.Result;
		if (result != null)
		{
			foreach (GameObject go in result)
			{
				if (go != null)
				{
					Object.Destroy(go);
				}
			}
		}
		item.Original.Release();
	}

	// A loading screen waits for the world anyway: finish every copy now.
	private static void FinishAll()
	{
		foreach (Item item in pending)
		{
			item.Op.WaitForCompletion();
			if (Gone(item))
			{
				Unmark(item);
				discard.Add(item);
			}
			else
			{
				Finish(item);
			}
		}
		pending.Clear();
	}

	internal static void Drain()
	{
		if (pending.Count == 0 && discard.Count == 0)
		{
			return;
		}
		if (pending.Count > 0 && LoadingScreen)
		{
			FinishAll();
		}
		// Proxies unloaded meanwhile (the placeholder is their child): unmark now, destroy the copy when it is done.
		for (int i = pending.Count - 1; i >= 0; i--)
		{
			if (Gone(pending[i]))
			{
				Unmark(pending[i]);
				discard.Add(pending[i]);
				pending.RemoveAt(i);
			}
		}
		watch.Restart();
		for (int i = 0; i < pending.Count;)
		{
			Item item = pending[i];
			if (!item.Op.isDone)
			{
				i++;
				continue;
			}
			pending.RemoveAt(i);
			Finish(item);
			if (watch.ElapsedTicks >= budgetTicks)
			{
				break;
			}
		}
		for (int i = discard.Count - 1; i >= 0; i--)
		{
			if (discard[i].Op.isDone)
			{
				Dispose(discard[i]);
				discard.RemoveAt(i);
			}
		}
	}

	internal static void Shutdown()
	{
		foreach (Item item in pending)
		{
			item.Op.WaitForCompletion();
			Dispose(item);
		}
		foreach (Item item in discard)
		{
			item.Op.WaitForCompletion();
			Dispose(item);
		}
		pending.Clear();
		discard.Clear();
	}

	[HarmonyPatch(typeof(LocationProxy), nameof(LocationProxy.SpawnLocation))]
	private static class Context
	{
		private static void Prefix(LocationProxy __instance)
		{
			proxy = __instance;
		}

		private static void Finalizer()
		{
			proxy = null;
		}
	}

	[HarmonyPatch(typeof(ZoneSystem), "OnDestroy")]
	private static class Leave
	{
		private static void Prefix()
		{
			Shutdown();
		}
	}

	[HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.SpawnLocation), typeof(ZoneSystem.ZoneLocation), typeof(int), typeof(Vector3), typeof(Quaternion),
		typeof(ZoneSystem.SpawnMode), typeof(List<GameObject>), typeof(bool))]
	private static class Copy
	{
		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var original = AccessTools.Method(typeof(SoftReferenceableAssets.Utils), nameof(SoftReferenceableAssets.Utils.Instantiate),
				new[] { typeof(SoftReference<GameObject>), typeof(Vector3), typeof(Quaternion) });
			var replacement = AccessTools.Method(typeof(Locations), nameof(Spawn));
			var matcher = new CodeMatcher(instructions);
			int matches = matcher.Instructions().Count(i => i.Calls(original));
			if (matches != 1)
			{
				Plugin.Log.LogWarning($"Expected one Utils.Instantiate call in ZoneSystem.SpawnLocation, found {matches} (a game update?); the fix is off.");
				return instructions;
			}
			matcher.MatchStartForward(new CodeMatch(i => i.Calls(original)));
			matcher.Set(OpCodes.Call, replacement);
			return matcher.InstructionEnumeration();
		}
	}
}
