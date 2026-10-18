using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Straft4Dead.Phase0
{
    /// <summary>Phase 0 asset pipeline proof. In the main menu, next to the menu Aboubi: loads the locally built L4D2
    /// bundle; shows the common infected playing its L4D2 run (Crowbar SMD), one Ellis driven live by Aboubi's humanoid
    /// pose (the retarget test, Aboubi is made to run then crouch), a second Ellis playing L4D2's collapse-to-incap, and
    /// Ellis's first-person arms; plays one Ellis voice line; renders three shots from its own camera into the BepInEx folder.
    /// Off by default (config Phase0.AssetProof).</summary>
    internal sealed class AssetProof : MonoBehaviour
    {
        internal string BundlePath;
        internal string VoicePath;
        internal string ScreenshotPath;

        private Animator _source;
        private Animator _target;
        private HumanPoseHandler _sourcePose;
        private readonly System.Collections.Generic.List<(Animator anim, HumanPoseHandler pose)> _targets =
            new System.Collections.Generic.List<(Animator, HumanPoseHandler)>();
        private HumanPose _pose;

        private IEnumerator Start()
        {
            yield return new WaitForSeconds(4f); // let the menu settle
            var log = Plugin.Logger;
            if (!File.Exists(BundlePath))
            {
                log.LogError($"[phase0] bundle not found: {BundlePath}");
                yield break;
            }
            var bundle = AssetBundle.LoadFromFile(BundlePath);
            log.LogInfo($"[phase0] bundle loaded: {string.Join(", ", bundle.GetAllAssetNames())}");

            // Retarget source: STRAFTAT's humanoid Aboubi in the menu scene.
            foreach (var an in FindObjectsOfType<Animator>().Where(x => x.avatar != null && x.avatar.isHuman))
                log.LogInfo($"[phase0] humanoid animator '{an.name}' at {an.transform.position} ctrl='{an.runtimeAnimatorController?.name}' enabled={an.isActiveAndEnabled} hasTransformHierarchy={an.hasTransformHierarchy}");
            _source = FindObjectsOfType<Animator>()
                .Where(a => a.avatar != null && a.avatar.isHuman && a.name.ToLower().Contains("aboubi"))
                .FirstOrDefault();
            Transform anchor = _source != null ? _source.transform : (Camera.main != null ? Camera.main.transform : transform);
            Vector3 fwd = Vector3.ProjectOnPlane(anchor.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 basePos = anchor.position;
            Quaternion facing = Quaternion.LookRotation(fwd, Vector3.up);

            var ellis = Spawn(bundle, "Ellis_Body", basePos - right * 1.0f, facing);
            var infected = Spawn(bundle, "Infected_Common", basePos + right * 1.0f, facing);
            var arms = Spawn(bundle, "Ellis_Arms", basePos - right * 2.0f + Vector3.up * 1.2f, facing);
            var ellisOwn = Spawn(bundle, "Ellis_Body", basePos + right * 2.0f, facing); // plays its own L4D2 clip
            log.LogInfo($"[phase0] own clips: infected='{ClipName(infected)}' ellis='{ClipName(ellisOwn)}'");

            if (ellis != null)
            {
                _target = ellis.GetComponent<Animator>();
                log.LogInfo($"[phase0] Ellis avatar human={_target.avatar?.isHuman} valid={_target.avatar?.isValid}");
                if (_source != null && _target.avatar != null && _target.avatar.isHuman)
                {
                    if (!_source.hasTransformHierarchy)
                    {
                        // "Optimize Game Objects" strips bone transforms; restore them so the pose can be read.
                        AnimatorUtility.DeoptimizeTransformHierarchy(_source.gameObject);
                        log.LogInfo($"[phase0] deoptimized source hierarchy -> hasTransformHierarchy={_source.hasTransformHierarchy}");
                    }
                    _sourcePose = new HumanPoseHandler(_source.avatar, _source.transform);
                    // The menu Aboubi idles almost motionless; make it run so the copied pose visibly changes.
                    _source.SetFloat("MovementSpeed", 1f);
                    _source.SetFloat("Vertical", 1f);
                    AddTarget(_target);
                    log.LogInfo($"[phase0] retarget source: '{_source.name}' avatar '{_source.avatar.name}' controller '{_source.runtimeAnimatorController?.name}' at {_source.transform.position}");
                }
                else
                {
                    log.LogWarning("[phase0] no humanoid Aboubi animator found; Ellis plays its own clip");
                }
            }

            // Our own camera for the proof shot: 3.5 m in front of the lineup, chest height, no UI.
            _shotCam = new GameObject("S4D_ProofCam").AddComponent<Camera>();
            _shotCam.enabled = false;
            _shotCam.fieldOfView = 60f;
            _shotCam.nearClipPlane = 0.05f;
            _shotCam.transform.position = basePos + fwd * 4.6f + Vector3.up * 1.1f;
            _shotCam.transform.rotation = Quaternion.LookRotation(basePos + Vector3.up * 0.9f - _shotCam.transform.position, Vector3.up);
            log.LogInfo($"[phase0] infected spawned={infected != null} ellis={ellis != null} arms={arms != null}");

            yield return PlayVoice();
            yield return new WaitForSeconds(2f);
            LogHeight("Aboubi", _source);
            LogHeight("Ellis", _target);
            LogHeight("Infected", infected != null ? infected.GetComponent<Animator>() : null);
            SaveShot(ScreenshotPath);
            yield return new WaitForSeconds(0.35f); // later frames show the pose is live
            SaveShot(ScreenshotPath.Replace(".png", "_b.png"));
            if (_source != null)
                _source.SetBool("Crouch", true);
            yield return new WaitForSeconds(1.0f);
            SaveShot(ScreenshotPath.Replace(".png", "_c.png"));
        }

        private void LateUpdate()
        {
            if (_sourcePose == null)
                return;
            _sourcePose.GetHumanPose(ref _pose);
            foreach (var (anim, handler) in _targets)
            {
                Vector3 keepPos = anim.transform.position;
                Quaternion keepRot = anim.transform.rotation;
                handler.SetHumanPose(ref _pose);
                anim.transform.SetPositionAndRotation(keepPos, keepRot); // only copy the body pose, not root placement
            }
        }

        private Camera _shotCam;

        private static string ClipName(GameObject go)
        {
            var c = go != null ? go.GetComponent<Animator>()?.runtimeAnimatorController : null;
            return c != null && c.animationClips.Length > 0 ? c.animationClips[0].name : "none";
        }

        private void AddTarget(Animator a)
        {
            if (a == null || a.avatar == null || !a.avatar.isHuman)
                return;
            a.runtimeAnimatorController = null;
            _targets.Add((a, new HumanPoseHandler(a.avatar, a.transform)));
        }

        private void SaveShot(string path)
        {
            var rt = new RenderTexture(1600, 900, 24);
            _shotCam.targetTexture = rt;
            _shotCam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            _shotCam.targetTexture = null;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(rt);
            Destroy(tex);
            Plugin.Logger.LogInfo($"[phase0] screenshot -> {path}");
        }

        private static void LogHeight(string label, Animator a)
        {
            if (a == null || !a.isHuman)
                return;
            var head = a.GetBoneTransform(HumanBodyBones.Head);
            var foot = a.GetBoneTransform(HumanBodyBones.LeftFoot);
            var hips = a.GetBoneTransform(HumanBodyBones.Hips);
            Plugin.Logger.LogInfo($"[phase0] {label}: head-foot {(head.position.y - foot.position.y):F2} m, hips height {(hips.position.y - a.transform.position.y):F2} m, lossyScale {a.transform.lossyScale}");
        }

        private void OnDestroy()
        {
            _sourcePose?.Dispose();
            foreach (var t in _targets)
                t.pose.Dispose();
        }

        private static GameObject Spawn(AssetBundle bundle, string name, Vector3 pos, Quaternion rot)
        {
            var prefab = bundle.LoadAllAssets<GameObject>().FirstOrDefault(g => g.name == name);
            if (prefab == null)
            {
                Plugin.Logger.LogError($"[phase0] prefab {name} not in bundle");
                return null;
            }
            var go = Instantiate(prefab, pos, rot);
            int renderers = go.GetComponentsInChildren<Renderer>().Length;
            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
            Plugin.Logger.LogInfo($"[phase0] spawned {name}: renderers={renderers} bones={(smr ? smr.bones.Length : 0)} bounds={(smr ? smr.bounds.size.ToString("F2") : "-")}");
            return go;
        }

        private IEnumerator PlayVoice()
        {
            if (!File.Exists(VoicePath))
            {
                Plugin.Logger.LogError($"[phase0] voice line not found: {VoicePath}");
                yield break;
            }
            using (var req = UnityWebRequestMultimedia.GetAudioClip("file:///" + VoicePath.Replace('\\', '/'), AudioType.WAV))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Plugin.Logger.LogError($"[phase0] voice load failed: {req.error}");
                    yield break;
                }
                var clip = DownloadHandlerAudioClip.GetContent(req);
                var src = gameObject.AddComponent<AudioSource>();
                src.spatialBlend = 0f;
                src.PlayOneShot(clip);
                Plugin.Logger.LogInfo($"[phase0] voice line playing: {Path.GetFileName(VoicePath)} {clip.length:F2}s {clip.frequency}Hz ch={clip.channels}");
            }
        }
    }
}
