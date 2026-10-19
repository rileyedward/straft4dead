using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HarmonyLib;
using Straft4Dead.Survivors;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace Straft4Dead.Hud
{
    /// <summary>L4D2 survivor HUD (Phase 2), drawn on our own overlay canvas from textures converted out of the player's
    /// L4D2 install. Layout follows L4D2's resource/ui/hud/localplayerpanel.res (640x480 units, scaled): survivor panel
    /// bottom-left with portrait, health number and bar (green/orange/red, temp health striped white), item slots for
    /// medkit and pills, ammo bottom-right; plus the incap overlay and the medkit progress bar.
    /// Also owns the local player's item input and incap view (camera drop, movement lock, sting).</summary>
    internal sealed class L4D2Hud : MonoBehaviour
    {
        private readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>();
        private Canvas _canvas;
        private RectTransform _panel, _barFill, _barTemp, _healBar, _healFill;
        private float _lastTotal, _hurtFlash;
        private Image _barFillImg, _portrait, _medkitIcon, _pillsIcon, _redOverlay;
        private Text _healthText, _ammoText, _incapText, _medkitCount, _pillsCount;
        private Font _font;
        private AudioSource _audio;
        private AudioClip _sting;
        private bool _wasIncap;
        private float _hiddenAt = -1f;
        private Vector3 _camRestPos;
        private Quaternion _camRestRot;
        private Transform _cam;

        private const float BarWidth = 222f; // health bar width in px at 1080p (111 units in the .res, x2)

        private IEnumerator Start()
        {
            _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            LoadSprites(Plugin.Settings.HudTextureDir.Value);
            BuildCanvas();
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.spatialBlend = 0f;
            string sting = Path.Combine(Plugin.Settings.L4D2SoundDir.Value, "music", "tags", "clingingtohellhit1.wav");
            if (File.Exists(sting))
            {
                using (var req = UnityWebRequestMultimedia.GetAudioClip("file:///" + sting.Replace('\\', '/'), AudioType.WAV))
                {
                    yield return req.SendWebRequest();
                    if (req.result == UnityWebRequest.Result.Success)
                        _sting = DownloadHandlerAudioClip.GetContent(req);
                }
            }
            Plugin.Logger.LogInfo($"[hud] L4D2 HUD ready: {_sprites.Count} textures, sting {(_sting != null ? "loaded" : "missing: " + sting)}");
        }

        private void LoadSprites(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                Plugin.Logger.LogWarning($"[hud] texture folder not found: '{dir}' (HUD falls back to flat colours)");
                return;
            }
            foreach (var path in Directory.GetFiles(dir, "*.png"))
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                if (!tex.LoadImage(File.ReadAllBytes(path)))
                    continue;
                _sprites[Path.GetFileNameWithoutExtension(path)] = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            }
        }

        private Sprite S(string name) => _sprites.TryGetValue(name, out var s) ? s : null;

        private void BuildCanvas()
        {
            var go = new GameObject("S4D_L4D2Hud");
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 50;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;

            // Incap / low-health overlay first so it sits under the panel.
            _redOverlay = Img(go.transform, "RedOverlay", null, new Color(0.6f, 0f, 0f, 0f));
            Stretch(_redOverlay.rectTransform);

            // Survivor panel, bottom-left. healthbar_bg_player is 220x55 units in the .res; ~x2 at 1080p.
            _panel = Img(go.transform, "Panel", S("healthbar_bg_player"), new Color(80 / 255f, 76 / 255f, 82 / 255f, 1f)).rectTransform;
            Anchor(_panel, new Vector2(0, 0), new Vector2(40, 30), new Vector2(440, 110));
            _panel.localScale = Vector3.one * 1.3f;
            _portrait = Img(_panel, "Portrait", S("s_panel_" + Plugin.Settings.SurvivorName.Value), Color.white);
            Place(_portrait.rectTransform, new Vector2(14, 14), new Vector2(72, 70));

            var barBg = Img(_panel, "BarBack", null, new Color(0, 0, 0, 0.6f)).rectTransform;
            Place(barBg, new Vector2(88, 14), new Vector2(BarWidth, 30));
            _barFillImg = Img(barBg, "Fill", S("healthbar_withglow_green"), Color.white);
            _barFill = _barFillImg.rectTransform;
            LeftBar(_barFill);
            _barTemp = Img(barBg, "Temp", S("healthbar_ticks_withglow_white") ?? S("healthbar_withglow_white"), new Color(1, 1, 1, 0.85f)).rectTransform;
            LeftBar(_barTemp);
            var outline = Img(barBg, "Outline", S("s_healthbar_outline"), Color.white).rectTransform;
            Stretch(outline);

            var cross = Img(_panel, "Cross", S("s_panel_healthcross"), Color.white).rectTransform;
            Place(cross, new Vector2(92, 52), new Vector2(26, 26));
            _healthText = Txt(_panel, "Health", 34, TextAnchor.MiddleLeft, Color.white);
            Place(_healthText.rectTransform, new Vector2(124, 46), new Vector2(120, 40));

            // Item slots to the right of the panel.
            _medkitIcon = Img(go.transform, "Medkit", S("inventory_firstaidkit"), Color.white);
            Anchor(_medkitIcon.rectTransform, new Vector2(0, 0), new Vector2(630, 30), new Vector2(110, 110));
            _medkitCount = Txt(_medkitIcon.rectTransform, "Count", 22, TextAnchor.LowerRight, Color.white);
            Stretch(_medkitCount.rectTransform);
            _pillsIcon = Img(go.transform, "Pills", S("inventory_painpills"), Color.white);
            Anchor(_pillsIcon.rectTransform, new Vector2(0, 0), new Vector2(745, 30), new Vector2(110, 110));
            _pillsCount = Txt(_pillsIcon.rectTransform, "Count", 22, TextAnchor.LowerRight, Color.white);
            Stretch(_pillsCount.rectTransform);

            // Ammo, bottom-right.
            _ammoText = Txt(go.transform, "Ammo", 46, TextAnchor.LowerRight, new Color(1f, 0.95f, 0.85f));
            Anchor(_ammoText.rectTransform, new Vector2(1, 0), new Vector2(-40, 40), new Vector2(420, 70), new Vector2(1, 0));

            // Medkit progress bar, centre.
            _healBar = Img(go.transform, "HealBar", S("scalablepanel_bgblack50_outlinegrey"), Color.white).rectTransform;
            Anchor(_healBar, new Vector2(0.5f, 0.5f), new Vector2(0, -120), new Vector2(360, 26), new Vector2(0.5f, 0.5f));
            _healFill = Img(_healBar, "Fill", null, new Color(0.25f, 0.8f, 0.25f, 0.9f)).rectTransform;
            LeftBar(_healFill);

            _incapText = Txt(go.transform, "Incap", 40, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.85f));
            Anchor(_incapText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 160), new Vector2(900, 60), new Vector2(0.5f, 0.5f));
        }

        private void Update()
        {
            var me = SurvivorHealth.Local;
            bool show = me != null && Plugin.Settings.HudEnabled.Value && Plugin.Settings.Enabled.Value;
            _canvas.enabled = show;
            if (!show)
            {
                _wasIncap = false;
                if (_gameHudHidden && me != null) { _hiddenAt = -10f; SetGameHud(me.Player, true); } // switched off live: give STRAFTAT's HUD back
                return;
            }
            SetGameHud(me.Player, false);
            HandleInput(me);

            float max = me.Max, perm = me.Permanent, temp = me.Temporary, total = perm + temp;
            _healthText.text = me.Incapacitated ? "0" : Mathf.CeilToInt(total).ToString();
            string tint = total >= 40f * max / 100f ? "green" : total >= 25f * max / 100f ? "orange" : "red";
            _barFillImg.sprite = S("healthbar_withglow_" + tint) ?? _barFillImg.sprite;
            if (_barFillImg.sprite == null) _barFillImg.color = tint == "green" ? Color.green : tint == "orange" ? new Color(1f, 0.6f, 0f) : Color.red;
            SetWidth(_barFill, BarWidth * Mathf.Clamp01(perm / max), 0f);
            SetWidth(_barTemp, BarWidth * Mathf.Clamp01(temp / max), BarWidth * Mathf.Clamp01(perm / max));

            _medkitIcon.enabled = me.Medkits > 0;
            _medkitCount.text = me.Medkits > 1 ? me.Medkits.ToString() : "";
            _pillsIcon.enabled = me.Pills > 0;
            _pillsCount.text = me.Pills > 1 ? me.Pills.ToString() : "";
            _ammoText.text = AmmoText(me.Player);

            // One progress bar for healing yourself, being revived, or reviving a teammate.
            float bar = Mathf.Max(me.HealProgress, me.ReviveProgress, Revives.LocalReviveProgress);
            _healBar.gameObject.SetActive(bar > 0f);
            SetWidth(_healFill, 352f * bar, 4f);

            // Incap: portrait swap, red screen, camera drop, sting, movement lock.
            _portrait.sprite = S("s_panel_" + Plugin.Settings.SurvivorName.Value + (me.Incapacitated ? "_incap" : "")) ?? _portrait.sprite;
            float lowHp = !me.Incapacitated && total < 25f * max / 100f ? 0.12f + 0.06f * Mathf.Sin(Time.time * 6f) : 0f;
            // A faint flash per hit (zombie hits skip STRAFTAT's full-screen hit feedback, see GameAccess.DamagePlayer).
            if (total < _lastTotal - 0.5f) _hurtFlash = 0.16f;
            _lastTotal = total;
            _hurtFlash = Mathf.Max(0f, _hurtFlash - Time.deltaTime * 0.4f);
            _redOverlay.color = new Color(0.55f, 0f, 0f, me.Incapacitated ? 0.35f + 0.25f * me.IncapProgress : Mathf.Max(lowHp, _hurtFlash));
            // Boomer bile: a murky green film that clears over the last few seconds.
            float bile = Mathf.Clamp01((Zombies.Boomer.LocalBiledUntil - Time.time) / 4f);
            if (bile > 0f && !me.Incapacitated)
                _redOverlay.color = new Color(0.32f, 0.42f, 0.08f, 0.55f * bile + 0.05f * Mathf.Sin(Time.time * 3f) * bile);
            _incapText.text = me.Incapacitated ? (me.ReviveProgress > 0f ? "BEING REVIVED" : "YOU'RE INCAPACITATED") : Revives.Prompt ?? "";
            IncapView(me);
        }

        private void HandleInput(SurvivorHealth me)
        {
            var kb = Keyboard.current;
            if (kb == null || PauseManager.Instance != null && (PauseManager.Instance.pause || PauseManager.Instance.chatting))
                return;
            var pills = KeyOf(Plugin.Settings.PillsKey.Value);
            var medkit = KeyOf(Plugin.Settings.MedkitKey.Value);
            if (pills != null && pills.wasPressedThisFrame)
                me.TakePills();
            if (medkit != null && (medkit.isPressed || medkit.wasReleasedThisFrame))
                me.SetHealing(medkit.isPressed);
        }

        private static KeyControl KeyOf(string name)
        {
            var kb = Keyboard.current;
            if (kb == null || string.IsNullOrEmpty(name)) return null;
            if (name.Length == 1 && char.IsDigit(name[0])) name = "Digit" + name;
            return System.Enum.TryParse<Key>(name, true, out var key) ? kb[key] : null;
        }

        private void IncapView(SurvivorHealth me)
        {
            var fpc = me.Player.GetComponent<FirstPersonController>();
            if (fpc == null || fpc.playerCamera == null)
                return;
            if (me.Incapacitated && !_wasIncap)
            {
                _cam = fpc.playerCamera.transform;
                _camRestPos = _cam.localPosition;
                _camRestRot = _cam.localRotation;
                if (_sting != null) _audio.PlayOneShot(_sting);
            }
            if (me.Incapacitated)
            {
                fpc.canMove = false;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(me.IncapProgress * 4f));
                _cam.localPosition = _camRestPos + Vector3.down * 1.1f * t;
                _cam.localRotation = _camRestRot * Quaternion.Euler(0f, 0f, 18f * t);
            }
            else if (_wasIncap && _cam != null)
            {
                _cam.localPosition = _camRestPos;
                _cam.localRotation = _camRestRot;
            }
            _wasIncap = me.Incapacitated;
        }

        private static string AmmoText(PlayerHealth p)
        {
            var pickup = p.GetComponent<PlayerPickup>();
            if (pickup == null) return "";
            var right = Traverse.Create(pickup).Field("weaponInHand").GetValue<Weapon>();
            var left = Traverse.Create(pickup).Field("weaponInLeftHand").GetValue<Weapon>();
            var sb = new StringBuilder();
            if (left != null && left.needsAmmo) sb.Append(left.sync___get_value_currentAmmo()).Append("   ");
            if (right != null && right.needsAmmo) sb.Append(right.sync___get_value_currentAmmo());
            return sb.ToString();
        }

        private bool _gameHudHidden;

        /// <summary>Hide STRAFTAT's own health and ammo widgets (systems-map §8) while our HUD is up, or show them again.</summary>
        private void SetGameHud(PlayerHealth p, bool visible)
        {
            if (Time.time < _hiddenAt + 1f)
                return;
            _hiddenAt = Time.time;
            _gameHudHidden = !visible;
            foreach (var t in FindObjectsOfType<HealthTween>())
                SetActiveIfDifferent(GameHudWidget(t.transform), visible);
            // STRAFTAT's HUD is 3D: SM_HUD_00 under the UI camera holds the heart and the two ammo rings.
            var hud3d = FindDeep(p.transform, "SM_HUD_00");
            if (hud3d != null)
            {
                if (!_dumped) { _dumped = true; Plugin.Logger.LogInfo("[hud] STRAFTAT 3D HUD: " + Tree(hud3d, 0)); }
                var frame = hud3d.GetComponent<Renderer>();
                if (frame != null && frame.enabled == !visible) { frame.enabled = visible; Plugin.Logger.LogInfo($"[hud] STRAFTAT HUD frame mesh {(visible ? "shown" : "hidden")}"); }
                foreach (Transform child in hud3d)
                    if (HideNames.Exists(n => child.name.StartsWith(n)))
                        SetActiveIfDifferent(child.gameObject, visible);
            }
            var pm = PauseManager.Instance;
            if (pm != null)
                foreach (var txt in new Component[] { pm.rightGunAmmo, pm.leftGunAmmo, pm.rightGunAmmoReload, pm.leftGunAmmoReload, pm.minimalistHealthText })
                    if (txt != null) SetActiveIfDifferent(GameHudWidget(txt.transform), visible);
        }

        private bool _dumped;
        private static readonly List<string> HideNames = new List<string> { "SM_Heart", "SM_BulletCounter" }; // keep SM_FPS_Counter and SM_MatchPoints

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var r = FindDeep(c, name); if (r != null) return r; }
            return null;
        }

        private static string Tree(Transform t, int depth)
        {
            if (depth > 2) return "";
            var sb = new StringBuilder(t.name);
            if (t.childCount > 0)
            {
                sb.Append(" {");
                foreach (Transform c in t) sb.Append(Tree(c, depth + 1)).Append("; ");
                sb.Append("}");
            }
            return sb.ToString();
        }

        /// <summary>The widget root: climb to the child of the HUD canvas so the whole frame goes, not just the number.</summary>
        private static GameObject GameHudWidget(Transform t)
        {
            // Climb only inside a canvas: the HealthTween can sit under the player object itself, and hiding that
            // root would deactivate the player.
            var canvas = t.GetComponentInParent<Canvas>();
            if (canvas == null || canvas.transform == t)
                return t.gameObject;
            while (t.parent != null && t.parent != canvas.transform)
                t = t.parent;
            return t.gameObject;
        }

        // Only what we hid comes back when the mode is switched off (some widgets are off in vanilla too).
        private static readonly HashSet<GameObject> HiddenByUs = new HashSet<GameObject>();

        private static void SetActiveIfDifferent(GameObject go, bool active)
        {
            if (go.activeSelf != active && (!active || HiddenByUs.Remove(go)))
            {
                if (!active) HiddenByUs.Add(go);
                go.SetActive(active);
                Plugin.Logger.LogInfo($"[hud] {(active ? "showing" : "hiding")} STRAFTAT widget '{PathOf(go.transform)}'");
            }
        }

        private static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;

        // --- small uGUI helpers -------------------------------------------------------------------------------------

        private static Image Img(Transform parent, string name, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private Text Txt(Transform parent, string name, int size, TextAnchor align, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = _font;
            t.fontSize = size;
            t.fontStyle = FontStyle.Bold;
            t.alignment = align;
            t.color = color;
            t.raycastTarget = false;
            go.AddComponent<Shadow>().effectDistance = new Vector2(2, -2);
            return t;
        }

        private static void Anchor(RectTransform r, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
        {
            r.anchorMin = r.anchorMax = anchor;
            r.pivot = pivot ?? Vector2.zero;
            r.anchoredPosition = pos;
            r.sizeDelta = size;
        }

        private static void Place(RectTransform r, Vector2 pos, Vector2 size) => Anchor(r, Vector2.zero, pos, size);

        private static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
        }

        private static void LeftBar(RectTransform r)
        {
            r.anchorMin = new Vector2(0, 0);
            r.anchorMax = new Vector2(0, 1);
            r.pivot = new Vector2(0, 0.5f);
            r.offsetMin = new Vector2(0, 2);
            r.offsetMax = new Vector2(0, -2);
        }

        private static void SetWidth(RectTransform r, float width, float x)
        {
            r.anchoredPosition = new Vector2(x, r.anchoredPosition.y);
            r.sizeDelta = new Vector2(width, r.sizeDelta.y);
        }
    }
}
