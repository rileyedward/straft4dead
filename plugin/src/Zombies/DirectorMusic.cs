using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Straft4Dead.Zombies
{
    /// <summary>The horde cue from the L4D2 install: a mob stinger (music/mob/*germ*) when the horde is called, then
    /// the zombat drum loops (music/zombat/horde) until it's over. Loaded on demand from the loose sound files.</summary>
    internal sealed class DirectorMusic : MonoBehaviour
    {
        private readonly List<AudioClip> _stingers = new List<AudioClip>(), _drums = new List<AudioClip>();
        private AudioSource _sting, _loop;
        private bool _playing, _loaded;

        private void Awake()
        {
            _sting = gameObject.AddComponent<AudioSource>();
            _loop = gameObject.AddComponent<AudioSource>();
            foreach (var s in new[] { _sting, _loop }) { s.spatialBlend = 0f; s.playOnAwake = false; }
            _sting.volume = 0.8f;
            if (Plugin.Settings.HordeMusic.Value) StartCoroutine(Load());
        }

        private IEnumerator Load()
        {
            string music = Path.Combine(Plugin.Settings.L4D2SoundDir.Value, "music");
            var stingers = Directory.Exists(Path.Combine(music, "mob"))
                ? Directory.GetFiles(Path.Combine(music, "mob"), "*germ*.wav").OrderBy(_ => Random.value).Take(8).ToArray() : new string[0];
            var drums = Directory.Exists(Path.Combine(music, "zombat", "horde"))
                ? Directory.GetFiles(Path.Combine(music, "zombat", "horde"), "*.wav") : new string[0];
            foreach (var (files, list) in new[] { (stingers, _stingers), (drums, _drums) })
                foreach (var f in files)
                    using (var r = UnityWebRequestMultimedia.GetAudioClip("file:///" + f.Replace('\\', '/'), AudioType.WAV))
                    {
                        yield return r.SendWebRequest();
                        if (r.result == UnityWebRequest.Result.Success) list.Add(DownloadHandlerAudioClip.GetContent(r));
                    }
            _loaded = true;
            Plugin.Logger.LogInfo($"[director] horde music: {_stingers.Count} stingers, {_drums.Count} drum loops");
        }

        internal void StartHorde()
        {
            if (!_loaded) return;
            _playing = true;
            if (_stingers.Count > 0) _sting.PlayOneShot(_stingers[Random.Range(0, _stingers.Count)]);
            _loop.volume = 0.55f;
        }

        internal void EndHorde() => _playing = false;

        private void Update()
        {
            if (_playing)
            {
                // Chain random drum segments back to back while the horde lasts.
                if (!_loop.isPlaying && _drums.Count > 0)
                {
                    _loop.clip = _drums[Random.Range(0, _drums.Count)];
                    _loop.Play();
                }
            }
            else if (_loop.isPlaying)
            {
                _loop.volume = Mathf.MoveTowards(_loop.volume, 0f, Time.deltaTime * 0.3f);
                if (_loop.volume <= 0f) _loop.Stop();
            }
        }
    }
}
