# Devlog

## 2026-10-18 — Phase 0 (Recon), session 1

**Changed**
- Repo set up at `C:\Users\Riley\source\Straft4Dead`: BepInEx 5 plugin project (`dev.riley.straft4dead`, netstandard2.1) that builds against the local STRAFTAT `Managed/` folder; it only logs scene / spawn / navmesh diagnostics for now. Config master switch `General.Enabled`.
- REA pass over `Assembly-CSharp.dll` written up in `docs/systems-map.md` (health and damage path, death → take end, rounds/takes, spawns, navmesh, HUD, animator and IK rig, FishNet networking, STRAFTAT's mod-aware matchmaking key).
- L4D2 extraction proven without third-party tools: `tools/pipeline/vpk.py` pulled one common infected (`common_male_tshirt_cargos`), its animation set (`anim_common`, 407 sequences), Ellis (`survivor_mechanic`, `anim_mechanic` 819 sequences, `v_arms_mechanic_new`) and materials. Voice lines are loose `.wav` files under `left4dead2\sound`.
- `tools/pipeline/mdl_info.py` confirmed L4D2 uses the ValveBiped skeleton, which has every bone Unity Humanoid needs.

**Broke / blocked**
- No Blender, SourceIO, Crowbar, Unity 2021.3.45 or UnityPy on the PC, and BepInEx isn't installed in STRAFTAT. Asked Riley for the OK to install them. Until then: MDL → FBX → AssetBundle, the in-game load test, and the asset-file checks (Aboubi avatar type, prefab full health, navmesh data, HUD hierarchy, layer names) are open.
- REA's managed analysis gives metadata and IL call/field order, not C# bodies. Enough to place hooks; branch details are marked [inferred].

**Next**
1. Install the tool chain once approved; BepInEx into STRAFTAT and confirm the plugin's `[diag]` log on a map.
2. UnityPy pass over `data.unity3d`: Aboubi Avatar (Humanoid?), bones, player prefab `PlayerHealth.fullHealth`, NavMeshData, HUD prefab, TagManager layers.
3. SourceIO import → FBX for the infected and Ellis (TP body, FP arms, one animation each), Unity 2021.3.45 bundle build, load in STRAFTAT, play one Ellis voice line.
4. Phase 0 report to Riley, then stop before Phase 1.
