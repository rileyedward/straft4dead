# Straft 4 Dead roadmap

Finish and verify a phase before starting the next, then stop and check in. Local single-player first; never touch public matchmaking. Every feature sits behind a BepInEx config toggle.

| # | Phase | Done when |
|---|---|---|
| 0 | **Recon**: systems map + asset pipeline proof (one infected, one survivor) | `docs/systems-map.md` covers health/damage, rounds/takes, maps/spawns, navmesh, HUD, rig, netcode; one common infected and Ellis (FP arms, TP body, one animation, one voice line) load in STRAFTAT from an AssetBundle built from Riley's local L4D2 install |
| 1 | **Hello zombie**: placeholder enemy that chases, hits, and dies; health multiplier | A capsule enemy spawns on a STRAFTAT map, paths to the local player, deals damage through STRAFTAT's health, dies to every STRAFTAT weapon; player health multiplier in config |
| 2 | **L4D2 HUD and health**, incap then take ends | L4D2 HUD replaces STRAFTAT's; 100 HP scale with temp health (pills) and medkits; at 0 HP a short incap state (camera drop, red screen, sting) then the take ends |
| 3 | **Real common infected**: models, animations, sounds, navmesh | Common infected use extracted models, animation set and sounds, path on a navmesh baked at runtime |
| 4 | **Survivors**: arms, then bodies, then voice lines | Players render as L4D2 survivors in first and third person; contextual voice lines |
| 5 | **Director-lite**: wave pacing per take | Hordes ramp and relax per take, modeled on L4D2's director |
| 6 | **Special infected**, one at a time (Tank, Boomer, Witch first) | Each special has its L4D2 behavior and a PvP twist (bile draws the horde to the slimed player, the Witch hunts whoever startles her, ...) |
| 7 | **Mode toggle and launcher**: rule config, character select, first-run asset extraction | A player with STRAFTAT + L4D2 installed can set up the mod and extract assets locally without manual Blender/Unity steps |
| 8 | **Multiplayer**: host-authoritative zombies, private lobbies only, 2v2 revives | Friends in a private lobby see the same zombies; teammates can revive in 2v2 |

Status lives in `docs/devlog.md`.
