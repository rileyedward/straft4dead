# Straft 4 Dead

Left 4 Dead 2 inside STRAFTAT.

## Overview

### What is Straft 4 Dead?

Straft 4 Dead is a STRAFTAT mod that throws Left 4 Dead 2's infected into STRAFTAT's arena fights. Every take opens with a horde storming in while you still have to outplay your opponent with STRAFTAT's movement and guns. You play as Ellis, Nick, Rochelle or Coach, with their own arms, bodies and voice lines, on L4D2's survivor HUD with permanent and temporary health, pills, medkits and incaps. A director paces the infected the way L4D2 does, and the Tank, the Boomer and the Witch turn up when you least want them.

All the zombie AI, the director and the health rules are written from scratch for STRAFTAT. Left 4 Dead 2 is only the source of the models, animations, sounds and HUD art, which are built locally from your own L4D2 install and never shipped.

Straft 4 Dead is not produced, approved or endorsed by Valve or Lemaitre Bros. It ships no code or assets from Left 4 Dead 2 or STRAFTAT.

> **Status: personal test build.** Single player has been played and tested in game on several maps. Multiplayer has been tested with two copies of the game on one PC; a real game with friends over Steam hasn't been played yet. See [docs/devlog.md](docs/devlog.md).

### Key Features

- **Common Infected**: Six L4D2 outfits with random heads, bodies, skin and clothing colours, L4D2's own animations and sounds, and a navmesh built at runtime for every map. Wanderers shamble until they see or hear you, then set off their neighbours.
- **Director**: Each take opens with a horde, then waves if the take lasts, with calm spells when you're hurting. Horde size scales with the map, and huge maps only build zombies around the spawns.
- **Special Infected**: The Tank punches and throws rocks, with its own music. The Boomer's bile blinds you and calls a horde onto you. The Witch cries until you get close, then takes you down in one hit.
- **Survivors**: Ellis, Nick, Rochelle and Coach as first-person arms that hold STRAFTAT's guns, third-person bodies (corpses included) and voice lines for getting hurt, healing, going down and calling out specials.
- **L4D2 Health and HUD**: Permanent and temporary health, pain pills, medkits and incaps on the L4D2 survivor HUD. Most guns one-shot a common.
- **Multiplayer**: Private lobbies with friends. The host runs the infected and everyone sees the same zombies, and in 2v2 you can revive a downed teammate.
- **Host Settings Panel**: Press `F9` for the mode switch, your survivor and every zombie setting, applied live.

## Getting Started

### Prerequisites

Ensure you have the following installed on your system. You can verify each one with the provided commands in a terminal.

