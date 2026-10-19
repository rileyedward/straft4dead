using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Serializing;
using FishNet.Transporting;
using Straft4Dead.Survivors;
using Straft4Dead.Zombies;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Straft4Dead.Net
{
    /// <summary>The single FishNet broadcast all Straft 4 Dead traffic rides on. Our plugin isn't processed by
    /// FishNet's code generator, so its serializer is registered by hand (GenericWriter/GenericReader); the payload
    /// is our own little binary format (see NetSync).</summary>
    internal struct S4DPacket : IBroadcast
    {
        public byte[] Data;
    }

    internal enum Msg : byte
    {
        // host -> clients
        Spawn = 1, States, Say, Hurt, Death, Despawn, Clear, Rock, Music, Startle, HealthState, Voice, Bile, SurvivorMap, Downed,
        // client -> host
        Hello = 64, Hit, UsePills, Heal, Revive,
    }

    /// <summary>Phase 8: Straft 4 Dead over STRAFTAT's own FishNet session (host-authoritative, private lobbies).
    ///
    /// The host runs everything that thinks: the director, zombie and special AI, L4D2 health rules, revives.
    /// Clients get puppets: every infected is spawned on clients with the same look, then follows the host's
    /// position/animation stream (10 Hz) and plays the host's sounds and events. Clients send their weapon hits on
    /// infected, pills/medkit use and revive attempts to the host, and get back their own health state for the HUD
    /// and their survivor's voice lines. Survivor choices are exchanged so every player shows as their own survivor.
    /// With no other players connected (single player) nothing is sent.</summary>
    internal sealed class NetSync : MonoBehaviour
    {
        internal static NetSync Instance;

        /// <summary>A client of someone else's game (not the host).</summary>
        internal static bool IsRemoteClient => InstanceFinder.IsClient && !InstanceFinder.IsServer;
        internal static bool IsHost => InstanceFinder.IsServer;

        private NetworkManager _registeredOn;
        private float _nextStates, _nextHealth;
        private bool _helloSent;
        private string _helloScene;
        private ushort _nextId = 1;
        private readonly Dictionary<ushort, MonoBehaviour> _infected = new Dictionary<ushort, MonoBehaviour>();
        /// <summary>FishNet client id -> survivor name, kept by the host and mirrored to everyone.</summary>
        internal static readonly Dictionary<int, string> SurvivorOf = new Dictionary<int, string>();

        internal static readonly string[] States =
            { "", "Idle", "Walk", "Run", "Attack", "Flinch", "Death", "DeathRunning", "Throw", "Yell", "Vomit", "Agitated" };

        static NetSync()
        {
            GenericWriter<S4DPacket>.Write = (w, p) =>
            {
                int n = p.Data?.Length ?? 0;
                w.WriteInt32(n, AutoPackType.Unpacked);
                if (n > 0) w.WriteBytes(p.Data, 0, n);
            };
            GenericReader<S4DPacket>.Read = r =>
            {
                int n = r.ReadInt32(AutoPackType.Unpacked);
                return new S4DPacket { Data = n > 0 ? r.ReadBytesAllocated(n) : new byte[0] };
            };
        }

        private void Awake() => Instance = this;

        private void Update()
        {
            var nm = InstanceFinder.NetworkManager;
            if (nm != null && nm != _registeredOn)
            {
                // STRAFTAT reloads its NetworkManager (leaving a lobby, returning to the menu): register again.
                nm.ServerManager.RegisterBroadcast<S4DPacket>(OnServerPacket);
                nm.ClientManager.RegisterBroadcast<S4DPacket>(OnClientPacket);
                _registeredOn = nm;
            }
            if (nm == null || !Plugin.Settings.Enabled.Value) return;

            if (IsHost && nm.ServerManager.Clients.Count > 1)
            {
                if (Time.unscaledTime >= _nextStates) { _nextStates = Time.unscaledTime + 0.1f; SendStates(); }
                if (Time.unscaledTime >= _nextHealth) { _nextHealth = Time.unscaledTime + 0.1f; SendHealthStates(); }
            }
            if (IsRemoteClient)
                ClientUpdate();
        }

        // ---------------------------------------------------------------- encoding

        private static MemoryStream _ms = new MemoryStream(256);
        private static BinaryWriter Begin(Msg type)
        {
            _ms.SetLength(0);
            var w = new BinaryWriter(_ms);
            w.Write((byte)type);
            return w;
        }
        private static S4DPacket End(BinaryWriter w) { w.Flush(); return new S4DPacket { Data = _ms.ToArray() }; }
        private static void V3(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
        private static Vector3 V3(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        private static bool HasRemotePlayers => IsHost && InstanceFinder.ServerManager.Clients.Count > 1;

        private static void ToAll(BinaryWriter w, Channel ch = Channel.Reliable)
        {
            if (!HasRemotePlayers) return;
            InstanceFinder.ServerManager.Broadcast(End(w), true, ch);
        }

        private static void ToConnection(NetworkConnection c, BinaryWriter w, Channel ch = Channel.Reliable)
        {
            if (c == null || !c.IsActive) return;
            InstanceFinder.ServerManager.Broadcast(c, End(w), true, ch);
        }

        private static void ToHost(BinaryWriter w)
        {
            if (!IsRemoteClient || !InstanceFinder.ClientManager.Started) return;
            InstanceFinder.ClientManager.Broadcast(End(w));
        }

        /// <summary>The connection that owns this player, when it isn't the host's own.</summary>
        private static NetworkConnection RemoteOwner(PlayerHealth p)
        {
            if (p == null || !IsHost) return null;
            var owner = p.Owner;
            return owner != null && owner.IsActive && !owner.IsLocalClient ? owner : null;
        }

        // ---------------------------------------------------------------- host: infected

        internal ushort Register(MonoBehaviour infected)
        {
            ushort id = _nextId++;
            if (_nextId == 0) _nextId = 1;
            _infected[id] = infected;
            return id;
        }

        internal void Unregister(ushort id, bool announce)
        {
            if (!_infected.Remove(id) || !announce || !IsHost) return;
            var w = Begin(Msg.Despawn); w.Write(id); ToAll(w);
        }

        internal static void SpawnZombie(Zombie z)
        {
            if (!HasRemotePlayers) return;
            ToAll(WriteSpawn(z));
        }

        internal static void SpawnSpecial(SpecialInfected s)
        {
            if (!HasRemotePlayers) return;
            ToAll(WriteSpawn(s));
        }

        private static BinaryWriter WriteSpawn(MonoBehaviour m)
        {
            var w = Begin(Msg.Spawn);
            if (m is Zombie z)
            {
                w.Write(z.NetId); w.Write((byte)0);
                V3(w, z.transform.position); w.Write(z.transform.eulerAngles.y);
                z.Look.Write(w);
            }
            else
            {
                var s = (SpecialInfected)m;
                w.Write(s.NetId); w.Write(s.Kind);
                V3(w, s.transform.position); w.Write(s.transform.eulerAngles.y);
            }
            return w;
        }

        private void SendStates()
        {
            var live = _infected.Where(kv => kv.Value != null).ToList();
            if (live.Count == 0) return;
            // Unreliable and frequent; split so one packet stays well under a datagram.
            for (int start = 0; start < live.Count; start += 40)
            {
                var w = Begin(Msg.States);
                var chunk = live.Skip(start).Take(40).ToList();
                w.Write((byte)chunk.Count);
                foreach (var kv in chunk)
                {
                    var t = kv.Value.transform;
                    w.Write(kv.Key);
                    V3(w, t.position);
                    w.Write((ushort)(Mathf.Repeat(t.eulerAngles.y, 360f) / 360f * 65535f));
                    w.Write(StateIndex(kv.Value is Zombie z ? z.State : ((SpecialInfected)kv.Value).State));
                }
                ToAll(w, Channel.Unreliable);
            }
        }

        private static byte StateIndex(string state)
        {
            int i = Array.IndexOf(States, state ?? "");
            return (byte)Math.Max(0, i);
        }

        internal static void Say(ushort id, string group, float volume)
        {
            if (!HasRemotePlayers || id == 0) return;
            var w = Begin(Msg.Say); w.Write(id); w.Write(group); w.Write(volume); ToAll(w);
        }

        internal static void Hurt(ushort id, float hp)
        {
            if (!HasRemotePlayers || id == 0) return;
            var w = Begin(Msg.Hurt); w.Write(id); w.Write(hp); ToAll(w);
        }

        internal static void Death(ushort id, Vector3 dir, bool running)
        {
            if (!HasRemotePlayers || id == 0) return;
            var w = Begin(Msg.Death); w.Write(id); V3(w, dir); w.Write(running); ToAll(w);
        }

        internal static void Clear()
        {
            if (!HasRemotePlayers) return;
            ToAll(Begin(Msg.Clear));
        }

        internal static void Rock(Vector3 from, Vector3 velocity)
        {
            if (!HasRemotePlayers) return;
            var w = Begin(Msg.Rock); V3(w, from); V3(w, velocity); ToAll(w);
        }

        internal static void Music(bool horde)
        {
            if (!HasRemotePlayers) return;
            var w = Begin(Msg.Music); w.Write(horde); ToAll(w);
        }

        internal static void Startle(ushort id)
        {
            if (!HasRemotePlayers || id == 0) return;
            var w = Begin(Msg.Startle); w.Write(id); ToAll(w);
        }

        // ---------------------------------------------------------------- host: survivors

        /// <summary>A voice line for a player whose game runs elsewhere.</summary>
        internal static bool VoiceForRemote(PlayerHealth p, string concept, bool interrupt)
        {
            var c = RemoteOwner(p);
            if (c == null) return false;
            var w = Begin(Msg.Voice); w.Write(concept); w.Write(interrupt); ToConnection(c, w);
            return true;
        }

        internal static void BileForRemote(PlayerHealth p)
        {
            var c = RemoteOwner(p);
            if (c == null) return;
            ToConnection(c, Begin(Msg.Bile));
        }

        private void SendHealthStates()
        {
            foreach (var s in SurvivorHealth.All)
            {
                var c = RemoteOwner(s != null ? s.Player : null);
                if (c == null) continue;
                var w = Begin(Msg.HealthState);
                w.Write(s.Permanent); w.Write(s.Temporary); w.Write(s.Max);
                w.Write((byte)s.Medkits); w.Write((byte)s.Pills);
                w.Write(s.Incapacitated); w.Write(s.IncapProgress); w.Write(s.HealProgress); w.Write(s.ReviveProgress);
                ToConnection(c, w, Channel.Unreliable);
            }
        }

        private static float _nextDowned;

        /// <summary>Who is down and how far their revive is, for everyone's prompts and bars (5 Hz).</summary>
        internal static void SendDowned()
        {
            if (!HasRemotePlayers || Time.unscaledTime < _nextDowned) return;
            _nextDowned = Time.unscaledTime + 0.2f;
            var w = Begin(Msg.Downed);
            w.Write((byte)Revives.Downed.Count);
            foreach (var kv in Revives.Downed) { w.Write(kv.Key); w.Write(kv.Value); }
            ToAll(w, Channel.Unreliable);
        }

        private static void SendSurvivorMap()
        {
            if (!HasRemotePlayers) return;
            var w = Begin(Msg.SurvivorMap);
            w.Write((byte)SurvivorOf.Count);
            foreach (var kv in SurvivorOf) { w.Write(kv.Key); w.Write(kv.Value); }
            ToAll(w);
        }

        /// <summary>Which survivor this player is: their own pick when known, else the local setting.</summary>
        internal static string SurvivorFor(PlayerHealth p)
        {
            if (p == null || p.IsOwner || p.Owner == null) return Plugin.Settings.SurvivorName.Value;
            return SurvivorOf.TryGetValue(p.OwnerId, out var name) ? name : Plugin.Settings.SurvivorName.Value;
        }

        // ---------------------------------------------------------------- host: receiving

        private void OnServerPacket(NetworkConnection conn, S4DPacket packet)
        {
            if (packet.Data == null || packet.Data.Length == 0 || conn == null || conn.IsLocalClient) return;
            try
            {
                using (var r = new BinaryReader(new MemoryStream(packet.Data)))
                {
                    var type = (Msg)r.ReadByte();
                    switch (type)
                    {
                        case Msg.Hello:
                        {
                            string survivor = r.ReadString();
                            SurvivorOf[conn.ClientId] = survivor;
                            if (InstanceFinder.ClientManager.Connection != null)
                                SurvivorOf[InstanceFinder.ClientManager.Connection.ClientId] = Plugin.Settings.SurvivorName.Value;
                            Plugin.Logger.LogInfo($"[net] client {conn.ClientId} joined as {survivor}; sending {_infected.Count} infected");
                            foreach (var kv in _infected.Where(kv => kv.Value != null).ToList())
                                ToConnection(conn, WriteSpawn(kv.Value));
                            SendSurvivorMap();
                            break;
                        }
                        case Msg.Hit:
                        {
                            ushort id = r.ReadUInt16(); float hp = r.ReadSingle();
                            Vector3 point = V3(r), dir = V3(r); bool head = r.ReadBoolean();
                            if (_infected.TryGetValue(id, out var m) && m is IInfected inf && !inf.IsDead && hp > 0f && hp < 10000f)
                            {
                                bool killed = inf.TakeDamage(hp, point, dir, head);
                                if (killed) SurvivorVoice.KillBy(PlayerOf(conn));
                            }
                            break;
                        }
                        case Msg.UsePills:
                            SurvivorHealth.For(PlayerOf(conn))?.TakePills();
                            break;
                        case Msg.Heal:
                            SurvivorHealth.For(PlayerOf(conn))?.SetHealing(r.ReadBoolean());
                            break;
                        case Msg.Revive:
                        {
                            bool holding = r.ReadBoolean();
                            Revives.SetReviving(PlayerOf(conn), holding);
                            break;
                        }
                    }
                }
            }
            catch (Exception e) { Plugin.Logger.LogWarning($"[net] bad packet from {conn.ClientId}: {e.Message}"); }
        }

        private static PlayerHealth PlayerOf(NetworkConnection conn) =>
            conn == null ? null : FindObjectsOfType<PlayerHealth>().FirstOrDefault(p => p.OwnerId == conn.ClientId);

        // ---------------------------------------------------------------- client

        private void ClientUpdate()
        {
            // Our own survivor's numbers live on the host; keep a mirror for the HUD.
            if (SurvivorHealth.Local == null)
            {
                var mine = FindObjectsOfType<PlayerHealth>().FirstOrDefault(p => p.IsOwner);
                if (mine != null) SurvivorHealth.For(mine);
            }
            string scene = SceneManager.GetActiveScene().name;
            if (scene != _helloScene) { _helloScene = scene; _helloSent = false; }
            if (_helloSent || scene == "MainMenu" || scene == "VictoryScene" || !InstanceFinder.ClientManager.Started) return;
            if (ZombieDirector.Instance == null) return; // map not up yet
            _helloSent = true;
            var w = Begin(Msg.Hello); w.Write(Plugin.Settings.SurvivorName.Value); ToHost(w);
            Plugin.Logger.LogInfo($"[net] hello sent to host as {Plugin.Settings.SurvivorName.Value}");
        }

        /// <summary>Client: a weapon hit on an infected puppet goes to the host, which owns the real one.</summary>
        internal static void SendHit(IInfected target, float hp, Vector3 point, Vector3 dir, bool head)
        {
            ushort id = target is Zombie z ? z.NetId : target is SpecialInfected s ? s.NetId : (ushort)0;
            if (id == 0) return;
            var w = Begin(Msg.Hit); w.Write(id); w.Write(hp); V3(w, point); V3(w, dir); w.Write(head); ToHost(w);
        }

        internal static void SendUsePills() => ToHost(Begin(Msg.UsePills));
        internal static void SendHeal(bool holding) { var w = Begin(Msg.Heal); w.Write(holding); ToHost(w); }
        internal static void SendRevive(bool holding) { var w = Begin(Msg.Revive); w.Write(holding); ToHost(w); }

        private void OnClientPacket(S4DPacket packet)
        {
            if (!IsRemoteClient || packet.Data == null || packet.Data.Length == 0) return; // the host already did it
            try
            {
                using (var r = new BinaryReader(new MemoryStream(packet.Data)))
                {
                    var type = (Msg)r.ReadByte();
                    switch (type)
                    {
                        case Msg.Spawn:
                        {
                            ushort id = r.ReadUInt16(); byte kind = r.ReadByte();
                            Vector3 pos = V3(r); float yaw = r.ReadSingle();
                            if (_infected.TryGetValue(id, out var existing) && existing != null) break;
                            MonoBehaviour m;
                            if (kind == 0) m = Zombie.CreatePuppet(id, pos, yaw, ZombieLook.Read(r));
                            else m = SpecialInfected.CreatePuppet(id, kind, pos, yaw);
                            if (m != null) _infected[id] = m;
                            break;
                        }
                        case Msg.States:
                        {
                            int n = r.ReadByte();
                            for (int i = 0; i < n; i++)
                            {
                                ushort id = r.ReadUInt16(); Vector3 pos = V3(r);
                                float yaw = r.ReadUInt16() / 65535f * 360f; string state = States[Math.Min(r.ReadByte(), States.Length - 1)];
                                if (_infected.TryGetValue(id, out var m) && m != null)
                                {
                                    if (m is Zombie z) z.PuppetTarget(pos, yaw, state);
                                    else ((SpecialInfected)m).PuppetTarget(pos, yaw, state);
                                }
                            }
                            break;
                        }
                        case Msg.Say:
                        {
                            ushort id = r.ReadUInt16(); string group = r.ReadString(); float vol = r.ReadSingle();
                            if (_infected.TryGetValue(id, out var m) && m != null)
                            {
                                if (m is Zombie z) z.SayLocal(group, vol);
                                else ((SpecialInfected)m).SayLocal(group, vol);
                            }
                            break;
                        }
                        case Msg.Hurt:
                        {
                            ushort id = r.ReadUInt16(); float hp = r.ReadSingle();
                            if (_infected.TryGetValue(id, out var m) && m != null)
                            {
                                if (m is Zombie z) z.HurtVisual(hp);
                                else ((SpecialInfected)m).HurtVisual(hp);
                            }
                            break;
                        }
                        case Msg.Death:
                        {
                            ushort id = r.ReadUInt16(); Vector3 dir = V3(r); bool running = r.ReadBoolean();
                            if (_infected.TryGetValue(id, out var m) && m != null)
                            {
                                if (m is Zombie z) z.PuppetDie(dir, running);
                                else ((SpecialInfected)m).PuppetDie(dir);
                            }
                            break;
                        }
                        case Msg.Despawn:
                        {
                            ushort id = r.ReadUInt16();
                            if (_infected.TryGetValue(id, out var m) && m != null) Destroy(m.gameObject);
                            _infected.Remove(id);
                            break;
                        }
                        case Msg.Clear:
                            foreach (var m in _infected.Values) if (m != null) Destroy(m.gameObject);
                            _infected.Clear();
                            Boomer.LocalBiledUntil = 0f;
                            break;
                        case Msg.Rock:
                        {
                            Vector3 from = V3(r), vel = V3(r);
                            TankRock.SpawnVisual(from, vel);
                            break;
                        }
                        case Msg.Music:
                        {
                            bool on = r.ReadBoolean();
                            var music = ZombieDirector.Instance != null ? ZombieDirector.Instance.Music : null;
                            if (music != null) { if (on) music.StartHorde(); else music.EndHorde(); }
                            break;
                        }
                        case Msg.Startle:
                        {
                            ushort id = r.ReadUInt16();
                            if (_infected.TryGetValue(id, out var m) && m is Witch witch) witch.StartleVisual();
                            break;
                        }
                        case Msg.HealthState:
                        {
                            var me = SurvivorHealth.Local;
                            float perm = r.ReadSingle(), temp = r.ReadSingle(), max = r.ReadSingle();
                            int kits = r.ReadByte(), pills = r.ReadByte();
                            bool incap = r.ReadBoolean(); float incapP = r.ReadSingle(), healP = r.ReadSingle(), reviveP = r.ReadSingle();
                            if (me != null) me.ApplyMirror(perm, temp, max, kits, pills, incap, incapP, healP, reviveP);
                            break;
                        }
                        case Msg.Voice:
                        {
                            string concept = r.ReadString(); bool interrupt = r.ReadBoolean();
                            SurvivorVoice.PlayLocal(concept, interrupt);
                            break;
                        }
                        case Msg.Bile:
                            Boomer.LocalBiledUntil = Time.time + 12f;
                            break;
                        case Msg.Downed:
                        {
                            int n = r.ReadByte();
                            Revives.Downed.Clear();
                            for (int i = 0; i < n; i++) { int id = r.ReadInt32(); Revives.Downed[id] = r.ReadSingle(); }
                            break;
                        }
                        case Msg.SurvivorMap:
                        {
                            int n = r.ReadByte();
                            SurvivorOf.Clear();
                            for (int i = 0; i < n; i++) { int id = r.ReadInt32(); SurvivorOf[id] = r.ReadString(); }
                            break;
                        }
                    }
                }
            }
            catch (Exception e) { Plugin.Logger.LogWarning($"[net] bad packet from host: {e.Message}"); }
        }

        /// <summary>Client: the map changed or we left; forget the puppets.</summary>
        internal void ForgetPuppets()
        {
            _infected.Clear();
        }
    }
}
