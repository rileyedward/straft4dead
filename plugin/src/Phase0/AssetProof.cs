using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Straft4Dead.Phase0
{
    /// <summary>Phase 0 asset pipeline proof. In the main menu: loads the locally built L4D2 bundle, shows the common
    /// infected (own run clip), Ellis's body driven by the menu Aboubi's humanoid pose (the retarget test), Ellis's
    /// first-person arms in front of the camera, plays one Ellis voice line, and saves a screenshot.
    /// Off by default (config Phase0.AssetProof).</summary>
    internal sealed class AssetProof : MonoBehaviour
    {
        internal string BundlePath;
        internal string VoicePath;
        internal string ScreenshotPath;

        private Animator _source;
        private Animator _target;
        private HumanPoseHandler _sourcePose;
        private HumanPoseHandler _targetPose;
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

            var cam = Camera.main;
            Transform anchor = cam != null ? cam.transform : transform;
            Vector3 fwd = Vector3.ProjectOnPlane(anchor.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 basePos = anchor.position + fwd * 3.5f + Vector3.down * 1.6f;
            Quaternion facing = Quaternion.LookRotation(-fwd, Vector3.up);

            var infected = Spawn(bundle, "Infected_Common", basePos + right * 1.2f, facing);
            var ellis = Spawn(bundle, "Ellis_Body", basePos - right * 1.2f, facing);
            var arms = Spawn(bundle, "Ellis_Arms", anchor.position, anchor.rotation);
            if (arms != null && cam != null)
            {
                arms.transform.SetParent(cam.transform, false);
                arms.transform.localPosition = new Vector3(0f, -0.1f, 0.25f);
                arms.transform.localRotation = Quaternion.identity;
            }

            // Retarget test: find STRAFTAT's humanoid Aboubi in the menu and drive Ellis from its human pose.
            _source = FindObjectsOfType<Animator>()
                .Where(a => a.avatar != null && a.avatar.isHuman && a.gameObject != ellis && a.name.ToLower().Contains("aboubi"))
                .OrderBy(a => cam != null ? Vector3.Distance(a.transform.position, cam.transform.position) : 0f)
                .FirstOrDefault();
            if (ellis != null)
            {
                _target = ellis.GetComponent<Animator>();
                log.LogInfo($"[phase0] Ellis avatar human={_target.avatar?.isHuman} valid={_target.avatar?.isValid}");
                if (_source != null && _target.avatar != null && _target.avatar.isHuman)
                {
                    _target.runtimeAnimatorController = null; // pose comes from Aboubi instead of Ellis's own clip
                    _sourcePose = new HumanPoseHandler(_source.avatar, _source.transform);
                    _targetPose = new HumanPoseHandler(_target.avatar, _target.transform);
                    log.LogInfo($"[phase0] retarget source: '{_source.name}' avatar '{_source.avatar.name}' controller '{_source.runtimeAnimatorController?.name}'");
                }
                else
                {
                    log.LogWarning($"[phase0] no humanoid Aboubi animator found in '{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}'; Ellis plays its own clip");
                }
            }
            log.LogInfo($"[phase0] infected spawned={infected != null} ellis={ellis != null} arms={arms != null}");

            yield return PlayVoice();
            yield return new WaitForSeconds(2f);
            ScreenCapture.CaptureScreenshot(ScreenshotPath);
            log.LogInfo($"[phase0] screenshot -> {ScreenshotPath}");
        }

        private void LateUpdate()
        {
            if (_sourcePose == null)
                return;
            _sourcePose.GetHumanPose(ref _pose);
            Vector3 keepPos = _target.transform.position;
            Quaternion keepRot = _target.transform.rotation;
            _targetPose.SetHumanPose(ref _pose);
            _target.transform.SetPositionAndRotation(keepPos, keepRot); // only copy the body pose, not root placement
        }

        private void OnDestroy()
        {
            _sourcePose?.Dispose();
            _targetPose?.Dispose();
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
