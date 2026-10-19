# Devlog

## Handoff for Phase 8 (multiplayer) and Phase 9 (README) — 2026-10-19

Riley asked to stop here and start Phase 8 in a fresh session together, then Phase 9 the same way. Nothing of
Phase 8 or 9 has been started.

**Where things are**
- Phases 0–7 done; all commits local on `main` (Riley pushes). Code under `plugin/src`, pipeline under `tools/`,
  bundle builders under `unity/S4DBundles/Assets/Editor`. Extracted assets and bundles live outside the repo in
  `C:\Users\Riley\source\straft4dead-research` (`bundles/`, `hud/`, `fbx/`, `crowbar/`).
- Everything runs **host-side and local**: zombies, specials, the director, `SurvivorHealth` and incap only run where
  `InstanceFinder.IsServer`; zombies are plain local GameObjects (no NetworkObject). Survivor arms/bodies/voice and
  the HUD are per client and use the local `[HUD] Survivor` for every player.
- Testing is solo in exploration mode via `[Dev] AutoTest` + `AutoTestMode` (zombies, health, arms, director,
  specials, panel, maps). Switching maps inside one exploration lobby doesn't work (HK_02 never loaded); test a
  map per launch with `[Dev] TestMap`.

**Phase 8 plan to discuss (nothing decided yet)**
1. Private lobbies only: keep the separate matchmaking key (plugin not `StraftatMod(vanillaCompatible)`); refuse to
   run the mode in public matchmaking.
2. Sync zombies host → clients. Options: (a) FishNet NetworkObjects spawned by the host (needs prefabs registered
   with the NetworkManager at runtime), or (b) our own lightweight state messages (id, position, yaw, anim state,
   death) over FishNet broadcasts, with clients spawning local visual zombies. (b) avoids touching STRAFTAT's
   prefab list; (a) gets interpolation for free.
3. Client hits on zombies: today `WeaponPatches` applies damage locally; clients must send hits to the host.
4. Damage to players already uses the synced `PlayerHealth.health`, so it replicates; check `SurvivorHealth`
   temp health / medkits / pills (server-side state) reach clients' HUDs (needs a sync).
