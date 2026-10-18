# Phase 0 report: recon and asset pipeline proof

2026-10-18. Details live in `systems-map.md` (STRAFTAT internals) and `asset-pipeline.md` (every tool and step).

## Verdict

Phase 0 is done. Every piece of the architecture was exercised once on the dev PC:

| goal | result |
|---|---|
| Workspace repo | `C:\Users\Riley\source\Straft4Dead`: BepInEx 5 plugin (builds, deploys, loads in STRAFTAT), docs, tools, Unity bundle project. Local commits only. |
| REA map of STRAFTAT | Health and damage, death → take end, rounds/takes, maps and spawns, navmesh, HUD, animator and IK rig, FishNet netcode, and STRAFTAT's own mod-aware matchmaking. |
| One common infected | `common_male_tshirt_cargos` loads in STRAFTAT from our bundle and plays L4D2's run (`a_Run_01`). Placeholder grey material. |
| Ellis third-person body | Loads textured, plays L4D2's `Collapse_to_Incap`, and a second copy is driven live by STRAFTAT's Aboubi animation. |
| Ellis first-person arms | Load textured from the bundle (static; not animated yet). |
| One voice line | `hurrah01.wav` plays in game straight from the L4D2 install. |
| Public matchmaking | STRAFTAT shows "Non-vanilla friendly mods detected, you will be using a separate matchmaking pool" with our plugin loaded. |

## Can STRAFTAT's rig take a retargeted L4D2 body?

**Yes for third person.** STRAFTAT's player (Aboubi) is a Unity **Humanoid** and every one of its body animations is a humanoid muscle clip. L4D2 bodies use the ValveBiped skeleton, which has every bone Unity Humanoid needs. With an explicit bone map and an enforced T-pose, Ellis follows Aboubi's run and crouch-slide in game, frame by frame, by copying the human pose after STRAFTAT's own animation and hand IK. Remaining work is tuning (Phase 4): spine and head lean and arm angles are close but not identical, since Aboubi has one spine bone and cartoon proportions.

**Not directly for first person.** STRAFTAT's first-person arms are a Generic rig with transform curves and IK-target clips, so humanoid retargeting doesn't apply. Options for Phase 4: map L4D2 arm bones onto the Aboubi FP arm bones each frame, or drive the L4D2 arms with L4D2's own viewmodel animations (`models/v_models/anim_v_*`) and STRAFTAT's weapon events.

## Biggest risks

1. **First-person arms** (above). The most hand work in the survivor phase; STRAFTAT's guns, hand IK and arm sway all assume the Aboubi FP rig.
2. **Infected materials.** L4D2 colours commons with a special `Infected` shader (palette tint driven by mask textures). No converter supports it, so Phase 3 needs a small custom Unity shader. Until then commons are flat grey.
3. **Animation extraction is a two-tool path.** SourceIO's L4D2 animation import is broken (204 of 205 long clips have garbage poses), so animations come from Crowbar SMDs, baked by our own script. It works, but Crowbar has no command line; it's driven by pressing its Decompile button programmatically. Fine for us; for Phase 7's first-run extraction a small custom `.ani` decoder may be cleaner.
4. **No navmesh in any map.** Confirmed: zero `NavMeshData` in the game's 697,704 assets. Zombies need a navmesh baked at runtime per map (Unity's runtime `NavMeshBuilder` ships with the game). STRAFTAT maps are vertical, with jump pads and movement tech, so expect gaps and off-mesh links to hand-tune.
5. **Damage plumbing.** STRAFTAT weapons only hurt objects on the `Body` layer under a `Player`-tagged root with `PlayerHealth`, and there are ~20 weapon/projectile types, each writing health itself. Zombie damage needs one Harmony hook per weapon family (the Phase 1 work).
6. **Multiplayer (Phase 8).** Everything is FishNet SyncVars and RPCs; spawning new networked zombie prefabs at runtime needs prefab ids that match on every peer. Not a Phase 0 blocker, but the hardest later phase.
7. **Licensing and redistribution.** All L4D2 assets stay on the player's machine; the bundle is built locally. Phase 7 has to ship the pipeline (Blender, SourceIO, Crowbar, a Unity 2021.3.45 editor with a licence) or replace parts of it, which is heavy for a "first-run extraction". Worth deciding early.

## Decisions for you before Phase 1

- **Phase 7 extraction approach** (risk 7): ship the full tool chain, or invest later in a custom MDL/ANI → Unity path that runs without Blender/Unity on the player's PC. Not needed until Phase 7, but it changes how much of today's tooling becomes permanent.
- **Health scale.** STRAFTAT's full health looks like 4 units shown as "100" (prefab value still unconfirmed; it's Q2 in the systems map). Phase 1's multiplier and Phase 2's 100 HP scale will map L4D2 100 HP onto that.

## Phase 1 starting points

- Spawn: `PlayerManager.OnLoadSceneEnd`, spawn points under the `Spawnpoints` / `Spawnpoints4Player` tags.
- Zombie layer: STRAFTAT layers 29–31 are unused.
- Damage in: postfix each weapon's server shot (`Gun.ShootServer` and siblings).
- Damage out: write `PlayerHealth.health` server-side, as the guns do.
- Testing: the main menu has a humanoid Aboubi and works as a sandbox; a match needs a hosted lobby (single-player via a private lobby).
