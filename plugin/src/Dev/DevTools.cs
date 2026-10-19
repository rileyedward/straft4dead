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
                StartCoroutine(cfg.DevAutoTestMode.Value == "maps" ? MapSweep() : AutoEnter());
            }
            else if (scene.name == cfg.DevTestMap.Value && !_selfTested)
            {
                _selfTested = true;
                var mode = Plugin.Settings.DevAutoTestMode.Value;
                StartCoroutine(mode == "health" ? HealthTest() : mode == "arms" ? ArmsTest() : mode == "director" ? DirectorTest() : mode == "specials" ? SpecialsTest() : mode == "panel" ? PanelTest() : SelfTest());
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
            Survivors.SurvivorBody.ForceVisible = true;
            yield return null;
            yield return new WaitForEndOfFrame();
            foreach (var go in FindObjectsOfType<Transform>().Where(t => t.name.Contains("Ragdoll") && t.parent == null))
            {
                var parts = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var ours = FindObjectsOfType<Survivors.SurvivorBody>().FirstOrDefault(b => b.transform == go)?.Body;
                Plugin.Logger.LogInfo($"[dev] ragdoll parts on [{string.Join(", ", parts.Select(r => r.name + "=" + (r.enabled && r.gameObject.activeInHierarchy)))}], survivor body {(ours ? ours.transform.position.ToString() : "none")}, ragdoll bounds {(parts.Length > 0 ? parts[0].bounds.ToString() : "-")}, ours bounds {(ours ? ours.GetComponentInChildren<SkinnedMeshRenderer>().bounds.ToString() + " on=" + ours.GetComponentInChildren<SkinnedMeshRenderer>().enabled + " L" + ours.GetComponentInChildren<SkinnedMeshRenderer>().gameObject.layer : "-")}, cam pos {Camera.main?.transform.position}");
                var an = go.GetComponentInChildren<Animator>(true);
                var hips = go.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "Hips") ?? go;
                Plugin.Logger.LogInfo($"[dev] ragdoll root scale {go.lossyScale}, hips {hips.position}");
                RenderView(hips.position + new Vector3(1.2f, 2.2f, 1.2f), hips.position, System.IO.Path.Combine(dir, "s4d_phase4_ragdoll.png"));
                Plugin.Logger.LogInfo($"[dev] ragdoll {go.name} active {go.gameObject.activeInHierarchy} at {go.position}: animator {(an ? TPath(an.transform) + " avatar " + (an.avatar ? an.avatar.name + " human=" + an.avatar.isHuman : "none") + " enabled " + an.enabled : "none")}, SMRs {go.GetComponentsInChildren<SkinnedMeshRenderer>().Length}, children [{string.Join(", ", Enumerable.Range(0, go.childCount).Select(i => go.GetChild(i).name))}]");
            }
            Survivors.SurvivorBody.ForceVisible = false;
            Plugin.Logger.LogInfo($"[dev] health test end: player killed={me != null && me.Player != null && me.Player.sync___get_value_isKilled()}");
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "s4d_phase2_f_after.png"));
        }

        /// <summary>Map sweep: load each map in [Dev] SweepMaps (or every map) in exploration mode, one after another, and record how the navmesh
        /// bake went (walkable area, triangles, time, how often a spawn point is found around the player) to
        /// BepInEx/s4d_mapsweep.tsv with a top-down shot per map in BepInEx/s4d_sweep/. Resumes where it left off.</summary>
        private IEnumerator MapSweep()
        {
            string root = BepInEx.Paths.BepInExRootPath, tsv = System.IO.Path.Combine(root, "s4d_mapsweep.tsv"), shots = System.IO.Path.Combine(root, "s4d_sweep");
            Directory.CreateDirectory(shots);
            var done = File.Exists(tsv) ? new System.Collections.Generic.HashSet<string>(File.ReadAllLines(tsv).Select(l => l.Split('\t')[0])) : new System.Collections.Generic.HashSet<string>();
            if (!File.Exists(tsv)) File.WriteAllText(tsv, "map\tstatus\tarea_m2\ttriangles\tbake_ms\tspawn_ok_of_20\tplayer_on_navmesh\n");
            var skip = new[] { "MainMenu", "VictoryScene", "EmptyScene", "TutorialScene", "EndGame" };
            var maps = Enumerable.Range(0, SceneManager.sceneCountInBuildSettings)
                .Select(i => System.IO.Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(i)))
                .Where(m => !skip.Contains(m) && !done.Contains(m)).ToList();
            var only = Plugin.Settings.DevSweepMaps.Value.Split(',').Select(m => m.Trim()).Where(m => m.Length > 0).ToList();
            if (only.Count > 0) maps = maps.Where(only.Contains).ToList();
            Plugin.Logger.LogInfo($"[sweep] {maps.Count} maps to go ({done.Count - 1} done)");
            yield return new WaitForSeconds(6f);
            foreach (var map in maps)
            {
                Plugin.Logger.LogInfo($"[sweep] loading {map}");
                SteamLobby.Instance.EnterExplorationMap(map);
                float t0 = Time.realtimeSinceStartup;
                ZombieDirector d = null;
                while (Time.realtimeSinceStartup - t0 < 45f)
                {
                    yield return new WaitForSeconds(1f);
                    d = ZombieDirector.Instance;
                    if (SceneManager.GetActiveScene().name == map && d != null && d.Baked && FirstPersonController.instance != null) break;
                }
                string line;
                if (d == null || !d.Baked || SceneManager.GetActiveScene().name != map)
                    line = $"{map}\tload_timeout\t\t\t\t\t";
                else
                {
                    yield return new WaitForSeconds(1f);
                    var pos = FirstPersonController.instance.transform.position;
                    bool onNav = UnityEngine.AI.NavMesh.SamplePosition(pos, out _, 4f, UnityEngine.AI.NavMesh.AllAreas);
                    int ok = ZombieDirector.SpawnProbe(pos, 20);
                    line = $"{map}\tok\t{d.NavArea:F0}\t{d.NavTriangles}\t{d.BakeMs:F0}\t{ok}\t{onNav}";
                    OverheadShot(System.IO.Path.Combine(shots, map + ".png"));
                }
                File.AppendAllText(tsv, line + "\n");
                Plugin.Logger.LogInfo("[sweep] " + line.Replace('\t', ' '));
            }
            Plugin.Logger.LogInfo("[sweep] done");
        }

        /// <summary>Phase 7 test: open the panel, switch the mode off and on, change survivor and horde size live,
        /// screenshotting each step.</summary>
        private IEnumerator PanelTest()
        {
            string dir = BepInEx.Paths.BepInExRootPath;
            var c = Plugin.Settings;
            var panel = GetComponent<Hud.HostPanel>();
            yield return new WaitForSeconds(8f);
            panel.SetOpen(true);
            yield return new WaitForSeconds(1f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "s4d_phase7_panel.png"));
            yield return new WaitForSeconds(1f);
            panel.SetOpen(false);
            Plugin.Logger.LogInfo($"[dev] panel: mode off (zombies alive {Zombie.Alive})");
            c.Enabled.Value = false;
            yield return new WaitForSeconds(3f);
            Plugin.Logger.LogInfo($"[dev] panel: mode off for 3 s: zombies {Zombie.Alive}, survivor arms {FindObjectsOfType<Survivors.SurvivorArms>().Length}, bodies {FindObjectsOfType<Survivors.SurvivorBody>().Length}");
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "s4d_phase7_off.png"));
            yield return new WaitForSeconds(1f);
            string before = c.SurvivorName.Value;
            c.SurvivorName.Value = "coach";
            c.HordeSize.Value = 20;
            c.Enabled.Value = true;
            yield return new WaitForSeconds(4f);
            Plugin.Logger.LogInfo($"[dev] panel: mode on as coach: arms {FindObjectsOfType<Survivors.SurvivorArms>().Length}, bodies {FindObjectsOfType<Survivors.SurvivorBody>().Length}, zombies {Zombie.Alive}");
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "s4d_phase7_on_coach.png"));
            yield return new WaitForSeconds(1f);
            c.SurvivorName.Value = before;
            c.HordeSize.Value = (int)c.HordeSize.DefaultValue;
            Plugin.Logger.LogInfo("[dev] panel test end");
        }

        /// <summary>Phase 6 test: spawn a Tank, a Boomer and a Witch around the player, render each close up, let them
        /// act (the test survivor has lots of health), then shoot each through the real weapon hit path.</summary>
        private IEnumerator SpecialsTest()
        {
            string dir = BepInEx.Paths.BepInExRootPath;
            yield return new WaitForSeconds(6f);
            var me = Survivors.SurvivorHealth.Local;
            if (me == null || ZombieDirector.Instance == null || !ZombieDirector.Instance.Baked) { Plugin.Logger.LogWarning("[dev] specials test: not ready"); yield break; }
            Vector3 at = me.Player.transform.position;
            Vector3 Near(float d)
            {
                for (int i = 0; i < 40; i++)
                {
                    var v = Random.insideUnitCircle.normalized * d;
                    if (UnityEngine.AI.NavMesh.SamplePosition(at + new Vector3(v.x, 0f, v.y), out var h, 3f, UnityEngine.AI.NavMesh.AllAreas)) return h.position;
                }
                return at;
            }
            var witch = Witch.Create(Near(16f));
            var boomer = Boomer.Create(Near(14f));
            var tank = Tank.Create(Near(22f));
            yield return new WaitForSeconds(2.5f);
            CloseUp(tank.transform, System.IO.Path.Combine(dir, "s4d_phase6_tank.png"));
            CloseUp(boomer.transform, System.IO.Path.Combine(dir, "s4d_phase6_boomer.png"));
            CloseUp(witch.transform, System.IO.Path.Combine(dir, "s4d_phase6_witch.png"));
            for (int t = 0; t < 8; t++)
            {
                yield return new WaitForSeconds(2f);
                Plugin.Logger.LogInfo($"[dev] specials t={t * 2 + 2}s: tank {(tank ? Vector3.Distance(tank.transform.position, me.Player.transform.position).ToString("F1") + " m" : "gone")}, boomer {(boomer && !boomer.IsDead ? Vector3.Distance(boomer.transform.position, me.Player.transform.position).ToString("F1") + " m" : "gone")}, witch rage {(witch ? witch.Rage.ToString("F2") : "gone")}, rocks {FindObjectsOfType<TankRock>().Length}, bile {Boomer.LocalBiledUntil > Time.time}, survivor {me.Permanent + me.Temporary:F0} HP incap {me.Incapacitated}");
                if (t == 3) ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "s4d_phase6_view.png"));
            }
            var weapon = Resources.FindObjectsOfTypeAll<Weapon>().FirstOrDefault(w => w.name.StartsWith("AR15")) ?? Resources.FindObjectsOfTypeAll<Weapon>().First(w => w.damage > 0f && !(w is MeleeWeapon));
            foreach (var target in new SpecialInfected[] { boomer, witch, tank })
            {
                if (target == null || target.IsDead) continue;
                var head = target.GetComponentsInChildren<Collider>().First(c => c.name == Zombie.HeadName).gameObject;
                int shots = 0;
                while (!target.IsDead && shots < 60) { weapon.TriggerEnvironment(head, head.transform.position, Vector3.back, Vector3.forward); shots++; yield return new WaitForSeconds(0.05f); }
                Plugin.Logger.LogInfo($"[dev] specials: {target.GetType().Name} dead={target.IsDead} after {shots} {weapon.name} headshots");
            }
            yield return new WaitForSeconds(3f);
            Plugin.Logger.LogInfo("[dev] specials test end");
        }

        /// <summary>Phase 5 test: stand still and let the director run (wanderers, a horde, the calm, a new take after
        /// the survivor goes down), logging its state every few seconds with screenshots during the horde.</summary>
        private IEnumerator DirectorTest()
        {
            string dir = BepInEx.Paths.BepInExRootPath;
            bool shot = false;
            // Which guns one-shot a common (body / head)?
            float zhp = Plugin.Settings.ZombieHealth.Value, mult = Plugin.Settings.WeaponDamageToZombies.Value;
            var guns = Resources.FindObjectsOfTypeAll<Weapon>().Where(w => w.damage > 0f).GroupBy(w => w.name.Replace("(Clone)", "")).Select(g => g.First()).OrderBy(w => w.damage).ToList();
            Plugin.Logger.LogInfo($"[dev] weapons vs {zhp:F0} HP commons: " + string.Join(", ", guns.Select(w =>
                $"{w.name.Replace("(Clone)", "")} {GameAccess.ToL4D2(w.damage) * mult:F0}/{GameAccess.ToL4D2(w.damage * w.headMultiplier) * mult:F0}{(GameAccess.ToL4D2(w.damage) * mult >= zhp ? " 1-shot" : "")}")));
            for (float t = 0f; t < 75f; t += 3f)
            {
                // Stand in for a player fighting back: anything that gets within a few metres dies.
                for (int k = 0; k < 6; k++)
                {
                    yield return new WaitForSeconds(0.5f);
                    var me = Survivors.SurvivorHealth.Local;
                    if (me == null) continue;
                    foreach (var z in FindObjectsOfType<Zombie>().Where(z => !z.IsDead && (z.transform.position - me.Player.transform.position).sqrMagnitude < 3.5f * 3.5f))
                        z.TakeDamage(100f, z.transform.position, z.transform.position - me.Player.transform.position, false);
                }
                var d = ZombieDirector.Instance;
                if (d == null) continue;
                var zs = FindObjectsOfType<Zombie>().Where(z => !z.IsDead).ToList();
                Plugin.Logger.LogInfo($"[dev] director t={t + 3f:F0}s: {d.State}, stress {d.Stress:F2}, next horde {d.NextHordeIn:F0}s, alive {zs.Count} (alerted {zs.Count(z => z.Alerted)}, horde {zs.Count(z => z.Horde)}), kills {ZombieDirector.Kills}");
                if (!shot && d.State == ZombieDirector.Phase.Horde && zs.Count(z => z.Horde) >= 10)
                {
                    shot = true;
                    yield return new WaitForSeconds(2f);
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "s4d_phase5_horde.png"));
                    OverheadShot(System.IO.Path.Combine(dir, "s4d_phase5_horde_top.png"));
                }
            }
            Plugin.Logger.LogInfo("[dev] director test end");
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
            // Third-person body recon (Phase 4 bodies).
            var playerRoot = setup.transform.root;
            foreach (var an in playerRoot.GetComponentsInChildren<Animator>(true))
                Plugin.Logger.LogInfo($"[dev] body: animator {TPath(an.transform)} avatar {(an.avatar ? an.avatar.name + " human=" + an.avatar.isHuman : "none")} ctrl {an.runtimeAnimatorController?.name} cull {an.cullingMode} active {an.isActiveAndEnabled}");
            foreach (var r in playerRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (r.gameObject.layer != 8)
                    Plugin.Logger.LogInfo($"[dev] body: SMR {TPath(r.transform)} L{r.gameObject.layer} on={r.enabled && r.gameObject.activeInHierarchy} shadows {r.shadowCastingMode} root {r.rootBone?.name}");
            var ph = playerRoot.GetComponentInChildren<PlayerHealth>(true);
            if (ph != null)
            {
                var rag = HarmonyLib.Traverse.Create(ph).Field("aboubiRagdoll").GetValue();
                Plugin.Logger.LogInfo($"[dev] body: PlayerHealth on {TPath(ph.transform)}, aboubiRagdoll {rag} ({rag?.GetType().Name})");
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

            // Third-person body: your own is hidden from you, so force it on and render it from the front.
            var sb = playerRoot.GetComponentInChildren<Survivors.SurvivorBody>();
            if (sb != null && sb.Body != null)
            {
                var aboubiParts = sb.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r => (r, r.enabled)).ToList();
                foreach (var (original, file) in new[] { (true, "s4d_phase4_body_straftat.png"), (false, "s4d_phase4_body_l4d2.png") })
                {
                    Survivors.SurvivorBody.ShowOriginal = original;
                    Survivors.SurvivorBody.ForceVisible = true;
                    foreach (var (r, _) in aboubiParts) r.enabled = original;
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    var body = sb.transform;
                    var cam = new GameObject("S4D_BodyCam").AddComponent<Camera>();
                    Vector3 fwd = Vector3.ProjectOnPlane(setup.transform.forward, Vector3.up).normalized;
                    Vector3 front = Quaternion.Euler(0f, 25f, 0f) * fwd;
                    cam.transform.position = body.position + front * 2.6f + Vector3.up * 1.3f;
                    cam.transform.LookAt(body.position + Vector3.up * 0.9f);
                    cam.fieldOfView = 50f;
                    cam.nearClipPlane = 0.05f;
                    cam.cullingMask &= ~(1 << 8); // not the first-person arms
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
                foreach (var (r, was) in aboubiParts) r.enabled = was;
                Survivors.SurvivorBody.ShowOriginal = false;
                Survivors.SurvivorBody.ForceVisible = false;
                Plugin.Logger.LogInfo($"[dev] arms test: body renders written (body at {sb.Body.transform.position}, aboubi at {sb.transform.position})");
            }
            Plugin.Logger.LogInfo("[dev] arms test: screenshot s4d_phase4_arms.png");
        }

        private static void RenderView(Vector3 from, Vector3 at, string path)
        {
            var cam = new GameObject("S4D_DevCam").AddComponent<Camera>();
            cam.transform.position = from;
            cam.transform.LookAt(at);
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.05f;
            cam.cullingMask &= ~(1 << 8);
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            RenderTexture.active = null;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(cam.gameObject); Destroy(rt); Destroy(tex);
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
