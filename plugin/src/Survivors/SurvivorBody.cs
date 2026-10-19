using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Straft4Dead.Survivors
{
    /// <summary>An L4D2 survivor body on a STRAFTAT player's third-person character. Both rigs are Humanoid, so the
    /// hidden Aboubi keeps running STRAFTAT's animator and hand IK and we copy its muscle pose every frame
    /// (HumanPoseHandler), which retargets across the different proportions.
    ///
    /// The Aboubi renderers are hidden with forceRenderingOff, so the game's own enabled flags (it hides your own
    /// body from yourself) still tell us when the survivor should be visible.</summary>
    [DefaultExecutionOrder(10000)]
    internal sealed class SurvivorBody : MonoBehaviour
    {
        /// <summary>Dev renders: show bodies even where the game hides them (your own).</summary>
        internal static bool ForceVisible;
        internal static bool ShowOriginal;

        private Animator _src;
        private HumanPoseHandler _srcPose, _dstPose;
        private HumanPose _pose;
        private GameObject _body;
        private readonly List<SkinnedMeshRenderer> _aboubi = new List<SkinnedMeshRenderer>();
        private Renderer[] _ours = new Renderer[0];
        private Transform _srcHips, _dstHips;
        private bool _ragdoll;

        internal GameObject Body => _body;

        /// <summary>Every humanoid Aboubi in the scene: live players (under PlayerSetup) and death ragdolls
        /// (SK_Aboubi_01_Ragdoll, a separate object with its own humanoid avatar and a disabled animator).</summary>
        internal static void AttachAll()
        {
            if (!Plugin.Settings.SurvivorBodies.Value || SurvivorAssets.Instance == null || !SurvivorAssets.Instance.Loaded)
                return;
            foreach (var a in Object.FindObjectsOfType<Animator>())
                if (a.avatar != null && a.avatar.isHuman && a.avatar.name.StartsWith("SK_Aboubi") && a.GetComponent<SurvivorBody>() == null
                    && (a.GetComponentInParent<PlayerSetup>() != null || a.name.Contains("Ragdoll"))) // not the menu or cosmetics previews
                    a.gameObject.AddComponent<SurvivorBody>();
        }

        private void Start()
        {
            _src = GetComponent<Animator>();
            var prefab = SurvivorAssets.Instance.Prefab(Plugin.Settings.SurvivorName.Value, "Body");
            if (prefab == null) { enabled = false; return; }

            _aboubi.AddRange(GetComponentsInChildren<SkinnedMeshRenderer>(true));
            _body = Instantiate(prefab, transform.parent, false);
            _body.name = "S4D_SurvivorBody";
            int layer = _aboubi.Count > 0 ? _aboubi[0].gameObject.layer : gameObject.layer;
            foreach (var t in _body.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            var dst = _body.GetComponent<Animator>();
            dst.enabled = false; // posed by us
            _ours = _body.GetComponentsInChildren<Renderer>(true);
            foreach (var r in _ours.OfType<SkinnedMeshRenderer>()) r.updateWhenOffscreen = true;

            _src.cullingMode = AnimatorCullingMode.AlwaysAnimate; // it must keep animating while hidden
            _ragdoll = !_src.enabled; // death ragdolls carry the avatar but physics moves the bones
            _srcHips = GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Hips");
            _dstHips = _body.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "ValveBiped.Bip01_Pelvis");
            _srcPose = new HumanPoseHandler(_src.avatar, _src.transform);
            _dstPose = new HumanPoseHandler(dst.avatar, _body.transform);
            Plugin.Logger.LogInfo($"[survivors] {Plugin.Settings.SurvivorName.Value} body on {transform.root.name}/{name} (layer {layer}, {_aboubi.Count} Aboubi parts hidden)");
        }

        private void LateUpdate()
        {
            if (_body == null) return;
            foreach (var r in _aboubi) if (r != null) r.forceRenderingOff = !ShowOriginal;

            _body.transform.SetPositionAndRotation(transform.position, transform.rotation);
            _srcPose.GetHumanPose(ref _pose);
            _dstPose.SetHumanPose(ref _pose);
            // A ragdoll's root is scaled and left where it spawned, so the root-relative body position lands metres
            // away; pin the pelvis to the ragdoll's hips instead (it's lying down, leg length doesn't matter).
            if (_ragdoll && _srcHips != null && _dstHips != null)
                _dstHips.position = _srcHips.position;

            // Visible when the game shows the Aboubi torso (it hides your own body, and every part on death).
            var lead = _aboubi.FirstOrDefault(r => r != null && r.name.Contains("Torso")) ?? _aboubi.FirstOrDefault();
            bool visible = !ShowOriginal && (ForceVisible || (lead != null && lead.enabled && lead.gameObject.activeInHierarchy));
            var shadows = lead != null ? lead.shadowCastingMode : UnityEngine.Rendering.ShadowCastingMode.On;
            foreach (var r in _ours)
            {
                r.enabled = visible;
                r.shadowCastingMode = shadows;
            }
        }

        private void OnDestroy()
        {
            foreach (var r in _aboubi) if (r != null) r.forceRenderingOff = false;
            _srcPose?.Dispose();
            _dstPose?.Dispose();
            if (_body != null) Destroy(_body);
        }
    }
}
