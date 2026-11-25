# Metroid Prime Dreamport (Somnium Space)

Metroid Prime (GameCube) inside a Somnium Space world, played as your own Somnium avatar, in VR or on a monitor. The world ships **no game
data**: every room, model, texture, material, sound, music track, HUD frame, font, string and number is read at run time from the player's
own disc image (`.iso`, `.gcm` or `.ciso`). Our code carries only ids, offsets and the rules of the game's formats (read from the
decompilation projects, never copied data).

Where the disc is looked for: `disc=<full path>` in `%USERPROFILE%\AppData\LocalLow\...\MetroidDreamport\settings.txt` (written after the
first find), the ISO folders in Dolphin's `Dolphin.ini`, then a bounded search of the drives. RVZ / WIA images are reported as unsupported
(convert them to ISO with Dolphin). Access is the same whitelist as the other Dreamports: anyone else starts nothing. Log lines are tagged
`[SomniumMetroid]`.

## Iterations

| # | Date | What |
|---|------|------|
| 1.1 | 2026-10-16 | Gun polish: the game's own particle system (PART / SWHC / WPSC / CRSC read from the disc and run at 60 Hz as the game does): every beam's normal and charged shots with their trails and lights, impacts picked by the surface hit with the game's impact sounds, missiles, the muzzle flash, the charge glow and aux muzzle with the GameCube charge timing (charged shot ready after 1 s, full at 1.8 s; damage and size follow the charge); the game's combat reticle on the monitor (charge gauge and ticks, beam brackets); shots pass a closed door the way the game lets them, so the hangar's upper lock buttons can be shot; doors pair the game's way (the door that opens first owns the doorway, so the other side's close timer no longer slams it shut and re-locks it - the Air Lock's far door now stays open); shots go through actors whose vulnerability to the weapon is pass-through |
| 1.0 | 2026-10-16 | First playable: the Space Pirate Frigate from your own disc (rooms streamed around you, the game's own materials, lightmaps, collision, doors, platforms, script logic, skybox); the arm cannon on the right hand with Power / Wave / Ice / Plasma beams (the game's damage, cooldowns, speeds), charge shots, missiles on B; visors (Combat, Scan, Thermal, X-Ray) and beams picked by holding grip and moving the hand; the scan visor (scan points, the game's scan times, the scan text, ScanDone opening what it opens in the game); the helmet HUD from the game's own frames; the game's own sound effects (MusyX sound macros and samples) and streamed music. **All beams and visors are unlocked for testing** (`MpPlayerState.UnlockAll`) |

## Controls

| | VR | Monitor |
|---|---|---|
| Fire / charge | right trigger (hold to charge, let go to fire) | left mouse (hold to charge) |
| Missile | B (right) - the Somnium tablet on B is held off while playing | F |
| Beam | hold right grip, move the hand up (Power), right (Wave), down (Ice), left (Plasma) | 1 Power, 2 Wave, 3 Ice, 4 Plasma |
| Visor | hold left grip, move the hand up (Combat), right (X-Ray), down (Thermal), left (Scan) | Z Combat, X Scan, C Thermal, V X-Ray |
| Scan | Scan visor, look at an orange marker, hold fire | same |

The directions are the game's own visor / beam menu layout (CHudVisorBeamMenu). A short tick on the controller marks each pick.

## Not in yet / known problems

* **The HUD message line does not draw**: scan texts and HUD memos are found and logged (`Scan: text "..."`) but not shown on the HUD yet;
  the scan visor's info panel (CScanDisplay) is not built either.
* **The cannon does not animate yet**: all four beams share one cannon model (CMDL 19AE626C) that the game reshapes with each beam's
  animations (the beam's own pose, the charge-up opening, the shot recoil). Skeletal animation (CINF / CSKR / ANIM) is not ported, so
  every beam shows the same bind-pose cannon and the charge has its effects but not the cannon's own charge movement.
* Not in the gun effects yet: electric effects (ELSC), the x-ray visor's subtractive muzzle drawing, the combo (missile + charge) beams.
* The monitor reticle is the game's lock-on reticle standing on what the view's centre is on (the GameCube game shows it only on a
  lock-on, and lock-on is not in yet).
* The thermal and x-ray views are per-pixel approximations with the game's palettes (the game uses frame-buffer passes and noise).
* Enemies and their animation (actors stand in their bind pose), the world's own particle effects (script effects; only the gun uses the
  particle system so far), damage to Samus, the morph ball, items to pick up, saving, cinematics (the intro music and voice play at the
  start instead), the map screen, worlds after the frigate.
* VR (hands, grip selection, laser aim, B missile, tablet held off) is written from the BO2 / Skyrim ports but not yet tried in the client.

## Lab results (2026-10-16, `DevTools/lab.sh play`)

Exterior Docking Hangar from the disc, the frigate's starfield sky, cannon / HUD / sound ready; a shot, a full charge and charged shot,
a missile, all four beams fired (every shot hit the hangar's collision); thermal, x-ray and scan visors; the scan of "POI_Samus Ship"
completed with its text; 31 game sound effects played with none missing; the intro music (stereo, 32 kHz) and voice streamed from the
disc. Pictures in `DevTools/lab/play_*.png`.

1.1: the gun's effects load from the disc (28 textures, 9 models); the muzzle flash shows at the barrel for 1/16 s after a shot
(`play_gun_muzzle.png`); the charge factor reads 0.69 at 1.25 s and 1.00 at 2.1 s as in the game, with the charge glow and aux muzzle at
the barrel (`play_charge_full.png`); the charged shot flies and lights the floor; every beam's shot drawn with its own systems; the
reticle report places the lock-on group at the view's centre with the tweak's colours and all 14 charge ticks when full
(`play_reticle_charged.png`).

1.1 doors (`DevTools/lab.sh doors`): one power shot each breaks the hangar door lock (the door opens), the four green switches
DamageableTrigger1-4 (upper pair at 7 units up) and the Air Lock's far door lock (both doors of that doorway open and stay open).

## Player.log lines

* `Metroid Prime Dreamport Iteration 1.0 (2026-10-16) - ...` which upload is running.
* `Disc: <path> (GM8...)` and `Disc: N resources in M archives` the disc that was found.
* `World: area N '<name>' ready ...` / `World: entered area ...` / `World: sky ... built` the rooms streaming.
* `Samus: energy ..., missiles ..., beams ...` the start inventory; `Gun: ...` the cannon's models and weapon data.
* `Sound: 72 sound groups, ... effects, lookup ...` the sound banks; `Music: playing ...` a music track.
* `Visor: ...`, `Scan: '<name>' done` gameplay events.

## Developer notes

* Code: `Data/` pure C# readers (disc, PAK, TXTR, CMDL, MREA, MLVL, SCLY, collision, ANCS, FRME, FONT, tweaks, WPSC, MusyX AGSC / DSP),
  shared with the `DevTools/probe` console tool; `Game/` the Unity side (world, script entities, player, gun, HUD, audio); `Shaders/` the
  GX TEV interpreter.
* Checks: `bash DevTools/metas.sh`, `bash DevTools/build/build.sh`, `bash DevTools/fusionapi/check.sh DevTools/build/out/server_like.dll`,
  labs `bash DevTools/lab.sh view|play` (pictures in `DevTools/lab`; when the project is open in Unity the lab runs in a private copy,
  `DevTools/labproject`).
