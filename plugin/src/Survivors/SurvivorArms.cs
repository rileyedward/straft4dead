using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Straft4Dead.Survivors
{
    /// <summary>L4D2 survivor first-person arms on STRAFTAT's arm rig. STRAFTAT keeps animating its own (now hidden)
    /// Aboubi arms, IK and all; every frame we copy that pose onto the L4D2 arms bone by bone.
    ///
    /// The two rigs have different bone axes and rest poses, so rotations can't be copied directly. Instead each bone
    /// gets an anatomical frame built the same way on both rigs (along the bone towards its child, rolled by the elbow
    /// bend or the knuckle line), and the L4D2 bone keeps its bind-pose offset to that frame. Shoulder, elbow and wrist
    /// positions are copied too, so the hands land where STRAFTAT holds the gun; fingers only rotate.</summary>
    [DefaultExecutionOrder(10000)]
    internal sealed class SurvivorArms : MonoBehaviour
    {
        private static readonly string[] FingerSrc = { "Thumb", "Index", "MiddleFinger", "RingFinger", "Pinkie" };

        private sealed class Side
        {
            // [0] clavicle, [1] upper arm, [2] forearm, [3] hand; fingers [f][0..2] (L4D2 has a third segment).
            public Transform[] A = new Transform[4], B = new Transform[4];
            public Transform[][] AF = new Transform[5][], BF = new Transform[5][];
            public Quaternion[] Off = new Quaternion[4];
            public Quaternion[][] OffF = new Quaternion[5][];
        }

        private readonly List<Side> _sides = new List<Side>();
        private readonly List<Renderer> _hidden = new List<Renderer>();
        private GameObject _arms;
        private Transform _armsRoot;
        private float _handScaleL = 1f, _handScaleR = 1f;
        private static int _logs;

        internal static void AttachAll()
        {
            if (!Plugin.Settings.SurvivorArms.Value || SurvivorAssets.Instance == null || !SurvivorAssets.Instance.Loaded)
                return;
            foreach (var fp in Object.FindObjectsOfType<FPArms>())
                if (fp.GetComponent<SurvivorArms>() == null)
                    fp.gameObject.AddComponent<SurvivorArms>();
        }

        private void Start()
        {
            var prefab = SurvivorAssets.Instance.Prefab(Plugin.Settings.SurvivorName.Value, "Arms");
            var byName = GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            if (prefab == null || !byName.TryGetValue("Armature", out var armature))
            {
                Plugin.Logger.LogWarning($"[survivors] arms: {(prefab == null ? "no arms prefab for " + Plugin.Settings.SurvivorName.Value : "no STRAFTAT arm rig")}");
                enabled = false;
                return;
            }

            _arms = Instantiate(prefab, armature.parent, false);
            _arms.name = "S4D_SurvivorArms";
            int layer = armature.gameObject.layer; // drawn by STRAFTAT's WeaponCam
            foreach (var t in _arms.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            var anim = _arms.GetComponent<Animator>();
            if (anim != null) anim.enabled = false;
            foreach (var r in _arms.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                r.updateWhenOffscreen = true; // bones move far from the prefab root
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            var bByName = _arms.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            _armsRoot = _arms.transform;

            // Hide the Aboubi arms but keep them animating: they are the pose source.
            foreach (var r in armature.parent.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (!r.transform.IsChildOf(_arms.transform)) _hidden.Add(r);
            foreach (var a in GetComponentsInChildren<Animator>(true))
                if (a != anim) a.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            foreach (var (s, v) in new[] { ("L", "L"), ("R", "R") })
            {
                var side = new Side();
                string[] aNames = { $"Shoulder.{s}", $"Arm.{s}", $"ForeArm.{s}", $"Wrist.{s}" };
                string[] bNames = { "Clavicle", "UpperArm", "Forearm", "Hand" };
                bool ok = true;
                for (int i = 0; i < 4; i++)
                {
                    ok &= byName.TryGetValue(aNames[i], out side.A[i]);
                    ok &= bByName.TryGetValue($"ValveBiped.Bip01_{v}_{bNames[i]}", out side.B[i]);
                }
                for (int f = 0; f < 5; f++)
                {
                    byName.TryGetValue($"{FingerSrc[f]}_1.{s}", out var a1);
                    byName.TryGetValue($"{FingerSrc[f]}_2.{s}", out var a2);
                    side.AF[f] = new[] { a1, a2 };
                    bByName.TryGetValue($"ValveBiped.Bip01_{v}_Finger{f}", out var b0);
                    bByName.TryGetValue($"ValveBiped.Bip01_{v}_Finger{f}1", out var b1);
                    bByName.TryGetValue($"ValveBiped.Bip01_{v}_Finger{f}2", out var b2);
                    side.BF[f] = new[] { b0, b1, b2 };
                    ok &= a1 && a2 && b0 && b1 && b2;
                }
                if (!ok) { Plugin.Logger.LogWarning($"[survivors] arms: missing bones on side {s}"); continue; }
                _sides.Add(side);
            }
            if (_sides.Count == 0) { Destroy(_arms); enabled = false; return; }

            // Size: match arm length (shoulder to wrist), then the hand separately so the grip lines up.
            float aLen = 0f, bLen = 0f;
            foreach (var sd in _sides)
            {
                aLen += Vector3.Distance(sd.A[1].position, sd.A[2].position) + Vector3.Distance(sd.A[2].position, sd.A[3].position);
                bLen += Vector3.Distance(sd.B[1].position, sd.B[2].position) + Vector3.Distance(sd.B[2].position, sd.B[3].position);
            }
            float scale = bLen > 1e-4f ? aLen / bLen : 1f;
            _armsRoot.localScale *= scale;

            for (int i = 0; i < _sides.Count; i++)
            {
                var sd = _sides[i];
                var bFrames = Frames(sd, false);
                for (int k = 0; k < 4; k++) sd.Off[k] = Quaternion.Inverse(bFrames.limb[k]) * sd.B[k].rotation;
                for (int f = 0; f < 5; f++)
                    sd.OffF[f] = new[] { Quaternion.Inverse(bFrames.finger[f][0]) * sd.BF[f][0].rotation, Quaternion.Inverse(bFrames.finger[f][1]) * sd.BF[f][1].rotation };
                float aHand = Vector3.Distance(sd.A[3].position, sd.AF[2][0].position);
                float bHand = Vector3.Distance(sd.B[3].position, sd.BF[2][0].position);
                float hs = Mathf.Clamp(bHand > 1e-4f ? aHand / bHand : 1f, 0.7f, 1.4f);
                if (i == 0) _handScaleL = hs; else _handScaleR = hs;
                if (_logs++ < 2)
                {
                    Vector3 u = sd.B[2].position - sd.B[1].position, l = sd.B[3].position - sd.B[2].position;
                    Plugin.Logger.LogInfo($"[survivors] arms side {i}: scale {scale:F2} hand {hs:F2}, L4D2 bind elbow angle {Vector3.Angle(u, l):F0}");
                }
            }
            Plugin.Logger.LogInfo($"[survivors] {Plugin.Settings.SurvivorName.Value} arms on {name}, hid {_hidden.Count} STRAFTAT arm renderers");
        }

        private void LateUpdate()
        {
            if (_arms == null) return;
            foreach (var r in _hidden) if (r != null) r.enabled = false;
            foreach (var r in _arms.GetComponentsInChildren<Renderer>(true)) r.enabled = true;

            for (int i = 0; i < _sides.Count; i++)
            {
                var sd = _sides[i];
                var a = Frames(sd, true);
                for (int k = 0; k < 4; k++)
                {
                    sd.B[k].position = sd.A[k].position;
                    sd.B[k].rotation = a.limb[k] * sd.Off[k];
                }
                float hs = i == 0 ? _handScaleL : _handScaleR;
                sd.B[3].localScale = Vector3.one * hs;
                for (int f = 0; f < 5; f++)
                    for (int seg = 0; seg < 2; seg++)
                        sd.BF[f][seg].rotation = a.finger[f][seg] * sd.OffF[f][seg];
            }
        }

        /// <summary>Dev: swap which arms are visible for a comparison render (LateUpdate restores ours).</summary>
        internal void ShowOriginal(bool original)
        {
            foreach (var r in _hidden) if (r != null) r.enabled = original;
            if (_arms != null) foreach (var r in _arms.GetComponentsInChildren<Renderer>(true)) r.enabled = !original;
        }

        internal Vector3 HandsCenter => _sides.Count == 0 ? transform.position : (_sides[0].A[3].position + _sides[_sides.Count - 1].A[3].position) * 0.5f;

        private void OnDestroy()
        {
            foreach (var r in _hidden) if (r != null) r.enabled = true;
            if (_arms != null) Destroy(_arms);
        }

        private struct FrameSet { public Quaternion[] limb; public Quaternion[][] finger; }

        /// <summary>Anatomical frames for one side of either rig (fromAboubi: STRAFTAT's, else the L4D2 arms).</summary>
        private static FrameSet Frames(Side sd, bool fromAboubi)
        {
            var limb = fromAboubi ? sd.A : sd.B;
            Vector3 P(int k) => limb[k].position;
            // Finger joint positions; Aboubi fingers have two segments, so its tip extends the last one.
            Vector3 F(int f, int seg)
            {
                if (!fromAboubi) return sd.BF[f][seg].position;
                if (seg < 2) return sd.AF[f][seg].position;
                var last = sd.AF[f][1];
                return last.position + last.up * Vector3.Distance(sd.AF[f][0].position, last.position) * 0.8f;
            }
            Vector3 handDir = F(2, 0) - P(3);
            Vector3 handLat = F(1, 0) - F(4, 0);
            Vector3 palm = Vector3.Cross(handDir, handLat);
            Vector3 elbow = Vector3.Cross(P(2) - P(1), P(3) - P(2));
            if (elbow.sqrMagnitude < 1e-6f * (P(2) - P(1)).sqrMagnitude * (P(3) - P(2)).sqrMagnitude) elbow = palm;

            var set = new FrameSet { limb = new Quaternion[4], finger = new Quaternion[5][] };
            set.limb[0] = Frame(P(1) - P(0), elbow);
            set.limb[1] = Frame(P(2) - P(1), elbow);
            set.limb[2] = Frame(P(3) - P(2), handLat);
            set.limb[3] = Frame(handDir, handLat);
            for (int f = 0; f < 5; f++)
            {
                Vector3 lat = f == 0 ? palm : handLat; // the thumb points along the knuckle line
                set.finger[f] = new[] { Frame(F(f, 1) - F(f, 0), lat), Frame(F(f, 2) - F(f, 1), lat) };
            }
            return set;
        }

        private static Quaternion Frame(Vector3 dir, Vector3 lateral)
        {
            if (dir.sqrMagnitude < 1e-10f) dir = Vector3.forward;
            Vector3 up = Vector3.Cross(dir, lateral);
            if (up.sqrMagnitude < 1e-10f) up = Vector3.Cross(dir, Mathf.Abs(Vector3.Dot(dir.normalized, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up);
            return Quaternion.LookRotation(dir, up);
        }
    }
}
