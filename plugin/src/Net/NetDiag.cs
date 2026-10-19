using System.Linq;
using FishNet;
using UnityEngine;

namespace Straft4Dead.Net
{
    /// <summary>Phase 8 recon: what STRAFTAT's FishNet setup looks like at runtime (NetworkManager object, its
    /// components, the active transport, every Transport in the scene).</summary>
    internal static class NetDiag
    {
        internal static void Log(string when)
        {
            var nm = InstanceFinder.NetworkManager;
            if (nm == null) { Plugin.Logger.LogInfo($"[net] {when}: no NetworkManager"); return; }
            var tm = nm.TransportManager;
            Plugin.Logger.LogInfo($"[net] {when}: NetworkManager '{nm.gameObject.name}' (scene '{nm.gameObject.scene.name}'), server {nm.IsServer} client {nm.IsClient} offline {nm.IsOffline}");
            Plugin.Logger.LogInfo($"[net]   components: {string.Join(", ", nm.GetComponents<Component>().Select(c => c.GetType().FullName + (c is Behaviour b && !b.enabled ? " (off)" : "")))}");
            Plugin.Logger.LogInfo($"[net]   active transport: {(tm != null && tm.Transport != null ? tm.Transport.GetType().FullName : "none")}");
            foreach (var t in Resources.FindObjectsOfTypeAll<FishNet.Transporting.Transport>())
                Plugin.Logger.LogInfo($"[net]   transport object: {t.GetType().FullName} on '{t.gameObject.name}' (scene '{t.gameObject.scene.name}')");
            Plugin.Logger.LogInfo($"[net]   command line: {string.Join(" ", System.Environment.GetCommandLineArgs().Skip(1))}");
        }
    }
}
