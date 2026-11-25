# Somnium Space world project — context for the coding agent

*Version 4.0 — 2026-10-20*

This file is for an AI coding agent (Claude Code or similar) working in a **Somnium Space world
project**: a Unity project with the Somnium Space SDK, used to build a world and write its C#
scripts. It ships with Community Modules in `Agents/`; the creator copies it to the root of the Unity
project (next to `Assets/`, see `Agents/README.md`), named after the file the agent reads automatically:
`CLAUDE.md` for Claude Code, `AGENTS.md` for most others. A `CLAUDE.md` / `AGENTS.md` that starts with this
title is this file. "You" means the agent working for the creator (the user). Keep section 12 ("This world")
up to date.

**Migrating an older world** (Photon Fusion 2.0, Community Modules before 4.0)? Follow
`Fusion_2.1_Migration_Guide.md`, which ships inside the migration tool and on the wiki's download page
(section 2). The migration tool can do the whole migration without the user.

**Written for:** Somnium Space SDK 3.1.9, Unity 6000.3.5f2, Photon Fusion 2.1.3 and Community Modules 4.0,
as of the date above. These are the versions this file was checked against, **not requirements**. Newer
versions are normal: if the project has different ones, mention it to the user once, expect some details here
to have changed, and carry on. Never ask the user to downgrade; the SDK warns by itself when a version does not
fit. If something here disagrees with what you observe in the project, trust the project and tell the user.

**Keep an open mind.** The SDK, the Somnium client and Community Modules keep evolving; parts of
this file may be out of date, simplified, or wrong in a case nobody tested. Treat it as a strong
head start, not as the law: when code, a log or a test says otherwise, believe the evidence. Never
refuse or abandon an idea only because this file calls it unusual or unconfirmed — say what is
uncertain, propose a small test, and let the user decide.

**Rules vs recommendations.** What the server or client refuses is stated as a rule. Everything
marked *recommended* raises the chance of success and is good organisation, but a creator who
knows what they are doing can go another way.

---

## 0. Read this first

### Core rules
1. Every world must work in **VR and PC mode**, and still be walkable when a player **declines
   scripts** (section 4).
2. All world code lives in **one asmdef** with a **unique assembly name and a unique namespace**,
   referencing only allowed assemblies (section 3).
3. Networking goes **only** through the existing network components — never your own
   `NetworkBehaviour`, `[Networked]`, `[Rpc]` or Photon connection. Frequent data goes in synced
   values, rare events in messages; stay under ~4–5 MB / ~500 networked objects (section 7).
4. Never assume the network, the local player or the camera exist in `Awake`/`Start` (section 5);
   every `SomniumBridge.*` property can be `null` (always in the editor) — null-check (section 6).
5. Input System only. No file, process, registry, hardware or sensor access, no reflection, no
   `DllImport` (section 3).
6. Never change SDK-enforced project settings, and do not change graphics, physics or other Unity
   settings in game — if you must, restore them when the player leaves (sections 2 and 5).
7. With Community Modules: new scripts go in your own folder inside its `Scripts` folder; **move
   scripts, never duplicate them** (always with their `.meta`); never modify `SceneNetworking`,
   `NetworkSpawn` or the network stubs (the namespace line the Namespace Helper sets excepted).
8. Null-check Unity objects with `== null`, avoid per-frame allocations, log with a `[ClassName]`
   prefix (section 3).
9. When unsure whether something works, say so and propose a small test (whether an API is
   *allowed*: section 3). Record what you learn in section 12.
10. **Consent is the key**: never read, record or transmit a player's voice or audio without their
    explicit consent (section 9).
11. Client internals (hierarchy, names, layers) can change in any update — prefer the Bridge, and keep any
    lookup into the client in one place (section 8).
12. **No abuse.** Using world code to invade a player's privacy without consent, to track or
    fingerprint them, to deceive them (fake login, payment or system screens), to harm them
    (seizure-risk flashing, harmful audio levels, trapping), or to misuse Somnium features, other
    players' clients or the platform is forbidden.

### Who you work with
- A **creator** is a Somnium Space player who builds worlds, avatars or items — not a Somnium
  employee, and often not a programmer. Some have never written C#, some are experienced developers;
  adapt to how they write.
- Most failures are **inspector setup**, not code: say which GameObject, component, field and layer.
- A beginner may not be able to fix a compile error: make code compile on the first try (section 10).

### What you can do yourself, and what needs the user
- **You can**: write and edit scripts, asmdef files and input action files (JSON); read the logs —
  Unity editor log `%LOCALAPPDATA%\Unity\Editor\Editor.log` (compile errors), the server compile log
  `<project>/Scripting/logs.txt`, the client log `Player.log` (section 5); compile-check with
  `dotnet build` (section 10); run the Namespace Helper from code or batch mode (section 3).
- **The user must**: work in the Unity editor UI (log in, inspector wiring, previews, Build & Upload — the agent only when the user explicitly asks, section 10) and test in the
  Somnium client (VR, PC, two players). Tell them exactly what to click or set, and which log lines to
  send back.
- Avoid hand-editing `.unity`, `.prefab` and `.asset` files (YAML with `fileID`/`guid` links): prefer
  code plus precise inspector instructions. Never invent GUIDs or write `.meta` files by hand.
- **Ground truth you can read in the project**: the Bridge API with doc comments in
  `SomniumSpace.Bridge.xml` (next to `SomniumSpace.Bridge.dll`, normally
  `Assets/Plugins/SomniumSpace/Bridge/`), the network stubs' source (section 7), and the Community
  Modules scripts (section 2).

### Terms
- **PC mode** = desktop (keyboard and mouse, or gamepad); API names call it **NonXR**. **VR** = XR.
- **Local player** = the player on this client.
- **State authority** = the one client that owns and writes a networked object (`HasStateAuthority`);
  it can change. **Proxy** = any other client for that object.
- **Master client** = one client in the room with extra duties (`SomniumBridge.Network.IsHost`,
  `SceneNetworking.IsMasterClient`); it changes when that player leaves.

---

## 1. The platform at a glance

- **Somnium Space** is a social VR world. Players join in **VR** (PCVR through OpenXR) or **PC
  mode** (keyboard and mouse, or a gamepad).
- A **world** is one Unity scene, built into an asset bundle by the SDK and uploaded to Somnium.
- **World scripts** are your own C# `MonoBehaviour`s, uploaded **as source code**, compiled by
  Somnium's servers, and loaded by the client with the world.
- Your code runs **inside the Somnium client**: the client owns the camera, the player, the
  network session, the quality settings and the lifetime of your objects.
- Your code reaches the platform through the **Somnium Bridge** (`SomniumSpace.Bridge`, section 6)
  and shares state between players through the **network components** (section 7).

| Item | Value |
|---|---|
| Unity | 6000.3 (Unity 6.3) at the time of writing — the SDK says which version it supports |
| Render pipeline | URP, Forward |
| XR | OpenXR, XR Interaction Toolkit (XRI) |
| Networking | Photon Fusion **2.1.3**, shared mode (per-object authority, no dedicated server) — section 7 lists what changed from 2.0 |
| Target | Windows 64-bit (`StandaloneWindows64`) |
| C# | compiled with the project's `LangVersion` — **9.0** in Unity 6: **no C# 10+ features** (no file-scoped namespaces, no `global using`, no record structs) |

---

## 2. Project setup

### Start from Community Modules (strongly recommended)
- **Community Modules** (4.0 and later) contains ready scripts and example scenes to start from, the
  **correct Photon Fusion version** (2.1.3), the **network components** for networking, the **Namespace
  Helper**, and assets — all in `Assets/#User/Community Modules/`.
- **New project → Pro SDK**: a normal SDK project with Community Modules already installed (plus
  Photon and pre-set settings). For an existing SDK project, import the Community Modules package
  (variants with or without Photon).
- Community Modules is **made by and for the community**; only its network component stubs are
  official Somnium code. It moves faster than the SDK — check its version when something does not
  match this file.
- Docs and downloads: the community wiki, <https://incrediworlds.gitbook.io/somnium-creators-wiki>
  (useful, not guaranteed 100 % accurate — when it disagrees with the code, trust the code).
- Official SDK docs: <https://somniumspace.com/documentation/somnium-sdk/download-install> and
  <https://www.somniumspace.com/documentation/worlds/c-scripting>.
- Help: the official Somnium Space Discord (tag `#Denevraut` for Community Modules questions).

### Organisation (recommended)
- **Put your own scripts in your own folder inside Community Modules' script folder**, e.g.
  `Assets/#User/Community Modules/Scripts/MyScripts/`. They share the world's single asmdef
  (section 3), everything stays in one place, and updating Community Modules does not touch them.
- **Never duplicate a script — move it, with its `.meta`.** A copy declares the same class twice and
  scene objects keep pointing at the old one; a script moved without its `.meta` loses all its
  serialized references. Scenes, prefabs and materials you want to customise can be copied; do not
  edit the package's own ones in place.
- **One Unity project per world**, or one code base for several worlds — then each world needs its
  own **assembly name and namespace** (section 3): rename the world asmdef and switch the namespace
  of all scripts with the **Namespace Helper** when you change world.
- Back up before updating a package. After an update or a migration, look for **missing scripts**
  (a component whose `m_Script` GUID matches no `.meta` in the project): the build refuses them in a scene
  (section 4). An unmodified Community Modules file that the new version no longer ships, and that nothing
  references, is safe to remove. For anything else (the creator's objects, modified files), ask the user first.
- After an update or a migration, if the Somnium SDK offers its own update when the project opens, the user
  should accept it: the build refuses an outdated SDK.

### Unity version and project settings
- **Run Unity with the project's own version**, the one in `ProjectSettings/ProjectVersion.txt` (Unity Hub
  installs editors under `<Hub>/Editor/<version>/Editor/Unity.exe`), so you never upgrade the project by
  accident: opening it with another version, even a newer patch, upgrades it. Moving to a newer Unity is the
  user's choice and is fine; the SDK tells which versions it supports and warns when one does not fit (then
  its `WorldSDKConfig` inspector stays empty).
- The SDK **enforces** project settings on editor start, on play mode and before every build: URP
  assets and renderer features, quality levels, graphics stripping, player settings, XR settings,
  **physics layers, the collision matrix and XRI interaction layers**. They must match the client,
  because uploaded content bakes them in.
- **Do not change Unity settings (recommended)** — everything is tuned to the client, and enforced
  ones are reverted anyway. The physics collision matrix never ships at all: the **client's**
  matrix governs your world. For custom collisions call `Physics.IgnoreLayerCollision` /
  `Physics.IgnoreCollision` from your script and undo it in `OnDestroy`.
- You may **add** names to unused layers and quality levels after index 1 (read section 8 before
  using an unnamed layer). Quality level 0 `Default` is APV, level 1 `Legacy` is classic light
  probes — the **client** picks which one runs.

