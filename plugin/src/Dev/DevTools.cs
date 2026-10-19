using System.Collections;
using System.IO;
using System.Linq;
using Straft4Dead.Zombies;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Straft4Dead.Dev
{
    /// <summary>Solo test helpers. F8 enters the configured map in STRAFTAT's exploration mode (a private, host-only
    /// lobby: no matchmaking). With Dev.AutoTest the plugin does that by itself from the main menu, checks that a weapon
    /// hit reaches a zombie, and saves screenshots, so a test run needs no hands on the keyboard.</summary>
    internal sealed class DevTools : MonoBehaviour
    {
        private bool _autoStarted;
        private bool _selfTested;

        internal static void LogMapScenes()
        {
            var names = Enumerable.Range(0, SceneManager.sceneCountInBuildSettings)
                .Select(i => Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(i)));
            Plugin.Logger.LogInfo($"[dev] build scenes: {string.Join(", ", names)}");
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f8Key.wasPressedThisFrame)
                EnterTestMap();
        }

        internal void OnSceneLoaded(Scene scene)
        {
            var cfg = Plugin.Settings;
            if (!cfg.DevAutoTest.Value)
                return;
            if (scene.name == "MainMenu" && !_autoStarted)
            {
                _autoStarted = true;
                StartCoroutine(AutoEnter());
            }
            else if (scene.name == cfg.DevTestMap.Value && !_selfTested)
            {
                _selfTested = true;
                var mode = Plugin.Settings.DevAutoTestMode.Value;
                StartCoroutine(mode == "health" ? HealthTest() : mode == "arms" ? ArmsTest() : SelfTest());
            }
        }

        private IEnumerator AutoEnter()
        {
            yield return new WaitForSeconds(6f);
            EnterTestMap();
        }

        private static void EnterTestMap()
        {
            var map = Plugin.Settings.DevTestMap.Value;
            if (SteamLobby.Instance == null || string.IsNullOrEmpty(map))
                return;
            Plugin.Logger.LogInfo($"[dev] entering '{map}' in exploration mode (private lobby)");
            SteamLobby.Instance.EnterExplorationMap(map);
        }

        /// <summary>Perspective render of a zombie from 3 m in front, at chest height.</summary>
        private static void CloseUp(Transform target, string path)
        {
            var cam = new GameObject("S4D_CloseCam").AddComponent<Camera>();
            Vector3 fwd = target.forward; fwd.y = 0f; if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            var rs = target.GetComponentsInChildren<Renderer>();
            var bounds = rs.Length > 0 ? rs[0].bounds : new Bounds(target.position + Vector3.up, Vector3.one);
            foreach (var r in rs) bounds.Encapsulate(r.bounds);
            Plugin.Logger.LogInfo($"[dev] close-up target bounds {bounds}");
            cam.transform.position = bounds.center + fwd.normalized * 2.4f + Vector3.up * 0.4f;
            cam.transform.LookAt(bounds.center);
            cam.fieldOfView = 55f;
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            RenderTexture.active = null;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(cam.gameObject); Destroy(rt); Destroy(tex);
            Plugin.Logger.LogInfo($"[dev] close-up of {target.name} -> {path}");
        }

        /// <summary>Orthographic top-down render of the map (with the cyan navmesh if Dev.DrawNavMesh) for checking coverage.</summary>
        private static void OverheadShot(string path)
        {
            var verts = UnityEngine.AI.NavMesh.CalculateTriangulation().vertices;
            if (verts.Length == 0) return;
            var b = new Bounds(verts[0], Vector3.zero);
            foreach (var v in verts) b.Encapsulate(v);
            b.Expand(4f);
            var cam = new GameObject("S4D_TopCam").AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = Mathf.Max(b.extents.x, b.extents.z) * 1.05f;
            cam.transform.SetPositionAndRotation(b.center + Vector3.up * (b.extents.y + 50f), Quaternion.Euler(90f, 0f, 0f));
            cam.farClipPlane = b.size.y + 200f;
            var rt = new RenderTexture(1024, 1024, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1024, 1024, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0);
            RenderTexture.active = null;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(cam.gameObject); Destroy(rt); Destroy(tex);
            Plugin.Logger.LogInfo($"[dev] top-down shot of {b.size.x:F0}x{b.size.z:F0} m -> {path}");
        }

        /// <summary>Phase 2 test: walk the local survivor through damage, pills, a medkit and an incap, with a screenshot
        /// at each step. Run it with [Zombies] Enabled = false so nothing else changes the numbers.</summary>
        private IEnumerator HealthTest()
        {
            string dir = BepInEx.Paths.BepInExRootPath;
            Survivors.SurvivorHealth me = null;
            for (int i = 0; i < 40 && (me = Survivors.SurvivorHealth.Local) == null; i++)
                yield return new WaitForSeconds(0.5f);
            if (me == null)
            {
                Plugin.Logger.LogWarning("[dev] health test: no local survivor");
                yield break;
            }
            yield return new WaitForSeconds(4f);
            void Log(string step) => Plugin.Logger.LogInfo($"[dev] health test {step}: perm {me.Permanent:F1} temp {me.Temporary:F1} game {GameAccess.ToL4D2(me.Player.sync___get_value_health()):F1} medkits {me.Medkits} pills {me.Pills} incap {me.Incapacitated}");
            Log("start");
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "s4d_phase2_a_full.png"));
            yield return new WaitForSeconds(1f);

            GameAccess.DamagePlayer(me.Player, 70f, "test");
            yield return new WaitForSeconds(0.5f);
            Log("after 70 damage");
            me.TakePills();
            yield return new WaitForSeconds(1f);
            Log("after pills");
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "s4d_phase2_b_pills.png"));
            yield return new WaitForSeconds(0.5f);

            GameAccess.DamagePlayer(me.Player, 20f, "test");
            yield return new WaitForSeconds(0.5f);
            Log("after 20 damage (temp first)");

            float t0 = Time.time;
            while (Time.time - t0 < Plugin.Settings.MedkitSeconds.Value + 0.5f)
            {
                me.SetHealing(true);
                if (Time.time - t0 > 2.5f && Time.time - t0 < 2.6f)
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir, "s4d_phase2_c_healing.png"));
                yield return null;
            }
            me.SetHealing(false);
            Log("after medkit");
            yield return new WaitForSeconds(0.5f);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "s4d_phase2_d_healed.png"));
            yield return new WaitForSeconds(0.5f);

            GameAccess.DamagePlayer(me.Player, 500f, "test");
            yield return new WaitForSeconds(1.2f);
            Log("after lethal damage");
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "s4d_phase2_e_incap.png"));
            yield return new WaitForSeconds(Plugin.Settings.IncapSeconds.Value + 1.5f);
            Plugin.Logger.LogInfo($"[dev] health test end: player killed={me != null && me.Player != null && me.Player.sync___get_value_isKilled()}");
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "s4d_phase2_f_after.png"));
        }

        /// <summary>Phase 4 recon and test: dump the local player's first-person arms rig (hierarchy, bones, renderers,
        /// the camera that draws them) to the log, then screenshot what the player sees.</summary>
        private IEnumerator ArmsTest()
        {
            string dir = BepInEx.Paths.BepInExRootPath;
            PlayerSetup setup = null;
            for (int i = 0; i < 40 && setup == null; i++)
            {
                var fpc = FirstPersonController.instance;
                if (fpc != null) setup = fpc.GetComponentInParent<PlayerSetup>() ?? fpc.GetComponentInChildren<PlayerSetup>();
                if (setup == null) yield return new WaitForSeconds(0.5f);
            }
            if (setup == null) { Plugin.Logger.LogWarning("[dev] arms test: no local PlayerSetup"); yield break; }
            yield return new WaitForSeconds(3f);
            var arms = HarmonyLib.Traverse.Create(setup).Field("fpArms").GetValue<GameObject[]>() ?? new GameObject[0];
            var suits = HarmonyLib.Traverse.Create(setup).Field("fpArmsSuits").GetValue<GameObject[]>() ?? new GameObject[0];
            Plugin.Logger.LogInfo($"[dev] arms: fpArms [{string.Join(", ", arms.Select(a => a ? a.name + (a.activeInHierarchy ? "*" : "") : "null"))}] suits [{string.Join(", ", suits.Select(a => a ? a.name + (a.activeInHierarchy ? "*" : "") : "null"))}]");
            foreach (var fa in FindObjectsOfType<FPArms>())
                Plugin.Logger.LogInfo($"[dev] arms: FPArms on {TPath(fa.transform)} animator {(fa.GetComponentInChildren<Animator>() ? fa.GetComponentInChildren<Animator>().runtimeAnimatorController?.name : "none")}");
            var roots = arms.Where(a => a).Select(a => a.transform).ToList();
            foreach (var fa in FindObjectsOfType<FPArms>()) if (!roots.Contains(fa.transform)) roots.Add(fa.transform);
            foreach (var root in roots)
            {
                Plugin.Logger.LogInfo($"[dev] arms tree {TPath(root)}:");
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    Plugin.Logger.LogInfo($"[dev]   {Depth(t, root)}{t.name} L{t.gameObject.layer} {(t.gameObject.activeInHierarchy ? "" : "(off) ")}lp {t.localPosition:F3} lr {t.localEulerAngles:F0} ls {t.localScale:F2} wp {t.position:F3}");
                foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    Plugin.Logger.LogInfo($"[dev]   SMR {r.name} on={r.enabled && r.gameObject.activeInHierarchy} mats [{string.Join(", ", r.sharedMaterials.Select(m => m ? m.name + "/" + m.shader.name : "null"))}] root {r.rootBone?.name} bones [{string.Join(", ", r.bones.Select(b => b ? b.name : "null"))}]");
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                    Plugin.Logger.LogInfo($"[dev]   MR {r.name} on={r.enabled && r.gameObject.activeInHierarchy}");
            }
            foreach (var cam in FindObjectsOfType<Camera>())
                Plugin.Logger.LogInfo($"[dev] camera {TPath(cam.transform)} depth {cam.depth} mask {cam.cullingMask:X} fov {cam.fieldOfView:F0} near {cam.nearClipPlane}");
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "s4d_phase4_arms.png"));
            yield return new WaitForSeconds(1f);
            var sa = FindObjectOfType<Survivors.SurvivorArms>();
            var fpCam = FindObjectsOfType<Camera>().FirstOrDefault(c => c.name == "WeaponCam");
            if (sa != null && fpCam != null)
            {
                // Same view as the weapon camera but pulled back and wider, so the hanging arms are in frame.
                foreach (var (original, file) in new[] { (true, "s4d_phase4_arms_straftat.png"), (false, "s4d_phase4_arms_l4d2.png") })
                {
                    sa.ShowOriginal(original);
                    var cam = new GameObject("S4D_ArmsCam").AddComponent<Camera>();
                    cam.CopyFrom(fpCam);
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(0.25f, 0.27f, 0.3f);
                    cam.transform.position = fpCam.transform.position - fpCam.transform.forward * 1.2f + fpCam.transform.up * 0.3f;
                    cam.transform.LookAt(sa.HandsCenter);
                    cam.fieldOfView = 70f;
                    var rt = new RenderTexture(1280, 720, 24);
                    cam.targetTexture = rt;
                    cam.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                    RenderTexture.active = null;
                    File.WriteAllBytes(System.IO.Path.Combine(dir, file), tex.EncodeToPNG());
                    Destroy(cam.gameObject); Destroy(rt); Destroy(tex);
                }
                sa.ShowOriginal(false);
                Plugin.Logger.LogInfo("[dev] arms test: comparison renders written");
            }

            // Hold a gun: put a weapon lying in the map in front of the camera and let PlayerPickup grab it.
            var pickup = setup.GetComponentInChildren<PlayerPickup>() ?? setup.GetComponentInParent<PlayerPickup>();
            var gun = FindObjectsOfType<Weapon>().Where(w => w.gameObject.scene.IsValid() && !(w is MeleeWeapon))
                .OrderBy(w => Vector3.Distance(w.transform.position, setup.transform.position)).FirstOrDefault();
            var view = fpCam != null ? fpCam.transform.parent : null;
            Plugin.Logger.LogInfo($"[dev] arms test: pickup {(pickup ? "ok" : "none")}, nearest map weapon {(gun ? gun.name : "none")}");
            if (pickup != null && gun != null && view != null)
            {
                var rb = gun.GetComponent<Rigidbody>();
                if (rb) { rb.velocity = Vector3.zero; rb.isKinematic = true; }
                gun.transform.position = view.position + view.forward * 1.2f;
                yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate();
                HarmonyLib.Traverse.Create(pickup).Method("RightHandPickup").GetValue();
                yield return new WaitForSeconds(2f);
                Plugin.Logger.LogInfo($"[dev] arms test: holding {HarmonyLib.Traverse.Create(pickup).Field("weaponInHand").GetValue<Weapon>()?.name ?? "nothing"}");
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "s4d_phase4_arms_gun.png"));
                yield return new WaitForSeconds(1f);
            }
            Plugin.Logger.LogInfo("[dev] arms test: screenshot s4d_phase4_arms.png");
        }

        private static string TPath(Transform t) => t.parent == null ? t.name : TPath(t.parent) + "/" + t.name;
        private static string Depth(Transform t, Transform root) { int d = 0; for (var p = t; p != root && p != null; p = p.parent) d++; return new string(' ', d * 2); }

        /// <summary>Wait for zombies to close in, screenshot, then fire a real weapon's hit path at one to prove the
        /// damage hook (Weapon.TriggerEnvironment), and screenshot again.</summary>
        private IEnumerator SelfTest()
        {
            string dir = BepInEx.Paths.BepInExRootPath;
            yield return new WaitForSeconds(Plugin.Settings.DevScreenshotDelay.Value);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "s4d_phase1_a.png"));
            OverheadShot(Path.Combine(dir, "s4d_phase1_top.png"));
            Plugin.Logger.LogInfo($"[dev] screenshot a; zombies alive {Zombie.Alive}");

            var weapon = Resources.FindObjectsOfTypeAll<Weapon>().FirstOrDefault(w => w.damage > 0f && !(w is MeleeWeapon));
            var zombie = FindObjectsOfType<Zombie>().FirstOrDefault(z => !z.IsDead);
            if (weapon == null || zombie == null)
            {
                Plugin.Logger.LogWarning($"[dev] self-test skipped: weapon={(weapon ? weapon.name : "none")} zombie={(zombie ? zombie.name : "none")}");
                yield break;
            }
            // Real shots reach TriggerEnvironment only if the gun's environment raycast mask includes the zombie layer.
            foreach (var w in Resources.FindObjectsOfTypeAll<Weapon>().GroupBy(w => w.GetType()).Select(g => g.First()))
                Plugin.Logger.LogInfo($"[dev] {w.GetType().Name} '{w.name}': environment mask includes zombie layer = {((w.defaultLayer.value >> Zombie.Layer) & 1) == 1}, player mask {w.playerLayer.value:X}");
            var head = zombie.GetComponentsInChildren<Collider>().First(c => c.name == Zombie.HeadName).gameObject;
            CloseUp(zombie.transform, Path.Combine(dir, "s4d_phase3_closeup.png"));
            for (int i = 0; i < 4 && !zombie.IsDead; i++)
            {
                Plugin.Logger.LogInfo($"[dev] self-test: {weapon.name} (damage {weapon.damage}, head x{weapon.headMultiplier}) -> {zombie.name} head");
                weapon.TriggerEnvironment(head, head.transform.position, Vector3.back, Vector3.forward);
                yield return new WaitForSeconds(0.3f);
            }
            Plugin.Logger.LogInfo($"[dev] self-test result: {zombie.name} dead={zombie.IsDead}");
            yield return new WaitForSeconds(Plugin.Settings.DevScreenshotDelay.Value);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "s4d_phase1_b.png"));
            Plugin.Logger.LogInfo($"[dev] screenshot b; zombies alive {Zombie.Alive}, kills {ZombieDirector.Kills}");
        }
    }
}
