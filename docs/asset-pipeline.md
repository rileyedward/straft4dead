# Asset pipeline: L4D2 → STRAFTAT

Everything here runs on the player's machine against their own Left 4 Dead 2 install. Outputs go to a folder outside the repo (dev PC: `C:\Users\Riley\source\straft4dead-research\extracted\`). Nothing extracted is ever committed; `.gitignore` blocks the Source and Unity asset formats as a backstop.

Pipeline: **VPK → MDL/VTF (stdlib reader) → Blender + SourceIO (meshes, textures) and Crowbar SMD (animations) → FBX → Unity 2021.3.45f2 bundle-builder project → AssetBundle → loaded by the plugin at runtime.**

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

### 3. Meshes and textures: MDL → Blender + SourceIO → FBX (done)

Tools: **Blender 5.2.2 LTS** (portable zip from the nluug mirror, SHA-256 checked; download.blender.org returned 403 to winget) at `C:\Users\Riley\Tools\blender-5.2.2-windows-x64\`, and **SourceIO 5.5.4** installed as a legacy addon in `%APPDATA%\Blender Foundation\Blender\5.2\scripts\addons\SourceIO`.

```
blender -b --factory-startup --addons SourceIO --python tools\pipeline\blender_export.py -- --mdl <file.mdl> --out <file.fbx> --no-anims
blender -b --factory-startup --addons SourceIO --python tools\pipeline\vtf_to_png.py -- <outdir> <file.vtf> ...
```

`blender_export.py` imports at **0.0254 m per unit** (1 inch, the character scale; SourceIO's 0.01905 default is the map scale), stands bodies up when the import comes in lying down (the commons do: pelvis→head must point +Z), and exports the FBX **in rest pose** (an active action at export time becomes Unity's bind pose).

| model | bones | result |
|---|---|---|
| `common_male_tshirt_cargos` | 33 | mesh OK; textures **not usable as-is** (below) |
| `survivor_mechanic` (Ellis) | 70 | mesh + body/head colour and normal maps, eyes, hair |
| `v_arms_mechanic_new` (Ellis FP arms) | 59 | mesh + arm textures |

- **Common infected materials:** L4D2's `Infected` shader packs blood, grime and palette indices into the base texture and tints skin/clothes from `ci_gradient_palette`. SourceIO doesn't implement it (its README says as much), so the converted `cim_tshirt_cargos` PNG is a channel mask, not a colour map. Phase 0 uses a flat placeholder; Phase 3 needs a small Unity shader that reproduces the palette tint.
- **Don't use SourceIO for L4D2 animation.** Its experimental `.ani` import loads 1,547 Ellis actions, but 204 of the 205 longer than 10 frames have broken poses (zero quaternions, head below the feet). Checked with a pose-sanity pass in Blender.

### 4. Animations: Crowbar → SMD → Blender → FBX (done)

**Crowbar 0.74** (`C:\Users\Riley\Tools\Crowbar\`, extracted with `py7zr`) decompiles `anim_*.mdl` + `.ani` into one SMD per animation (`anim_common`: 714 files, `anim_mechanic`: 1,577). Crowbar has no CLI, so `tools\pipeline\crowbar_decompile.ps1` launches it with the MDL (which pre-fills the Decompile tab) and presses the Decompile button via Win32 `BM_CLICK`. The output folder is `DecompileOutputFullPath` in `%APPDATA%\ZeqMacaw\Crowbar 0.74\Crowbar Settings.xml`.

```
powershell -ExecutionPolicy Bypass -File tools\pipeline\crowbar_decompile.ps1 -Crowbar <Crowbar.exe> -Mdl <anim_x.mdl> -OutDir <dir>
blender -b --factory-startup --addons SourceIO --python tools\pipeline\blender_export.py -- --mdl <body.mdl> --out <anim.fbx> --no-anims --smd <anim.smd>
```

`--smd` parses the SMD skeleton block (parent-relative position + XYZ Euler per bone per frame) and bakes it onto the SourceIO armature as one action, using the same rest-space maths SourceIO uses. Proof clips: infected `a_Run_01` (32 frames, the forward run inside the `Run_01` 9-way blend) and Ellis `Collapse_to_Incap` (76 frames).

QC files tell how L4D2 composes sequences (blends of directional anims, `_layer` / `_arms` overlays, `fps` per sequence); Phase 3/4 will read them to rebuild blend trees.

### 5. FBX → AssetBundle (done)

**Unity 2021.3.45f2** (STRAFTAT's exact build, changeset `88f88f591b2e`) installed silently from Unity's signed installer to `C:\Users\Riley\Unity\Editors\2021.3.45f2\`; Unity Hub 3.22.3 (MSIX via winget) holds Riley's free Personal licence, which batch mode needs.

```
Unity.exe -batchmode -quit -nographics -projectPath unity\S4DBundles -executeMethod S4DBundleBuilder.Build ^
  -s4dSource <fbx folder> -s4dOut <bundle folder> -logFile <log>
