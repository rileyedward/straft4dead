using UnityEngine;
using UnityEngine.AI;

namespace Straft4Dead.Zombies
{
    /// <summary>Phase 1 placeholder common infected: a capsule that paths to the nearest living player, swings when in
    /// reach, and dies to STRAFTAT weapons. Health and damage are in L4D2 HP (100 = a full STRAFTAT health bar).</summary>
    internal sealed class Zombie : MonoBehaviour
    {
        // Default layer: STRAFTAT's hitscan weapons raycast their environment mask against it, which is what lets
        // Weapon.TriggerEnvironment report our hits (see WeaponPatches).
        internal const int Layer = 0;
        internal const string HeadName = "S4D_ZombieHead";

        internal static int Alive;

        private NavMeshAgent _agent;
        private float _health;
        private float _nextAttack;
        private float _nextRepath;
        private PlayerHealth _target;
        private bool _dead;

        internal bool IsDead => _dead;

        internal static Zombie Create(Vector3 position)
        {
            var root = new GameObject("S4D_Zombie") { layer = Layer };
            root.transform.position = position;

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "S4D_ZombieBody";
            body.layer = Layer;
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.85f, 0f);
            body.transform.localScale = new Vector3(0.7f, 0.85f, 0.7f);
            Tint(body, new Color(0.35f, 0.42f, 0.30f));

            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = HeadName;
            head.layer = Layer;
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0f, 1.9f, 0f);
            head.transform.localScale = Vector3.one * 0.38f;
            Tint(head, new Color(0.55f, 0.50f, 0.40f));

            var z = root.AddComponent<Zombie>();
            z._agent = root.AddComponent<NavMeshAgent>();
            z._agent.radius = 0.35f;
            z._agent.height = 2.0f;
            z._agent.speed = Plugin.Settings.ZombieSpeed.Value;
            z._agent.acceleration = 40f;
            z._agent.angularSpeed = 720f;
            z._agent.stoppingDistance = 0.9f;
            z._agent.autoBraking = false;
            z._health = Plugin.Settings.ZombieHealth.Value;
            Alive++;
            return z;
        }

        private static void Tint(GameObject go, Color c)
        {
            var r = go.GetComponent<Renderer>();
            r.material.color = c;
        }

        /// <summary>Apply damage in L4D2 HP. Returns true when this hit killed the zombie.</summary>
        internal bool TakeDamage(float hp, Vector3 point, Vector3 direction, bool headshot)
        {
            if (_dead)
                return false;
            _health -= hp;
            Plugin.Logger.LogInfo($"[zombies] {name} hit for {hp:F0} HP{(headshot ? " (head)" : "")}, {Mathf.Max(0f, _health):F0} left");
            if (_health > 0f)
                return false;
            Die(direction);
            return true;
        }

        private void Die(Vector3 direction)
        {
            _dead = true;
            Alive--;
            _agent.enabled = false;
            foreach (var c in GetComponentsInChildren<Collider>())
                c.enabled = false;
            // Placeholder death: tip over away from the shot, sink, and clean up.
            Vector3 axis = Vector3.Cross(Vector3.up, direction.sqrMagnitude > 0.01f ? direction.normalized : transform.forward);
            transform.Rotate(axis, 85f, Space.World);
            Destroy(gameObject, 4f);
            ZombieDirector.Kills++;
            Plugin.Logger.LogInfo($"[zombies] {name} died (kills this map: {ZombieDirector.Kills})");
        }

        private void OnDestroy()
        {
            if (!_dead)
                Alive--;
        }

        private void Update()
        {
            if (_dead || !_agent.isOnNavMesh)
                return;

            if (_target == null || !GameAccess.IsAlive(_target) || Time.time >= _nextRepath)
            {
                _target = ZombieDirector.NearestLivingPlayer(transform.position);
                _nextRepath = Time.time + 0.25f;
                if (_target != null)
                    _agent.SetDestination(_target.transform.position);
            }
            if (_target == null)
            {
                _agent.ResetPath();
                return;
            }

            Vector3 to = _target.transform.position - transform.position;
            float flat = new Vector2(to.x, to.z).magnitude;
            if (flat <= Plugin.Settings.ZombieReach.Value && Mathf.Abs(to.y) < 1.8f && Time.time >= _nextAttack)
            {
                _nextAttack = Time.time + Plugin.Settings.ZombieAttackInterval.Value;
                GameAccess.DamagePlayer(_target, Plugin.Settings.ZombieDamage.Value, name);
            }
        }
    }
}
