using System;
using System.Collections;
using System.Linq;
using FishNet;
using FishNet.Managing;
using FishNet.Managing.Transporting;
using FishNet.Transporting;
using FishNet.Transporting.Tugboat;
using HarmonyLib;
using UnityEngine;

namespace Straft4Dead.Net
{
    /// <summary>Dev only: two STRAFTAT copies on one PC talking over a local UDP connection instead of Steam
    /// (a Steam account can't connect to itself). Enabled only by the command line, never by config:
    ///   host:   STRAFTAT.exe --s4d-lan host     (enters [Dev] TestMap in exploration mode as usual)
    ///   client: STRAFTAT.exe --s4d-lan client   (from tools/dev/make_client_instance.ps1's copy; joins 127.0.0.1)
    /// STRAFTAT drives its FishySteamworks transport directly from SteamLobby, so in LAN mode those calls are
    /// forwarded to a Tugboat transport that is made the NetworkManager's transport. STRAFTAT itself already has a
    /// non-Steam path for this (ClientInstance.nonSteamworksTransport).</summary>
    [HarmonyPatch]
    internal static class DevNet
    {
        internal static readonly string Role = ReadRole();
        internal static bool Lan => Role != null;
        internal static bool IsClientRole => Role == "client";

        private static string ReadRole()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "--s4d-lan");
            return i >= 0 && i + 1 < args.Length ? args[i + 1].ToLowerInvariant() : null;
        }

        /// <summary>LAN mode is set up (or not in use): the host may enter a map.</summary>
        internal static bool Ready => !Lan || Active is Tugboat;

        private static Transport Active => InstanceFinder.TransportManager != null ? InstanceFinder.TransportManager.Transport : null;

        /// <summary>The first MainMenu's NetworkManager woke before our patches; STRAFTAT's own reload makes a fresh one
        /// that our Awake prefix gives Tugboat.</summary>
        private static bool _clientRunning;

        internal static IEnumerator OnMainMenu()
        {
            if (!Lan) yield break;
            yield return new WaitForSeconds(1f);
            if (!(Active is Tugboat))
            {
                Plugin.Logger.LogInfo($"[lan] role {Role}: reloading the NetworkManager with a Tugboat transport");
                Traverse.Create(SteamLobby.Instance).Method("ReloadNetworkManager").GetValue(); // private
                yield break;
            }
            Plugin.Logger.LogInfo($"[lan] role {Role}: transport {Active.GetType().Name} port {((Tugboat)Active).GetPort()}");
            if (!IsClientRole || _clientRunning) yield break;
            _clientRunning = true;
            // Client: keep trying the host until it's up.
            while (InstanceFinder.ClientManager != null && !InstanceFinder.ClientManager.Started)
            {
                Plugin.Logger.LogInfo("[lan] client: connecting to 127.0.0.1");
                InstanceFinder.ClientManager.StartConnection("127.0.0.1");
                yield return new WaitForSeconds(5f);
            }
            Plugin.Logger.LogInfo("[lan] client: connected");
            // Dev evidence from the client copy: what it sees after joining (DevTools.LanHostTest drives the host side).
            for (int i = 1; i <= 24; i++)
            {
                yield return new WaitForSeconds(2.5f);
                var zs = UnityEngine.Object.FindObjectsOfType<Zombies.Zombie>();
                var sp = UnityEngine.Object.FindObjectsOfType<Zombies.SpecialInfected>();
                var me = Survivors.SurvivorHealth.Local;
                Plugin.Logger.LogInfo($"[lan] client view t={i * 2.5f:F0}s: {zs.Length} zombie puppets ({zs.Count(z => z.IsDead)} dead), {sp.Length} specials, " +
                    $"my health {(me != null ? $"{me.Permanent:F0}+{me.Temporary:F0} incap {me.Incapacitated} revive {me.ReviveProgress:F2}" : "none")}, bile {Zombies.Boomer.LocalBiledUntil > Time.time}, " +
                    $"host survivor {Net.NetSync.SurvivorFor(UnityEngine.Object.FindObjectsOfType<PlayerHealth>().FirstOrDefault(p => !p.IsOwner))}");
                string root = BepInEx.Paths.BepInExRootPath;
                if (i == 2 && zs.Length > 0)
                    Dev.DevTools.CloseUp(zs.OrderBy(z => (z.transform.position - (me != null ? me.Player.transform.position : Vector3.zero)).sqrMagnitude).First().transform, System.IO.Path.Combine(root, "s4d_phase8_client_puppet.png"));
                if (i == 3 || i == 8 || i == 12)
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(root, $"s4d_phase8_client_{i}.png"));
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(NetworkManager), "Awake")]
        private static void NetworkManagerAwake(NetworkManager __instance)
        {
            if (!Lan) return;
            var tm = __instance.GetComponent<TransportManager>();
            var tug = __instance.GetComponent<Tugboat>() ?? __instance.gameObject.AddComponent<Tugboat>();
            if (tm != null) tm.Transport = tug;
            Plugin.Logger.LogInfo("[lan] NetworkManager uses Tugboat");
        }

        /// <summary>FishySteamworks is no longer the NetworkManager's transport, so nothing initialises it, but
        /// SteamLobby still calls into it (SetMaximumClients, Update): initialise it once so those calls are harmless.</summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(NetworkManager), "Awake")]
        private static void NetworkManagerAwakePost(NetworkManager __instance)
        {
            if (!Lan || FishyType == null) return;
            var fishy = __instance.GetComponent(FishyType) as Transport;
            if (fishy == null) return;
            try { fishy.Initialize(__instance, 1); }
            catch (Exception e) { Plugin.Logger.LogWarning("[lan] FishySteamworks init: " + e.Message); }
        }

        [HarmonyPatch]
        private static class SetMaximumClientsPatch
        {
            private static bool Prepare() => Lan && FishyType != null;
            private static System.Reflection.MethodBase TargetMethod() => AccessTools.Method(FishyType, "SetMaximumClients", new[] { typeof(int) });
            private static void Postfix(object __instance, int value)
            {
                var t = Active;
                if (t != null && !ReferenceEquals(t, __instance)) t.SetMaximumClients(Math.Max(value, 4)); // exploration lobbies are 1 player
            }
        }

        /// <summary>STRAFTAT registers players by the SteamID it parses from the connection address (Steam's transport
        /// uses the SteamID as the address). Over Tugboat give each connection a SteamID-shaped address: connection 0
        /// (the host's own client) is the host's real SteamID, the client copy gets the next number up.</summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Tugboat), nameof(Tugboat.GetConnectionAddress))]
        private static void FakeSteamAddress(int connectionId, ref string __result)
        {
            if (!Lan) return;
            ulong me = Steamworks.SteamUser.GetSteamID().m_SteamID;
            __result = (me + (ulong)Math.Max(0, connectionId)).ToString();
        }

        /// <summary>SceneMotor loads a single-player lobby's map only for the host's connection, and decides "single
        /// player" from the Steam lobby's member list, which the LAN client never joins. Load it as a global scene so
        /// the client copy gets it too (FishNet loads global scenes for clients that connect later).</summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(SceneMotor), nameof(SceneMotor.RpcLogic___EnterScene_2943234052))]
        private static bool EnterSceneGlobal(SceneMotor __instance, string sceneName, FishNet.Connection.NetworkConnection conn)
        {
            if (!Lan || conn == null || !conn.IsHost) return true;
            Traverse.Create(__instance).Method("EnterSceneForAll").GetValue();
            var load = new FishNet.Managing.Scened.SceneLoadData(sceneName) { ReplaceScenes = FishNet.Managing.Scened.ReplaceOption.All };
            InstanceFinder.SceneManager.LoadGlobalScenes(load);
            Plugin.Logger.LogInfo($"[lan] loading '{sceneName}' as a global scene");
            return false;
        }

        // --- SteamLobby talks to FishySteamworks directly; forward to the active (Tugboat) transport ---

        private static Type FishyType => AccessTools.TypeByName("FishySteamworks.FishySteamworks");

        [HarmonyPatch]
        private static class StartConnectionPatch
        {
            private static bool Prepare() => Lan && FishyType != null;
            private static System.Reflection.MethodBase TargetMethod() => AccessTools.Method(FishyType, "StartConnection", new[] { typeof(bool) });
            private static bool Prefix(object __instance, bool server, ref bool __result)
            {
                var t = Active;
                if (t == null || ReferenceEquals(t, __instance)) return true;
                if (server && IsClientRole) { __result = false; return false; } // the client copy never hosts
                __result = t.StartConnection(server);
                // Steam's transport is up by the time StartConnection returns and SteamLobby spawns the SceneMotor right
                // after; Tugboat starts on its socket thread, so pump its events until FishNet sees it started.
                if (__result && server)
                    for (int i = 0; i < 200 && !InstanceFinder.ServerManager.Started; i++)
                    {
                        t.IterateIncoming(true);
                        System.Threading.Thread.Sleep(5);
                    }
                Plugin.Logger.LogInfo($"[lan] {(server ? "server" : "client")} start via {t.GetType().Name}: {__result}, server started {InstanceFinder.ServerManager.Started}");
                return false;
            }
        }

        [HarmonyPatch]
        private static class StopConnectionPatch
        {
            private static bool Prepare() => Lan && FishyType != null;
            private static System.Reflection.MethodBase TargetMethod() => AccessTools.Method(FishyType, "StopConnection", new[] { typeof(bool) });
            private static bool Prefix(object __instance, bool server, ref bool __result)
            {
                var t = Active;
                if (t == null || ReferenceEquals(t, __instance)) return true;
                __result = t.StopConnection(server);
                return false;
            }
        }

        [HarmonyPatch]
        private static class SetClientAddressPatch
        {
            private static bool Prepare() => Lan && FishyType != null;
            private static System.Reflection.MethodBase TargetMethod() => AccessTools.Method(FishyType, "SetClientAddress", new[] { typeof(string) });
            private static bool Prefix(object __instance) => Active == null || ReferenceEquals(Active, __instance); // keep Tugboat on localhost
        }

        [HarmonyPatch]
        private static class GetConnectionStatePatch
        {
            private static bool Prepare() => Lan && FishyType != null;
            private static System.Reflection.MethodBase TargetMethod() => AccessTools.Method(FishyType, "GetConnectionState", new[] { typeof(bool) });
            private static bool Prefix(object __instance, bool server, ref LocalConnectionState __result)
            {
                var t = Active;
                if (t == null || ReferenceEquals(t, __instance)) return true;
                __result = t.GetConnectionState(server);
                return false;
            }
        }
    }
}
