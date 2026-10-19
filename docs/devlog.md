# Devlog

## 2026-10-18 — Phase 1 (Hello zombie)

**Changed**
- Placeholder zombies (capsule + head) spawn 10–35 m from the player, path on a runtime navmesh, swing for 5 HP every second within 1.5 m, and die at 50 HP. All values in `[Zombies]` config, in L4D2 HP (100 = a full STRAFTAT bar = 4 units, confirmed at spawn).
- Weapon hits: one Harmony prefix on `Weapon.TriggerEnvironment` covers every hitscan gun (their environment masks include our Default-layer zombies, checked in game); melee via a `MeleeWeapon.Fire` postfix sweep. A default body shot does 25 HP, so a common dies to 2 body shots or 1 SMG headshot + 1.
- `[Players] HealthMultiplier` scales each spawn's health on the host (x2 verified: 200 HP, zombies took it from 200 down).
- Dev tooling: F8 enters `Dev.TestMap` in STRAFTAT's exploration mode (private, host-only); `Dev.AutoTest` does it from the menu, fires a real weapon's hit path at a zombie, and saves screenshots; `Dev.DrawNavMesh`.

**Verified in game (Arena_00, AutoTest)**
- Navmesh: 10 surfaces, ~50 ms. Zombies spawned, chased, hit the player 100 → 0, the player died ("Flashy died") and respawned; a weapon hit through `TriggerEnvironment` killed a zombie (25 + 25 HP headshots).

**Broke / learned**
- Navmesh was the hard part. Map collision is one MeshCollider without CPU or GPU mesh data, so Unity's source collection and GPU readback both fail; physics sampling works. Baking all surfaces in one NavMeshData produced holes in the floor (cause unclear, deterministic); baking each connected surface separately is clean.
- The BepInEx manager object never sees `sceneLoaded` in STRAFTAT; the plugin runs on its own persistent object.

**Not covered yet / open**
- Explosives, mines, launchers and thrown props don't hurt zombies.
- Zombies don't jump or drop between surfaces (no off-mesh links); players on block tops are safe.
- Zombies persist across takes; whether a take should clear them is a Phase 5 (director) decision.
- Not yet shot with real mouse input or a melee swing by hand (the hit path is proven by the self-test).

**Next**
- Riley's check-in, then Phase 2 (L4D2 HUD and health, incap then take ends).


## 2026-10-18 — Phase 0 (Recon), session 1, part 2: pipeline proof

**Changed**
- Installed (Riley's OK): BepInEx 5.4.23.5 in STRAFTAT, Blender 5.2.2 portable + SourceIO 5.5.4, Crowbar 0.74, Unity Hub (MSIX) + Unity 2021.3.45f2, UnityPy, py7zr.
- Plugin loads in STRAFTAT; the game puts us in a separate matchmaking pool. Plugin now runs on its own persistent object (the BepInEx manager object never received `sceneLoaded`).
- UnityPy scan: Aboubi third-person rig is Humanoid with humanoid clips; FP arms Generic; no NavMeshData anywhere; layer table recorded.
- Full pipeline proven in game: infected (L4D2 run), Ellis (L4D2 collapse-to-incap), Ellis retargeted live from Aboubi, Ellis FP arms, Ellis voice line. See `docs/phase0-report.md`.

**Broke / learned**
- download.blender.org 403s winget; used the nluug mirror. Unity Hub's winget install is an MSIX app, not in Program Files. Unity batch mode needs Riley's signed-in Personal licence.
- SourceIO's L4D2 `.ani` import produces broken poses; animations go through Crowbar SMDs instead. Unity's humanoid auto-mapper rejects ValveBiped; explicit map + enforced T-pose fixes it. SourceIO's default scale is the map scale (0.01905); characters need 0.0254. The commons import lying down. Exporting with an active action bakes that pose into Unity's bind pose.
- Infected `Infected` shader isn't convertible: placeholder grey until Phase 3.

**Next**
- Stop here for Riley's check-in (Phase 0 boundary). Then Phase 1: placeholder zombie that chases, hits, dies; health multiplier.


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
