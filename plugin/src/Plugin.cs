using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
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
        internal static PluginConfig Settings;

        private void Awake()
        {
            Logger = base.Logger;
            Settings = new PluginConfig(Config);

            Logger.LogInfo($"{MyPluginInfo.PLUGIN_NAME} {MyPluginInfo.PLUGIN_VERSION} loaded (Unity {Application.unityVersion}, enabled={Settings.Enabled.Value})");
            if (!Settings.Enabled.Value)
                return;

            // The BepInEx manager object doesn't survive STRAFTAT's startup (sceneLoaded never reached it), so
            // everything runs on our own persistent, hidden object instead of this component.
            new Harmony(MyPluginInfo.PLUGIN_GUID).PatchAll(typeof(Plugin).Assembly);

            var runtime = new GameObject("Straft4Dead.Runtime") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(runtime);
            runtime.AddComponent<Runtime>();
            runtime.AddComponent<Dev.DevTools>();
            runtime.AddComponent<Hud.L4D2Hud>();
            runtime.AddComponent<Zombies.InfectedAssets>();
            runtime.AddComponent<Survivors.SurvivorAssets>();
            Dev.DevTools.LogMapScenes();
        }
    }

    internal sealed class Runtime : MonoBehaviour
    {
        private bool _phase0Started;

        private void Awake()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void Start()
        {
            // The chainloader can start after the first scene is already up.
            var active = SceneManager.GetActiveScene();
            if (active.isLoaded && !string.IsNullOrEmpty(active.name))
                OnSceneLoaded(active, LoadSceneMode.Single);
        }

        private float _nextSurvivorScan;

        private void Update()
        {
            // Players (and their first-person arms) spawn per take; look for new ones once a second.
            if (Time.unscaledTime < _nextSurvivorScan || !Plugin.Settings.Enabled.Value) return;
            _nextSurvivorScan = Time.unscaledTime + 1f;
            Survivors.SurvivorArms.AttachAll();
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Plugin.Logger.LogWarning("Runtime object destroyed");
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            var cfg = Plugin.Settings;
            GetComponent<Dev.DevTools>().OnSceneLoaded(scene);

            // A map: anything but the menu and the victory screen (SceneMotor's scene names).
            if (scene.name != "MainMenu" && scene.name != "VictoryScene" && !string.IsNullOrEmpty(scene.name))
            {
                var dir = new GameObject("S4D_ZombieDirector");
                SceneManager.MoveGameObjectToScene(dir, scene); // dies with the map
                dir.AddComponent<Zombies.ZombieDirector>();
            }
            if (cfg.Phase0AssetProof.Value && !_phase0Started && scene.name == "MainMenu")
            {
                _phase0Started = true;
                var proof = new GameObject("S4D_Phase0Proof").AddComponent<Phase0.AssetProof>();
                proof.BundlePath = cfg.Phase0BundlePath.Value;
                proof.VoicePath = cfg.Phase0VoiceLine.Value;
                proof.ScreenshotPath = System.IO.Path.Combine(Paths.BepInExRootPath, "s4d_phase0.png");
            }

            if (!cfg.LogDiagnostics.Value)
                return;

            // Spawn roots are tagged "Spawnpoints" (1v1) and "Spawnpoints4Player" (2v2/FFA); see PlayerManager.PopulateSpawnPoints.
            int spawns1 = CountSpawns("Spawnpoints");
            int spawns4 = CountSpawns("Spawnpoints4Player");
            NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
            Plugin.Logger.LogInfo($"[diag] scene '{scene.name}' ({mode}): spawns 1v1={spawns1} 4p={spawns4}, navmesh verts={tri.vertices.Length}");
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
