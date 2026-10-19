# STRAFTAT systems map

STRAFTAT internals, mapped for Straft 4 Dead. Every claim cites its evidence:

- **[IL]**: REA static analysis of `Assembly-CSharp.dll` (types, fields, method signatures, and the calls, field accesses and string literals inside each method body). No C# bodies, so branch logic is read from call/field order.
- **[data]**: read from a game data file.
- **[inferred]**: a reasonable reading of names and call order, not proven.
- **[unknown]**: not answered yet; each has a check listed in §12.

Snapshot: dev PC, 2026-10-18. `Assembly-CSharp.dll` 1,093,632 bytes, SHA-256 `24ef4fda…62593`, MVID `c35bd62d-5d50-484e-87c9-7a4efee3aec8`, 497 types. Re-run the REA pass after any game update (§13). REA output and indexes live outside the repo in `C:\Users\Riley\source\straft4dead-research\rea\` and are never committed.

---

## 1. Engine and runtime

| fact | value | evidence |
|---|---|---|
| Install path | `C:\Program Files (x86)\Steam\steamapps\common\STRAFTAT\` | [data] |
| Unity version | **2021.3.45f2** (`Application.unityVersion` logged by the plugin; `UnityPlayer.dll` file version 2021.3.45.8976527). f2 is the Oct 2025 security patch of 2021.3.45, changeset `88f88f591b2e` | [data] + runtime log |
| Scripting backend | **Mono** (`MonoBleedingEdge/`, `STRAFTAT_Data/Managed/*.dll`, no `GameAssembly.dll`) | [data] |
| Render pipeline | Built-in renderer + Post Processing Stack v2 (`Unity.Postprocessing.Runtime`, `PlayerSetup.ChangeMotionBlur` → `PostProcessVolume.profile`). URP/HDRP runtime DLLs are not shipped | [data] + [IL] |
| Asset storage | One 6.4 GB `data.unity3d` (compressed bundle-style container) plus `sharedassets*.resource` streams | [data] |
| Developer | LemaitreBros (`app.info`) | [data] |
| Key libraries | FishNet (networking), FishySteamworks transport (in `Assembly-CSharp-firstpass`), Heathen Steamworks, DOTween, Odin Serializer, TextMeshPro, new Input System, **Unity Animation Rigging**, Bakery, Easy Decal, ProBuilder | [data] |
| JSON in-process | `Newtonsoft.Json.dll` ships with the game | [data] |
| BepInEx | **BepInEx 5.4.23.5 (win x64) installed 2026-10-18 and loads the plugin** (`BepInEx\LogOutput.log`: `Straft 4 Dead 0.0.1 loaded`) | runtime log |
| Supporter Edition | `Straftat_SupporterEdition/` contains the artbook and a text file linking the official **Character** and **Scenes** `.blend` packs | [data] |

## 2. Modding and matchmaking (read this first)

STRAFTAT ships its own mod-awareness: `ComputerysModdingUtilities.dll`.

- `AssemblyScanner.GetForeignAssemblies` lists every loaded assembly that is not in `Managed/` and not BepInEx/Harmony/MonoMod/Cecil infrastructure. [IL]
- `AssemblyScanner.CreateMatchMakingKey` drops assemblies whose `[StraftatMod]` attribute says `IsVanillaCompatible = true`, then SHA-256 hashes the rest (`name@version`) into `MatchMakingKey`. [IL]
- `Assembly-CSharp` reads `MatchMakingKey` (3 call sites) and `ClientInstance.ReportModsToServer` / `ReceiveModsFromServer` log each client's mod list to the lobby. [IL]
- **Consequence:** a plugin *without* the vanilla-compatible attribute changes the matchmaking key, so modded clients are kept away from vanilla public matchmaking automatically. **Confirmed:** with the plugin loaded, `Player.log` prints `Incompatible assemblies found: Straft4Dead.Plugin, Version=0.0.1.0 …, Total: 1`.
- **Rule for this repo:** never add `[StraftatMod(true)]` / `IsVanillaCompatible`. Multiplayer (Phase 8) goes through private Steam lobbies only.

## 3. Player object

Spawned per player by `PlayerManager.SpawnPlayer` (`Instantiate(characterPrefab)` → `NetworkBehaviour.Spawn` with the owner's connection). Root is tagged **`Player`**. [IL]

| component | role | evidence |
|---|---|---|
| `FirstPersonController` (335 fields) | movement, camera, lean, slide, wall-jump, ladders, vault, footsteps, FOV; holds `animator : Animator`, `networkAnimator : NetworkAnimator`, `characterController`, `legsPivot`, `torso`, `legLeft/Right` | [IL] |
| `PlayerHealth` | health, death, ragdoll, kill-cam, dismemberment (§4) | [IL] |
| `PlayerPickup` | weapon pickup/drop/switch, hand IK (§9) | [IL] |
| `PlayerSetup` | enables/disables components for local vs remote, layers, HUD visibility (`HideHUD`), cosmetics (`ChangeDress`), FP arms (`fpArms[]`, `fpArmsSuits[]`) | [IL] |
| `PlayerValues` | links to `ClientInstance` (`playerClient` SyncVar), head transform, item raycast | [IL] |
| `PlayerShoot`, `Suppression`, `Vault`, `SlopeSlide`, `CameraEffect`, `Screenshake` | as named | [IL] |
| `FPArms` | first-person arm sway/bob; has its own `animator` | [IL] |

Local-player handle: `FirstPersonController.instance` (static) [IL]; per-connection data: `ClientInstance.Instance` (local) and `ClientInstance.playerInstances : Dictionary<int, ClientInstance>` keyed by `PlayerId`. [IL]

Character name in assets: **Aboubi** (`SK_Aboubi00_Hand_Left00`, `SK_Aboubi00_Hand_Right00`, `SM_Aboubi_Head00`, `Hips`). [IL string literals]

## 4. Health and damage

**`PlayerHealth : NetworkBehaviour`** [IL]

- `health : float` is a FishNet **SyncVar** (`syncVar___health`), plus SyncVars `isKilled`, `isShot`, `killer`, `canMove`, `spawnGeneration`, `lastKillDirection`, `playerValues`.
- `fullHealth : float`, `tempHealth : float` exist on the component (tempHealth usage still to trace).
- Field default `health = 10` in the constructor; the prefab value overrides it. [IL] **Full health is 4.0 units, shown as "100"** (logged at spawn in game, 2026-10-18; the minimalist HUD prints `Ceil(health / 4 * 100)`).
- Weapon defaults: `Weapon.damage = 1`, `headMultiplier = 2`. [IL] So a default body shot takes a quarter of a full bar.

**Hit path, hitscan (`Gun.ShootServer`)** [IL]

1. `Physics.RaycastAll(…, Weapon.defaultLayer)` for environment, glass (`tag == "ShatterableGlass"`), ragdolls (layer `Ragdoll`), surface VFX by tag.
2. `Physics.Raycast(…, Weapon.playerLayer)`; accept if hit layer is **11** and `hit.transform.root.CompareTag("Player")`.
3. `GetComponentInParent<PlayerHealth>` → `Weapon.FriendlyFireCheck`.
4. Headshot if the collider is named `Head_Col` or `Neck_1_Col` (× `headMultiplier`).
5. Server writes `health` directly (`sync___set_value_health`) in `RpcLogic___GiveDamage` / `KillServer`, sets `killer`, triggers `Dismemberment`, `KillCam`, `HitFeeback`.

The same `PlayerHealth` writes appear in `Shotgun`, `Minigun`, `LargeRaycastGun`, `BeamGun`, `ChargeGun`, `MeleeWeapon`, `RepulsiveGun`, `Claymore`, `HandGrenade(Two)`, `PhysicsGrenade`, `ProximityMine`, `Obus`, `PredictedProjectile`, `RebondBalle`, `ShrapnelBallistic`, `Bubble`, `PhysicsProp`, and `FirstPersonController` (falls / damage zones). [IL]

**How zombies take damage (Phase 1):** weapons only damage players on layer 11 under a `Player`-tagged root with a `PlayerHealth`, but every hitscan weapon also reports each *environment* hit through `Weapon.TriggerEnvironment(GameObject hit, Vector3 point, Vector3 normal, Vector3 direction)` (props, pigs, glass, mines). Zombies live on the Default layer, which is in the environment raycast mask (`defaultLayer`) of Gun, Shotgun, LargeRaycastGun, Minigun, BeamGun, ChargeGun, RepulsiveGun, Taser and DualLauncher (runtime check), so one Harmony prefix on `TriggerEnvironment` covers them. Melee (`MeleeWeapon.Fire`, private) gets a postfix sphere-sweep. Not covered yet: grenades, mines, launchers' explosions, thrown props. Non-player damage targets already exist as precedent: `DollHealth` (training doll), `PigHealth`, `PropDamage`. [IL]

**Death → take end** [IL]

1. `PlayerHealth.Update` (owner): when `health <= 0` and not `isKilled` and `!GameManager.roundWasWon` → `GameManager.PlayerDied(PlayerId, spawnGeneration)`, logs "fell into the void" / "commited suicide", `DespawnObject`, sets `health = -2000`, `isKilled = true`; `SafeDeathFix` coroutine as backup.
2. `GameManager.PlayerDied` (ServerRpc) → `RecordPlayerDeath` removes the id from `alivePlayers`, `GetAliveTeams`, starts `WaitForDraw`.
3. `WaitForDraw` waits ~1 s for a draw, then `ScoreManager.AddRoundScore` for the surviving team and `ProgressToNextTake` (`ScoreManager.SetRoundIndex` + `ObserversRoundSpawn` → `PlayerManager.RoundSpawn` → `CmdNewRoundSpawn`). `SceneMotor.testMap` short-circuits this.

**Incap hook (Phase 2):** prefix `PlayerHealth.Update` on the owner so the death branch is held while an incap state plays, then let it run. Keep `GameManager.PlayerDied` untouched so the take still ends through the game's own flow.

## 5. Rounds, takes and matches

| type | role | evidence |
|---|---|---|
| `GameManager` (NetworkBehaviour, singleton) | `alivePlayers`, `roundWasWon`, `currentTakeGeneration`, `playingTeams`, `FriendlyFireEnabled`, `EnemyOutlinesEnabled` (SyncVars); `StartGame`, `RoundWon`, `ProgressToNextTake`, `ObserversRoundSpawn`, `ScrambleTeams`, competitive tracking | [IL] |
| `ScoreManager` | `PlayerIdToTeamId`, `TeamIdToPlayerIds`, `Points`, `RoundScore`, `RoundScoreRequiredToWin`, `TakeIndex` (SyncVar); `CheckForRoundWin`, `AddRoundScore`, `ResetRound` | [IL] |
| `RoundManager` | end-of-round UI (names, scores, "+1", win/lose clips); `CmdEndRound` → `EndRoundObservers` → `NextRoundCall` | [IL] |
| `PauseManager` | round start UI and events: `OnRoundStarted`, `OnBeforeSpawn` (C# events, invoked on each spawn), `BetweenRounds`, `StartRoundDelay` | [IL] |
| `SceneMotor` | map rotation and network scene changes (`ServerStartGameScene`, `ChangeNetworkScene`, `GetNextMap`, `PlayListMaps`, `roundAmount`, `firstToXWins`, `testMap`) | [IL] |

Terminology as the game uses it: a **take** is one life-to-death exchange (`TakeIndex`, `currentTakeGeneration`); **rounds** are won by taking `RoundScoreRequiredToWin` takes; the **match** is first to `roundAmount` rounds (string "First Player who wins {0} Rounds wins the Match"). [IL, inferred]

**Director hook (Phase 5):** subscribe to `PauseManager.OnRoundStarted` / `OnBeforeSpawn` (public events, no patch needed) and watch `ScoreManager.TakeIndex`. [IL]

## 6. Maps, scenes and spawn points

- Maps are build-settings scenes (`MapsManager.InitMaps` iterates `SceneManager.sceneCountInBuildSettings`), grouped into playlists. Network scene loads go through FishNet `SceneManager` (`PlayerManager.OnLoadSceneStart/End`). [IL]
- Spawn points: `SpawnPoint` components (radius, snap-to-ground) under a root tagged **`Spawnpoints`** (1v1) or **`Spawnpoints4Player`** (4-player), collected by `PlayerManager.PopulateSpawnPoints`; `ReturnSpawnPoint` shuffles and rejects occupied points with `Physics.OverlapSphereNonAlloc`. [IL]
- Other gameplay tags: `Killz` (void), `DamageZone`, `Teleport`, `MovingObject`, `Ladder`. [IL]
- Weapons spawn from `ItemSpawner` / `WeaponDropper` (`Spawner`) using `SpawnerManager` weights. [IL]

## 7. NavMesh

- No game code calls `NavMesh` or `NavMeshAgent` except an Easy Decal demo script (`ch.sycoforge.Decal.Demo.PathAgent`). [IL]
- **STRAFTAT maps ship without baked NavMesh data.** A UnityPy census of all 697,704 objects in `data.unity3d` finds **0 `NavMeshData`** (375 scenes each carry only the default `NavMeshSettings`). [data] Zombies need a navmesh built at runtime from map geometry. Unity 2021.3 includes `NavMeshBuilder.BuildNavMeshData` in `UnityEngine.AIModule` (shipped), so a runtime bake per scene load is feasible without extra packages. [data]
- The plugin's `[diag]` log also prints the navmesh vertex count per scene as a runtime cross-check.
- **How we build one (Phase 1):** map collision is a single merged `MeshCollider` per map whose mesh has neither CPU data (`isReadable = false`) nor GPU buffers (index buffer is null), so `NavMeshBuilder.CollectSources` silently gets nothing and GPU readback is impossible. `Zombies/NavMeshBaker` samples walkable ground with physics instead: a 0.4 m grid of top-down raycasts per column (restarting under each hit, backface hits enabled), keeping hits that face up and have 1.75 m of headroom, then a dual-grid surface mesh, split into connected surfaces, each baked as its own `NavMeshData`. Arena_00: 20k rays, ~50 ms. Baking all surfaces together made Recast punch holes into the floor; per-surface baking is clean. Surfaces at different heights aren't linked yet (off-mesh links come later). [runtime]

## 8. HUD

- In-game HUD is uGUI + TextMeshPro, owned per player: `PlayerSetup.hudObject`, `HUD`, `HUDTween`, `HealthTween` (heart icon + number, colour lerp), `Crosshair`, `CrosshairThing`, `MatchPoitnsHUD`, `KillCam` (spectate text, enemy HP). [IL]
- `PauseManager` owns shared HUD pieces: ammo counters (`rightGunAmmo`, `leftGunAmmo`, reload variants), interact prompts, round start banners, `enemyHealthText`, `deadText`, `minimalistUi` (`minimalistHealthText`, FPS, ping). [IL]
- `PlayerSetup.HideHUD(bool)` is the game's own switch. [IL]
- **Replacement plan (Phase 2):** hide via `HideHUD(true)` / disabling `hudObject`, then draw the L4D2 HUD on our own Screen Space Overlay canvas reading `PlayerHealth.health` and the held `Weapon.currentAmmo`.

## 9. Player animator and rig

- Third-person body is animated by a Unity `Animator` synced with FishNet `NetworkAnimator`. Parameters set by `FirstPersonController`: `Grounded`, `Crouch`, `Slide`, `LeanLeft`, `LeanRight` (bool), `MovementSpeed`, `Vertical`, `crouchMove` (float), `Jump` (trigger). `PlayerPickup` sets hand-pose bools `RightHanded`, `LeftHanded`, `SingleHanded`, `DoubleHanded`, `DoubleSingle`, `TwoHanded`. `Weapon` triggers `Shoot`; reload coroutines trigger `Reload`. [IL]
- `FirstPersonController` also turns the legs separately (`legsPivot`, `legsRotation`) and has `torso` / `legLeft` / `legRight` references. [IL]
- **Unity Animation Rigging** drives the hands: `PlayerPickup` has `RightHandIKTarget`, `LeftHandIKTarget`, `TargetForRightIK`, `TargetForLeftIK`, idle positions, and calls `RigBuilder.Build()` on every pickup, drop, switch and hand fix. [IL]
- Ragdoll: `PlayerHealth.bodyParts[]`, `aboubiRagdoll`, `RagdollDress`; hit colliders named `Head_Col`, `Neck_1_Col`; bones include `Hips`. [IL]
- First-person arms are a separate object set (`PlayerSetup.fpArms[]`, `fpArmsSuits[]`, `FPArms` sway script) with their own animator. [IL]
- **The third-person rig is Humanoid.** [data, `tools/unity/inspect_rig.py`]
  - Avatars `SK_Aboubi_00Avatar` and `SK_Aboubi_01Avatar` (cosmetic variant): 18 of 25 body human bones mapped, both hands mapped, avatar scale 1.12, 48/51 transforms.
  - Skeleton: `SK_Aboubi_00/Hips/{Leg.L,Leg.R}/Shin/Foot` and `Hips/Torso/{Neck/Head, Shoulder.L/R/Arm/ForeArm/Wrist}` with two-segment fingers (`Thumb/Index/MiddleFinger/RingFinger/Pinkie_1,_2`). One spine bone (`Torso`), no toes, no chest. Body meshes are split per part (`SM_Aboubi_Head00`, `SM_Aboubi00_Torso`, `SK_Aboubi00_Arm_*`, `_Hand_*`, `_Leg_*`), which is how dismemberment works.
  - Every third-person clip is a **humanoid muscle clip** (130 Animator bindings, 0 Transform bindings): `Aboubi_Idle_00`, `Walk`, `Run`, `CrouchWalk`, `CrouchPose`, `Slide`, `Torso{Up,Down,Left,Right}_Pose`, `Gun_{Right,Left,Double,TwoHanded}`.
  - Controller `SK_Aboubi_00`: 26 clips, layers include Base (locomotion blend trees, lean states, jump) and an `Arms` layer for gun holds; params as above plus `speed`.
  - First-person arms are **Generic**: controller `PF_FPArm_Aboubi_00` (8 clips: `NoGun00`, `HoldGun_Right/Left/TwoHanded_00`, `Hold2Guns00`, `ReloadGun_TwoHanded_00`) uses Transform curves (68 bindings), and `Aboubi_IK_00` (5 clips) poses the IK targets.
- **Retarget proven in game (2026-10-18):** an Ellis body imported as Humanoid follows the menu Aboubi's run and crouch-slide live via `HumanPoseHandler.GetHumanPose` (Aboubi, after IK, in `LateUpdate`) → `SetHumanPose` (Ellis). Needed: an explicit ValveBiped bone map and an enforced T-pose in the Ellis avatar. Visible gaps to tune in Phase 4: spine/head lean and arm angles differ somewhat (Aboubi has one spine bone, Ellis four; proportions differ), and the menu Aboubi's root faces differently from its transform forward. First-person arms can't use humanoid retargeting; they need bone-to-bone mapping onto the Aboubi FP arm rig or L4D2's own viewmodel animations.

## 10. Networking

- **FishNet** with the **FishySteamworks** transport (Steam P2P via lobbies, `SteamLobby`, Heathen Steamworks). `ClientInstance.nonSteamworksTransport` / `PauseManager.nonSteamworksTransport` indicate a non-Steam transport path exists, likely for local testing. [IL]
- **Exploration mode** (`SteamLobby.EnterExplorationMap(sceneName)`, the menu's map buttons): creates a lobby if needed, keeps it private, and loads the map with the local player as host. No matchmaking involved; it's our solo test bed (F8 / `Dev.AutoTest`). Players respawn there after dying. [IL + runtime]
- Gameplay state uses SyncVars and Server/Observers/Target RPCs (the `RpcWriter___…` / `RpcLogic___…` methods are FishNet's code-gen). The server is a player-host. [IL]
- Player-hosted, so "host-authoritative zombies" in Phase 8 means: zombies simulate on the host and replicate to clients. Registering new `NetworkObject` prefabs at runtime in FishNet needs care (prefab ids must match on all peers); a lighter option is our own state messages over a FishNet broadcast. Decide in Phase 8.

## 11. Useful hook points (summary)

| need | hook | phase |
|---|---|---|
| Scene ready, spawn points known | `PlayerManager.OnLoadSceneEnd` postfix, or `SceneManager.sceneLoaded` | 1 |
| Zombies take weapon damage | prefix `Weapon.TriggerEnvironment` (all hitscan guns), postfix `MeleeWeapon.Fire` | 1 (done) |
| Zombies hurt players | `PlayerHealth.RpcLogic___RemoveHealth_431000436` on the server (syncs health, plays hit feedback) | 1 (done) |
| Health multiplier | scale `health` via `sync___set_value_health` once per spawn generation | 1 (done) |
| Incap before take ends | prefix `PlayerHealth.Update` death branch | 2 |
| HUD swap | `PlayerSetup.HideHUD`, `PlayerSetup.hudObject` | 2 |
| Take/round pacing | `PauseManager.OnRoundStarted`, `OnBeforeSpawn`; `ScoreManager.TakeIndex` | 5 |
| Survivor body | `PlayerSetup.ChangeDress` / `meshesToChange`, the player `Animator` | 4 |

## 12. Open questions and how to close them

| # | question | check |
|---|---|---|
| Q1 | ~~Is the Aboubi Avatar Humanoid?~~ **Yes** (§9) | closed |
| Q2 | ~~Full health~~ **4.0 units = 100** (runtime log) | closed |
| Q3 | ~~Any NavMeshData in scenes?~~ **None** (§7) | closed |
| Q4 | HUD prefab hierarchy | UnityPy, or UnityExplorer at runtime |
| Q5 | ~~Does the matchmaking key change with our plugin loaded?~~ **Yes** (§2) | closed |
| Q6 | ~~Layer names~~ closed: 3 Player, 6 Self, 7 Interactable, 8 HeldWeapon, 10 Ladder, **11 Body** (hit layer), 12 Members, 14 ShootThrough, 16 SelfBody, 18 DontCollideWithPlayer, 19 InteractEnvironment, 20 Grenade, 21 Melee, 23 Ragdoll, 24 Glass, 25 HUD, 27 InvisibleWall; **29–31 unused** (candidates for a zombie layer) | closed |
| Q7 | What reads `PlayerHealth.tempHealth`? | REA field-access pass |
| Q8 | Is there an offline/local mode we can launch without Steam lobbies? | Trace `nonSteamworksTransport` |

## 13. Re-running the REA pass

1. In Claude Code with the REA MCP server: `inspect_managed_members` on `STRAFTAT_Data\Managed\Assembly-CSharp.dll`, then `export_evidence_bundle` to `…\straft4dead-research\rea\assembly-csharp-evidence.json`.
2. `python -I tools/rea/rea_index.py <bundle> idx` writes `types.txt` and one `members/<Type>.txt` per type (fields, methods, callees).
3. `python -I tools/rea/rea_method.py <bundle> Type.Method` prints one method's calls, field accesses and string literals in IL order.
4. Compare the SHA-256 above; if it changed, diff `types.txt` and re-check every hook in §11.
