using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace Straft4Dead.Zombies
{
    /// <summary>A common infected. Phase 3: a random L4D2 outfit (random head and body variant, random skin and clothes
    /// palette rows) animated with L4D2's own clips through a humanoid avatar, with L4D2 infected sounds. Falls back to
    /// the Phase 1 capsule when the s4d_infected bundle isn't there. Health and damage are in L4D2 HP.</summary>
    internal sealed class Zombie : MonoBehaviour
    {
        // Default layer: STRAFTAT's hitscan weapons raycast their environment mask against it, which is what lets
        // Weapon.TriggerEnvironment report our hits (see WeaponPatches).
        internal const int Layer = 0;
        internal const string HeadName = "S4D_ZombieHead";

        internal static int Alive;
        private static int _diagLogs;

        private NavMeshAgent _agent;
        private Animator _anim;
        private AudioSource _voice;
        private bool _female;
        private float _health;
        private float _nextAttack, _nextRepath, _nextVoice, _flinchUntil;
        private PlayerHealth _target;
        private bool _dead, _chasing;
        private string _state;
        private float _runSpeed, _nextAlertCheck, _nextWander;

        internal bool IsDead => _dead;
        /// <summary>Chasing survivors. Wanderers shamble about until they see or hear a survivor, get hit, or a
        /// neighbour alerts them; horde zombies spawn alerted.</summary>
        internal bool Alerted { get; private set; }
        internal bool Horde { get; set; }

        internal static Zombie Create(Vector3 position, bool alerted = true)
        {
            var root = new GameObject("S4D_Zombie") { layer = Layer };
            root.transform.position = position;
            var z = root.AddComponent<Zombie>();

            // Hit volumes: a body capsule on the root, a head sphere that follows the head bone when there is a model.
            var body = root.AddComponent<CapsuleCollider>();
            body.center = new Vector3(0f, 0.85f, 0f);
            body.radius = 0.28f;
            body.height = 1.6f;
            var head = new GameObject(HeadName) { layer = Layer };
            var headCol = head.AddComponent<SphereCollider>();
            headCol.radius = 0.15f;

            var outfits = InfectedAssets.Instance != null ? InfectedAssets.Instance.Outfits : null;
            if (outfits != null && outfits.Count > 0)
            {
                var prefab = outfits[Random.Range(0, outfits.Count)];
                var model = Instantiate(prefab, root.transform, false);
                model.transform.localPosition = Vector3.zero;
                z._female = prefab.name.Contains("female");
                z._anim = model.GetComponent<Animator>();
                PickBodygroups(model);
                Tint(model);
                var headBone = model.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "ValveBiped.Bip01_Head1");
                head.transform.SetParent(headBone != null ? headBone : root.transform, false);
                head.transform.localPosition = headBone != null ? Vector3.zero : new Vector3(0f, 1.65f, 0f);

                z._anim.Play("Run", 0, Random.value); // desync the horde
                if (_diagLogs++ < 2)
                {
                    var rs = model.GetComponentsInChildren<SkinnedMeshRenderer>();
                    var m = rs.Length > 0 ? rs[0].sharedMaterial : null;
                    Plugin.Logger.LogInfo($"[infected] {prefab.name}: {rs.Length} active renderers [{string.Join(", ", rs.Select(r => r.name))}], " +
                        $"shader {(m != null ? m.shader.name + " supported=" + m.shader.isSupported : "none")}, bounds {(rs.Length > 0 ? rs[0].bounds.ToString() : "-")}, root {position}, head bone {(headBone != null)}");
                }
            }
            else
            {
                var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Destroy(capsule.GetComponent<Collider>());
                capsule.transform.SetParent(root.transform, false);
                capsule.transform.localPosition = new Vector3(0f, 0.85f, 0f);
                capsule.transform.localScale = new Vector3(0.6f, 0.8f, 0.6f);
                capsule.GetComponent<Renderer>().material.color = new Color(0.35f, 0.42f, 0.30f);
                head.transform.SetParent(root.transform, false);
                head.transform.localPosition = new Vector3(0f, 1.65f, 0f);
            }

            z._voice = root.AddComponent<AudioSource>();
            z._voice.spatialBlend = 1f;
            z._voice.rolloffMode = AudioRolloffMode.Linear;
            z._voice.maxDistance = 35f;
            z._voice.volume = 0.8f;
            z._nextVoice = Time.time + Random.Range(1f, 6f);

            z._agent = root.AddComponent<NavMeshAgent>();
            z._agent.radius = 0.35f;
            z._agent.height = 1.8f;
            z._runSpeed = Plugin.Settings.ZombieSpeed.Value * Random.Range(0.9f, 1.1f);
            z._agent.speed = alerted ? z._runSpeed : WanderSpeed;
            z.Alerted = alerted;
            z._agent.acceleration = 40f;
            z._agent.angularSpeed = 720f;
            z._agent.stoppingDistance = 0.9f;
            z._agent.autoBraking = false;
            z._health = Plugin.Settings.ZombieHealth.Value;
            Alive++;
            return z;
        }

        private const float WanderSpeed = 1.2f;
        private const float AlertDistance = 12f;   // sees a survivor in the open
        private const float HearDistance = 4f;     // notices a survivor right next to it, line of sight or not
        private const float ShoutDistance = 7f;    // an alerted common sets off its neighbours

        /// <summary>Start chasing (and, like L4D2 commons, set off the wanderers around us).</summary>
        internal void Alert(bool spread = true)
        {
            if (_dead || Alerted) return;
            Alerted = true;
            _agent.speed = _runSpeed;
            _chasing = false;
            if (!spread) return;
            foreach (var other in FindObjectsOfType<Zombie>())
                if (other != this && !other.Alerted && (other.transform.position - transform.position).sqrMagnitude < ShoutDistance * ShoutDistance)
                    other.StartCoroutine(other.AlertAfter(Random.Range(0.2f, 0.8f)));
        }

        private System.Collections.IEnumerator AlertAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            Alert();
        }

        private void Wander()
        {
            if (Time.time >= _nextAlertCheck)
            {
                _nextAlertCheck = Time.time + 0.25f;
                var p = ZombieDirector.NearestLivingPlayer(transform.position);
                if (p != null)
                {
                    Vector3 eye = transform.position + Vector3.up * 1.6f, to = p.transform.position + Vector3.up * 1.2f - eye;
                    float d = to.magnitude;
                    // Line of sight: only the static map blocks it (Default-layer colliders like us are ignored by
                    // starting the ray outside our own capsule).
                    bool seen = d < AlertDistance && !Physics.Raycast(eye + to.normalized * 0.4f, to.normalized, d - 0.8f, ZombieDirector.SightMask, QueryTriggerInteraction.Ignore);
                    if (d < HearDistance || seen) { Alert(); return; }
                }
            }
            if (Time.time >= _nextWander)
            {
                _nextWander = Time.time + Random.Range(4f, 10f);
                if (Random.value < 0.4f) _agent.ResetPath(); // stand around
                else if (NavMesh.SamplePosition(transform.position + Random.insideUnitSphere * 6f, out var hit, 3f, NavMesh.AllAreas))
                    _agent.SetDestination(hit.position);
            }
            if (Time.time >= _flinchUntil)
                Play(_agent.velocity.magnitude > 0.3f ? "Walk" : "Idle", 0.3f);
            Ambient("idle");
        }

        /// <summary>L4D2 commons ship every head and body variant in one model; show one of each.</summary>
        private static void PickBodygroups(GameObject model)
        {
            var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var heads = renderers.Where(r => r.name.ToLowerInvariant().Contains("head")).ToArray();
            var bodies = renderers.Except(heads).ToArray();
            foreach (var group in new[] { heads, bodies })
            {
                if (group.Length == 0) continue;
                int keep = Random.Range(0, group.Length);
                for (int i = 0; i < group.Length; i++)
                    group[i].gameObject.SetActive(i == keep);
            }
        }

        private static void Tint(GameObject model)
        {
            var block = new MaterialPropertyBlock();
            block.SetFloat("_SkinRow", Random.Range(0, 8));
            block.SetFloat("_ClothRow", Random.Range(8, 16));
            block.SetFloat("_Blood", Random.Range(0.6f, 1.4f));
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                r.SetPropertyBlock(block);
        }

        /// <summary>Apply damage in L4D2 HP. Returns true when this hit killed the zombie.</summary>
        internal bool TakeDamage(float hp, Vector3 point, Vector3 direction, bool headshot)
        {
            if (_dead)
                return false;
            _health -= hp;
            Alert();
            Plugin.Logger.LogInfo($"[zombies] {name} hit for {hp:F0} HP{(headshot ? " (head)" : "")}, {Mathf.Max(0f, _health):F0} left");
            if (_health > 0f)
            {
                Say("shot", 0.5f);
                if (_anim != null && Time.time > _flinchUntil && hp >= 20f)
                {
                    _flinchUntil = Time.time + 0.6f;
                    Play("Flinch", 0.08f);
                }
                return false;
            }
            Die(direction);
            return true;
        }

        private void Die(Vector3 direction)
        {
            bool running = _agent.enabled && _agent.velocity.magnitude > 2f;
            _dead = true;
            Alive--;
            _agent.enabled = false;
            foreach (var c in GetComponentsInChildren<Collider>())
                c.enabled = false;
            Say("die", 1f);
            if (_anim != null)
            {
                Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up);
                if (flat.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.LookRotation(-flat.normalized); // fall away from the shot
                Play(running ? "DeathRunning" : "Death", 0.1f);
            }
            else
            {
                Vector3 axis = Vector3.Cross(Vector3.up, direction.sqrMagnitude > 0.01f ? direction.normalized : transform.forward);
                transform.Rotate(axis, 85f, Space.World);
            }
            Destroy(gameObject, 6f);
            ZombieDirector.OnZombieKilled(transform.position);
            Survivors.SurvivorVoice.Kill();
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
            if (!Alerted)
            {
                Wander();
                return;
            }

            if (_target == null || !GameAccess.IsAlive(_target) || Time.time >= _nextRepath)
            {
                var previous = _target;
                _target = ZombieDirector.NearestLivingPlayer(transform.position);
                _nextRepath = Time.time + 0.25f;
                if (_target != null)
                {
                    _agent.SetDestination(_target.transform.position);
                    if (previous == null && !_chasing) { _chasing = true; Say("alert", 1f); }
                }
            }
            bool flinching = Time.time < _flinchUntil;
            _agent.isStopped = flinching;
            if (_target == null)
            {
                _agent.ResetPath();
                _chasing = false;
                if (!flinching) Play("Idle", 0.3f);
                Ambient("idle");
                return;
            }

            Vector3 to = _target.transform.position - transform.position;
            float flat = new Vector2(to.x, to.z).magnitude;
            bool inReach = flat <= Plugin.Settings.ZombieReach.Value && Mathf.Abs(to.y) < 1.8f;
            if (inReach)
            {
                var look = new Vector3(to.x, 0f, to.z);
                if (look.sqrMagnitude > 0.001f)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look), Time.deltaTime * 10f);
                if (Time.time >= _nextAttack && !flinching)
                {
                    _nextAttack = Time.time + Plugin.Settings.ZombieAttackInterval.Value;
                    GameAccess.DamagePlayer(_target, Plugin.Settings.ZombieDamage.Value, name);
                    Say("hit_player", 1f, oneShotOnly: true);
                }
            }
            if (!flinching)
                Play(inReach ? "Attack" : _agent.velocity.magnitude > 2.5f ? "Run" : _agent.velocity.magnitude > 0.3f ? "Walk" : "Idle", 0.2f);
            Ambient(_female ? "rage_female" : "rage_male");
        }

        private void Play(string state, float fade)
        {
            if (_anim == null || _state == state) return;
            _state = state;
            _anim.CrossFadeInFixedTime(state, fade);
        }

        private void Ambient(string category)
        {
            if (Time.time < _nextVoice) return;
            _nextVoice = Time.time + Random.Range(2.5f, 7f);
            Say(category, 0.7f);
        }

        private void Say(string category, float volume, bool oneShotOnly = false)
        {
            var clip = InfectedAssets.Instance != null ? InfectedAssets.Instance.Sound(category) : null;
            if (clip == null) return;
            if (oneShotOnly || _voice.isPlaying)
                _voice.PlayOneShot(clip, volume);
            else
            {
                _voice.clip = clip;
                _voice.volume = volume;
                _voice.Play();
            }
        }
    }
}
