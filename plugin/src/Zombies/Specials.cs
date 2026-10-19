using System.Collections;
using System.Linq;
using Straft4Dead.Net;
using UnityEngine;
using UnityEngine.AI;

namespace Straft4Dead.Zombies
{
    /// <summary>Anything STRAFTAT weapons can hurt: commons and specials (see WeaponPatches).</summary>
    internal interface IInfected
    {
        bool IsDead { get; }
        bool TakeDamage(float hp, Vector3 point, Vector3 direction, bool headshot);
    }

    /// <summary>Shared body of the special infected (Phase 6): an L4D2 model from the s4d_specials bundle (capsule
    /// stand-in without it), hit volumes on the Default layer like the commons, a NavMeshAgent, crossfaded animator
    /// states and a 3D voice. Health and damage are L4D2 HP.
    ///
    /// Phase 8: the host's specials think; clients get puppets that follow the host's stream (position, animation
    /// state, sounds, hurt/death and the special's own events, see NetSync). Visuals that both sides show (death,
    /// hit reactions, the Witch's startle, the Tank's entrance) live in *Visual methods that both run.</summary>
    internal abstract class SpecialInfected : MonoBehaviour, IInfected
    {
        internal const byte KindTank = 1, KindBoomer = 2, KindWitch = 3;

        protected NavMeshAgent Agent;
        protected Animator Anim;
        protected AudioSource Voice;
        protected float Health;
        protected bool Dead;
        private string _state;
        private Vector3 _netPos;
        private float _netYaw;

        public bool IsDead => Dead;
        internal ushort NetId { get; private set; }
        internal byte Kind { get; private set; }
        internal bool Puppet { get; private set; }
        internal string State => _state;

        protected static T Spawn<T>(byte kind, string model, Vector3 position, float health, float speed, float height, float radius, Color fallback, bool puppet = false, float yaw = 0f) where T : SpecialInfected
        {
            var root = new GameObject("S4D_" + typeof(T).Name + (puppet ? "Puppet" : "")) { layer = Zombie.Layer };
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            var s = root.AddComponent<T>();
            s.Kind = kind;
            s.Puppet = puppet;
            s._netPos = position;
            s._netYaw = yaw;
            var body = root.AddComponent<CapsuleCollider>();
            body.center = new Vector3(0f, height * 0.5f, 0f);
            body.radius = radius;
            body.height = height;
            var head = new GameObject(Zombie.HeadName) { layer = Zombie.Layer };
            head.AddComponent<SphereCollider>().radius = radius * 0.55f;

            var prefab = InfectedAssets.Instance != null ? InfectedAssets.Instance.Special(model) : null;
            if (prefab != null)
            {
                var m = Instantiate(prefab, root.transform, false);
                s.Anim = m.GetComponent<Animator>();
                var headBone = m.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "ValveBiped.Bip01_Head1");
                head.transform.SetParent(headBone != null ? headBone : root.transform, false);
                head.transform.localPosition = headBone != null ? Vector3.zero : new Vector3(0f, height - 0.2f, 0f);
                foreach (var r in m.GetComponentsInChildren<SkinnedMeshRenderer>()) r.updateWhenOffscreen = true;
            }
            else
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Destroy(c.GetComponent<Collider>());
                c.transform.SetParent(root.transform, false);
                c.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
                c.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
                c.GetComponent<Renderer>().material.color = fallback;
                head.transform.SetParent(root.transform, false);
                head.transform.localPosition = new Vector3(0f, height - 0.2f, 0f);
            }

