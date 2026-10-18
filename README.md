# Straft 4 Dead

A personal mod that brings Left 4 Dead 2 into STRAFTAT: survive L4D2 hordes and special infected while outplaying your opponent in STRAFTAT's arena modes, as the L4D2 survivors with the L4D2 HUD, on STRAFTAT's movement and guns.

- **Host game:** STRAFTAT (Unity 2021.3, Mono). Gameplay is a BepInEx 5 plugin with Harmony patches.
- **Asset donor:** your own Left 4 Dead 2 install. Models, animations, sounds and HUD textures are extracted locally (VPK → SourceIO/Crowbar → Blender → FBX → Unity AssetBundle). **No Valve or STRAFTAT assets are ever committed or redistributed.**
- **Local single-player first.** Multiplayer later, private lobbies only. The plugin is deliberately not marked vanilla-compatible, so STRAFTAT keeps modded clients out of public matchmaking.

## Layout

| path | what |
|---|---|
| `plugin/` | BepInEx 5 plugin (`Straft4Dead.Plugin.dll`, GUID `dev.riley.straft4dead`) |
| `tools/rea/` | helpers that index REA's Evidence export of `Assembly-CSharp.dll` |
| `tools/pipeline/` | asset pipeline scripts (L4D2 → Unity) |
| `docs/roadmap.md` | phases 0–8 |
| `docs/systems-map.md` | STRAFTAT internals and hook points |
| `docs/asset-pipeline.md` | every extraction step and tool |
| `docs/devlog.md` | what changed, what broke, next steps |

## Build

1. Copy `plugin/Directory.Build.props.user.example` to `plugin/Directory.Build.props.user` and check the STRAFTAT path.
2. `dotnet build plugin -c Release`
3. With BepInEx 5 installed in STRAFTAT: `dotnet build plugin -c Release -p:Deploy=true` copies the DLL to `BepInEx\plugins\Straft4Dead\`. Don't deploy while STRAFTAT is running.

Config: `BepInEx\config\dev.riley.straft4dead.cfg`. Every feature has its own toggle; `General.Enabled = false` turns the whole mod off.
