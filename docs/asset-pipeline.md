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

### 3. MDL → Blender → FBX (done)

Tools: **Blender 5.2.2 LTS** (portable zip from the nluug mirror, SHA-256 checked; download.blender.org returned 403 to winget) at `C:\Users\Riley\Tools\blender-5.2.2-windows-x64\`, and **SourceIO 5.5.4** installed as a legacy addon in `%APPDATA%\Blender Foundation\Blender\5.2\scripts\addons\SourceIO`. SourceIO 5.5.4 reads MDL v49 plus external `.ani` animation blocks and include-model animation chains. Crowbar 0.74 is downloaded as a fallback but wasn't needed.

```
blender -b --factory-startup --addons SourceIO --python tools\pipeline\blender_export.py -- --mdl <file.mdl> --list-anims
blender -b --factory-startup --addons SourceIO --python tools\pipeline\blender_export.py -- --mdl <file.mdl> --out <file.fbx> --anim <action>
blender -b --factory-startup --addons SourceIO --python tools\pipeline\vtf_to_png.py -- <outdir> <file.vtf> ...
```

| model | armature | actions imported | exported clip | textures |
|---|---|---|---|---|
| `common_male_tshirt_cargos` | 33 bones | 266 (from `anim_common`) | `a_Run_01Zero` (79 frames) | **not usable as-is**, see below |
| `survivor_mechanic` (Ellis) | 70 bones | 1,547 (from `anim_mechanic` + gestures; some `@…` layer sequences fail to load) | `a_Run_RifleZero` (99 frames) | body/head colour + normal maps, eyes, hair |
| `v_arms_mechanic_new` (Ellis FP arms) | 59 bones | 1 (reference pose) | none | arm colour + normal |

Notes:
- Scale: SourceIO imports at 0.01905 m per Hammer unit; the FBX is in metres.
- **Common infected materials:** L4D2's `Infected` shader packs blood, grime and palette indices into the base texture and tints skin/clothes from `ci_gradient_palette`. SourceIO doesn't implement it (README: "L4D2 full support, except infected materials"), so the converted `cim_tshirt_cargos` PNG is a channel mask, not a colour map. Phase 0 uses a flat placeholder material; Phase 3 needs a small Unity shader that reproduces the palette tint.
- Survivor FP animations are per-weapon viewmodel files (`models/v_models/anim_v_*.mdl`), not in `v_arms_*`.

### 4. FBX → AssetBundle (built, blocked on Unity licence)

**Unity 2021.3.45f2** (STRAFTAT's exact build, changeset `88f88f591b2e`) installed silently from Unity's signed installer to `C:\Users\Riley\Unity\Editors\2021.3.45f2\`. Unity Hub 3.22.3 is installed as an MSIX app (winget). Batch mode needs an activated Unity Personal licence, which requires signing in to the Hub.

```
Unity.exe -batchmode -quit -nographics -projectPath unity\S4DBundles -executeMethod S4DBundleBuilder.Build ^
  -s4dSource <fbx folder> -s4dOut <bundle folder> -logFile <log>
```

`S4DBundleBuilder` copies the FBX exports into `Assets/L4D2/` (git-ignored), imports bodies as **Humanoid** and FP arms as Generic, logs every human-bone mapping, makes prefabs with a one-state Animator, and builds `s4d_phase0` for StandaloneWindows64.

### 5. Load in STRAFTAT

Config `[Phase0] AssetProof = true` with `BundlePath` and `VoiceLine` set. In the main menu the plugin loads the bundle, spawns the infected (own run clip) and Ellis, **drives Ellis from the menu Aboubi's human pose** with `HumanPoseHandler` (the retarget test), parents the FP arms to the camera, plays `hurrah01.wav` via `UnityWebRequestMultimedia`, and saves `BepInEx\s4d_phase0.png`.
