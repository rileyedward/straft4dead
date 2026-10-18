# Asset pipeline: L4D2 → STRAFTAT

Everything here runs on the player's machine against their own Left 4 Dead 2 install. Outputs go to a folder outside the repo (dev PC: `C:\Users\Riley\source\straft4dead-research\extracted\`). Nothing extracted is ever committed; `.gitignore` blocks the Source and Unity asset formats as a backstop.

Pipeline: **VPK → (MDL/VTX/VVD/ANI, VTF/VMT, WAV) → Blender + SourceIO → FBX/PNG → Unity 2021.3.45 bundle-builder project → AssetBundle → loaded by the plugin at runtime.**

## Where things live in L4D2

| what | location | notes |
|---|---|---|
| Models, materials, scripts | `left4dead2\pak01_dir.vpk` (+ `_000…_040.vpk`), DLC paks in `left4dead2_dlc1..3\`, patches in `update\` | 22,273 files in the base pak |
| Sounds | **loose** `left4dead2\sound\…` (13,878 files) | not in the VPKs; voice lines are plain `.wav` |
| Common infected (proof target) | `models/infected/common_male_tshirt_cargos.{mdl,vvd,dx90.vtx}`, skins in `materials/models/infected/common/` | 33 bones, no own sequences |
| Common infected animations | `models/infected/anim_common.{mdl,ani,phy}` | 407 sequences (idle, alert, run, attack, shove, death, ragdoll, …) shared by all commons |
| Ellis third-person | `models/survivors/survivor_mechanic.{mdl,vvd,dx90.vtx,phy}`, `materials/models/survivors/mechanic/` | 70 bones, ValveBiped |
| Ellis animations | `models/survivors/anim_mechanic.{mdl,ani}` (+ `anim_gestures`) | 819 sequences |
| Ellis first-person arms | `models/weapons/arms/v_arms_mechanic_new.{mdl,vvd,dx90.vtx}` | 59 bones incl. `ValveBiped.Camera`, `weapon_bone`; FP weapon anims live in `models/v_models/anim_v_*.mdl` |
| Ellis voice | `sound/player/survivor/voice/mechanic/*.wav` | talker rules in `scripts/talker/mechanic.txt` (inside the pak) |

## Steps

### 1. Extract from VPK (done, no third-party tool)

`tools/pipeline/vpk.py` is a stdlib-only VPK v1/v2 reader written for this repo.

```
python -I tools\pipeline\vpk.py list    "<L4D2>\left4dead2\pak01_dir.vpk" infected common_male
python -I tools\pipeline\vpk.py extract "<L4D2>\left4dead2\pak01_dir.vpk" <out> models/infected/common_male_tshirt_cargos. models/infected/anim_common. models/survivors/survivor_mechanic. models/survivors/anim_mechanic. models/survivors/anim_gestures. models/weapons/arms/v_arms_mechanic_new. materials/models/infected/common/ materials/models/survivors/mechanic/ materials/models/weapons/arms/
```

Result on the dev PC: 742 files, 268 MB (mostly the shared common-infected texture set).

### 2. Inspect skeletons (done)

`tools/pipeline/mdl_info.py <file.mdl> [--seq]` prints bones (name, parent) and sequence names from the MDL header (v49).

Findings: survivors and commons use the **ValveBiped** skeleton (`Bip01_Pelvis → Spine → Spine1 → Spine2 → Spine4 → Neck1 → Head1`, clavicle/upper arm/forearm/hand, five fingers × 3 segments, thigh/calf/foot/toe, plus helper bones like `Ulna`, `Wrist`, `Elbow`, `Knee`, `weapon_bone`, `attachment_*`). Every bone Unity's Humanoid avatar requires is present, so L4D2 bodies can be imported as **Humanoid**.

### 3. MDL → Blender → FBX (pending tools)

Tools: Blender 4.x + SourceIO addon (reads MDL/VVD/VTX/ANI and VTF/VMT directly, can mount the VPKs). Crowbar is the fallback decompiler (MDL → SMD/QC) if SourceIO mis-reads something.

Plan per model: import MDL (+ its `anim_*.mdl` for sequences), apply materials, export FBX with armature and the chosen actions; convert VTFs to PNG. A headless Blender script in `tools/pipeline/` will do this so Phase 7 can run it on first launch.

### 4. FBX → AssetBundle (pending tools)

A minimal Unity **2021.3.45f1** project (same editor version as STRAFTAT, so bundles are binary-compatible) with one editor script that imports the FBX as Humanoid, builds a prefab, and calls `BuildPipeline.BuildAssetBundles` for StandaloneWindows64. Only the project's editor scripts and settings are tracked; imported L4D2 assets and built bundles are git-ignored.

### 5. Load in STRAFTAT (pending)

Plugin loads the bundle from a local path with `AssetBundle.LoadFromFile`, instantiates the prefab, and plays one clip; plays one Ellis `.wav` through an `AudioSource` (wav can be loaded at runtime with `UnityWebRequestMultimedia` without bundling).