            s.Voice = root.AddComponent<AudioSource>();
            s.Voice.spatialBlend = 1f;
            s.Voice.rolloffMode = AudioRolloffMode.Linear;
            s.Voice.maxDistance = 45f;
            s.Health = health;
            if (!puppet)
            {
                s.Agent = root.AddComponent<NavMeshAgent>();
                s.Agent.radius = Mathf.Min(radius, 0.5f);
                s.Agent.height = height;
                s.Agent.speed = speed;
                s.Agent.acceleration = 30f;
                s.Agent.angularSpeed = 540f;
                s.Agent.autoBraking = false;
                if (NetSync.Instance != null)
                {
                    s.NetId = NetSync.Instance.Register(s);
                    NetSync.SpawnSpecial(s);
                }
            }
            Plugin.Logger.LogInfo($"[specials] {typeof(T).Name}{(puppet ? " puppet" : "")} spawned at {position} ({(prefab != null ? prefab.name : "capsule")}, {health:F0} HP)");
            return s;
        }

        /// <summary>Client side: the host's special <paramref name="id"/>.</summary>
        internal static SpecialInfected CreatePuppet(ushort id, byte kind, Vector3 position, float yaw)
        {
            SpecialInfected s = kind == KindTank ? (SpecialInfected)Tank.Build(position, true, yaw)
                : kind == KindBoomer ? Boomer.Build(position, true, yaw)
                : kind == KindWitch ? Witch.Build(position, true, yaw) : null;
            if (s != null) s.NetId = id;
            return s;
        }

        public virtual bool TakeDamage(float hp, Vector3 point, Vector3 direction, bool headshot)
        {
            if (Dead || Puppet) return false;
            Health -= hp;
            if (Health > 0f)
            {
                HurtVisual(hp);
                NetSync.Hurt(NetId, hp);
                return false;
            }
            Die(direction);
            return true;
        }

        /// <summary>Hit reaction (pain sound), on the host and on puppets.</summary>
        internal virtual void HurtVisual(float hp) { }

        protected virtual void Die(Vector3 direction)
        {
            if (Agent != null)
            {
                if (Agent.isOnNavMesh) Agent.isStopped = true;
                Agent.enabled = false;
            }
            DeathVisual(direction);
            NetSync.Death(NetId, direction, false);
            ZombieDirector.OnZombieKilled(transform.position);
            Plugin.Logger.LogInfo($"[specials] {GetType().Name} killed");
        }

        /// <summary>Client: the host's special died.</summary>
        internal void PuppetDie(Vector3 direction)
        {
            if (!Dead) DeathVisual(direction);
        }

        /// <summary>Death as everyone sees it; subclasses add their animation and sounds.</summary>
        protected virtual void DeathVisual(Vector3 direction)
        {
            Dead = true;
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            Destroy(gameObject, 6f);
        }

        private void OnDestroy()
        {
            if (NetSync.Instance != null && NetId != 0 && !Puppet)
                NetSync.Instance.Unregister(NetId, announce: !Dead);
        }

        internal void PuppetTarget(Vector3 position, float yaw, string state)
        {
            _netPos = position;
            _netYaw = yaw;
            if (!Dead && !string.IsNullOrEmpty(state) && state != "Death") Play(state);
        }

        /// <summary>Puppets just follow the host; returns true when this is one (the caller skips its AI).</summary>
        protected bool PuppetUpdate()
        {
            if (!Puppet) return false;
            if (Dead) return true;
            transform.position = Vector3.Lerp(transform.position, _netPos, Time.deltaTime * 12f);
            if ((transform.position - _netPos).sqrMagnitude > 25f) transform.position = _netPos;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, _netYaw, 0f), Time.deltaTime * 12f);
            return true;
        }

        protected void Play(string state, float fade = 0.2f)
        {
            if (Anim == null || _state == state) return;
            _state = state;
            Anim.CrossFadeInFixedTime(state, fade);
        }

        /// <summary>A sound the host's AI decided on: play it here and on every puppet.</summary>
        protected void Say(string group, float volume = 1f)
        {
            SayLocal(group, volume);
            NetSync.Say(NetId, group, volume);
        }

        internal void SayLocal(string group, float volume = 1f)
        {
            var clip = SoundBank.Get(group);
            if (clip != null) Voice.PlayOneShot(clip, volume);
        }

        protected void Face(Vector3 at, float rate = 10f)
        {
            var to = at - transform.position; to.y = 0f;
            if (to.sqrMagnitude > 0.001f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), Time.deltaTime * rate);
        }

        protected static bool CanSee(Vector3 from, Vector3 to)
        {
            var d = to - from;
            return !Physics.Raycast(from + d.normalized * 0.6f, d.normalized, d.magnitude - 1.2f, ZombieDirector.SightMask & ~(1 << Zombie.Layer), QueryTriggerInteraction.Ignore);
        }

        protected void Locomote(float runAbove = 2.5f)
        {
            float v = Agent != null && Agent.enabled ? Agent.velocity.magnitude : 0f;
            Play(v > runAbove ? "Run" : v > 0.3f ? "Walk" : "Idle");
        }
    }

    /// <summary>The Tank: huge health, punches hard, throws rocks at survivors out of reach, its own music.</summary>
    internal sealed class Tank : SpecialInfected
    {
        internal static Tank Create(Vector3 at) => Build(at, false, 0f);

        internal static Tank Build(Vector3 at, bool puppet, float yaw) =>
            Spawn<Tank>(KindTank, "hulk", at, Plugin.Settings.TankHealth.Value, 6.2f, 2.3f, 0.6f, new Color(0.35f, 0.4f, 0.3f), puppet, yaw);

        private PlayerHealth _target;
        private float _nextPunch, _nextRock, _busyUntil, _nextRepath, _nextGrowl;
        private AudioSource _music;

        /// <summary>The entrance (roar, music) runs on the host and on every puppet.</summary>
        private IEnumerator Start()
        {
            SoundBank.Preload("tank_yell", "tank_throw", "tank_attack", "tank_growl", "tank_pain", "tank_die", "tank_punch", "tank_rock_hit", "tank_rip_rock", "tank_music");
            _music = gameObject.AddComponent<AudioSource>();
            _music.spatialBlend = 0f; _music.loop = true; _music.volume = 0.45f;
            _nextRock = Time.time + 5f;
            Play("Yell", 0.1f);
            _busyUntil = Time.time + 1.5f;
            yield return new WaitForSeconds(0.3f);
            SayLocal("tank_yell");
            for (int i = 0; i < 20 && _music.clip == null; i++) { _music.clip = SoundBank.Get("tank_music"); yield return new WaitForSeconds(0.25f); }
            if (_music.clip != null && Plugin.Settings.HordeMusic.Value && !Dead) _music.Play();
        }

        private void Update()
        {
            if (PuppetUpdate()) return;
            if (Dead || !Agent.isOnNavMesh || Time.time < _busyUntil) return;
            if (_target == null || !GameAccess.IsAlive(_target) || Time.time >= _nextRepath)
            {
                _target = ZombieDirector.NearestLivingPlayer(transform.position);
                _nextRepath = Time.time + 0.3f;
                if (_target != null) Agent.SetDestination(_target.transform.position);
            }
            if (_target == null) { Agent.ResetPath(); Play("Idle"); return; }

            Vector3 tp = _target.transform.position;
            float d = Vector3.Distance(transform.position, tp);
            if (d < 2.4f && Time.time >= _nextPunch)
            {
                Face(tp, 30f);
                _nextPunch = Time.time + 1.6f;
                StartCoroutine(Punch());
                return;
            }
            if (d > 10f && d < 40f && Time.time >= _nextRock && CanSee(transform.position + Vector3.up * 2f, tp + Vector3.up * 1.2f))
            {
                _nextRock = Time.time + Random.Range(7f, 11f);
                StartCoroutine(ThrowRock());
                return;
            }
            Agent.isStopped = false;
            Locomote();
            if (Time.time >= _nextGrowl) { _nextGrowl = Time.time + Random.Range(3f, 6f); Say("tank_growl", 0.8f); }
        }

        private IEnumerator Punch()
        {
            Agent.isStopped = true;
            Play("Attack", 0.05f);
            Say("tank_attack");
            _busyUntil = Time.time + 1.1f;
            yield return new WaitForSeconds(0.45f);
            if (!Dead && _target != null && Vector3.Distance(transform.position, _target.transform.position) < 2.8f)
            {
                GameAccess.DamagePlayer(_target, Plugin.Settings.TankPunchDamage.Value * Plugin.Settings.ZombieStrength.Value, "Tank");
                Say("tank_punch");
            }
        }

        private IEnumerator ThrowRock()
        {
            Agent.isStopped = true;
            var target = _target;
            Play("Throw", 0.1f);
            Say("tank_rip_rock");
            _busyUntil = Time.time + 2.2f;
            var rock = TankRock.MakeRock();
            var hand = GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "ValveBiped.Bip01_R_Hand");
            for (float t = 0f; t < 1.1f; t += Time.deltaTime)
            {
                if (Dead || target == null) { Destroy(rock); yield break; }
                Face(target.transform.position, 8f);
                rock.transform.position = hand != null ? hand.position : transform.position + Vector3.up * 2.6f;
                yield return null;
            }
            Say("tank_throw");
            var r = rock.AddComponent<TankRock>();
            r.Launch(rock.transform.position, target);
            NetSync.Rock(rock.transform.position, r.Velocity);
        }

        internal override void HurtVisual(float hp)
        {
            if (Random.value < 0.15f) SayLocal("tank_pain", 0.8f);
        }

        protected override void DeathVisual(Vector3 direction)
        {
            base.DeathVisual(direction);
            Play("Death", 0.1f);
            SayLocal("tank_die");
            if (_music != null) StartCoroutine(FadeOut(_music));
        }

        private static IEnumerator FadeOut(AudioSource s)
        {
            while (s != null && s.volume > 0f) { s.volume -= Time.deltaTime * 0.25f; yield return null; }
        }
    }

    /// <summary>A thrown chunk of concrete: ballistic, aimed where the target is heading; hurts on contact. Clients get
    /// a visual copy with the same launch (the host's rock does the damage).</summary>
    internal sealed class TankRock : MonoBehaviour
    {
        private Vector3 _vel;
        private float _dieAt;
        private bool _visual;

        internal Vector3 Velocity => _vel;

        internal static GameObject MakeRock()
        {
            var rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            rock.name = "S4D_TankRock";
            Destroy(rock.GetComponent<Collider>());
            rock.transform.localScale = Vector3.one * 0.9f;
            rock.GetComponent<Renderer>().material.color = new Color(0.45f, 0.42f, 0.38f);
            return rock;
        }

        internal static void SpawnVisual(Vector3 from, Vector3 velocity)
        {
            var rock = MakeRock();
            rock.transform.position = from;
            var r = rock.AddComponent<TankRock>();
            r._vel = velocity;
            r._visual = true;
            r._dieAt = Time.time + 6f;
        }

        internal void Launch(Vector3 from, PlayerHealth target)
        {
            const float speed = 26f;
            Vector3 aim = target.transform.position + Vector3.up * 1.0f;
            float t = Vector3.Distance(from, aim) / speed;
            var cc = target.GetComponentInParent<CharacterController>();
            if (cc != null) aim += new Vector3(cc.velocity.x, 0f, cc.velocity.z) * t * 0.6f; // lead the target a little
            _vel = (aim - from) / t - 0.5f * Physics.gravity * t;
            _dieAt = Time.time + 6f;
        }

        private void Update()
        {
            Vector3 p = transform.position;
            _vel += Physics.gravity * Time.deltaTime;
            Vector3 next = p + _vel * Time.deltaTime;
            transform.Rotate(220f * Time.deltaTime, 90f * Time.deltaTime, 0f);
            foreach (var ph in FindObjectsOfType<PlayerHealth>())
                if (GameAccess.IsAlive(ph) && DistanceToSegment(ph.transform.position + Vector3.up * 1f, p, next) < 1.1f)
                {
                    if (!_visual) GameAccess.DamagePlayer(ph, Plugin.Settings.TankPunchDamage.Value * Plugin.Settings.ZombieStrength.Value, "Tank rock");
                    Smash();
                    return;
                }
            if (Physics.Linecast(p, next, ZombieDirector.SightMask & ~(1 << Zombie.Layer), QueryTriggerInteraction.Ignore) || Time.time > _dieAt)
            {
                Smash();
                return;
            }
            transform.position = next;
        }

        private void Smash()
        {
            var clip = SoundBank.Get("tank_rock_hit");
            if (clip != null) AudioSource.PlayClipAtPoint(clip, transform.position);
            Destroy(gameObject);
        }

        private static float DistanceToSegment(Vector3 x, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector3.Dot(x - a, ab) / ab.sqrMagnitude);
            return Vector3.Distance(x, a + ab * t);
        }
    }

    /// <summary>The Boomer: slow, fragile, vomits bile on survivors in front of it (blinding them and calling a horde
    /// onto them) and bursts into bile when killed.</summary>
    internal sealed class Boomer : SpecialInfected
    {
        /// <summary>Until when the local survivor is covered in bile (HUD tints green).</summary>
        internal static float LocalBiledUntil;

        internal static Boomer Create(Vector3 at) => Build(at, false, 0f);

        internal static Boomer Build(Vector3 at, bool puppet, float yaw) =>
            Spawn<Boomer>(KindBoomer, "boomer", at, 50f, 4.2f, 1.8f, 0.45f, new Color(0.45f, 0.5f, 0.3f), puppet, yaw);

        private PlayerHealth _target;
        private float _nextVomit, _busyUntil, _nextRepath, _nextLurk;

        private void Start()
        {
            SoundBank.Preload("boomer_lurk", "boomer_alert", "boomer_vomit", "boomer_vomit_fx", "boomer_explode", "boomer_pain");
            _nextVomit = Time.time + 2f;
        }

        private void Update()
        {
            if (PuppetUpdate()) return;
            if (Dead || !Agent.isOnNavMesh || Time.time < _busyUntil) return;
            if (_target == null || !GameAccess.IsAlive(_target) || Time.time >= _nextRepath)
            {
                _target = ZombieDirector.NearestLivingPlayer(transform.position);
                _nextRepath = Time.time + 0.3f;
                if (_target != null) Agent.SetDestination(_target.transform.position);
            }
            if (_target == null) { Agent.ResetPath(); Play("Idle"); return; }
            Vector3 tp = _target.transform.position;
            float d = Vector3.Distance(transform.position, tp);
            if (d < 5f && Time.time >= _nextVomit && CanSee(transform.position + Vector3.up * 1.5f, tp + Vector3.up * 1.2f))
            {
                _nextVomit = Time.time + 20f;
                StartCoroutine(Vomit());
                return;
            }
            Agent.isStopped = d < 1.5f;
            if (d < 1.5f) { Face(tp); Play("Attack"); } else Locomote(3.5f);
            if (Time.time >= _nextLurk) { _nextLurk = Time.time + Random.Range(3f, 7f); Say("boomer_lurk", 0.8f); }
        }

        private IEnumerator Vomit()
        {
            Agent.isStopped = true;
            Play("Vomit", 0.1f);
            Say("boomer_vomit");
            Say("boomer_vomit_fx");
            _busyUntil = Time.time + 1.6f;
            for (float t = 0f; t < 1.2f; t += 0.1f)
            {
                yield return new WaitForSeconds(0.1f);
                if (Dead) yield break;
                if (_target != null) Face(_target.transform.position, 20f);
                foreach (var ph in FindObjectsOfType<PlayerHealth>().Where(GameAccess.IsAlive))
                {
                    Vector3 to = ph.transform.position - transform.position;
                    if (to.magnitude < 6f && Vector3.Angle(transform.forward, new Vector3(to.x, 0f, to.z)) < 35f)
                        Bile(ph);
                }
            }
        }

        private static readonly System.Collections.Generic.Dictionary<PlayerHealth, float> BiledUntil = new System.Collections.Generic.Dictionary<PlayerHealth, float>();

        /// <summary>Host: cover a survivor in bile: green screen for them and a horde called onto them (once per
        /// coating).</summary>
        internal static void Bile(PlayerHealth ph)
        {
            if (BiledUntil.TryGetValue(ph, out var until) && Time.time < until - 9f) return; // already dripping
            BiledUntil[ph] = Time.time + 12f;
            var who = Survivors.SurvivorHealth.For(ph);
            var me = Survivors.SurvivorHealth.Local;
            if (me != null && me.Player == ph) LocalBiledUntil = Time.time + 12f;
            else NetSync.BileForRemote(ph);
            Plugin.Logger.LogInfo($"[specials] {ph.transform.root.name} biled");
            ZombieDirector.Instance?.CallBileHorde(ph.transform.position);
            Survivors.SurvivorVoice.Say(who, "biled", interrupt: true);
        }

        internal override void HurtVisual(float hp) => SayLocal("boomer_pain", 0.8f);

        protected override void Die(Vector3 direction)
        {
            base.Die(direction);
            foreach (var ph in FindObjectsOfType<PlayerHealth>().Where(GameAccess.IsAlive))
                if (Vector3.Distance(ph.transform.position, transform.position) < 3.5f)
                    Bile(ph);
        }

        protected override void DeathVisual(Vector3 direction)
        {
            base.DeathVisual(direction);
            SayLocal("boomer_explode");
            foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false; // pop
            StartCoroutine(Burst());
        }

        private IEnumerator Burst()
        {
            var cloud = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(cloud.GetComponent<Collider>());
            cloud.transform.position = transform.position + Vector3.up * 1f;
            var mat = cloud.GetComponent<Renderer>().material;
            mat.shader = Shader.Find("Sprites/Default");
            for (float t = 0f; t < 0.6f; t += Time.deltaTime)
            {
                cloud.transform.localScale = Vector3.one * Mathf.Lerp(0.5f, 4.5f, t / 0.6f);
                mat.color = new Color(0.45f, 0.55f, 0.15f, Mathf.Lerp(0.6f, 0f, t / 0.6f));
                yield return null;
            }
            Destroy(cloud);
        }
    }

    /// <summary>The Witch: sits crying away from the action. Getting close (or shooting her) builds her rage; once
    /// startled she charges whoever set her off and takes them down in one hit, then flees.</summary>
    internal sealed class Witch : SpecialInfected
    {
        internal static Witch Create(Vector3 at) => Build(at, false, Random.Range(0f, 360f));

        internal static Witch Build(Vector3 at, bool puppet, float yaw) =>
            Spawn<Witch>(KindWitch, "witch", at, Plugin.Settings.WitchHealth.Value, 9.5f, 1.7f, 0.35f, new Color(0.85f, 0.85f, 0.8f), puppet, yaw);

        internal float Rage { get; private set; }
        private PlayerHealth _target;
        private bool _startled, _fleeing;
        private float _nextCry, _busyUntil;
        private AudioSource _music;

        private void Start()
        {
            SoundBank.Preload("witch_cry", "witch_mad", "witch_attack", "witch_die", "witch_pain", "witch_music", "witch_rage_music");
            if (Agent != null) Agent.isStopped = true;
            Play("Idle", 0f);
            _music = gameObject.AddComponent<AudioSource>();
            _music.spatialBlend = 0.6f; _music.rolloffMode = AudioRolloffMode.Linear; _music.maxDistance = 30f; _music.loop = true; _music.volume = 0.5f;
        }

        private void Update()
        {
            if (_music != null && !_startled && !Dead && _music.clip == null)
            {
                // Her theme plays around her for everyone, host and clients alike.
                _music.clip = SoundBank.Get("witch_music");
                if (_music.clip != null && Plugin.Settings.HordeMusic.Value) _music.Play();
            }
            if (PuppetUpdate()) return;
            if (Dead || !Agent.isOnNavMesh) return;
            if (!_startled)
            {
                // Rage builds while survivors are close, faster the closer they are; it slowly calms otherwise.
                var p = ZombieDirector.NearestLivingPlayer(transform.position);
                float d = p != null ? Vector3.Distance(p.transform.position, transform.position) : 999f;
                Rage = d < 12f ? Rage + Time.deltaTime * (12f - d) / 12f * 0.7f : Mathf.Max(0f, Rage - Time.deltaTime * 0.1f);
                if (Rage >= 1f && p != null) { Startle(p); return; }
                Play(Rage > 0.5f ? "Agitated" : "Idle", 0.4f);
                if (Time.time >= _nextCry) { _nextCry = Time.time + Random.Range(2.5f, 4.5f); Say(Rage > 0.5f ? "witch_mad" : "witch_cry", 0.9f); }
                return;
            }
            if (Time.time < _busyUntil) return;
            if (_fleeing) { Locomote(); return; }
            if (_target == null || !GameAccess.IsAlive(_target)) { Flee(); return; }
            Agent.SetDestination(_target.transform.position);
            Agent.isStopped = false;
            if (Vector3.Distance(transform.position, _target.transform.position) < 1.6f) StartCoroutine(Strike());
            else Play("Run");
        }

        private void Startle(PlayerHealth by)
        {
            if (_startled || by == null) return;
            _target = by;
            Agent.isStopped = true;
            _busyUntil = Time.time + 1.0f;
            StartleVisual();
            NetSync.Startle(NetId);
            Plugin.Logger.LogInfo($"[specials] Witch startled by {by.transform.root.name}");
        }

        /// <summary>The scream and her rage theme, on the host and on puppets.</summary>
        internal void StartleVisual()
        {
            _startled = true;
            Play("Agitated", 0.1f);
            SayLocal("witch_attack");
            var rage = SoundBank.Get("witch_rage_music");
            if (rage != null && Plugin.Settings.HordeMusic.Value && _music != null) { _music.Stop(); _music.spatialBlend = 0f; _music.loop = false; _music.clip = rage; _music.Play(); }
        }

        private IEnumerator Strike()
        {
            _busyUntil = Time.time + 1.2f;
            Agent.isStopped = true;
            Face(_target.transform.position, 40f);
            Play("Attack", 0.05f);
            yield return new WaitForSeconds(0.3f);
            if (!Dead && _target != null && Vector3.Distance(transform.position, _target.transform.position) < 2.2f)
                GameAccess.DamagePlayer(_target, 1000f, "Witch"); // one hit: straight to incap (L4D2 normal)
        }

        private void Flee()
        {
            _fleeing = true;
            var away = transform.position + Random.insideUnitSphere * 30f;
            if (NavMesh.SamplePosition(away, out var hit, 10f, NavMesh.AllAreas)) { Agent.isStopped = false; Agent.SetDestination(hit.position); }
            Destroy(gameObject, 8f);
        }

        public override bool TakeDamage(float hp, Vector3 point, Vector3 direction, bool headshot)
        {
            if (!_startled && !Dead && !Puppet) Startle(ZombieDirector.NearestLivingPlayer(transform.position));
            return base.TakeDamage(hp, point, direction, headshot);
        }

        internal override void HurtVisual(float hp) => SayLocal("witch_pain", 0.8f);

        protected override void DeathVisual(Vector3 direction)
        {
            base.DeathVisual(direction);
            Play("Death", 0.1f);
            SayLocal("witch_die");
            if (_music != null) _music.Stop();
        }
    }
}
