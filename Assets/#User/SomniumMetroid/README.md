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
| 1.8.2 | 2026-11-11 | **The character moves, not the world** (the user: "If I go into morph ball, and they do, it goes all over the place. Please make the CHARACTER move, not the world"): the third-person morph ball now holds the player's avatar the WoW Dreamport's way (MpAvatarHold: client collision / fly / teleport / speed / gravity / jump off, falling animation and VRIK legs off, CharacterController off) and carries it each frame so its eyes are at the ball camera; unmorphing gives it back standing where the ball is (the client's own teleport). Elevators carry the avatar by the platform's motion (Somnium's own elevator way: the body moved directly + Physics.SyncTransforms). The world stays at MpSpace.Home for everyone. Lab `ballhold`: world moved 0.000 m, eyes on the ball camera 0.000 m, released 0.05 m from the ball. First-person cutscenes still turn / slide the world locally while they run (undone at the end). |
| 1.8.1 | 2026-11-11 | **One world for everyone in co-op:** rides and the third-person morph ball move a player's own world under their avatar, and the client places every avatar in the same space for everyone - so after a roll (or a ride alone) the other players stood out of place ("the remote players are in a different offset to me"). Now, once nothing is moving it, the world goes back to its shared place (MpSpace.Home) and the avatar is teleported by the same amount, keeping Samus's place in the game (lab `settle`: home 0.000 m, Samus 0.03 units, one settle). During a roll or a ride the others still look offset to the one rolling / riding. |
| 1.8 | 2026-11-10 | **Co-op, the way the live Annihilator world does it:** the Community Modules 4.0 SceneNetworking is now in this world's own code (Game/Net/SceneNetworking.cs, as Annihilator keeps its copy in its own assembly), added at start when no other one runs. It registers the scene's network objects from Fusion's own "local player joined" callback (with a catch-up if the world started after the join), hands orphaned objects to the master and follows master changes; 1.7's home-made polling registration is gone (live it ran too early and took the client's own objects too). Proven over a real Fusion runner with the Community Modules removed (lab `fusionsolo`: Local Player Joined, 18 of 18 registered, online, slot claimed). The build's Span rule now accepts that one Fusion 2.1 callback (ReadOnlySpan, exactly as Annihilator). |
| 1.7 | 2026-11-10 | **Co-op, live:** an uploaded world has no Community Modules (the upload server skips them and the client does not run them), so their SceneNetworking - which registers the scene's network objects with the client's runner - never ran: the world record and the player slots never came alive and nobody could join anybody (Player.log: "no scene NetworkObjects will be spawned", "Net: no free player slot"). MpNet now registers this world's own network objects itself (once in the session, only this scene's, sorted by SortKey) and the master claims any left without an owner; our code no longer names the Community Modules at all. Proven over a real Fusion runner with the Community Modules removed (lab `fusionsolo`: 18 of 18 registered, a slot claimed). **Skip the intro** is on by default (a new game starts on the landing deck). |
| 1.6 | 2026-10-21 | **The other players' arm cannons on their own avatars** (the separate little Samus figures are gone): a full copy of the cannon in their right hand (VR) or beside their head (monitor), posed as they hold it, running the cannon's own animations and effects from their replicated inputs - beam choice and the beam change, every shot with its muzzle flash and projectiles (the wave beam's three), charge-up, charge loop, charge effects and the charged shot, missiles with the hatch and reload, the scan holster - with the gun sounds at their cannon; their right hand hidden inside it in VR (forearm too in forearm mode); their morph ball (their avatar hidden in third person) and bombs placed through their own world's origin, under their avatar. **Also in 1.6:** **Network upgrade to Photon Fusion 2.1.3 + Community Modules 4.0** (Somnium moved to 2.1; 2.0 worlds stop syncing), done with the official Somnium Fusion 2.1 Migration Tool (unattended); our own SceneNetworking copy is retired: the scene's "Scene Networking" object now runs the official Community Modules 4.0 SceneNetworking (CommunityModules assembly), so the Dreamport's own code references no Span type (the upload server's rule) and the build is clean; the 18 scene NetworkObjects re-baked with V2 flags; build checks now compile against this project's own Fusion 2.1 libraries. Backups in `MigrationBackups/`. **Skip the intro** (tablet, Sound  | play): a new game starts past the frigate's opening cutscene; the Reset button moved to the Game tab. **Live co-op fix:** an uploaded world does not compile the Community Modules, so their SceneNetworking never ran and the scene's network objects (world record, player slots) were never registered - nobody could join (Net: no free player slot); MpNet now registers them itself (RegisterSceneObjects, the master claims orphans). Our code no longer names the Community Modules (the server skips them). Skip the intro is on by default: a new game starts on the landing deck. |
| 1.5 | 2026-10-20 | **Co-op, the Skyrim Dreamport's way** (see *Co-op* below): the instance master runs the game - script world, enemies, bosses, escape timer, world, save - and everyone else in the instance joins it, standing beside the master with the master's unlocks (beams, visors, morph ball, bombs, suits, tanks, missiles). Doors, switches, relays, memory relays, platforms, elevators, pickups, cutscenes (started and skipped), layers, the escape timer and the world switches to Tallon IV / the Chozo Ruins follow the master; enemies are the master's (where, animation, health, deaths and gibs) and hunt the nearest Samus. **Enemy and boss health +25 % for each player beyond the first**, again when someone joins or leaves. Each player is their own Samus (cannon, beam, shots, damage, morph ball, bombs, first-person cutscenes); other players are drawn as Samus in their suit and their shots and bombs are seen. Helpers never save. The master leaving hands the game to the next master. Fusion and the Community Modules networking (the Skyrim copy) are now in the project. |
| 1.4 | 2026-10-19 | **Samus speed & jump** (tablet, on by default): your avatar walks at Samus's 16.5 units/s (10.9 m/s at our scale), falls at her 35 units/s² and jumps to her 3.8 units (2.5 m), through the client's own speed / jump / gravity multipliers. Somnium never says its own speeds, so they are measured as you move (full stick or WASD on the ground; each jump) and kept for next time. **Wrist mode hides only your hand** (your forearm stays visible behind the cannon); forearm mode hides both. **Past the frigate:** the escape ending plays (the hatches behind the escape elevator open, Samus runs to her gunship on the cinema screen) and the game moves to **Tallon IV**: the Landing Site with the upgrades lost, the rooms to the elevator, the **Chozo Ruins** and the first boss, the **Hive Totem** (its War Wasps, the target in its mouth, the Missile Launcher). New enemies: Beetles, War Wasps, Eyeballs, Fire Fleas, Geemers, animals. Saves remember their world. Not yet: water surfaces, fog, snake weeds, fish swarms. **Also:** the frigate's freight-clamp energy field drops (plain script actors now take Increment / Decrement, and switches answer Closed), so the Reactor Core Entrance door opens after the clamp; Samus's footsteps by floor material, jump and landing sounds; the morph ball camera follows the game's CBallCamera (trails the rolling, 5 to 6 units back, 2.7 up, spring-smoothed; turning your head no longer swings the world) and the ball and your hidden avatar can no longer fall out of the world; the cannon's idle fidgets are off. **Beams:** the wave beam fires its three braided shots (CWaveBeam, 120 degrees apart); each beam's own charge effects (Power2nd_1, Ice2nd_1 frost with its Ice2nd_2 release, Plasma2nd_1, the wave's electric arcs Wave2nd_1 / Wave2nd_2 as ELSC) and the ice beam's muzzle smoke; the dark core that closed the cannon's back now only sits in the power beam (it showed through the other beams' open plates). **Cutscenes:** you are the camera (tablet, on by default): the world moves so each shot's camera is at your eyes, you look around freely, your avatar, cannon and HUD are put away; and the instance master's avatar (yours when alone; the longest-present player otherwise) plays Samus in them, posed live. **Fix:** the Reactor Core's exit barrier drops after the Parasite Queen dies (platforms take Increment / Decrement too); the tablet's Reset button no longer covers the last options. The avatar playing Samus now moves with her cutscene animation (her skeleton's hips, spine, neck, head, arms and legs aimed onto the avatar's humanoid bones each frame, the Skyrim / WoW GearPoser way: posed, baked, put back), and the view turns you to face every new shot. First-person cutscenes are the game's exact shots: the world is turned (MpSpace.Turn) so each shot's view, pitch and roll included, is straight ahead of where you faced, and its camera follows the game's path through terrain as on the TV; and the avatar playing Samus stands on her actual feet (landings, jumps). |
| 1.3 | 2026-10-18 | **Morph ball bombs** (fire while morphed: three at a time, the game's fuse, refill, damage, radius and bomb jump; triggers that detect bombs). **The morph ball in third person** (tablet, on by default): your avatar is hidden (WoW polymorph way) and the world slides under it so your own eyes are the ball camera; off = the cinema screen. **The avatar's right hand and forearm hidden inside the cannon** in VR (wrist or the new **Cannon on forearm** mode: the cannon follows your forearm). Fixes: every compressed animation lost its last key pose (doors only opened part way); the save station sent "not saved" and its second cutscene held the controls; debris blockages break to a charged shot. |
| 1.2 | 2026-10-17 | The frigate playable end to end. **Animated arm cannon**: skeletal animation from the disc (CINF / CSKR / ANIM / EVNT, the game's pose builder and skinning), each beam's own shape and material set, the game's shoot, charge-up / loop / charged shot, missile hatch (open, shoot, reload, close after 7 s or on fire), beam change (from-beam, the hologram, to-beam) and fidgets, charge shake, controller ticks; Samus's suit forearm at the cannon's elbow; the open shells closed from behind (both faces + a dark core). **The METROID PRIME tablet app** (HUD distance / size / height / opacity / helmet, cannon fit, volumes, haptics, damage, testing unlocks, save, load, respawn, room warp, controls). **The frigate**: enemies (Parasites, wall swarms, Space Pirates of all four frigate kinds, gun turrets) and the Parasite Queen with the reactor rings and boss bar; cutscenes on a cinema screen in front of the player on their real timing (skippable); elevators (the world slides under the player); the morph ball; animated doors; effects; generators; pickups and drops; the save station, checkpoints and respawn; hazard and radial damage; the escape countdown; the scan text in the game's scan panel; script layers. |
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
| Morph Ball | X (left) on / off; left stick rolls it (seen from behind with your own eyes; or on the screen in front of you); trigger drops a bomb | Q on / off; WASD rolls it; left mouse drops a bomb |
| Skip a cutscene | both grips | Enter |
| Tablet app "METROID PRIME" | Y (left) - the Somnium tablet | Tab |

The directions are the game's own visor / beam menu layout (CHudVisorBeamMenu). A short tick on the controller marks each pick.

## Co-op (1.5)

Played together the Skyrim Dreamport's way, over the instance's own Photon Fusion session (Community Modules objects in the scene:
`Scene Networking`, `Mp Net World`, `Mp Net Events`, `Mp Net Player 0..15`; no spawns, no networked behaviours of our own; only
members the upload server already compiled for sibling worlds, `DevTools/fusionapi/check.sh`).

* **The master** is Fusion's shared-mode master (the first in the instance; alone, you). Its client runs **the** game: the script world,
  the enemies and bosses, the escape timer, the world it is in, the save. If the master leaves, Fusion makes someone else master and
  that player's game - kept in step until then - simply runs on from where it was.
* **Joining:** anyone else whitelisted who enters plays in the master's game. Their Dreamport asks for it, loads it like a save (the
  master's world, every room's script layers, the memory relays, the escape timer) and stands them next to the master. Each room they
  build, they get that room's changes from the master (doors open, locks broken, pickups taken, counters, generated enemies, moving
  platforms). They have **whatever the master has unlocked** - beams, visors, morph ball, bombs, suits, energy tanks, missile
  capacity, and the master's "testing unlocks" setting - and get every new unlock the moment the master's game gets it.
* **The script world is the master's:** every message its scripts deliver goes to the others, whose script worlds only react - doors,
  switches, relays and memory relays, platforms and elevators, cutscenes (they start for everyone), layer changes, the escape timer,
  the frigate's end and the elevators to and from Tallon IV / the Chozo Ruins. The master's triggers count every Samus (a helper
  walking into a trigger fires it; morph-ball triggers know who is morphed); the rooms every player stands in stay loaded on the
  master. A helper's skip asks the master, who skips the cutscene for everyone.
* **Enemies and bosses** run on the master: they hunt the nearest Samus and what they do to a player reaches that player; the others
  see them where the master has them, with the master's animation and health, and every death (normal, massive, frozen) and its gibs.
  Their shots fly on every client and hurt that client's Samus. **Health: +25 % for each player beyond the first** (max and current;
  again whenever someone joins or leaves; the boss bar follows).
* **Each player is their own Samus:** own cannon, beam, visor, charge, missiles, energy, morph ball and bombs, own first-person
  cutscene view (the world slides and turns on your client only). **Other players are their own Somnium avatars, with their arm
  cannon on them (1.6):** a full copy of the cannon (the same beam models, materials, suit forearm and scale) in their right hand in
  VR, or at the gun place beside their head on a monitor, posed as they hold it relative to that hand or head - so it stays on their
  avatar however each client's world is slid. It runs the cannon's own state machine from their replicated inputs: their beam and
  the beam-change animation and hologram, every fire press (the shoot animation, muzzle flash and the shot from its muzzle - the wave
  beam's three), the fire hold (charge-up and loop animations, charge glow, each beam's secondary effects, the charged shot on
  release), missiles (hatch, shot, reload), the scan visor's holster; the gun sounds play at their cannon. In VR their avatar's right
  hand (and forearm in forearm mode) is hidden inside it, as yours is. Morphed in third person their avatar is hidden and their
  morph ball rolls where their game has it, placed through their own world's origin so it is under their avatar as they see it; their
  bombs likewise. Their shots are only seen here; their hits count through the master (a helper's shot or bomb hit is sent to the
  master, who applies it). Pickups: the unlock is the game's (everyone), the refill is the
  taker's.
* **Saves:** the master's save is the game; **helpers never save** (save stations, the tablet's Save / Load and the room checkpoints
  are the master's). Dying while others play: Samus comes back beside one of them with full energy (nothing is reloaded). The master
  loading a save takes everyone into it. After a hand-over the new master's own save slot is the one written from then on.

**Not synced yet / known limits:** remote players' Somnium avatars stand where the client puts them, and each client slides its own
Metroid world (platform rides, the morph ball camera, cutscenes): their avatar - and so their cannon, shots, ball and bombs, all
anchored to it - can stand off the floor of YOUR world by the difference of the two slides (after an elevator ride one of you took
alone); what the game does with them (triggers, enemies) follows their real game position. Their cannon is hidden while they are in
a cutscene. Their missile count and charge-beam ownership are not sent (their copy always has both). Parasite wall swarms run on each client by itself (kills not shared). Effects an enemy makes from its behaviour rather
than its animation are missing on helpers (the Parasite Queen's and the Eyeballs' beams are not drawn for them; the damage still
comes). The Hive Totem is fought by hits, not health: it is not made harder. Scans are each player's own. No player-versus-player
damage. Tested with two Unity clients over a local socket (`DevTools/coop.sh`), not yet in the real Somnium client.

**Lab (2026-10-20, `DevTools/coop.sh`, 26 of 26 checks):** two Unity clients over a local socket - the helper joined the master's frigate
next to it; both counted 2 players and enemy health x1.25 (a Chozo beetle 10 -> 12.5); the master's shot was drawn on the helper, an
enemy hit routed to the helper reached it, the master's new unlocks (Space Jump, Varia) became the helper's; 264 script deliveries and
206 entity states mirrored; a door lock broken by the master went on the helper too (same doors open on both); the helper's hit on a
lock and on a beetle went to the master, which broke / killed them, the beetle puppeted on the helper at the master's exact place and
died there; the helper followed the master to the Chozo Ruins and to Tallon IV; when the master quit, the helper became the master and its script world ran on. Not shown by
the lab: a door actually opening (the hangar locks also need the player near), pickups taken by a helper, cutscene skips, the real
Fusion session.

**Lab (2026-10-21, `DevTools/coop.sh 47617 coopcannon`, 14 of 14 checks):** the master's head pose stood the SDK's RPM template avatar
up on the helper (in the client Somnium shows the real avatar); the master worked its own cannon and the helper's copy, on that
avatar, followed: idle 0.35 m from its head (`DevTools/lab/coopcannon_idle.png`), a full charge with the charge glow
(`coopcannon_charging.png`) and the charged shot (`coopcannon_charged_shot.png`), each beam's change and shape and a shot of each
(`coopcannon_change_*.png`, `coopcannon_beam_*.png`, `coopcannon_fired_*.png`: the wave beam's braided shot from the purple wave
cannon), a missile with the hatch open and its reload (`coopcannon_missile*.png`), and the cannon held in the hand as in VR - in the
stand-in's right hand to the millimetre, the hand hidden inside (596 vertices; `coopcannon_hand*.png`). Not shown by the lab: a
remote morph ball and its bombs, forearm mode, the real Somnium avatar's hand transforms.

## Not in yet / known problems

* **Never run in the real client yet** (VR or monitor): everything below the lab line is tested in Unity batch labs only.
* Cutscenes play on a screen in front of you (your view is never moved); the game's camera-blur / filter / flash effects are not drawn.
* The morph ball has bombs (bomb jumps too) but no spider / boost ball or power bombs yet. In third person your avatar stays standing,
  hidden, and the world slides under it; when you unmorph the world puts the ball's place under your feet (nobody is teleported).
* Not in: the space jump's second jump (the Somnium jump is the only jump), the grapple beam, the map screen (the map station only says
  "map data downloaded"), debris pieces, water and visor-goo effects, camera shakes on the view (they tick the controllers instead), the
  gun's sway on a monitor (GunMotion), additive enemy animations, worlds after the frigate (the frigate's end saves and shows a message).
* Not in the gun effects yet: electric effects (ELSC), the x-ray visor's subtractive muzzle drawing, the combo (missile + charge) beams.
* The monitor reticle is the game's lock-on reticle standing on what the view's centre is on (lock-on is not in yet).
* The thermal and x-ray views are per-pixel approximations with the game's palettes (the game uses frame-buffer passes and noise).
* Death: you come back at the last room entered (a checkpoint) with full energy; dying in the escape returns you to the save station. In co-op you come back beside another player instead.

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
* `Co-op: ...` and `Net: ...` the co-op: master or helper, players, the enemy health scale, joins, room censuses, hand-overs.

## Developer notes

* Code: `Data/` pure C# readers (disc, PAK, TXTR, CMDL, MREA, MLVL, SCLY, collision, ANCS, FRME, FONT, tweaks, WPSC, MusyX AGSC / DSP),
  shared with the `DevTools/probe` console tool; `Game/` the Unity side (world, script entities, player, gun, HUD, audio); `Shaders/` the
  GX TEV interpreter.
* Checks: `bash DevTools/metas.sh`, `bash DevTools/build/build.sh`, `bash DevTools/fusionapi/check.sh DevTools/build/out/server_like.dll`,
  labs `bash DevTools/lab.sh view|play` (pictures in `DevTools/lab`; when the project is open in Unity the lab runs in a private copy,
  `DevTools/labproject`).
* Co-op (1.5): `Game/Net` - `MpNet` (Fusion, the Skyrim SkyNet rules), `MpNetWire` (records, messages, the slot claim), `MpGame.Coop`
  (the game over it), `MpRemoteCannons` (the other players' arm cannons, hands, morph balls), the official `CommunityModules.SceneNetworking` (Community Modules 4.0) on the scene's "Scene Networking" object. Scene objects
  `Mp Net *` (hand-written YAML, fileIDs 4400000xxx). The two-client lab `bash DevTools/coop.sh` runs the master in this project and the
  helper in `DevTools/labproject`, linked by `Editor/MpLabLink.cs` - it needs the project closed in Unity (it refuses otherwise).
  `MpScripts.Trace` also traces the master's mirrored deliveries on a helper.