1. **STRAFTAT** on Steam (Windows), with [BepInEx 5](https://github.com/BepInEx/BepInEx/releases) (x64, 5.4.23 or newer) extracted into the game folder and the game started once.

2. **Left 4 Dead 2** on Steam. The mod builds its models, animations, sounds and HUD art from your copy.

3. **Python** (3.10+) runs the asset pipeline. Check it by running:

    ```bash
    python --version
    ```

4. **Blender** (5.2 is what the mod is built with) with the [SourceIO](https://github.com/REDxEYE/SourceIO) add-on enabled converts L4D2 models and textures.

5. **Crowbar** (0.74) decompiles L4D2's animation sets. Open it once and set the Decompile output folder to `<asset folder>\crowbar`.

6. **Unity 2021.3.45f2** (a free Personal licence through Unity Hub) packs the converted assets into bundles the game can load.

### Installation

1. Download `Straft4Dead-<version>.zip` from the [Releases](../../releases) page and extract it anywhere, for example `Documents\Straft4Dead`.

2. Copy its `BepInEx\plugins\Straft4Dead` folder into `<STRAFTAT>\BepInEx\plugins\`.

3. Close STRAFTAT, then build the L4D2 assets from your own install. Pick any folder for them, and fix the paths to match your PC:

    ```bash
    powershell -ExecutionPolicy Bypass -File tools\setup\setup.ps1 ^
        -L4D2 "C:\Program Files (x86)\Steam\steamapps\common\Left 4 Dead 2" ^
        -Straftat "C:\Program Files (x86)\Steam\steamapps\common\STRAFTAT" ^
        -Work "%USERPROFILE%\Straft4DeadAssets" ^
        -Blender "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" ^
        -Crowbar "C:\Tools\Crowbar\Crowbar.exe" ^
        -Unity "C:\Program Files\Unity\Hub\Editor\2021.3.45f2\Editor\Unity.exe"
    ```

    This takes a while the first time. Steps that are already done are skipped on the next run (`-Force` redoes everything), and at the end it points the mod's config at the new assets.

4. Start STRAFTAT. If the main menu says the L4D2 assets aren't set up, check the setup script's output.

To play plain STRAFTAT again, switch the mode off in the `F9` panel, or delete the `Straft4Dead` plugin folder.

Straft 4 Dead marks itself as not vanilla-compatible, so STRAFTAT puts you in a separate matchmaking pool and you never meet unmodded players. Play it in single player or in private lobbies with friends who have the mod and the same version.

### Controls

| Key | Action |
|---|---|
| `5` | Take pain pills (+50 temporary health) |
| `4` (hold) | Heal with a medkit (80% of missing health, 5 s) |
| `E` (hold) | Revive a downed teammate in 2v2 (stand over them) |
| `F9` | Open the Straft 4 Dead panel: mode switch, survivor, host zombie settings |
| `F8` | Dev: enter `[Dev] TestMap` in exploration mode (a private, host-only lobby) |

Everything else is STRAFTAT's own controls.

### Configuration

Settings are in `BepInEx\config\dev.riley.straft4dead.cfg`, created on first run. The `F9` panel changes the most used ones in game and saves them. The main switches:

| Setting | Default | What it does |
|---|---|---|
| `[General] Enabled` | `true` | The whole mode; `false` is plain STRAFTAT |
| `[General] PanelKey` | `F9` | Opens the in-game panel |
| `[HUD] Survivor` | `mechanic` | Your survivor: `mechanic` (Ellis), `gambler` (Nick), `producer` (Rochelle), `coach` |
| `[Zombies] Enabled` | `true` | Common infected |
| `[Zombies] MaxAlive` | `30` | Most zombies alive at once |
| `[Zombies] Strength` | `2` | Zombie strength: how hard commons and the Tank hit (1 = L4D2 normal, 2 = arena pace) |
| `[Zombies] WeaponDamageMultiplier` | `4` | Gun damage against zombies (4 makes most guns one-shot a common) |
| `[Director] HordeSize` | `12` | Commons in a horde before map scaling |
| `[Director] FirstHorde` | `2` | Seconds into a take before the opening horde |
| `[Director] HordeIntervalMin` / `Max` | `40` / `60` | Gap before the next wave if the take lasts |
| `[Director] FullSizeArea` | `1500` | Walkable m² for full numbers; smaller maps get fewer (0 turns scaling off) |
| `[Specials] TankChance` / `WitchChance` | `0.35` / `0.5` | Chance per take of a Tank or a Witch |
| `[Specials] BoomerInterval` | `30` | Average seconds between Boomers |
| `[Players] HealthMultiplier` | `1` | Scales every player's health |
| `[Players] TeamIncapSeconds` / `ReviveSeconds` | `20` / `4` | 2v2: bleed-out time and revive time |
| `[Survivors] Arms` / `Bodies` / `Voice` | `true` | Each part of the survivors |

The rest of the file holds zombie speed, damage and reach, the bundle and texture paths the setup script writes, the health rules and the dev test switches. In multiplayer the host's zombie settings are the ones that apply.

## Development

### Prerequisites

1. **.NET SDK** (8+) builds the plugin. Check it by running:

    ```bash
    dotnet --version
    ```

2. **STRAFTAT** with **BepInEx 5** installed in the game folder: the plugin compiles against the game's own assemblies, which are never copied into the repository.

3. The asset tools from [Getting Started](#prerequisites) (Python, Blender with SourceIO, Crowbar, Unity 2021.3.45f2) to build the L4D2 assets.

### Installation

1. Point the build at your STRAFTAT install. Copy the example file and fix the path if your Steam library is elsewhere:

    ```bash
    copy plugin\Directory.Build.props.user.example plugin\Directory.Build.props.user
    ```

2. Build and deploy. This copies `Straft4Dead.Plugin.dll` into `<STRAFTAT>\BepInEx\plugins\Straft4Dead`. Close STRAFTAT first:

    ```bash
    dotnet build plugin -c Release -p:Deploy=true
    ```

3. Build the L4D2 assets once with `tools\setup\setup.ps1` (see [Installation](#installation)), then start STRAFTAT and press `F8` to enter the test map.

### Project Structure

| Folder | What it is |
|---|---|
| `plugin/src/Zombies/` | Common infected, specials, the director, the runtime navmesh baker and the L4D2 sound and asset loaders |
| `plugin/src/Survivors/` | L4D2 health rules, incaps and revives, first-person arms, third-person bodies and voice lines |
| `plugin/src/Hud/` | The L4D2 HUD and the `F9` panel |
| `plugin/src/Net/` | Multiplayer: the host → client sync, and the dev LAN mode for two copies on one PC |
| `plugin/src/Patches/` | Harmony patches on STRAFTAT's weapons and health |
| `plugin/src/Dev/` | Automated in-game tests and screenshots |
| `tools/setup/` | The one-command asset setup |
| `tools/pipeline/` | The asset pipeline: VPK reader, Blender export, texture conversion, and the build scripts for commons, survivors, specials and the HUD |
| `unity/S4DBundles/` | Unity project that packs the converted assets into bundles (editor scripts and the infected shader) |
| `tools/dev/` | Config helper, second-copy builder and the two-copy LAN test |
| `tools/rea/`, `tools/unity/` | Helpers for mapping STRAFTAT's internals and assets |
| `tools/release/` | The release build script |
| `docs/` | [Roadmap](docs/roadmap.md), [systems map](docs/systems-map.md) (STRAFTAT internals with evidence), [asset pipeline](docs/asset-pipeline.md) and the [devlog](docs/devlog.md) |

## How It Works

The plugin runs inside STRAFTAT through BepInEx and leaves STRAFTAT's own systems in charge wherever it can:

- **Damage**: STRAFTAT's hitscan guns already report every environment hit, so zombies sit on that layer and one Harmony patch turns those hits into zombie damage. Zombie hits on players go through STRAFTAT's synced health value, so its HUD, deaths and take endings keep working.
- **Health**: L4D2's permanent and temporary health are split on top of STRAFTAT's single health value. At zero you go down instead of dying; bleeding out runs STRAFTAT's normal death.
- **Navmesh**: STRAFTAT maps ship no navmesh and no readable meshes, so the walkable floor is sampled with physics rays when a map loads and baked surface by surface.
- **Survivors**: The STRAFTAT character is hidden but keeps animating; its humanoid pose is copied onto the L4D2 body every frame, and its first-person arm rig drives the L4D2 arms bone by bone.
- **Multiplayer**: The host runs the director and every zombie's AI. Clients get puppets that follow the host's positions, animations and sounds. Clients send their hits, pills, medkits and revives to the host and get their own health back for the HUD. It all rides on one FishNet broadcast inside STRAFTAT's own session, so nothing extra is opened.

## Releasing

Releases are built locally, because the plugin compiles against STRAFTAT's assemblies:

```bash
powershell -ExecutionPolicy Bypass -File tools\release\build-release.ps1 -Downloads
```

This builds the plugin and writes `dist\Straft4Dead-<version>.zip` (the plugin, the setup script, the asset pipeline and the bundle-builder project) and its `.sha256` file, and copies both to your Downloads folder with `-Downloads`.

## Testing

Build the plugin:

```bash
dotnet build plugin -c Release
```

The in-game tests run without input. Set `[Dev] AutoTest = true`, pick `[Dev] AutoTestMode` and a `[Dev] TestMap`, then start STRAFTAT. They write their results to `BepInEx\LogOutput.log` and screenshots to the `BepInEx` folder:

| Mode | What it checks |
|---|---|
| `zombies` | Spawning, chasing and a real weapon hit killing a zombie |
| `health` | Damage, pills, a medkit, incap and bleed-out |
| `arms` | The survivor arms and body, holding a picked-up gun |
| `director` | The opening horde, waves and calm spells over two minutes |
| `specials` | A Tank, a Boomer and a Witch acting and dying to weapon hits |
| `panel` | The `F9` panel and switching the mode and survivor live |
| `lan` | With the two-copy setup: damage, bile, a 2v2 down and revive across copies |

To test multiplayer on one PC, build a second copy of the game with `tools\dev\make_client_instance.ps1`, then run both with `bash tools/dev/lan_test.sh <second copy>`. The copies talk over a local connection that only exists when the game is started with `--s4d-lan`.

## Data and Licensing

Straft 4 Dead contains no Left 4 Dead 2 or STRAFTAT code or assets. Models, animations, sounds and HUD art are converted at setup from the player's own Left 4 Dead 2 install and loaded at runtime; they are never committed or redistributed. STRAFTAT's internals in [docs/systems-map.md](docs/systems-map.md) were mapped from assembly metadata and reading the game's code locally; no decompiled code is included.

Built with [BepInEx](https://github.com/BepInEx/BepInEx) (LGPL-2.1) and [Harmony](https://github.com/pardeike/Harmony) (MIT), which are installed separately and not included in the release. The asset setup uses [SourceIO](https://github.com/REDxEYE/SourceIO), [Crowbar](https://steamcommunity.com/groups/CrowbarTool) and Unity, which are installed separately.

Left 4 Dead 2 is a trademark of Valve Corporation. STRAFTAT is developed by Lemaitre Bros.