Unity.exe -batchmode -quit -nographics -projectPath unity\S4DBundles -executeMethod S4DProbe.Run -logFile <log>
```

`S4DBundleBuilder`:
- copies FBX exports into `Assets/L4D2/` (git-ignored);
- bodies → **Humanoid** with an explicit ValveBiped → Unity human-bone map (Unity's auto-mapper rejected the rig) and an **enforced T-pose** (arms along ±X, legs down) so muscle space matches other humanoids;
- FP arms → Generic;
- animation FBX → Humanoid via the body's avatar ("copy from other avatar"), looping, root motion baked into the pose so it plays in place;
- prefabs with a one-state Animator, bundle `s4d_phase0` (30 MB) for StandaloneWindows64.

`S4DProbe` prints head/foot/hips positions in bind pose, at a clip sample and at a neutral human pose (catches collapsed or lying rigs).

### 6. Load in STRAFTAT (done)

Config `[Phase0] AssetProof = true` with `BundlePath` and `VoiceLine` set (it's `false` by default and on Riley's install now). In the main menu, next to the menu's own Aboubi, the plugin:

1. loads the bundle with `AssetBundle.LoadFromFile`;
2. spawns the infected playing L4D2's run, Ellis playing L4D2's collapse-to-incap, and Ellis's FP arms;
3. spawns a second Ellis driven every `LateUpdate` by Aboubi's human pose (`HumanPoseHandler.GetHumanPose` → `SetHumanPose`) and makes Aboubi run, then crouch;
4. plays `sound\player\survivor\voice\mechanic\hurrah01.wav` via `UnityWebRequestMultimedia` (4.46 s, 22 kHz mono);
5. renders three frames from its own camera to `BepInEx\s4d_phase0*.png`.

All five worked on 2026-10-18. Copies of the shots are in `straft4dead-research\phase0-shots\` (they show Valve assets, so they stay out of the repo).

## Common infected set (Phase 3)

```
python -I tools\pipeline\build_infected.py "<L4D2>\left4dead2" <work dir> <blender.exe>
Unity.exe -batchmode -quit -projectPath unity\S4DBundles -executeMethod S4DInfectedBuilder.Build -s4dSource <work>\fbx -s4dOut <bundles>
```

- Needs `anim_common` decompiled by Crowbar into `<work>\crowbar\anim_common_anims` first (`crowbar_decompile.ps1`).
- Outfit FBX are exported `--raw` (Source armature space, not stood up) with the armature renamed `S4D_Armature`; the animation FBX is baked on the same armature with the root bone pinned horizontally. Unity imports both as **Generic** so the clips bind by bone path on every outfit.
- Materials: one `S4D/Infected` material per base texture, palette assigned; rows are randomised per zombie at runtime through a MaterialPropertyBlock.
- **Don't pass `-nographics`** to the bundle build: custom shaders then ship without compiled programs and render magenta.
- Sounds aren't bundled: the plugin loads `<L4D2>\left4dead2\sound\npc\infected\…` at startup, skipping special variants (CEDA, mud men, clowns, riot, Jimmy Gibbs, fallen survivors).