### SDK menus
| Menu | Use |
|---|---|
| `Somnium SDK/Welcome Window` | first-run info |
| `Somnium SDK/Somnium Window` | **login**, your uploaded content |
| `Somnium SDK/Check Update Window` | SDK updates — the build refuses an outdated SDK |
| `Somnium SDK/Packages Manager` | optional Somnium packages |
| `Somnium SDK/World Testing Window` | toggle the local test player for play mode |
| `Somnium SDK/Check And Fix Settings` | re-apply the SDK's settings (temporary item, may be removed) |

### Reference material: examples and Community Modules scripts
When you need to see how something is done in Somnium, read these first — they are tested in the
client. They are **references, not templates**: reuse what fits, do not force their style.
**Exception: `SceneNetworking` and `NetworkSpawn` are highly recommended — use them as they are and
never modify them** (apart from the namespace line the Namespace Helper sets; see section 7).

| Where | What to look at |
|---|---|
| `Community Modules/Scripts/Networking Plugin/` | `SceneNetworking`, `NetworkSpawn`, `NetworkEvent*`, `NetworkGrabbable`, `NetValue`, `BytesWriter/Reader`, `_Examples/NetworkedScriptExample` |
| `…/Interactions Plugin/` | `Interact` (XRI grab/click/hover → UnityEvents), `PushButton`, `LocalPlayerTrigger`, `ObjectTrigger`/`ObjectCollision` (filter by name/tag), `PlayerAnchor`, `PlayerPlatform` (carry the player), `HoverHighlight`, `UI/CanvasAssignCamera`, `UI/TogglesSelector` |
| `…/General Plugin/` | `WaitForCamera`, `LookAtCamera`, `DistanceEvent`, `AnimatorParameterSetter`, `UnitySettings` (the sanctioned way to change light count / LOD fade, restored on exit) |
| `…/Somnium Examples/SomniumFeaturesExample` | every `LocalPlayer.Features.*` call in one demo |
| `…/ARS Plugin/` | audio-reactive lights/materials/animation, video player audio and texture access |
| `…/SPS Plugin/`, `…/ThadMirror Plugin/` | avatar SPS, planar mirror |
| `…/Boats Plugin/` (when included) | physics vehicle with Input System |

Some SDK versions also ship official examples — their location and presence depend on the SDK version and
what is installed, so search the project for them rather than assuming a path: a full example world
(`Somnium_WorldLounge.unity`), Bridge examples (asmdef `SomniumSpace.Bridge.Examples`: player list, teleport,
attach to hand, voice, environment, network status) and interaction examples (asmdef
`SomniumSpace.WorldInteractions.Examples`, scene `SomniumSpaceWorldInteractionsExamples.unity`: combination lock,
reaction game, whack-a-mole).

**Community Modules demo scenes** (`Community Modules/Scenes/`) — the quickest way to see what
exists. Each object is a prefab from `Community Modules/Prefabs/`, already set up:
- `Community Interactions.unity` — interactions and networking: `Scene Networking`; networked
  grabbables (`Networked Grabable Kinematic / NonKinematic / Throw / Hitter / Hittable Example`),
  `Networked Animator Example`, and in `Networking Components Examples/` synced bool, color, event,
  string, int and float sliders, object switcher, spawn, teleport object; `Grab Tests Near/Far`,
  `Push Cube`, `Somnium Player Teleport Simple`; `Modern UI Example` (UI kit for VR and PC);
  `Somnium Video Player`; `SPS Controller`.
- `Community Assets.unity` — effects: `Pseudo Fog`, `UnderWater Fog`, `Caustic Decal`, `Wind Shader
  Example`, particles and trails.
- `Community ARS.unity` — the audio-reactive system: `ARS Controller`, `ARS Controls`, channel
  examples (light, material, animator, particles, transforms), shader examples (sound bars,
  spectrograms, lasers, spotlights), `ARS Somnium Video Player`. Manual: `Documentation/ARS Documentation.pdf`.

---

## 3. Writing world scripts — the rules

### Account and scripting setup (user, in the editor)
- The user must be **logged in** (otherwise: "To use this settings you need to be authorized.") and
  the account **approved for creator features** (otherwise: "You need to be a verified user to use
  C# scripting", with a Discord link — ask Somnium there). The same approval unlocks the **Tip Jar**
  and larger world sizes.
- In `WorldSDKConfig` → *Advance Tab → C# scripting*: turn on **Enable C# scripting**, choose the
  world asmdef, then run the **assembly name availability check** — the build stays blocked ("You
  need to check if your Assembly name is available.") until it passes.
- Scripting accounts have different levels; the allowed API set is **applied on the server** and not
  published. **This file assumes the basic level** — code that follows it should compile for everyone.

### One asmdef
- All world scripts live under **one asmdef**. Scripts under any other asmdef are not part of the
  world and will most likely be ignored. With Community Modules, that single asmdef is the one in
  its `Scripts` folder (section 2).
- The scripting dropdown lists **every** asmdef under `Assets/#User` — choose the world asmdef,
  **never** `SomniumSpace.Network` (the network stub asmdef, section 7) or another plugin's asmdef.
  An asmdef elsewhere can be dragged in. *Create New Assembly* makes
  `Assets/#User/<Name>/<Name>.asmdef` with root namespace `SomniumSpace.Worlds.<Name>`.
- **The asmdef file name must equal its assembly name** (else "Can't find `<Name>.csproj` file"), and
  it must contain **at least one script**. To rename, change the file name and its `name` field
  together; other asmdefs that reference it by name must be updated (GUID references survive).
- Assembly name: 1–40 characters, `a–z A–Z 0–9 .`, starts with a letter, no trailing dot, and
  **unique across all uploaded Somnium worlds** (server check: "Your assembly name is already in use
  in some uploaded World") — so a Community Modules asmdef must be renamed for your world too.
  Choose e.g. `CreatorName.WorldName`; never reuse a template's name. An asmdef still called
  `CommunityModules` (the package default) has not been renamed yet: tell the user before the first upload.
- An **external script editor** (Visual Studio or Rider) must be set in Unity preferences — unset is
  a build error, because Unity only generates the `.csproj` the SDK reads when one is.

### Namespaces
- **Every public type must be in a namespace** equal to the asmdef's `rootNamespace` or starting
  with `rootNamespace.` (errors: "Namespace is missing" / "Namespace is incorrect").
- **This includes every script sharing your asmdef — Community Modules' too.** Before the first
  upload, give the world its own name with the **Namespace Helper**: it renames the asmdef (file,
  `name`, `rootNamespace`) and the namespace of every script in the asmdef's folder and sub-folders,
  keeping sub-namespaces (`Old.UI` → `New.UI`). Hundreds of namespace errors on a first upload mean
  this step was skipped. It only touches scripts under your world asmdef; the network stubs keep
  `SomniumSpace.Network.Bridge`.
  - The user: select the existing `NamespaceHelper` asset in Community Modules' `Scripts` folder (create one
    with *Assets > Create > - Community Modules > NamespaceHelper* only if there is none — two break it),
    then *Rename All* in its inspector. The new name is the creator's choice: ask them for it.
  - From code, e.g. `unity command eval` (section 10): `NamespaceHelperUtility.RenameAll("CreatorName.WorldName")`
    — uses the project's single `NamespaceHelper` asset to find the asmdef, or pass the asmdef path
    first. Returns `false` and logs `[NamespaceHelper] …` on failure (invalid name, not exactly one
    asmdef or helper asset). `NamespaceHelperUtility.GetStatus()` lists the scripts outside the root
    namespace without changing anything.
  - Editor closed: `Unity -batchmode -quit -projectPath "<project>" -executeMethod
    NamespaceHelperUtility.RenameFromCommandLine -namespace CreatorName.WorldName` (exit code 1 on failure;
    tested after a migration: every script renamed, then a normal open recompiles with 0 errors).
  - Copies of the changed files go to `#Scripts Backup` next to the asmdef. The `.meta` files are kept,
    so scenes keep their references. Scripts without any namespace are not changed: add one by hand.
  - The assembly name changes too: the user must run the assembly name availability check again.
- Root namespace: same characters as the assembly name, max 60 characters, **never empty** (existing
  asmdefs often have an empty one, which is an error).
- **The namespace must be unique per world, across all of Somnium Space** — two worlds with the same
  namespace clash in the client. Use one tree, e.g. `CreatorName.WorldName` with `.Gameplay`, `.UI`,
  `.Net` (the SDK default `SomniumSpace.Worlds.<Name>` is fine). Never declare types in Somnium's or
  libraries' own namespaces (`SomniumSpace.Bridge`, `SomniumSpace.Network`, `UnityEngine`, `Fusion`…).

### Allowed asmdef references
Anything else is reported as "Unsupported Scripting References Detected" — only a **warning** in the
SDK, but the server compile fails on it. The list is downloaded by the SDK **after** the name check
(no reference warning appears before that) and can change:

```
AVProVideo.Runtime  DotNetZip  DOTween
Fusion.Addons.Physics  Fusion.Common  Fusion.Log  Fusion.Realtime  Fusion.Runtime
Fusion.Sockets  Fusion.Unity  NanoSockets  Photon3Unity3D  PhotonVoice  PhotonVoice.Fusion
Newtonsoft.Json  RootMotion  SocketIOUnityAssembly  websocket-sharp
SomniumSpace.Bridge  SomniumSpace.Network
Unity.AI.Navigation  Unity.Burst  Unity.Collections  Unity.InputSystem  Unity.Mathematics
Unity.RenderPipelines.Core.Runtime  Unity.RenderPipelines.Universal.Runtime
Unity.TextMeshPro  Unity.XR.Interaction.Toolkit  UnityEngine.UI
```

This list was read before the Fusion 2.1 update and mirrors the server, so it can lag behind. With Fusion 2.1:
reference `Photon.Realtime` (Photon Realtime 5; the network stubs need it) instead of `PhotonRealtime`, and do
not reference `Fusion.Addons.Physics`, `PhotonVoice` or `PhotonVoice.Fusion` — Somnium worlds no longer use
them. If the server refuses a 2.1 assembly, ask Somnium.

Unity engine modules come in automatically. **No other DLLs** in your asmdef, and no loading of
assemblies at runtime (`Assembly.Load`, reflection-emit) — not allowed.

### What C# may do
Principle: **world code may build anything for the world's own experience — never act against
players, their PC, their privacy, or Somnium Space** (core rule 12).

**Forbidden:**
- **file access**, except the Bridge's local storage (section 6) — no `System.IO` files/directories;
- **running programs** (`System.Diagnostics.Process`, exes, shell commands), the **registry**;
- **hardware access** and **reading sensors** or devices directly;
- anything else that tries to **tamper with the PC**, directly or indirectly;
- **closing the player's game** (`Application.Quit`);
- **reflection** (`System.Reflection`, `Reflection.Emit`), **native calls** (`DllImport` / P/Invoke);
- **Unity's old Input Manager** (`UnityEngine.Input`) — use the Input System (section 9).
  Server exit code **8192** means the old Input Manager was used.
- **reading the player's system environment** (`System.Environment`: user and machine names,
  environment variables, command line) — privacy invasion;
- **identifying the player's PC** (`SystemInfo.deviceUniqueIdentifier`) — fingerprinting;
- **`unsafe` code**.

**Allowed, but untested in the client** (verify before relying on them):
- **Threads** (`Thread`, `Task.Run`) — only when really necessary, for heavy work that cannot be
  spread over frames. Use them cleanly: stop every thread you start when the world unloads
  (`OnDestroy`) — your assembly stays loaded after the player leaves, so a thread left running is
  orphaned. Never touch Unity objects from a background thread. Abuse is forbidden (hidden background
  work, using the player's CPU for anything but the world).
- `Interlocked`, `System.Diagnostics.Stopwatch`.

Main-thread alternatives always work: coroutines, `async`/`await` on the main thread,
`Time.realtimeSinceStartupAsDouble` / `Time.unscaledTime`. `System.Random` with a seed is fine.

**Allowed but affects the whole client — avoid** (section 5): `DontDestroyOnLoad`, `QualitySettings`,
`Time.timeScale`, global `Physics` settings, searching the client's own objects (`FindObjectOfType`,
`GameObject.Find` outside your world; if you must, section 8).

**Safe ground:** plain `MonoBehaviour` logic, math, collections, LINQ (not in per-frame code),
coroutines, Unity components (physics, animation, audio, particles, UI, TextMeshPro, XRI), DOTween,
Newtonsoft.Json, the Bridge, the network components.

**Outside connections are allowed** — for *your own* services, not for syncing players (that is
Fusion only, section 7):
- web requests (`UnityWebRequest`), including **asset bundles** (`UnityWebRequestAssetBundle`),
  textures and audio;
- WebSockets and sockets (`websocket-sharp`, `SocketIOUnityAssembly`, `NanoSockets`).

Never block the main thread, always set a timeout and handle failure (the world must work when your
server is down), never put secrets or API keys in world code (players can read it), never send
players' personal data anywhere. Downloaded asset bundles must be built with the same Unity version
and render pipeline settings as the SDK. Arbitrary internet content can crash the client or behave
unpredictably — load only content you control and trust.

**When unsure whether an API is allowed:** prefer an alternative known to work; otherwise ask the
Somnium Space team (official Discord). If the server refuses a script, `Scripting/logs.txt` says why
(section 4) — replace that code, never look for a way around the server's checks. Record answers in
section 12.

### Editor code
- **Normal way: wrap editor logic in `#if UNITY_EDITOR … #endif`.** The define is dropped on upload.
  The SDK may warn "Editor code detected" whenever the world assembly uses a `UnityEditor` API —
  harmless when the code is wrapped.
- Optional: bigger editor tooling can live in a separate asmdef whose only platform is "Editor".
- **Visual Scripting is deprecated** — the build fails on Visual Scripting components.

### Coding conventions
- Log with `Debug.Log/LogWarning/LogError` and a `[ClassName]` prefix (`SomniumBridgeDebug.Log /
  LogWarning / LogError / LogException` also exist).
- Null-check Unity objects with `== null` / `!= null`, **never** `is null` or `?.` (they skip Unity's
  destroyed-object check).
- No per-frame allocations (cache `GetComponent`, reuse buffers) — the world shares the frame with a
  VR client at 90 Hz. One responsibility per component; settings as `[SerializeField] private`.
- Subscribe to events in `Awake` / `OnEnable` and unsubscribe in `OnDestroy` / `OnDisable`.

---

## 4. Building, uploading and loading

### The world object (`WorldSDKConfig`)
Every world scene needs **exactly one** `WorldSDKConfig` (it adds `WorldConfig`). Its inspector,
top to bottom:
1. **Select World** (login): world token or free world, size, or "Replace Existing World".
2. **Upload Information**: name (required, max 40, letters/digits/spaces); description 50–300
   characters, at most 10 lines.
3. **Preview Picture** (630×410, *Capture*) and **Preview 360 Picture** (*Capture*) — **both required**.
4. **Spawn Logic**: spawn position/direction, *Respawning Behavior* (`PlayerSpawnPosition` /
   `LastPlayerPosition` / `Disabled`), *Respawn Height Y* (default −100); spawn must be above it
   (not checked when `Disabled`).
5. **Advance Tab** → **Bridge** (install/update) and **C# scripting** (section 3).
6. **Tags**, then **Build & Upload**.

### Build checks
**Errors** (block the build): not logged in, outdated SDK or Bridge, `WorldSDKConfig` GameObject
disabled; no or empty active scene; a Camera tagged `MainCamera` (the client brings its own); zero
or several `WorldSDKConfig`; missing scripts; more than one Tip Jar or Tip Jar limits reached;
Terrain not on layer `Terrain`; no walkable surface (non-trigger collider on `Default`, `Terrain` or
`Environment`); missing name, description or preview, preview not 630×410, no 360 preview; spawn
Y ≤ respawn Y; size or quota over your token's limit; server maintenance; scripting errors (no
script editor, missing asmdef or `.csproj`, name not checked, namespace errors, Visual Scripting).

