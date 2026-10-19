using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace Straft4Dead.Zombies
{
    /// <summary>Director-lite (Phase 5). After a map loads it bakes a navmesh, then paces the infected the way L4D2's
    /// director does, in a loop:
    ///   BuildUp: wanderers trickle in up to [Director] Wanderers while the mob timer runs;
    ///   Horde:   the timer fires, a mob of HordeSize spawns alerted from one direction with the horde music;
    ///   Relax:   once survivor stress peaks (or the horde is over), nothing new spawns for RelaxSeconds.
    /// Stress is L4D2's "intensity": damage taken, kills at close range and incaps push it up, it bleeds off when
    /// nothing is hurting you. Host only; zombies are local objects until Phase 8.</summary>
    internal sealed class ZombieDirector : MonoBehaviour
    {
        internal enum Phase { BuildUp, Horde, Relax }

        internal static int Kills;
        internal static ZombieDirector Instance;
        internal static int SightMask => NavMeshBaker.EnvironmentMask;
        private static readonly List<PlayerHealth> Players = new List<PlayerHealth>();
        private static float _nextPlayerScan;

        internal Phase State { get; private set; } = Phase.BuildUp;
        internal float Stress { get; private set; }
        internal float NextHordeIn => Mathf.Max(0f, _hordeAt - Time.time);

        private float _nextSpawn, _hordeAt, _phaseStart, _lastHurt;
        private int _hordeLeft;
        private Vector3 _hordeOrigin;
        private bool _ready;
        private long _takeKey = -1;
        private DirectorMusic _music;

        internal static PlayerHealth NearestLivingPlayer(Vector3 from)
        {
            RefreshPlayers();
            PlayerHealth best = null;
            float bestD = float.MaxValue;
            foreach (var p in Players)
            {
                if (!GameAccess.IsAlive(p))
                    continue;
                float d = (p.transform.position - from).sqrMagnitude;
                if (d < bestD) { bestD = d; best = p; }
            }
            return best;
        }

        private static void RefreshPlayers()
        {
            if (Time.time < _nextPlayerScan)
                return;
            _nextPlayerScan = Time.time + 0.5f;
            Players.Clear();
            Players.AddRange(FindObjectsOfType<PlayerHealth>());
        }

        // ---- stress (L4D2 "intensity", 0..1, peak at 1) ----

        internal static void OnSurvivorHurt(float damage)
        {
            if (Instance == null) return;
            Instance.Stress = Mathf.Min(1f, Instance.Stress + damage / 40f);
            Instance._lastHurt = Time.time;
        }

        internal static void OnSurvivorIncap()
        {
            if (Instance == null) return;
            Instance.Stress = 1f;
            Instance._lastHurt = Time.time;
        }

        internal static void OnZombieKilled(Vector3 at)
        {
            Kills++;
            var p = NearestLivingPlayer(at);
            if (Instance != null && p != null && (p.transform.position - at).sqrMagnitude < 6f * 6f)
                Instance.Stress = Mathf.Min(1f, Instance.Stress + 0.04f); // close calls
        }

        private IEnumerator Start()
        {
            Instance = this;
            Kills = 0;
            _music = gameObject.AddComponent<DirectorMusic>();
            yield return new WaitForSeconds(1.5f); // let the map and its moving parts settle
            float t0 = Time.realtimeSinceStartup;
            int tris = NavMeshBaker.Bake(gameObject.scene);
            Plugin.Logger.LogInfo($"[zombies] navmesh bake took {(Time.realtimeSinceStartup - t0) * 1000f:F0} ms");
            _ready = tris > 0;
            if (!_ready)
                Plugin.Logger.LogWarning("[zombies] no navmesh, zombies disabled on this map");
            NewTake();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            NavMeshBaker.Clear();
        }

        private void Update()
        {
            var cfg = Plugin.Settings;
            if (!GameAccess.IsServer)
                return;
            RefreshPlayers();
            foreach (var p in Players)
                Survivors.SurvivorHealth.For(p); // L4D2 health rules, incl. the health multiplier

            // A new take: every player respawned (their spawn generations all moved on).
            long key = Players.Count == 0 ? -1 : Players.Aggregate(0L, (k, p) => k * 31 + p.sync___get_value_spawnGeneration());
            if (key != _takeKey && Players.Count > 0 && Players.All(GameAccess.IsAlive))
            {
                if (_takeKey != -1) NewTake();
                _takeKey = key;
            }

            if (!_ready || !cfg.ZombiesEnabled.Value)
                return;
            if (!cfg.DirectorEnabled.Value)
            {
                Trickle(cfg.MaxAlive.Value, cfg.SpawnInterval.Value);
                return;
            }

            // Stress bleeds off once nothing has hurt anyone for a few seconds.
            if (Time.time - _lastHurt > 4f)
                Stress = Mathf.Max(0f, Stress - 0.05f * Time.deltaTime);

            switch (State)
            {
                case Phase.BuildUp:
                    Trickle(Mathf.Min(cfg.Wanderers.Value, cfg.MaxAlive.Value), cfg.SpawnInterval.Value, alerted: false);
                    if (Stress >= 1f) Enter(Phase.Relax);
                    else if (Time.time >= _hordeAt) StartHorde();
                    break;
                case Phase.Horde:
                    SpawnHordeWave();
                    bool hordeGone = _hordeLeft == 0 && !FindObjectsOfType<Zombie>().Any(z => z.Horde && !z.IsDead);
                    if (hordeGone || Time.time - _phaseStart > 60f) Enter(Phase.Relax);
                    break;
                case Phase.Relax:
                    if (Time.time - _phaseStart >= cfg.RelaxSeconds.Value && Stress < 0.3f)
                    {
                        ScheduleHorde(Random.Range(cfg.HordeIntervalMin.Value, cfg.HordeIntervalMax.Value));
                        Enter(Phase.BuildUp);
                    }
                    break;
            }
        }

        private void Enter(Phase next)
        {
            if (State == Phase.Horde) _music.EndHorde();
            Plugin.Logger.LogInfo($"[director] {State} -> {next} (stress {Stress:F2}, alive {Zombie.Alive}, kills {Kills})");
            State = next;
            _phaseStart = Time.time;
        }

        private void NewTake()
        {
            if (Plugin.Settings.ClearOnNewTake.Value)
                foreach (var z in FindObjectsOfType<Zombie>()) Destroy(z.gameObject);
            Stress = 0f;
            _hordeLeft = 0;
            _music.EndHorde();
            State = Phase.BuildUp;
            _phaseStart = Time.time;
            ScheduleHorde(Plugin.Settings.FirstHorde.Value);
            Plugin.Logger.LogInfo($"[director] new take: zombies cleared, first horde in {Plugin.Settings.FirstHorde.Value:F0} s");
        }

        private void ScheduleHorde(float seconds) => _hordeAt = Time.time + seconds;

        private void StartHorde()
        {
            var anchor = NearestLivingPlayer(Vector3.zero);
            if (anchor == null || !TryFindSpawnPoint(anchor.transform.position, Plugin.Settings.SpawnMinDistance.Value * 1.5f, Plugin.Settings.SpawnMaxDistance.Value, out _hordeOrigin))
            {
                ScheduleHorde(5f); // try again shortly
                return;
            }
            _hordeLeft = Plugin.Settings.HordeSize.Value;
            Enter(Phase.Horde);
            if (Plugin.Settings.HordeMusic.Value) _music.StartHorde();
            Survivors.SurvivorVoice.Say(Survivors.SurvivorHealth.Local, "incoming", interrupt: true);
            // Wanderers hear it too.
            foreach (var z in FindObjectsOfType<Zombie>()) z.Alert(spread: false);
            Plugin.Logger.LogInfo($"[director] horde of {_hordeLeft} from {_hordeOrigin} ({Vector3.Distance(_hordeOrigin, anchor.transform.position):F0} m out)");
        }

        /// <summary>Spawn the mob in quick bursts around one origin, so it pours in from a direction.</summary>
        private void SpawnHordeWave()
        {
            if (_hordeLeft <= 0 || Time.time < _nextSpawn) return;
            _nextSpawn = Time.time + 0.35f;
            for (int i = 0; i < 3 && _hordeLeft > 0 && Zombie.Alive < Plugin.Settings.MaxAlive.Value; i++)
            {
                if (!NavMesh.SamplePosition(_hordeOrigin + Random.insideUnitSphere * 5f, out var hit, 4f, NavMesh.AllAreas)) continue;
                var z = Zombie.Create(hit.position, alerted: true);
                z.Horde = true;
                z.name = $"S4D_Zombie_{Time.frameCount}_{i}";
                _hordeLeft--;
            }
        }

        private void Trickle(int cap, float interval, bool alerted = true)
        {
            if (Time.time < _nextSpawn) return;
            _nextSpawn = Time.time + interval;
            if (Zombie.Alive >= cap) return;
            var anchor = NearestLivingPlayer(Vector3.zero);
            if (anchor == null) return;
            if (TryFindSpawnPoint(anchor.transform.position, Plugin.Settings.SpawnMinDistance.Value, Plugin.Settings.SpawnMaxDistance.Value, out var pos))
            {
                var z = Zombie.Create(pos, alerted);
                z.name = $"S4D_Zombie_{Time.frameCount}";
                Plugin.Logger.LogInfo($"[zombies] spawned {(alerted ? "chaser" : "wanderer")} {z.name} {Vector3.Distance(pos, anchor.transform.position):F0} m from {anchor.transform.root.name}, alive {Zombie.Alive}");
            }
        }

        private static int _spawnFailLogs;

        /// <summary>Random navmesh point in a ring around the player that the player can walk to.</summary>
        private static bool TryFindSpawnPoint(Vector3 around, float min, float max, out Vector3 pos)
        {
            pos = default;
            if (!NavMesh.SamplePosition(around, out var playerHit, 4f, NavMesh.AllAreas))
            {
                LogSpawnFail($"player at {around} is not near the navmesh");
                return false;
            }
            var path = new NavMeshPath();
            int sampled = 0, unreachable = 0;
            for (int i = 0; i < 30; i++)
            {
                Vector2 dir = Random.insideUnitCircle.normalized * Random.Range(min, max);
                Vector3 probe = around + new Vector3(dir.x, Random.Range(-4f, 4f), dir.y);
                if (!NavMesh.SamplePosition(probe, out var hit, 8f, NavMesh.AllAreas))
                    continue;
                sampled++;
                if ((hit.position - around).sqrMagnitude < min * min * 0.5f)
                    continue;
                if (NavMesh.CalculatePath(hit.position, playerHit.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete)
                {
                    pos = hit.position;
                    return true;
                }
                unreachable++;
            }
            LogSpawnFail($"no spawn point: {sampled} probes on navmesh, {unreachable} unreachable (player {around}, on navmesh at {playerHit.position})");
            return false;
        }

        private static void LogSpawnFail(string why)
        {
            if (_spawnFailLogs++ < 5)
                Plugin.Logger.LogWarning("[zombies] " + why);
        }
    }
}