5. Survivor choice per player (sync each player's `Survivor`, so bodies/voices differ), host zombie settings apply
   to everyone (the F9 panel already labels them "host").
6. 2v2 revives: incapped teammate can be helped up (hold E, 5 s) instead of bleeding out.
7. Known gap to check in MP: the third-person gun stays on the hidden Aboubi hand, so it may sit off the survivor's.

**Phase 9 (README)**: model it on github.com/rileyedward/valrally and rileyedward/how-to-steve using Riley's
README-writing skill. That skill wasn't available to the Phase 7 session; ask Riley where it is.

**Open items from Phases 5–7**
- Witch rendered standing in the close-up although `Idle_Sitting` data is a sitting pose; check the Witch clips.
- A Tank rock hit hasn't been seen in a test (the Tank closed in too fast to throw).
- Explosives still don't hurt zombies; zombies don't climb between navmesh surfaces.
- The setup script still needs Unity (free licence) and Crowbar's output folder set once by hand.

## 2026-10-19 — Phases 5, 6, 7 (Director, specials, panel and setup)

**Changed**
- Director-lite (`ZombieDirector`): every take opens with a horde storming in (`FirstHorde` 2 s, spawned at once
  from one direction with L4D2's mob stinger, zombat drums and an "incoming" line), then if the take lasts:
  build-up (wanderers that shamble until they see/hear you, get shot, or a neighbour alerts them) → next wave
  (40–60 s) → relax (10 s, and until stress drops). Stress = damage taken, close kills, incaps. New takes clear the
  map. Wanderer and horde counts scale with walkable area (`FullSizeArea` 1500 m²: Arena_00 gets 8, bigger maps
  12). Huge maps bake only `MaxBakeSize` 140 m around the spawn points.
- Guns do 4× damage to zombies (`WeaponDamageMultiplier`): everything one-shots a common except Phoenix, Kusma,
  Yangtse and Mac10 (two). Zombie hits write health directly, so STRAFTAT's PvP hit flash no longer paints the
  screen red during a horde; the HUD gives a faint flash instead.
- Specials (`build_specials.py` + `S4DSpecialBuilder` → `s4d_specials`; `Specials.cs`): Tank (1000 HP, punches
  24, throws rocks at range, its own music), Boomer (vomit → green screen + horde called onto you, bursts into bile
  on death), Witch (cries; rage builds when you're close or on a shot, then charges and incaps in one hit, flees).
  Survivors call them out. All on the shared `IInfected` damage path.
- Phase 7: F9 panel (`HostPanel`) with the mode switch, survivor pick, and the host's zombie settings; switching the
  mode or a part of it off applies live (STRAFTAT's HUD, arms and bodies come back, zombies are cleared, L4D2
  health rules stop). `tools/setup/setup.ps1` builds every asset from the player's own L4D2 install and writes the
  config; the main menu says when assets are missing. `build_hud.py` makes the HUD textures.

**Verified in game (AutoTest)**: director cycle on Arena_00, Chateaux_01 and Backrooms_00; specials spawn, act,
take weapon hits and die; bile horde; panel: mode off → vanilla HUD and Aboubi, on as Coach → Coach's arms,
body, portrait.

**Broke / learned**
- The opening horde never came on Backrooms_00: no reachable floor in the 15–35 m ring and stress hit the peak
  first. The horde now widens its search and the opening horde ignores stress.
- Babbdi's full-map bake ran for minutes → `MaxBakeSize`.
- Re-showing STRAFTAT's HUD must only restore what we hid (the minimalist health text is off in vanilla).


## 2026-10-19 — Phase 4 (Survivors: arms, bodies, voice)

**Changed**
- All four L4D2 survivors (Ellis, Nick, Rochelle, Coach) are built into an `s4d_survivors` bundle (`tools/pipeline/build_survivors.py` → `S4DSurvivorBuilder.Build`). `[HUD] Survivor` now picks the arms, body, voice and portrait together; `[Survivors]` has Bundle, Arms, Bodies, Voice toggles.
- First-person arms (`SurvivorArms`): STRAFTAT's Aboubi arms stay in place, hidden, and keep running their animator and gun IK; every frame their pose is copied onto the L4D2 viewmodel arms. Rotations go through anatomical frames built the same way on both rigs (bone direction rolled by the elbow bend or knuckle line), shoulders, elbows and wrists are copied in position, the hands are scaled to match the grip.
- Third-person bodies (`SurvivorBody`): humanoid-to-humanoid retarget with `HumanPoseHandler` from each player's Aboubi, and from death ragdolls (their own humanoid avatar; the pelvis is pinned to the ragdoll hips). The Aboubi is hidden with `forceRenderingOff` so the game's own show/hide (your own body, dismemberment) still decides when the survivor shows.
- Voice (`SurvivorVoice`): the survivor's own L4D2 lines for hurt (minor/major/critical), pills, first aid, incap, death and every few zombie kills, loaded from `sound/player/survivor/voice/<survivor>`.
- `AutoTestMode=arms` dumps the arm and body rigs, picks up a map weapon and renders arms and body.

**Verified in game (Arena_00, AutoTest)**
- Ellis's and Rochelle's arms holding a pistol in first person; Ellis and Rochelle bodies standing with the gun pose; Ellis as a death ragdoll; voice lines fire for damage, pills, medkit, incap and bleed-out; the zombie test still passes with everything on.

**Broke / learned**
- STRAFTAT's arm rig is camera-space, layer 8 (drawn by `WeaponCam`), armature scaled 100 under a ×2 parent; its fingers have two segments, L4D2's three.
- The local player's third-person renderers are disabled by the game (never `forceRenderingOff`), which is how we know to keep the survivor hidden too.
- `build_*.py` must pass Blender an absolute work dir: relative image save paths resolved elsewhere and the textures silently went missing.
- The HUD portraits for Nick, Rochelle and Coach (`s_panel_<survivor>[_incap]`) were missing from the HUD folder; converted now.

**Known gaps**
- The third-person gun stays on the hidden Aboubi's hand, so it can sit slightly off the survivor's hand (only other players see this; check in Phase 8).
- Remote players use your own `Survivor` choice until multiplayer syncs it (Phase 8).
- Survivor normal maps and phong aren't used (Standard shader, base texture only).

**Next**
- Phase 5: director-lite.


## 2026-10-19 — Phase 3 (Real common infected)

**Changed**
- Commons are real L4D2 infected now: six outfits (tshirt/cargos, polo/jeans, dress shirt/jeans, tank top/jeans; female tank top/jeans, t-shirt/skirt), each zombie with a random head and body variant and its own skin and clothing colours, animated with L4D2's idle, shamble, run, melee, shove and two death clips, and voiced with 254 L4D2 infected sounds (alert, rage, idle moans, claw hits, bullet impacts, deaths). Capsules stay as the fallback without the bundle.
- Pipeline: `tools/pipeline/build_infected.py` → Unity `S4DInfectedBuilder.Build` → `s4d_infected` bundle, config `[Zombies] InfectedBundle`.
- `S4D/Infected` shader rebuilds L4D2's Infected look: base texture alpha = grey detail, blue = skin mask, red/green = blood/grime; colours from `ci_gradient_palette` rows (skin ramp in rows 0–7, top/bottom clothing ramps in rows 8–15). Our reading of the textures, tuned by eye.

**Verified in game (Arena_00, AutoTest)**
- Close-up render of a female common running with correct skin, hair, shirt and blood; top-down shot of the horde on the player; weapon hit kills one and its death clip plays.

**Broke / learned**
- Humanoid retargeting placed commons tens of metres away and under the floor (clip root offsets from the Source rig). Commons use a Generic rig with a shared armature name and in-place root bakes instead; survivors (Phase 4) can still use Humanoid for STRAFTAT's animations.
- L4D2 commons import with the pelvis at the origin; non-raw exports are now grounded at the feet.
- Unity batch builds with `-nographics` produce custom shaders without GPU programs (magenta in game). Build bundles without it.
- Several bodygroup meshes share one material; recreating the material asset per mesh orphaned the earlier references (magenta heads).

**Next**
- Phase 4: survivors (first-person arms, then bodies, then voice lines).


## 2026-10-19 — Phase 2 (L4D2 HUD and health, incap then take ends)

**Changed**
- Health: STRAFTAT's single synced value is split into permanent + temporary HP and kept equal to their sum, so guns, death and the take flow are untouched. Damage eats temp first; temp decays 0.27 HP/s; pills (key 5) +50 temp; first aid kit (hold 4 for 5 s) heals 80% of missing health and clears temp; each spawn gets 1 kit + 1 pills. `[Players]` config.
- Incap: a health write that would reach 0 starts a 3 s incap (health held at a sliver so guns don't run their kill code); camera drops 1.1 m and rolls, movement locks, red screen, L4D2's incap sting (`music/tags/clingingtohellhit1.wav`), portrait swaps to the incap one; then health goes to 0 and STRAFTAT's own death ends the take. Void/suicide still kill instantly.
- HUD: L4D2 survivor panel bottom-left (Ellis portrait, health number, green/orange/red bar with striped temp health), kit/pills slots, ammo bottom-right, kit progress bar, low-health pulse. STRAFTAT's 3D heart and ammo counters (`SM_HUD_00`) are hidden; FPS counter and match points stay. Textures load at runtime from `[HUD] TextureDir` (PNG converted from the local L4D2 install, `straft4dead-research\hud`).

**Verified in game (Arena_00, AutoTestMode=health)**
- 100 → 70 damage → 30; pills → 30 + 50 temp (striped); 20 damage → temp 29.5 (temp first); kit → 86 (30 + 80% of 70), temp cleared; 500 damage → incap 3 s (screenshots: camera down, red screen, incap portrait) → bled out → death → respawn.

**Broke / learned**
- STRAFTAT's HUD is 3D meshes under the UI camera, not uGUI; an early "climb to the canvas" helper disabled the whole player object.
- Not tested: a PvP gun kill going through incap (needs a second player), and the sting's audio (loaded, not heard).

**Next**
- Riley's check-in, then Phase 3 (real common infected: models, animations, sounds).


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