**Warnings:** not exactly one directional light; nothing to stand on between spawn and respawn
height; no XRI `TeleportationArea` (VR players cannot teleport there); objects below the respawn
height; textures without mipmaps.

- Only the **active scene** is built and **worlds have no component filtering**: whatever is in the
  scene when you press Build ships — reset debug/test state first.
- If your scripts create the ground at runtime, still save a **placeholder floor** (a plane with a
  non-trigger collider under the spawn) to pass "Missing surface"; hide it from script once your
  floor exists. It also keeps the world walkable when scripts are declined.

### From upload to player
1. Build & Upload reads the Unity-generated `<AssemblyName>.csproj`, collects every `.cs` file of
   your asmdef plus a manifest, and uploads them **as source** (`UNITY_EDITOR` defines dropped,
   editor/test references excluded).
2. The **server compiles** them. On failure the SDK shows `C# scripting build failed. Exit code: N`,
   saves the server log to `<project>/Scripting/logs.txt` and offers **Open Logs** — **read it
   first**. Every failure, compile error or tier violation alike, shows the same hint about
   "features not allowed for your user tier"; the log tells you which.
3. When a player opens a scripted world, the client shows a **scripting consent popup** listing the
   APIs the server flagged (possibly none): **Agree** loads scripts and world; **Decline** loads the
   world **without scripts** (your components behave like missing scripts); **Cancel** loads nothing.
   **Design for Decline**: keep floors, colliders, teleport areas and lighting script-free.
- **An assembly name loads only once per client session.** After a re-upload, **restart the client**
  before testing (an already-loaded assembly is kept; the log shows `[WorldScriptLoader] … failed`).
  Two worlds with the same assembly name cannot both run scripts in one session.

---

## 5. How the client runs your world

### Order of events
1. Your assembly and the world bundle are downloaded; the scene activates.
2. The client collects the Bridge components and spawns the local player.
3. **The network connects on a later frame.**
4. **The client's camera arrives late too**: `Camera.main` can be `null` in `Awake`/`Start`. Poll
   until it exists (a few times per second, then stop), like `WaitForCamera` / `CanvasAssignCamera`.
5. Always use `Camera.main`: every VR player's head anchor (remote players' too) is also tagged
   `MainCamera`, so `FindWithTag("MainCamera")` can return another player's head. The tag `PlayerCollider`
   exists on VR bodies only — do not rely on it.

So never assume a connection, a local player or a camera in `Awake`/`Start`: subscribe to
`SomniumBridge.Network.OnConnected` / `OnJoinedToGameSession`, `PlayersContainer.OnLocalPlayerAdded`,
`SceneNetworking.OnLocalPlayerJoined`, or a network component's `OnSpawned`.

### What the client does to your scene
- **Cameras**: every camera active at load has its **whole GameObject deactivated** (children and
  scripts included) — keep each camera on its own leaf object. Runtime cameras: see section 9.
- **Audio**: every `AudioSource` present at load (inactive ones too) is routed to the client's
  **ambient** mixer group and follows the player's ambient volume; sources created later keep their
  own routing.
- **DOTween**: all tweens are paused and killed on world enter — start yours after load.
- **Settings**: quality settings, `Time.fixedDeltaTime` and `Time.timeScale` are saved on enter and
  restored on exit. **Recommended: never change graphics, physics or other Unity settings in game**
  — it can break the experience, even after the player leaves. If you must, store the defaults and
  **restore them yourself** (`OnDisable` / `OnDestroy`).
- **`DontDestroyOnLoad`** objects are **destroyed when the player leaves** the world.
- **Reload**: on **Reconnect** or **instance switch** the scene unloads and loads again. Scene objects
  restart, but your assembly stays loaded: **static fields and DDOL objects survive** (a DDOL
  singleton made in `Awake` gets duplicated), DOTween is not cleaned and cameras are deactivated
  again. Make statics and singletons reload-safe; reset statics on load — they also keep their values
  between visits in one session.

### Logs (on the user's PC)
`%USERPROFILE%\AppData\LocalLow\Somnium Space Ltd\Somnium Space VR\Player.log` (previous session:
`Player-prev.log`). Your `Debug.Log` lines appear there; script load failures carry the prefix
`[WorldScriptLoader]`, and the world then loads without scripts.

---

## 6. The Bridge API (`SomniumSpace.Bridge`)

Entry point: class `SomniumBridge`, four **static** properties — **`null` until the client
initializes them, always `null` in SDK play mode**. Exact signatures: `SomniumSpace.Bridge.xml`
(section 0).

| Property | Type | Use |
|---|---|---|
| `SomniumBridge.PlayersContainer` | `ISomniumPlayersContainer` | players, join/leave |
| `SomniumBridge.Network` | `ISomniumNetwork` | connection state, host |
| `SomniumBridge.Environment` | `ISomniumEnvironment` | skybox, sun, ambient |
| `SomniumBridge.LocalStorage` | `ISomniumWorldsLocalStorage` | save data on the player's PC |

Scene components for inspector wiring: `SomniumPlayersContainer` (UnityEvents `LocalPlayerAdded`,
`PlayerAdded`, `PlayerRemoved` only, plus lookups — spelled `GetPlayerByNickname` here);
`SomniumNetwork` (`Connected`, `Disconnected`, `JoinedToGameSession`, `LeaveFromGameSession`);
`SomniumEnvironment` (no events; configure, then `ApplyEnvironment()`).

### Players
- `ISomniumPlayersContainer`: events (`Action<ISomniumPlayer>`) `OnLocalPlayerAdded`,
  `OnPlayerAdded`, `OnPlayerRemoved`, `OnPlayerAvatarChanged`, `OnPlayerOutfitPutOn`,
  `OnPlayerOutfitTakeOff`; `LocalPlayer`, `AllPlayers` (a copy), `PlayersCount`;
  `GetPlayerByID(string userId)`, `GetPlayerByPhotonID(int)`, `GetPlayerByNickName(string)` (first
  match — nicknames are not unique).
- `ISomniumPlayer.Properties`: `NickName`, `Id`, `PhotonId`, `IsLocal`. **`PhotonId` is the player's
  Fusion `PlayerRef.PlayerId`**: `GetPlayerByPhotonID(playerRef.PlayerId)` gives the Somnium player of
  a network player; for the reverse, match `PlayerId` in `SceneNetworking.NetworkRunnerRef.ActivePlayers`.
