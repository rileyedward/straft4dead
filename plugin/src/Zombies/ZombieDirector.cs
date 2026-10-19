using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Straft4Dead.Zombies
{
    /// <summary>Phase 1 spawner: after a map loads, bakes a navmesh and keeps up to MaxAlive placeholder zombies around
    /// the players. Host only; zombies are local objects until Phase 8. Pacing is a constant trickle; Phase 5 replaces it.</summary>
    internal sealed class ZombieDirector : MonoBehaviour
    {
        internal static int Kills;
        private static readonly List<PlayerHealth> Players = new List<PlayerHealth>();
        private static float _nextPlayerScan;

        private readonly HashSet<int> _scaled = new HashSet<int>();
        private float _nextSpawn;
        private bool _ready;

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

        private IEnumerator Start()
        {
            Kills = 0;
            yield return new WaitForSeconds(1.5f); // let the map and its moving parts settle
            float t0 = Time.realtimeSinceStartup;
            int tris = NavMeshBaker.Bake(gameObject.scene);
            Plugin.Logger.LogInfo($"[zombies] navmesh bake took {(Time.realtimeSinceStartup - t0) * 1000f:F0} ms");
            _ready = tris > 0;
            if (!_ready)
                Plugin.Logger.LogWarning("[zombies] no navmesh, zombies disabled on this map");
        }

        private void OnDestroy()
        {
            NavMeshBaker.Clear();
        }

        private void Update()
        {
            var cfg = Plugin.Settings;
            if (!GameAccess.IsServer)
                return;
            RefreshPlayers();
            ApplyHealthMultiplier(cfg.PlayerHealthMultiplier.Value);

            if (!_ready || !cfg.ZombiesEnabled.Value || Time.time < _nextSpawn)
                return;
            _nextSpawn = Time.time + cfg.SpawnInterval.Value;
            if (Zombie.Alive >= cfg.MaxAlive.Value)
                return;

            var anchor = NearestLivingPlayer(Vector3.zero);
            if (anchor == null)
                return;
            if (TryFindSpawnPoint(anchor.transform.position, cfg.SpawnMinDistance.Value, cfg.SpawnMaxDistance.Value, out var pos))
            {
                var z = Zombie.Create(pos);
                z.name = $"S4D_Zombie_{Time.frameCount}";
                Plugin.Logger.LogInfo($"[zombies] spawned {z.name} at {pos} ({Vector3.Distance(pos, anchor.transform.position):F0} m from {anchor.transform.root.name}), alive {Zombie.Alive}");
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

        private void ApplyHealthMultiplier(float multiplier)
        {
            foreach (var p in Players)
            {
                if (p == null || !GameAccess.IsAlive(p))
                    continue;
                int key = p.GetInstanceID() * 31 + (int)p.sync___get_value_spawnGeneration();
                if (!_scaled.Add(key))
                    continue;
                float before = GameAccess.ToL4D2(p.sync___get_value_health());
                float after = Mathf.Approximately(multiplier, 1f) ? before : GameAccess.ScaleHealth(p, multiplier);
                Plugin.Logger.LogInfo($"[zombies] player {p.transform.root.name} spawned with {p.sync___get_value_health():F2} units ({before:F0} HP), x{multiplier} -> {after:F0} HP");
            }
        }
    }
}
