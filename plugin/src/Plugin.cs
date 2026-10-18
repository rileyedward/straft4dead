using BepInEx;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Straft4Dead
{
    // BepInEx finds this class by its [BepInPlugin] attribute and calls Awake() once at game start.
    //
    // Deliberately NOT marked [StraftatMod(isVanillaCompatible: true)]: STRAFTAT's AssemblyScanner hashes every
    // non-vanilla-compatible plugin into its matchmaking key, which keeps modded clients out of vanilla matchmaking.
    // See docs/systems-map.md, "Modding and matchmaking".
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        internal static new ManualLogSource Logger;

        private PluginConfig _config;
        private bool _phase0Started;

        private void Awake()
        {
            Logger = base.Logger;
            _config = new PluginConfig(Config);

            Logger.LogInfo($"{MyPluginInfo.PLUGIN_NAME} {MyPluginInfo.PLUGIN_VERSION} loaded (Unity {Application.unityVersion}, enabled={_config.Enabled.Value})");
            if (!_config.Enabled.Value)
                return;

            SceneManager.sceneLoaded += OnSceneLoaded;
            // The chainloader starts after the first scene is already up, so handle it once here.
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (_config.Phase0AssetProof.Value && !_phase0Started && scene.name == "MainMenu")
            {
                _phase0Started = true;
                var proof = new GameObject("S4D_Phase0Proof").AddComponent<Phase0.AssetProof>();
                proof.BundlePath = _config.Phase0BundlePath.Value;
                proof.VoicePath = _config.Phase0VoiceLine.Value;
                proof.ScreenshotPath = System.IO.Path.Combine(Paths.BepInExRootPath, "s4d_phase0.png");
            }

            if (!_config.LogDiagnostics.Value)
                return;

            // Spawn roots are tagged "Spawnpoints" (1v1) and "Spawnpoints4Player" (2v2/FFA); see PlayerManager.PopulateSpawnPoints.
            int spawns1 = CountSpawns("Spawnpoints");
            int spawns4 = CountSpawns("Spawnpoints4Player");
            NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
            Logger.LogInfo($"[diag] scene '{scene.name}' ({mode}): spawns 1v1={spawns1} 4p={spawns4}, navmesh verts={tri.vertices.Length}");
        }

        private static int CountSpawns(string tag)
        {
            GameObject root;
            try { root = GameObject.FindGameObjectWithTag(tag); }
            catch (UnityException) { return -1; } // tag not defined in this build
            return root == null ? 0 : root.GetComponentsInChildren<SpawnPoint>(true).Length;
        }
    }
}