- `ISomniumPlayer.References.Body`: `Root`, `Head`, `Neck`, `LeftHand`, `RightHand` — the player's **rig
  (tracking) transforms, not avatar bones** (section 8, "How a player is built").
  - `Root` = the object with the body capsule (`CharacterController`). ⚠️ **Never use its position to
    find where the player is**: the transform is the **play-space origin**, which can be metres from where
    the player stands. Use the capsule's **`center`** (the real body center) —
    `cc.transform.TransformPoint(cc.center)` with `cc = Body.Root.GetComponent<CharacterController>()` —
    or `Body.Head` (its x/z for where the player stands). Same in PC mode, so one code path works everywhere.
  - `Head` = the tracked head (VR: the headset; PC: a point at head height). It can differ from the
    avatar's head bone (seated play, avatar proportions).
  - ⚠️ **In PC mode `LeftHand`, `RightHand` and `Neck` are `null`** — null-check and give PC players
    another anchor. `Body.LeftHand != null` is a simple **VR vs PC test** (local player:
    `UnityEngine.XR.XRSettings.enabled` too).
  - For avatar bones, use the avatar's humanoid `Animator` (section 8).
- `ISomniumPlayer.Features`:
  - **Motion** (local player): `DoTeleportToPoint(pos, eulerRot, done)`, `SetMovementSpeed`,
    `SetGlideSpeed`, `SetGravity`, `SetJump`, `SetTeleportation(distance, cooldown, rayGradient)`,
    `SetTeleportationDisableState`, `SetCollisionDisableState`, `SetFlyModeDisableState`,
    `SetGlideDisableState`. Speeds, gravity, jump and teleport distance/cooldown are **multipliers**
    (1 = default) clamped to 0–10 — except `SetGlideSpeed`, not clamped when called directly.
  - **Scale**: `SetHeight(nonXRHeight, XRHeight)`, 0.6–5 m — VR only for now (`nonXRHeight` ignored).
  - **Voice**: `EnableAmplify(distance)`, `DisableAmplify()`, `SetVoiceLevel(0..1)`.
  - **Avatar**: `OnAvatarChanged`, `SetVRIKLegMovementDisabled(bool)`. **Wearable**:
    `OnOutfitPutOn`, `OnOutfitTakeOff`. **Poses**: `SetPose(PoseType)` (sitting, kneeling, squat,
    prone, lying; `None` = −1). **Animation**: `SetFallingAnimationDisabled(bool)`. **Info**:
    `SetPlayerTagDisableState(bool)` hides the name tag.

### Network status — `ISomniumNetwork`
`OnConnected`, `OnDisconnected`, `OnJoinedToGameSession`, `OnLeaveFromGameSession`, `IsConnected`,
`Status` (`INITIALIZATION`, `DISCONNECTED`, `CONNECTING`, `CONNECTED`, `FAILED`, `ENTERING_LOBBY`,
`IN_LOBBY`), `IsHost` (the master client — `false` while offline or not yet connected).

### Environment — `ISomniumEnvironment`
`SetSkybox(Material)`, `SetSunSource(Light)`, `SetAmbientModeSkybox(intensity)`,
`SetAmbientModeGradient(sky, equator, ground)`, `SetAmbientModeColor(Color)`, `UpdateEnvironment()`.

### Local storage — `ISomniumWorldsLocalStorage`
- **The only way to save data**: small text / JSON files of a format you control.
- Text: `ReadText` (`""` when missing), `WriteText(name, text, usePlainText = false)`, `DeleteText`,
  `TextExists`, `GetAllTextNames`. JSON: `ReadJson<T>` (`new T()` when missing), `WriteJson`,
  `DeleteJson`, `JsonExists`, `GetAllJsonNames`. `GetUsedSpaceBytes()`, `ClearAllData()`. `Write*`
  returns `false` on failure — check it.
- Kept **per world** on the player's PC: *Replace Existing World* is expected to keep it; a new world
  starts empty. **Not durable, not secure** — settings and local progress only, never secrets.
- Version your data (a `version` field) and validate it on load; throttle writes (on important
  events, not every frame); simple file names (letters, digits, `_`, `-`, no paths). When
  `LocalStorage` is `null` (editor), keep state in memory and log once.

### No-code components
- **Triggers** — `SomniumTriggerAction`: Enter / Stay / Exit event lists (fields spelled
  `OnTrigerEnterEvents` / `OnTrigerStayEvents` / `OnTrigerExitEvents`), each entry with its own
  filters ("is player", "is local player", layer). Args: `Player`, `IsLocal`, `Layer`, `IsPlayer`,
  `IsName(string)`, `IsTag(string)`, `GetComponent<T>`, `GetComponentInChildren<T>`,
  `GetComponentInParent<T>`. Only listeners **wired in the inspector** fire (not `AddListener`). With
  "is player" ticked the layer filter is skipped; the layer filter is currently unreliable — filter by
  layer in code. `ISomniumPlayerTrigger.Player` is on the player's trigger collider (section 8).
- **Player actions** — derive from `SomniumPlayerAction`, override `DoAction(ISomniumPlayer)`; wire its
  `DoAction(SomniumTriggerActionArgs)` overload to a trigger: `SomniumPlayerTeleport`,
  `SomniumPlayerVoiceAmplifier`, `SomniumPlayerVoiceLevel`.
- **Player rules** — `SomniumPlayerFlyRules`, `…CollisionRules`, `…TagRules`, `…TeleportationRules`,
  `…GlideRules`, `…GravityRules`, `…JumpRules`, `…MovementSpeedRules`, `…AvatarHeightRules` (0.6–5 m,
  default 1.58). Each has `Global…` properties and setters
  (`SetDisable{FlyMode|Collision|PlayerTag|Teleportation|Glide}State`;
  `SetGlobalGravity` / `SetGlobalJump` / `SetGlobalSpeed` for both modes or
  `SetGlobalNonXR…` / `SetGlobalXR…` for one; teleport `SetGlobalDistance` / `SetGlobalCooldown` /
  `SetGlobalRayGradient`; height `SetGlobalApplyHeight` / `SetGlobalXRHeight`) and `AddArea` /
  `UpdateArea` / `RemoveArea` / `ClearAllAreas` / `GetArea(int)`.
  - Start once the local player exists; re-applied **every 0.1 s** from the local player's **head**.
  - Areas are axis-aligned boxes (the `Area(pos, size)` constructor enforces ≥ 1 m). For on/off rules
    (Fly, Collision, Tag, Glide, Teleportation) an area is an **allowed zone** (feature back on
    inside). Overlaps: **last** match wins for Height, Gravity, Jump, Speed, Teleportation; **first**
    for Glide.
  - **Rules override your own calls** every 0.1 s (e.g. `TagRules` overwrites
    `SetPlayerTagDisableState` for every player, `AvatarHeightRules` overwrites `SetHeight`) — use one
    or the other.

### Built-in runtime components (`SomniumControlComponent`)
You place them in the scene; at runtime **the client attaches its implementation as a child**. They
are numbered by **hierarchy order, skipping inactive ones** — keep them active at load, never create,
delete or reorder them at runtime, keep the hierarchy identical for every player.

- **`SomniumVideoPlayer`** — AVPro-based, **synced** (one *host* controls, others follow).
  - Media: `SetAndPrepareLink(url)`, `WaitUntilPrepareAndPlay()`, `MediaPath()`, `Reload()`,
    `CloseMedia()`, `SetAutoPlay(bool)`, `SetLooping(bool)` / `IsLooping()`.
  - Playback: `Play()`, `Pause()`, `Stop()`, `Rewind()`, `Seek(double)`, `SetProgress(0..1)`,
    `GetCurrentTime()`, `GetDuration()`, `IsPlaying()`, `IsPaused()`, `IsFinished()`, `IsSeeking()`,
    `IsBuffering()`, `IsLive()`, `CanPlay()`, `IsProgressChanging()`.
  - Audio: `SetVolume(float)` / `GetVolume()` (use these, not the `AudioSource`), `MuteAudio(bool)` /
    `IsMuted()`, `ConnectAudioSettings(SomniumVideoPlayerAudioSettings)`.
  - Control: `TakeControl()`, `ReleaseControl()`, `CanTakeControl()`, `IsHost()`, `GetHostName()`,
    `Sync()`.
  - Output: `GetTexture()` (appears some time after load — poll it); extra outputs on the component as
    `SomniumRenderTarget` (`MeshRenderer` + material index) and `SomniumAudioSource` (`AudioSource` +
    positional/2D).
  - Events: `OnEventReceived` (`UnityEvent<SomniumVideoEvent>`, use `AddListener`): `Initialized`,
    `MetaDataReady`, `ReadyToPlay`, `Started`, `FirstFrameReady`, `Paused`, `Unpaused`,
    `FinishedPlaying`, `Closing`, `Error`, `Stalled`, `Unstalled`, `StartedSeeking`/`FinishedSeeking`,
    `StartedBuffering`/`FinishedBuffering`, `StartedLinkPreparing`/`FinishedLinkPreparing`,
    `Muted`/`Unmuted`, `HostChanged`, `ResolutionChanged`, `PropertiesChanged`,
    `PlaylistItemChanged`, `PlaylistFinished`, subtitle/text-track events.
  - Helpers: `SomniumVideoEventsHandler` (UnityEvents per event), `SomniumVideoPlayerControls` (UI
    actions incl. a link field: `SetLink` / `PasteLink` / `ClearLink`), `SomniumVideoPlayerAudioSettings`
    (spatial blend, rolloff, min/max distance, doppler, spread, reverb mix, priority, pitch).
- **`SomniumKeyboard`** — connects a TMP `InputField` to Somnium's virtual keyboard (needed in VR):
  `IsHidden`, `DuplicatedInput`, `CanAttach`, `ActivateImmediately` / `DeactivateImmediately`,
  `AttachTransform`, `OffsetPosition`, `OffsetRotation` (properties, `Set…` methods, or one
  `SomniumKeyboardSettings`).
- **`SomniumInventoryItem`** (+ `SomniumAvatarInventoryItem`, `SomniumWearableInventoryItem`,
  `SomniumBuildingInventoryItem`) — shows an existing Somnium item: `ItemType` (`Custom` / `Minted`),
  `Id`, `BundleId`, `PreviewId`, `Name`, `Width`, `Height`, `CanBeGrabbed`, `NeedToSync`; pick it in
  the inspector (login).
- **`SomniumTipJar`** (max one; `Width`, `Height`), **`SomniumClimbableSurface`** (climbing).

**Not in the Bridge**: no UI/notification API, no input API, no server time — use Unity UI and XRI,
`UnityWebRequest` for your own services, and synced values for shared timers (section 7).

---

## 7. Networking

### Principles
- **The only way to share anything between players is Photon Fusion, through network components
  that already exist in the Somnium client**, driven from your own plain `MonoBehaviour`s. Do not
  open your own Photon connection or runner, and do not look for a way around this.
- **You cannot write your own `NetworkBehaviour`, `[Networked]` property or `[Rpc]` method.** Fusion
  only knows the networked types **built into the client**; your world DLL is compiled separately and
  loaded later, so its types compile but do not work. Build everything from **messages** (byte
  arrays), **synced ints**, and Fusion's ready-made **transform / rigidbody / animator sync**.
- Fusion runs in **shared mode**: no server simulates your world; each networked object has one
  **state authority**, and authority can move (section 0, Terms).
- **Fusion is only needed for networking.** It must be installed to use networking — use the one in
  the Pro SDK or the "Community Modules + Photon" package, matching the client: **Photon Fusion 2.1.3**,
  with Photon Realtime 5. Community Modules does not need Photon Voice (the client handles voice). Local-only scripting does not need it; do not
  install it "just in case". The installed version is in `Assets/Photon/Fusion/build_info.txt`.

### Photon Fusion 2.1 — what differs from 2.0
Most Fusion examples online, and most of what you may remember, are **Fusion 2.0**. The client runs **2.1.3**;
worlds built against 2.0 do not sync and must be updated and re-uploaded. Check these before writing code:

| Topic | Fusion 2.0 (do not use) | Fusion 2.1 |
|---|---|---|
| Physics sync | Physics addon: `NetworkRigidbody3D/2D`, `RunnerSimulatePhysics3D`, `using Fusion.Addons.Physics;` | **Gone** — in shared mode it despawns the object. Put `NetworkTransform` on the GameObject with the Rigidbody |
| Teleport a physics object | `NetworkRigidbody3D.Teleport` / `ResetState()` | `networkTransform.Teleport(pos, rot)` (state authority only), then reset the Rigidbody velocity |
| Photon Realtime | asmdef `PhotonRealtime`; `using Fusion.Photon.Realtime;`, `using ExitGames.Client.Photon;`, `DebugLevel` | asmdef `Photon.Realtime`; `using Photon.Realtime;`, `using Photon.Client;`, `LogLevel` |
| Take authority | `Object.RequestStateAuthority()` (no result) | returns `bool`: `false` = request not sent (e.g. no *Allow State Authority Override*). `Runner.BatchRequestStateAuthority` takes up to 16 objects at once |
| Owner leaves | object may get a new authority automatically | remaining objects have `StateAuthority == PlayerRef.None` until someone requests them |
| Master client | `PlayerRef.MasterClient` | `Runner.IsSharedModeMasterClient`, `Runner.GetMasterClient()` (Community Modules: `SceneNetworking.IsMasterClient`) |
| Send priority | `NetworkObject.SendPriority` | obsolete — `NetworkObject.SetPriority(player, priority)` |
| Reliable data | `OnReliableDataReceived(..., ArraySegment<byte> data)` | `ReadOnlySpan<byte> data` (`data.ToArray()` to keep it); `SendReliableDataToPlayer(player, key, byte[])` still compiles |
| Object flags | `NetworkObjectFlags.V1` | baked as `V2`: never assign a fixed flags value, add or remove single flags |
| Version defines | `FUSION_2_0_*` | `FUSION_2_1_OR_NEWER` (plus `PHOTON_REALTIME_5_OR_NEWER`) |

`NetworkTransform` in 2.1 (details in *Components* below): it syncs **parent-relative** position, its
**Forecast** must be off, and with Forecast off Fusion makes the Rigidbody **kinematic on clients that do not own
it** (re-evaluated when authority moves) — keep that in mind if your code changes `isKinematic` itself.
Prefabs and scenes are re-baked on import; if a network prefab misbehaves, run **Tools > Fusion > Rebuild Prefab
Table** and check the Console.

### The network stubs (Community Modules)
- The Somnium network components are a **temporary networking solution** until an official one
  exists in the Bridge. They ship as **stubs with Community Modules** in
  `Assets/#User/Community Modules/Plugins/SomniumSpace/Networking/` (the `NetworkBridge*` scripts and
  the shared base in `Virtual/`), with their own asmdef `SomniumSpace.Network`.
- **How stubs work:** each has the **same assembly name, namespace and class name** as the component
  inside the client (assembly `SomniumSpace.Network`, namespace `SomniumSpace.Network.Bridge`). Your
  scene stores a reference to that name; in the client, the client's own component is used. The stub
  assembly is not part of your upload. The stubs contain working code, so they also run in editor play
  mode (section 10) — read them for exact signatures. **Never edit, rename or move them** — any
  difference breaks the match.
- The same principle applies to any networked component in the client: a matching stub makes it
  usable unless the scripting tool blocks it. Only the components below are provided and tested.
  **VRIK stubs never worked**; **Climbing stubs are no longer needed** (use `SomniumClimbableSurface`).
- In your asmdef reference `SomniumSpace.Network` and write `using SomniumSpace.Network.Bridge;`; for
  the Fusion components reference `Fusion.Runtime` and `Fusion.Unity` (`using Fusion;`); Photon Realtime 5 is
  the asmdef `Photon.Realtime` (see the 2.1 table above).

### Components
| Component | From | Use |
|---|---|---|
| `NetworkBridgeEvents` | Somnium stub | messages only |
| `NetworkBridgeData1`, `2`, `4`, `6`, `8`, `12`, `16`, `32`, `64` | Somnium stub | messages + N synced ints |
| `NetworkTransform` | Fusion | sync position/rotation/scale — also of a physics body (on the GameObject with the Rigidbody) |
| `NetworkMecanimAnimator` | Fusion | sync an `Animator`'s parameters and states |
| `NetworkBridge` | Somnium stub, **legacy** | **do not use** — carries large fixed arrays, too much network buffer |

- Every networked object needs a `NetworkObject` on its root with **Allow State Authority Override** on
  (only such objects can be taken over with `RequestStateAuthority()`) and **Destroy When State Authority
  Leaves** off, so objects a leaving player controlled stay and are taken over by the master. Fusion's
  baker owns the flag version bits (`V2` since 2.1): add or remove single flags, never assign a fixed value.
- When the owner leaves, a remaining object has `StateAuthority == PlayerRef.None` until someone takes it.
- The bridge components are Fusion `NetworkBehaviour`s: `bridge.Object` (its `NetworkObject`),
  `bridge.HasStateAuthority`, `bridge.Runner`; take authority with `bridge.Object.RequestStateAuthority()`,
  which returns `false` when the request was not sent.
- Only the **state authority** moves an object with `NetworkTransform`; on other clients its movement is
  overwritten. To let a player move it (e.g. grabbing), take authority first. Move it instantly with
  `networkTransform.Teleport(position, rotation)` (state authority only), then reset the Rigidbody's velocity.
- `NetworkTransform` **Forecast** (Physics Settings) must be **off**: with it on, other players see a local
  physics prediction — a held or thrown object floats and drifts away from the hand.
- Objects grabbed with XRI **Kinematic** movement: **always** tick **Disable Shared Mode Interpolation** on their
  `NetworkTransform` (`ConfigFlags`). Otherwise Fusion keeps interpolating the owner's own copy between ticks
  and fights the grab — it stutters badly for everyone. Velocity Tracking and Instantaneous grabs need nothing extra.
- The same applies to your own scripts: if one moves a networked object in `Update()` (outside the network tick)
  and the object stutters for its owner, tick **Disable Shared Mode Interpolation** on its `NetworkTransform`. It
  only changes what the owner sees; other players still receive the same updates.
- `NetworkTransform` syncs the position **relative to the parent**, and XRI unparents an object while it is
  grabbed: keep grabbable network objects at the **root of the scene** (`NetworkGrabbable`'s *Detach From
  Parent*, on by default, does it when the world loads), or other players see a held object offset.
- `NetworkMecanimAnimator`: drive the `Animator` on the state authority only; sync few parameters.

**`NetworkBridgeEvents` — messages:** `RPC_SendMessageToAll(byte[])` (everyone incl. sender),
`RPC_SendMessageToProxies(byte[])` (everyone but the state authority),
`RPC_SendMessageToController(byte[])` (the state authority), `RPC_SendMessageToPlayer(PlayerRef,
byte[])` (one player); receive via the matching `OnMessageToAll` / `OnMessageToProxies` /
`OnMessageToController` / `OnMessageToPlayer` event. Lifecycle events: `OnSpawned`, `OnDespawned`,
`OnRender` (every frame while spawned), `OnFixedUpdateNetwork` (the network tick, state authority only — see
*Changes in the network tick*),
`OnStateAuthorityChanged(bool)`. A `PlayerRef` comes from `SceneNetworking.OnOtherPlayerJoined`,
`NetworkRunnerRef.LocalPlayer` or `NetworkRunnerRef.ActivePlayers`.

**`NetworkBridgeDataN` — synced state:** the same plus a networked **int array** (`IntArray` with
`Get(i)` / `Set(i, value)`, size `IntCount`).
- **Only the state authority writes**; everyone reads; **late joiners get the current values**.
- **There is no change event**: compare `IntArray` with your last applied copy in `OnRender`, or send a
  message along with the write (the `NetworkEvent*` pattern below).
- `IntArray` is only valid while the object is spawned (between `OnSpawned` and `OnDespawned`) — never
  touch it offline or before `OnSpawned`. Stay inside `IntCount`.
- Encode everything as ints (bools 0/1, enums as ids, floats fixed-point) and document each array's
  layout next to its constants.
- **Subscribe to the components' events in `Awake`**: a spawned prefab can get `OnSpawned` before your
  `Start` runs.

### Message rules
- The payload is raw bytes, with **no message id and no sender** passed to your handler: put an id in
  the first byte (constants in one place; **never renumber** once live, append) and the sender in the
  payload if needed (e.g. `NetworkRunnerRef.LocalPlayer.PlayerId`).
- **Treat every message as untrusted**: validate length, ranges, and whether the action is possible
  now. The state authority decides; others request.
- **Messages are not stored** — late joiners never get past messages; what they must see belongs in
  synced state.
- **At most 512 bytes per message** (Fusion's RPC limit, payload plus a little overhead) — aim for
  ≤ 200–400 bytes.
- **Use messages sparingly.** All of a client's messages must fit in **one packet per network frame**,
  so they saturate early (around 18 KB/s) and each carries ~20 B overhead; synced values can use
  several packets per frame. Frequent or continuous data → **synced values**; rare events →
  **messages**; **never stream data through messages**, not even small amounts per frame.
- **Large one-off data** (e.g. a big JSON): Fusion's reliable data transfer
  (`Runner.SendReliableDataToPlayer(player, key, bytes)`, received via
  `SceneNetworking.OnReliableMessageReceived`) — rudimentary, not tested in Somnium worlds; verify first.

### `SceneNetworking` and spawning (Community Modules)
- **One `SceneNetworking` per scene**, anywhere, never disabled. **Use it as is.**
- **Scene network objects** (placed in the editor) are **detected when the world starts** and must be
  registered on every client **in the same order**: they must stay enabled, cannot be created or
  destroyed at runtime, and none may be added later. Known issue: they can disappear after a
  connection loss until the player re-joins.
- **Network prefabs** for anything created later: add each prefab asset to `SceneNetworking`'s
  Network Prefabs list (the GUID list fills itself; same length, no duplicates) — unlisted prefabs are
  not spawned. **Spawn** with `NetworkSpawn.Spawn()` or
  `SceneNetworking.NetworkRunnerRef.Spawn(SceneNetworking.Instance.NetworkPrefabs[prefab], position)`.
  Never spawn any other way, never register prefabs or touch Fusion's prefab table yourself — it is
  shared with the client and easy to damage. Spawned and scene objects count the same in the budget.
- **Removing network objects is not settled.** There is no despawn helper. Fusion's
  `Runner.Despawn(obj)` (by the state authority) is standard for spawned prefabs but not confirmed in
  Somnium worlds; whether it works on **scene-placed** objects is unknown. Safe alternative: a pool —
  spawn or place a fixed set once, then hide and reuse. Test early with two players.
- Static API: `IsNetworkReady` (local player joined, scene objects registered), `IsMasterClient`,
  `NetworkRunnerRef`, `IsConnected` / `IsDebugMode`; events `OnNetworkRunnerCreated`,
  `OnLocalPlayerJoined`, `OnBecomeWorldMaster`, `OnOtherPlayerJoined(PlayerRef)`,
  `OnOtherPlayerLeft(PlayerRef)`, `OnReliableMessageReceived(key, bytes)` — **cleared when the scene
  unloads**, so subscribe again every time the world loads.
- Authority: the first player in the room controls every object; others request it; the master takes
  objects left without an owner (every second and when a player leaves).
- **Offline / single player** must use the same code path: when `IsDebugMode` is true, apply changes
  locally (the community components do). `IsDebugMode` only means "no running network": true in the
  editor **and during the first moments in the client** — wait for `IsNetworkReady` /
  `OnLocalPlayerJoined` before sending or reading network state. In Community Modules versions before
  October 2026, `IsConnected` / `IsDebugMode` threw without a runner — update, or check
  `NetworkRunnerRef != null`.

### Patterns
**Authority pattern:** any client **detects** (collision, button) → sends a **request** to the state
authority (`RPC_SendMessageToController`) → the authority **validates and executes** (writes synced
ints and/or broadcasts a result). Make requests **idempotent** (a "consumed" latch or sequence number
— two clients may detect the same event) and handle `OnStateAuthorityChanged`: the next owner must
continue from the synced state alone.

**Changes in the network tick** (recommended for anything that moves a networked object):
- Fusion captures an object's networked state once per **network tick**. A change made between ticks — in
  `Update()`, a coroutine, a UnityEvent, an `Invoke` — can be overwritten or blended by the next tick. Teleporting
  a `NetworkTransform` object that way was found **unreliable** in Somnium (sometimes it snaps, sometimes not).
- Pattern: **record, then apply in the tick.** Events and messages only store the request; the state authority
  applies it in `OnFixedUpdateNetwork`. Messages and `IntArray` writes from events are fine as they are; the tick
  matters most for moving, teleporting or snapping a networked transform, and for state that must line up with it.
- `OnFixedUpdateNetwork` comes from a bridge component: an object with only `NetworkObject` + `NetworkTransform`
  has no tick callback — add a `NetworkBridgeEvents` to it (messages only, ~0.4 KB of network budget). It runs on
  the state authority only, so other players send their request with `RPC_SendMessageToController`.
- Offline (not spawned) there is no tick: apply the change directly.

```csharp
// Same GameObject: NetworkObject + NetworkTransform + NetworkBridgeEvents (_bridge). Subscribe in Awake.
public void RequestTeleport()                        // any player, any time (button, trigger, UnityEvent)
{
    if (!_isSpawned) { transform.SetPositionAndRotation(_target.position, _target.rotation); return; }
    if (_bridge.HasStateAuthority) _teleportPending = true;           // we control it: next tick
    else _bridge.RPC_SendMessageToController(new[] { MSG_TELEPORT }); // ask the controller
}
private void OnMessageToController(byte[] m) { if (m != null && m.Length == 1 && m[0] == MSG_TELEPORT) _teleportPending = true; }
private void OnFixedUpdateNetwork()                  // the network tick, state authority only
{
    if (!_teleportPending) return;
    _teleportPending = false;
    _networkTransform.Teleport(_target.position, _target.rotation); // others see a jump, not a slide
}
private void OnStateAuthorityChanged(bool has) { if (!has) _teleportPending = false; } // the new owner did not get it
```

**Example — a switch shared by all players** (compiled against the real stubs; adapt as needed):

```csharp
using Fusion;
using SomniumSpace.Network.Bridge;
using UnityEngine;

namespace CreatorName.WorldName
{
    // Anyone toggles, the state authority decides, late joiners see the current state,
    // offline (editor) it works locally. Same GameObject: NetworkObject + NetworkBridgeData1.
    public class SyncedSwitch : MonoBehaviour
    {
        private const byte MSG_TOGGLE = 1; // message ids: never renumber, only append

        [SerializeField] private NetworkBridgeData1 _net; // slot 0: 0 = off, 1 = on
        [SerializeField] private GameObject _target;

        private bool _isSpawned;
        private bool _offlineState;
        private int _appliedState = -1;

        private void Awake() // not Start: a spawned prefab can get OnSpawned before Start
        {
            if (_net == null || _target == null)
            {
                Debug.LogError($"[{nameof(SyncedSwitch)}] Missing reference.");
                enabled = false;
                return;
            }
            _net.OnSpawned += OnSpawned;
            _net.OnDespawned += OnDespawned;
            _net.OnMessageToController += OnMessageToController;
            _net.OnRender += ApplyState; // every frame while spawned
        }

        private void OnDestroy()
        {
            if (_net == null)
                return;
            _net.OnSpawned -= OnSpawned;
            _net.OnDespawned -= OnDespawned;
            _net.OnMessageToController -= OnMessageToController;
            _net.OnRender -= ApplyState;
        }

        public void Toggle() // call from a button, a trigger or an XRI event
        {
            if (!_isSpawned) // offline or not connected yet: local only
            {
                _offlineState = !_offlineState;
                _target.SetActive(_offlineState);
                return;
            }
            _net.RPC_SendMessageToController(new[] { MSG_TOGGLE }); // ask the state authority
        }

        private void OnSpawned() => _isSpawned = true;

        private void OnDespawned(NetworkRunner runner, bool hasState) => _isSpawned = false;

        private void OnMessageToController(byte[] message) // runs on the state authority only
        {
            if (message == null || message.Length != 1 || message[0] != MSG_TOGGLE)
                return; // validate every message
            int state = _net.IntArray.Get(0);
            _net.IntArray.Set(0, state == 0 ? 1 : 0); // reaches everyone, late joiners included
        }

        private void ApplyState() // no change event: compare with the last applied value
        {
            int state = _net.IntArray.Get(0);
            if (state == _appliedState)
                return;
            _appliedState = state;
            _target.SetActive(state == 1);
        }
    }
}
```

**Proven in Community Modules' network scripts** (references, not templates):
- **Synced value with events** (`NetworkEventInt` / `Float` / `Bool` / `Color`): `SetValue` messages
  everyone (`[index byte][value]`); the authority also stores it in a `NetworkBridgeData` slot; on
  `OnSpawned` the authority writes the initial value and others read the slot (late joiners included);
  offline it applies locally. For values changed now and then, not every frame.
- **Plain events** (`NetworkSimpleEvent`, `NetworkEventString`): an event-id byte; several share one
  `NetworkBridgeEvents`; no memory for late joiners.
- **Packing**: `NetValue(data, index, subIndex)` reads/writes bool (bit), byte, short, float, int,
  `Color32` in one int slot; `BytesWriter` / `BytesReader` build and read payloads.
- **Taking authority**: `RequestStateAuthority()`, then wait (up to ~2 s) for `HasStateAuthority` —
  see `NetworkGrabbable` (on grab) and `TeleportNetworkRigidbody3D` (`NetworkTransform.Teleport`, then
  reset the velocity); `NetworkOwnershipEvent` (authority → UnityEvents);
  `NetworkAnimator` (animator triggers/bools via the authority). `NetworkGrabbable.IsGrabbed` starts
  `true` on an untouched object — use `XRGrabInteractable.isSelected` for "is it held".
- `NetworkedScriptExample` shows the whole message/authority flow on one object.

**General multiplayer patterns:**
- **Shared seed**: for random results everyone must agree on, the master sends a seed once and every
  client computes them (`System.Random(seed)`, not `UnityEngine.Random`).
- **Shared timers**: the state authority owns the timer and syncs it in a synced value (e.g. the
  remaining time); if you use Fusion ticks (`Runner.Tick`) as a shared clock, first test that clients
  agree.
- **Echo suppression**: when applying a received change, set a flag so your "value changed" handler
  does not send it back.
- **Contain failures**: every handler in `try/catch`, log once, never throw into Unity.
- **One client runs shared logic** (timers, moving obstacles, scores) — usually the master — and keeps
  its state in synced values so the next master can continue.
- Per-player progress stays local; send only what others must see.
- Heavy work in a single frame freezes that player's networking too, so others may think they left —
  spread it over frames and keep "player absent" timeouts generous.

### Network budget — stay under ~500 networked objects
Each client has a network buffer of about **8 MB**, filled by every **active networked object** in
the scene. **Overloading it breaks sync** (players freeze, objects misbehave). Rough cost per object
(Somnium measurements, 2026 — keep a margin):
- no synced values (`NetworkObject` alone, or + `NetworkBridgeEvents`): **~0.4 KB**;
- with synced values: **~3.3 KB + ~0.14 KB per synced word** (word = 4 bytes).

| Object | Synced words | Cost |
|---|---|---|
| `NetworkBridgeData1` / `2` / `4` / `8` / `16` / `32` | 1 / 2 / 4 / 8 / 16 / 32 | ~3.3 / 3.3 / 3.9 / 4.4 / 5.5 / 7.8 KB |
| `NetworkTransform` | ~16 | ~5.5 KB (measured with Fusion 2.0; on a Rigidbody with 2.1: not measured yet) |

- An object reaches its **full cost the first time a value changes** and keeps it — budget the full
  cost even if it is idle in tests.
- **Keep the total at most 50–60 % (~4–5 MB)** to leave room for players. Example: 300
  `NetworkTransform` (~1.7 MB) + 200 `NetworkBridgeData4` (~0.8 MB) ≈ 2.5 MB — OK.
  `NetworkBridgeData64` on many objects overloads the buffer.
- **The first synced value is the expensive part**: one shared "manager" `NetworkBridgeDataN` for world
  state beats one tiny data component per item; use messages-only objects where no state is needed.
- Every variable takes **whole words** (`byte`, `bool`, `int`, `float` = 1, `long` = 2, `Vector4` = 4):
  pack small values into ints. Splitting the same values over components on one object changes little,
  but every added synced value counts (e.g. `NetworkBridgeData6` on a rigidbody, ~+2 KB).

---

## 8. Player, interaction and physics

### Physics layers (same in every world; the SDK enforces the named ones)
| # | Layer | # | Layer | # | Layer |
|---|---|---|---|---|---|
| 0 | Default | 9 | Overlay | 21 | NotRendererByTabletCamera |
| 1 | TransparentFX | | | 22 | Media_360 |
| 2 | Ignore Raycast | 14 | **Terrain** | 23 | **Environment** |
| 4 | Water | 15 | Triggers | 24 | Player |
| 5 | UI | 16 | BuildingItem | 25 | Underground |
| 6 | XRInteractable | 17 | Parcel | 26 | Bullet |
| 7 | **PhysicalObject** | 20 | ActionTracker | 28 | Vehicle |
| 8 | NotRendererByMainCamera | | | 30 | IkContact |

- Walkable ground: `Default`, `Terrain` (required for Unity Terrain) or `Environment`. Grabbable /
  physical items: `PhysicalObject` or `XRInteractable` (see grab setups below); child colliders use
  the root's layer and have no Rigidbody of their own.
- **Not rendered by the player camera:** 8, `Triggers` (15 — good for trigger volumes), 20, 22, 25, 30 in both
  modes, and the **unnamed layers 3, 10, 11, 12, 13, 18, 19, 27, 29, 31** (PC hides all of them, VR all but 10,
  11 and 29). Visible objects go on Default, Terrain, Environment, PhysicalObject and the like — never on an
  unnamed layer.
- `Overlay` (9) is the client's tablet layer: drawn after post-processing, so no bloom or upscaling — not for
  world objects.
- **Rendering layer bit 31** is used by the client — do not give it a meaning.
- **Tags**: use Unity's built-in tags only. The client has its own list of custom tags, different from your
  project's, so a custom tag you add may not reach the client as you wrote it (not tested). Filter by name,
  component or layer instead.
- **Your own physics queries**: `QueryTriggerInteraction.Ignore` and `Physics.DefaultRaycastLayers`
  (excludes Ignore Raycast, the player body's layer) keep the player's body and triggers out of your
  rays. The local VR hand boxes (`PhysicalObject`) and the local tablet (5, 6) can still be hit — exclude
  those layers or filter.
- **Collision callbacks are reused** (*Reuse Collision Callbacks* is on in the client): read what you need
  inside `OnCollision*`, never keep the `Collision` object for later. `OnTriggerStay` events are on.

### How a player is built (client internals — may change in any update)
⚠️ This is the client's internal structure, read from a live client (2026-10). Names, hierarchy and layers
can change without notice, so features that dig into it can break after an update. Prefer the Bridge
where it covers your need. When you must search by name or hierarchy: do it once, null-check every step,
fail softly (disable the feature, log once), and keep the lookup in one place so it is easy to fix.

Each player is **several root objects in your world's scene** (not one hierarchy, not
`DontDestroyOnLoad`), so scene-wide searches also find players:

| Root object | What it is |
|---|---|
| `Original Player: [Nick] [N] [Player:P]` (local) / `Simulator Player: …` (remote) | the rig: body capsule, head, hands, camera (local) |
| `Avatar for Player [Nick] [N]` | the avatar (several copies of the model) |
| `… Player Network Agent: [Player:P]` and other `… Network Agent` roots | network objects (and voice, section 9) |

`P` = `Properties.PhotonId`; `N` links a rig root to its avatar root.
- **Avatar bones**: every copy has a **humanoid `Animator`** — use `GetBoneTransform(HumanBodyBones.X)`
  rather than bone names. Use the main copy: `…/MainRoot/Root` (VR) or `…/NonXRMainRoot/Root` (PC). The
  other copies (mirror, shadow-only `WearablesRoot`) share the pose. A default "dummy" avatar replaces the
  main copy while the player's own avatar is not set up or is detached — read bones from whichever is active.
- **Avatar copies change layer at runtime** (usually: local main copy 21, its mirror copy 8, shadow copy 6;
  remote avatars 6) — never find or filter avatars by layer.
- Player → avatar: find the rig root containing `[Player:<PhotonId>]`, read its `[N]`, then
  `Avatar for Player [<NickName>] [<N>]`; cache it, resolve again on `OnPlayerAvatarChanged`.
- Avatars have no colliders of their own (except what the avatar's creator added).

### The player's colliders
| Object | Collider | Layer | Exists on |
|---|---|---|---|
| body `XR.Body` / `NonXR.Body` (remote VR: `Root`) | CharacterController + kinematic Rigidbody (r 0.15 VR / 0.25 PC) | Ignore Raycast (2) | every player |
| head trigger `PlayerTrigger` (remote VR: `XR.Simulator.PlayerTrigger`) | sphere trigger r 0.18, **at the head** | Triggers (15) | every player |
| `ActionTracker` | sphere trigger r 0.18, at the head | Triggers (15) | local player only |
| `WaterCollisionNotifier` | sphere trigger + Rigidbody, at the camera | 21 | local player only |
| `Hand Collider Left Variant`, `Hand Collider Right` | box + Rigidbody, joint-driven | PhysicalObject (7) | local VR player only |
| `Climb Grab Left/Right` | capsule trigger | XRInteractable (6) | local VR player only |

- Body capsule: PC — feet to about head height; VR — center and height follow the headset (short when
  seated). The capsule's transform stays at the play-space origin: use its `center` or `Body.Head`, never
  its position (section 6). Remote players keep their capsule (physics objects bump into them) but have no active hand
  colliders. The local player's tablet also has colliders (layers 5 and 6) while it is out.
- What a trigger on your layer meets:
  | Your trigger's layer | Body capsule (2) | Head trigger (15) | VR hands (7) |
  |---|---|---|---|
  | Default, Environment, BuildingItem | yes | yes | yes |
  | Triggers (15) | **no** | yes | yes |
  | XRInteractable (6) | no | no | yes |
  Bodies do not collide with XRInteractable: players walk through layer-6 objects.
- ⚠️ **One player can fire `OnTriggerEnter` several times.** Identify players by **component**, not name:
  `other.GetComponent<ISomniumPlayerTrigger>()` (`.Player`) — the head trigger exists on every player.
  It is **at head height**, so make trigger volumes tall enough. `new SomniumTriggerActionArgs(...)` also
  works. De-duplicate per player; triggers fire before `FixedUpdate`.
- **VR hands are physical**: joint-driven boxes that push with real force, so every grab or touch
  fires `OnCollisionEnter` on your items. Filter them by the **name prefix `Hand Collider`** (same
  layer as items; a client name — keep the check in one place). They **do not exist in PC mode** — every "touch" interaction needs a PC path too
  (a click, a trigger volume, a grab).

### XR interactors (per hand, VR)
Each interactor has **XR Interaction layers** (the interactable's XRI layer must match) and a
**physics mask** (which physics layers its sphere/ray hits) — two different lists.

| Interactor | XR Interaction layers | Physics mask |
|---|---|---|
| `Grab Left` / `Grab Right` | **Grabbable** | sphere: Default, PhysicalObject, Terrain, Environment · ray: Default, PhysicalObject, Environment |
| `Near-Far Interactor` | XRInteractable, XRRay | sphere: XRInteractable · ray: Default, UI, XRInteractable, Overlay, Terrain, BuildingItem, Environment |
| `Climb Grab` | Climbing | Default |
| Teleport pointer | Teleport | Default, layer 11, Terrain, BuildingItem, Environment |

XRI interaction layers: 0 Default, 1 Climbing, 3 Grabbable, 4 XRInteractable, 5 GrabbableObject,
6 PointCollider, 7 XRRay, 8 Inspectable, 31 Teleport.

### Grabbing
**Two working setups** (both grab near and at a distance):

| Physics layer of the collider | XR Interaction layer of `XRGrabInteractable` | Grabbed by |
|---|---|---|
| **PhysicalObject** (or Default / Environment) | **Grabbable** | `Grab Left/Right` |
| **XRInteractable** | **XRInteractable** | `Near-Far Interactor` |

⚠️ Mixing them (physics XRInteractable + interaction layer Grabbable) is never picked up — some older
docs suggest it.
- Collider + Rigidbody (Interpolate on) + `XRGrabInteractable` with its interaction layer set
  yourself. Movement Type: *Velocity Tracking* (smooth), *Kinematic* (bats, rackets), *Instantaneous*
  (thrown balls); *Dynamic Attach* recommended. In YAML, `m_MovementType` is 0 Velocity Tracking,
  1 Kinematic, 2 Instantaneous; check the value, not the object's name (some example names don't match).
- *Velocity Tracking* and *Kinematic* move the Rigidbody at the physics rate (50 Hz in the client) while the
  hand moves every rendered frame: the held object stutters, even with Rigidbody interpolation. Put the
  renderers on a child without colliders (e.g. `Visuals`) and assign it to the grab's **Predicted Visuals
  Transform** — XRI then draws it at the hand pose each frame (see `Networked Grabable Hittable/Hitter Example`).
- **Recommended layout for a grabbable** (best visual result; a suggestion, not a requirement):
  ```text
  Grabbable        Rigidbody + XRGrabInteractable (+ NetworkObject, NetworkTransform, NetworkGrabbable)
  ├─ Colliders     colliders only (they may also sit on the parent; a separate child keeps things clear)
  └─ Visuals       renderers only, no colliders → the grab's Predicted Visuals Transform
  ```
  Not for **jointed** objects (levers, valves, drawers, anything on a Joint or with a body joined to it): the
  prediction follows the hand and ignores the joint, so the visual would leave its hinge. Not used by *Instantaneous*.
- "Is it held?" → `XRGrabInteractable.isSelected`. A networked grabbable also needs
  `NetworkTransform` (Forecast off), authority on grab and a place at the scene root — see `NetworkGrabbable`
  (section 7).
- VR teleport needs XRI `TeleportationArea` / `TeleportationAnchor` on surfaces. A
  `TeleportationArea` on a parent covers all its child colliders.
- Fast VR swings and throws can pass through thin colliders. A continuous *Collision Detection* mode
  on the moving Rigidbody helps, but how well depends on the collider shape — test it; kinematic
  bodies only support *Continuous Speculative*. Thicker target colliders also help.

### Moving the player
Scripts that move the player (platforms, seats, anchors) need a `DefaultExecutionOrder` relative to
the client's player controller (see `PlayerPlatform` / `PlayerAnchor`). When releasing the player,
restore motion (`SetGravity(1, 1)`, `SetMovementSpeed(1, 1, 1)`).

---

## 9. Input, UI, cameras and rendering

### Input
- **Input System only.** Client PC keys — do not bind: W A S D, arrows, Space, Shift, Tab (tablet),
  Q/E (rotate), M (microphone), I (inspector), **F (interact)**, **R (quick menu)**, **X**, Esc, mouse
  buttons and wheel. Keys found free: Z, Y, U, O, P, K, L, N, digits. Prefer XRI interactions (grab,
  poke, UI ray) over raw keys — they work in VR and PC.
- **Your own input actions are allowed.** Community Modules ships copies of the client's input action
  assets in `Plugins/SomniumSpace/Inputs/` (`PC Interactions`, `XR Interactions`, plus originals) to
  show what the client already binds — maybe slightly outdated, the principle matters; read, do not
  edit. To add your own:
  1. In your own folder, create an Input Actions asset, one action map per feature.
  2. Bind **both modes**: PC (`<Keyboard>/…`, `<Mouse>/…`, `<Gamepad>/…`) and VR through common usages
     (`<XRController>{LeftHand}/{PrimaryButton}`, `{RightHand}/{Primary2DAxis}`, `{TriggerButton}`,
     `{GripButton}`…), which work across headsets.
  3. Code: `[SerializeField] InputActionAsset` (or `InputActionReference`), `FindAction("Name",
     throwIfNotFound: true)`, `Enable()` in `OnEnable`, `Disable()` in `OnDisable`, `ReadValue<T>()` /
     `WasPressedThisFrame()` or `performed` (unsubscribe in `OnDisable`). Example: `BoatDriver` (Boats Plugin, when included).
  4. *Generate C# Class* → set its namespace to your world's namespace.
  5. Avoid what the client binds (movement/turn sticks, grip/trigger for grabbing, menu, the keys
     above), or enable your actions only in context (in your vehicle, holding your tool).

### UI
- World Space canvas (scale ~0.01, size ~100), layer `UI`, with `TrackedDeviceGraphicRaycaster` (VR)
  **and** `GraphicRaycaster` (PC). Set its **event camera** at runtime once `Camera.main` exists
  (`CanvasAssignCamera`).
- The client already has an **EventSystem** (Input System UI module): create one only if
  `EventSystem.current == null` (the editor). The PC cursor is free (right mouse looks); locking it
  from world code was reported to have no effect.
- **VR click rule**: XRI clicks a canvas only if the UI hit is **not farther than the nearest physics
  hit** on the ray — keep colliders behind the canvas plane (a few mm) or remove them.
- In VR a plain `GraphicRaycaster` can react to the desktop mouse over the window — disable plain
  raycasters in VR if that matters. A button clicked through both your own ray and the client's fires
  twice — use one path.
- Text input in VR needs a `SomniumKeyboard` on the input field (section 6).
- **PC mode, clicking 3D objects:** the PC camera has a `PhysicsRaycaster`, so `IPointerClickHandler` /
  `IPointerEnterHandler` on a 3D collider receive mouse events — only on layers Default, TransparentFX,
  Water, UI, 8, Terrain and BuildingItem. VR uses XRI instead; support both.

### Cameras
- The PC camera can be first or third person: for "where the player looks from", use `Body.Head` when
  `Camera.main` is more than ~0.75 m from `Body.Head` (third person).
- **Feed cameras** (monitors, mirrors, handheld cameras): create the `Camera` **at runtime** after load
  (saved cameras are deactivated; runtime ones are expected to survive — verify once, and re-create
  after a scene reload); render to a `RenderTexture`, `stereoTargetEye = None`, start from the client's
  own photo-camera mask (layers 0,1,2,4,5,6,7,8,11,14,15,16,17,20,22,23,24,25,26,28,30): it shows remote
  avatars (6) and the local player's mirror copy (8) and hides the local first-person copy (21); avatar
  copies change layers at runtime, so check the result in the client. Enable it only while watched.
  Test such effects in the client, not only in play mode.
- **Players hear from their head**: the `AudioListener` is on the headset camera in VR and on the head in PC
  mode — even when the PC camera is in third person, several metres behind.
- **Per-camera post-processing**: a local URP `Volume` under your camera, on a layer in its
  `volumeLayerMask`.

### Rendering
- **Unused shader variants are stripped from your upload.** A shader or keyword you only switch on from
  code (`Shader.Find`, enabling a keyword at runtime) must be **referenced by an asset in the scene**
  (e.g. a component holding the `Shader`s and materials with each keyword combination). Symptom: fine in
  the editor, opaque or wrong fallback in the client.
- Shader variants are also frozen **at upload**: re-upload with a newer SDK to get rendering fixes.
- Shaders made with Shader Graph or Amplify Shader Editor are generated: edit the graph, never the
  generated shader code (it is overwritten on the next save).
- Per-object colour/emission: `MaterialPropertyBlock`, not a material instance per object; destroy
  materials you instantiate.
- **Players choose the graphics quality** (Basic, Low, Medium, High, Ultra) and can change single values:
  LOD distance (LOD bias 0.5–2; presets 1–2), shadow distance (0–1000 m; presets 50–250 m), additional
  lights per object (0–8; Basic 2), and textures are half resolution on Low, quarter on Basic. Never rely on
  a fixed LOD bias, shadow range or light count; check the world at Basic.
- One directional light recommended; bake lighting where you can (the world runs next to avatars and a
  VR client at 90 Hz). The client's temporal upscaling may smear objects without proper motion
  vectors — handled by the client, not fixable in your shaders.

### Player voices and audio — consent is mandatory
- Other players' voices play from a 3D `AudioSource` (Photon `Speaker`, mixer group `OtherPlayersVoice`,
  ~1–50 m custom rolloff, low-pass filter) under `… Player Network Agent: [Player:P]/Body/Head/…` (or
  `Body/Root/…` for players without a tracked head). It is often disabled while the player is silent. The
  local player's lip sync uses an `AudioSource` on their avatar (`…MainRoot/Root`). Internals — may change.
- Prefer the Bridge voice features (`EnableAmplify`, `SetVoiceLevel`). If a world must move or reshape a
  voice source (a stage, speakers, a quiet zone), change as little as possible and **restore it** when the
  effect ends or the player leaves.
- ⛔ **Reading, recording, analysing or transmitting a player's voice or microphone audio without their
  explicit consent is strictly forbidden**, and so is any feature that lets someone listen to players without
  their knowledge. Such worlds may be flagged. **Consent is the key**: tell players clearly what happens
  to their voice and let them opt in.
- `UnityEngine.Microphone` (listing or opening microphones) is hardware access — forbidden (section 3).

---

## 10. Testing and debugging

### In the editor (play mode)
- With exactly one `WorldSDKConfig` and *Autolaunch* on (World Testing Window), play mode spawns a
  **local test player** (PC-style: walk, jump, respawn). VR behaviour is best tested in the client.
- **The Bridge does not work in play mode**: every `SomniumBridge.*` property is `null` (silently);
  scene components report defaults (`PlayersCount` 0, `Status` `DISCONNECTED`).
- **Networking in play mode**: tick *Editor Connect Photon Network* on `SceneNetworking` for a real
  Photon test session (messages, synced values, authority, spawning; two editors or builds can join).
  The Bridge is still `null`. The option only works in editor play mode: it is ignored in the Somnium client,
  so leaving it ticked when uploading does no harm.
- After adding or changing network prefabs, let Unity finish importing (Fusion bakes NetworkObjects); if
  spawning fails, run **Tools > Fusion > Rebuild Prefab Table**.
- Wrap platform access behind **one small interface** (e.g. `IWorldPlatform` with `LocalPlayerHead`,
  `IsNetworked`, `Send(...)`): a Bridge implementation plus an offline fallback when
  `SomniumBridge.Network == null`. Check `null` once, not every frame.
- Compile errors and play-mode logs: the Unity editor log `%LOCALAPPDATA%\Unity\Editor\Editor.log`.

### Compile check without Unity
`dotnet build` on the Unity-generated `<Assembly>.csproj` catches C# errors in seconds — but that project
defines `UNITY_EDITOR`, and the server compiles **without** it. For a faithful check, build once with the
`UNITY_EDITOR*` defines removed from `DefineConstants`. New files must be in the csproj (`<Compile Include>`
or let Unity regenerate); check the output DLL exists. It does not check the server's tier rules.

### Uploading from the agent (optional, advanced)
With the Unity CLI (`unity`, beta) and the `com.unity.pipeline` package in the project, an agent can run C#
inside the user's open editor (`unity command eval --project-path <project> --code '<C#>'`) — including the
SDK's own Build & Upload code.
- **Only when the user explicitly asks** — it publishes to the live service. Validate first.
- The user must be logged in through *Somnium SDK/Somnium Window*
  (`SomniumSpace.SDK.Controllers.DataController.IsHaveToken` is true). **Never type credentials.**
- The open scene must hold exactly one `SomniumSpace.SDK.Configs.WorldSDKConfig`.
- Call: `var b = new SomniumSpace.SDK.Editor.UIToolkitWindows.AssetBundleBuilder();
  b.Init(configGameObject, ok => { if (ok && b.CanBeBuild()) b.BuildAssetBundle(); });`
  Without `BuildAssetBundle()` it only validates. `Init`'s callback is asynchronous: `eval` returns at once.
- **Send the upload once.** An eval timeout only means the CLI stopped waiting — the code may still run, and
  a retry can upload twice. Confirm through the log instead.
- Result: every SDK popup is also logged as `[<title>]: <text>`. Watch the Unity editor log (`grep -a`) for
  `[Build & Upload]: Build and Upload Success` (with the upload size), or for errors such as
  `[Build & Upload]: …`, `[Build Error]: …`, `error CS…`.
- Never call `EditorApplication.Exit` through eval (it closes the user's editor). An editor restart can drop
  the SDK login.

### In the client (user)
- Upload (a test world or a replace), **restart the client**, join, read `Player.log`.
- Keep a hidden debug toggle that shows your state on a TextMeshPro panel — there is no console in VR.
- **Test online with at least two players** before calling a networked feature done: authority
  changes, late join, a player leaving mid-action.

### "Works in the editor, not in the client" — check in this order
1. A shader variant was stripped (section 9).
2. A camera or `AudioSource` present at load was deactivated or re-routed (section 5).
3. The object is on a layer the player camera does not render (section 8).
4. Your key clashes with a client key.
5. The client still runs your previous upload — restart it.
6. A Bridge object was `null` in `Start` — subscribe to the "added/connected" events.

---

## 11. Checklist before every upload

- [ ] One `WorldSDKConfig`, no `MainCamera`, no missing scripts, both previews captured.
- [ ] All scripts in the world's single asmdef; unique assembly name and namespace; references in the
      allowed list; editor code inside `#if UNITY_EDITOR`.
- [ ] Nothing from test/debug modes left active in the scene.
- [ ] Works in **VR and PC**, and with scripts **declined** (walkable, not broken).
- [ ] Player position comes from the body capsule's `center` or `Body.Head`, never from `Body.Root.position`
      (section 6).
- [ ] Networked features tested with two players, late join, and the master leaving.
- [ ] Fusion 2.1 only: no Physics addon or other 2.0 API (section 7 table); `NetworkTransform` Forecast off.
- [ ] No per-frame RPCs or allocations, no `DontDestroyOnLoad`, no settings changed in game (or
      defaults restored on leave).
- [ ] Network budget under ~4–5 MB / ~500 objects; no data streamed through messages.

---

## 12. This world (keep up to date)

- **World name / scene:** …
- **World asmdef / assembly name / root namespace:** `…` (at `…`); own scripts in `…`
- **Package versions** (Pro SDK / Community Modules / Fusion): …
- **Network message ids** (first byte of the payload; append only):
  | Id | Name | Direction | Payload |
  |---|---|---|---|
  | … | … | … | … |
- **Synced int layouts** (`NetworkBridgeDataN` → index meaning): …
- **Systems** (one line each: what, where, how it syncs): …
- **Verified platform findings** (date — what was tested — result): …
- **Open questions:** …
