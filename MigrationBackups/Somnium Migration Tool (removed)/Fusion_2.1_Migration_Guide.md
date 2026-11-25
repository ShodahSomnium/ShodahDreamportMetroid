# Networked worlds: migration to Photon Fusion 2.1.3

*Version 4.0 — 2026-10-20*

*Written for Photon Fusion 2.1.3, Community Modules 4.0 and the migration tool V1.0, with Unity 6000.3. Newer versions of
Unity or of the Somnium SDK are normal; details (menu names, log messages) may differ from what is described here.*

Somnium Space now runs on Photon Fusion 2.1.3 (previously 2.0.12). Worlds built with the old version will not sync
correctly and must be updated and re-uploaded.

Worlds made with Pro SDK 3.0 or older: **update the Somnium SDK first** (from its update popup). Community Modules 4.0
needs the current Somnium SDK.

**Where to get the files:** the public Google Drive folder
<https://drive.google.com/drive/folders/1vulElmjJ5OkWHmwxLvC17PmX7WoIaUDx> (also linked from the Somnium creators wiki,
page "Download Community Modules"):

- main folder: the migration tool, the 3 Community Modules 4.0 packages and `ProSDK V4.0.zip`;
- `Agents/`: `WorldCreatorContext.md` and this guide;
- `Plugins/`: the Photon package;
- `Archives/`: older builds (do not use them).

Agents can work without a browser: list a folder with
`https://drive.google.com/embeddedfolderview?id=<folder id>#list` (plain HTML with the `/file/d/<file id>` links), and
download a file with `https://drive.usercontent.google.com/download?id=<file id>&export=download&confirm=t`.

## Three ways to migrate

- **Recommended: the migration tool.** Import `Somnium Fusion 2.1 Migration Tool V1.0.unitypackage`, then open
  **Somnium SDK → Fusion 2.1 Migration**. It walks you through every step below, one button at a time, makes a backup
  before each change and can restore it. It also updates Community Modules to 4.0 and keeps your assembly name,
  namespace and your own scripts. It works without Community Modules too: installing them is then optional.
  **The tool already contains Photon Fusion 2.1.3 and Community Modules 4.0**: you do not need to download them
  separately.
  - Only want part of it? Click **Advanced mode** (bottom left of the window). It has two quick tasks, **Install
    Photon 2.1 only** (removes the old Photon, keeps your Photon settings, installs 2.1) and **Update Community
    Modules only** (for a project that already has Photon 2.1), and lets you run any step on its own, in any order,
    or again. It warns you instead of blocking you, so read the warnings. **Back to the wizard** returns to the guided
    steps.
- **With a coding agent, no window:** see *Unattended / agent migration* below. The same tool runs every step by
  itself from the command line.
- **Manual:** follow the steps below.

## Unattended / agent migration

For coding agents and scripts. The migration tool runs the whole migration from the command line, with no window and
no question, and quits Unity with an exit code.

Before you start:

- **Close the project in Unity.** The commands open it themselves (batch mode); Unity refuses a project that is
  already open. To check: `<project>/Temp/UnityLockfile` does not exist, or no running `Unity.exe` has this project in
  its `-projectPath` (on Windows: the process command lines, for example with
  `Get-CimInstance Win32_Process -Filter "Name='Unity.exe'"`).
- **Make a full copy of the project folder** (or commit everything) if you can. The tool also zips a backup before
  every step, in `MigrationBackups/<date>_step<N>/backup.zip` next to `Assets`.
- **Run the Unity version the project already uses**, written in `ProjectSettings/ProjectVersion.txt`
  (`m_EditorVersion`); with Unity Hub it is `C:\Program Files\Unity\Hub\Editor\<version>\Editor\Unity.exe`. A batch
  run with another version upgrades the project silently, as a side effect of the migration. Moving to a newer Unity
  is fine, but as its own decision, not by accident.
- You only need the tool: it contains Photon Fusion 2.1.3 and Community Modules 4.0.
- **The Somnium SDK must be up to date.** The tool checks it by itself, before changing anything. If it is too old
  (worlds made with Pro SDK 3.0 or older), the run stops with **exit code 2** and the message "The Somnium SDK in this
  project is too old"; nothing was changed. Then ask the user to open the project in Unity and update the Somnium SDK
  from its update popup, and run the migration again. If you have the Somnium SDK `.unitypackage`, pass it with
  `-sdkPackage` (below) instead: the tool imports it first and goes on.

1. Import the tool (once):

   ```
   "<Unity.exe>" -batchmode -ignorecompilererrors -projectPath "<project>" -importPackage "<path>\Somnium Fusion 2.1 Migration Tool V1.0.unitypackage" -quit -logFile "<project>\import_tool.log"
   ```

   `-ignorecompilererrors` matters: without it, Unity refuses to start a project that does not compile (for example
   one where Photon 2.1 is already installed but Community Modules is still the old one) and runs nothing.

2. Run the migration. **Do not add `-quit`**: Unity quits by itself when the migration is done.

   ```
   "<Unity.exe>" -batchmode -ignorecompilererrors -projectPath "<project>" -executeMethod Somnium.MigrationTool.MigrationAutomation.RunUnattended -migrationTask Full -logFile "<project>\migration.log"
   ```

   - `-migrationTask Full` (default): every step of the wizard. `PhotonOnly`: replace Photon only (refused while the
     old Community Modules is in the project, since it would no longer compile). `CmOnly`: update Community Modules
     only, in a project that already has Photon Fusion 2.1 (also the way to finish a project left half-migrated by
     hand).
   - Optional: `-sdkPackage "<Somnium SDK .unitypackage>"` imports the Somnium SDK first when it is too old;
     `-migrationTimeout <minutes>` (default 90).
   - It takes a few minutes (about 2 to 5 on a typical world, longer on a big one or a project opened for the first
     time), mostly Unity importing and compiling. Wait for the process to end.
   - **The Unity log (`-logFile`) shows errors that are expected** and not a failure. With the versions this guide was
     written for, they are: many
     `Error while saving Prefab ... missing script` while Photon is replaced, `NullReferenceException`s from the
     Somnium SDK update window (it cannot open in batch mode; a popup may still appear on screen), Fusion's
     `Asset file already exists at 'Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion'`,
     `PhotonNetwork:LoadOrCreateSettings` errors (when the old PUN was in the project),
     `[Licensing::Module] Error: Access token is unavailable` and `Curl error 6`. **Only the exit code and the result
     file count.**

3. Check the result:
   - **Exit code**: `0` success; `1` a step failed; `2` a prerequisite is missing (Somnium SDK too old, wrong task,
     `CmOnly` without Fusion 2.1, `PhotonOnly` with the old Community Modules): nothing was changed; `3` the steps are
     done but the project has compile errors; `4` timeout; `5` some NetworkTransforms lack the recommended settings.
   - The last line starting with `UNATTENDED RESULT:` in `MigrationBackups/migration_log.txt` says the same in words,
     and `MigrationBackups/unattended_result.txt` holds `exitCode`, `task` and `message`.
   - Success means: 0 compile errors, every NetworkTransform with the recommended settings (section 4), and the Fusion
     prefab table rebuilt (the tool runs **Tools → Fusion → Rebuild Prefab Table** itself). The message also lists
     **missing scripts** still in the project. The migration does not cause them (some worlds already had them), but a
     missing script in a scene breaks the upload. An **unmodified Community Modules file that 4.0 no longer ships**
     and that nothing references (for example `Networked Script Enable Object Example.prefab`) is safe to remove.
     Anything else (the creator's objects, files they changed): tell the user, do not delete it.

4. Finish:
   - Delete the tool (or move it out of the project if your sandbox blocks deleting): the folder
     `Assets/Somnium Migration Tool` **and** its `Assets/Somnium Migration Tool.meta`. Keep `MigrationBackups/` until you
     no longer need the backups. Leftover `.csproj` files of removed assemblies and the run's log files are harmless;
     delete them if you like.
   - Check the compile yourself: open the project once more, this time **without** `-ignorecompilererrors`, and look
     for `error CS` in the log (it recompiles after the tool is deleted):

     ```
     "<Unity.exe>" -batchmode -quit -projectPath "<project>" -logFile "<project>\verify.log"
     ```

     Exit code 0 and no `error CS` line: the project compiles. `Tundra build success` and `CompileScripts:` lines in the
     log show that a compile really ran.
   - If the world's assembly is still named `CommunityModules` (the template name, in
     `Assets/#User/Community Modules/Scripts/*.asmdef`), it has not been renamed yet: give it a unique name before the
     first upload, since assembly names must be unique on the server. The unique name is the creator's choice: ask
     them (for example `CreatorName.WorldName`).
     - In Unity: select the existing `NamespaceHelper` asset in Community Modules' `Scripts` folder (create one with
       **Assets → Create → - Community Modules → NamespaceHelper** only if there is none), then use its buttons.
     - In batch mode:

       ```
       "<Unity.exe>" -batchmode -quit -projectPath "<project>" -executeMethod NamespaceHelperUtility.RenameFromCommandLine -namespace CreatorName.WorldName -logFile "<project>\rename.log"
       ```

       It renames the assembly and the namespace of the Community Modules scripts; its backups are `.txt` files, never
       compiled. Then update `using CommunityModules;` in your own scripts outside that folder, if any.
   - When the project is opened next and the Somnium SDK offers an update, accept it: building a world refuses an
     outdated SDK.
   - Then test the world with two clients (section 7) and upload it again.

To undo: open the project in Unity, menu **Somnium SDK → Fusion 2.1 Migration**, tab **Backups**, and restore the
backups from the newest to the oldest (or put back your full copy). This needs the tool, so undo before deleting it.

## Manual migration

Which manual steps do you need?

- Your world only uses NetworkObject + NetworkTransform: steps 1, 2, 4 and 7.
- You use NetworkRigidbody3D: also step 3.
- You have grabbable network objects: also step 5.
- You have your own network scripts: also step 6.

## 1. Update the SDK

1. Back up your project (commit it or make a copy).
2. If you use NetworkRigidbody3D, do step 3 **first**, while the old Photon is still installed. Once the old Photon is
   deleted, those components become missing scripts and Unity will not let you save prefabs that contain them.
3. Delete the old Photon folders from `Assets/Photon` (`Fusion`, `FusionAddons`, `FusionMenu`, `FusionDemos`,
   `PhotonLibs`, `PhotonRealtime`, `PhotonVoice`, and `PhotonUnityNetworking` / `PhotonChat` if present). This avoids
   leftover 2.0 files. Photon Voice is not installed again: Somnium worlds and Community Modules 4.0 do not use it.
   Keep your settings: copy these files somewhere safe first and put them back at the same path after the import
   (keep their `.meta` files too):
   - `Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion`
   - `Assets/Photon/Fusion/Resources/PhotonAppSettings.asset`
4. **Before importing**, set the scripting defines in **Project Settings → Player → Scripting Define Symbols**
   (Windows / Standalone): remove `FUSION_2_0`, `FUSION_2_0_12` (any `FUSION_2_0_*` with a patch number) and, if
   present, the PUN defines (`PHOTON_UNITY_NETWORKING`, `PUN_2_0_OR_NEWER`, `PUN_2_OR_NEWER`, `PUN_2_19_OR_NEWER`) and
   the Photon Voice defines (`PHOTON_VOICE_*`); add `FUSION_WEAVER`, `FUSION2`, `FUSION_2`, `FUSION_2_1`,
   `FUSION_2_1_3`, `FUSION_2_OR_NEWER`, `FUSION_2_0_OR_NEWER`, `FUSION_2_1_OR_NEWER` and `PHOTON_REALTIME_5_OR_NEWER`.
   Fusion adds them itself, but only after a first successful compile; setting them first avoids errors in code that
   checks them.
5. Import the latest Somnium SDK. It includes Fusion 2.1.3 and Photon Realtime 5 (no Photon Voice).
6. Make sure there is only one NetworkProjectConfig in the project. If a second copy appears under `Assets/Photon/...`,
   delete it.
7. Remove the Fusion Physics addon if you have it (`Assets/Photon/FusionAddons/Physics`, or the `fusion-physics`
   package in the Package Manager): it does not work in Shared Mode (see step 3).
8. In your own assembly definitions: remove the references to `Fusion.Addons.Physics` and to the Photon Voice
   assemblies (`PhotonVoice`, `PhotonVoice.API`, `PhotonVoice.Fusion`...), and replace `PhotonRealtime` with
   `Photon.Realtime`.

If your own scripts use Photon Voice (`Recorder`, `Speaker`, `FusionVoiceClient`...), they no longer compile: remove
them, or the Voice parts of them.

## 2. Update Community Modules

If your world uses Community Modules, import Community Modules 4.0. It is updated for Fusion 2.1 (NetworkTransform
instead of NetworkRigidbody3D, the grab fix of step 5, and the API changes of step 6).

If you renamed the Community Modules assembly or namespace, or edited its scripts, the migration tool keeps those
changes for you. By hand: note your assembly name, namespace and asmdef references before the import, and set them
again after it.

## 3. Replace NetworkRigidbody3D with NetworkTransform

The Physics addon (NetworkRigidbody3D, NetworkRigidbody2D, RunnerSimulatePhysics3D) does not work in Shared Mode in 2.1:
it despawns the object. NetworkTransform now supports Rigidbodies directly.

On each prefab or scene object:

1. Remove NetworkRigidbody3D.
2. Add NetworkTransform to the same GameObject (the one with the Rigidbody), with the settings of step 4.
3. Fix the network prefabs first, then open and save every scene that uses them.

Code before:

```csharp
using Fusion.Addons.Physics;

var nrb = GetComponent<NetworkRigidbody3D>();
nrb.Teleport(position, rotation);
nrb.Rigidbody.linearVelocity = Vector3.zero;
```

Code after:

```csharp
using Fusion;

// Inside the network tick: the OnFixedUpdateNetwork event of a NetworkBridgeEvents / NetworkBridgeData component
var nt = GetComponent<NetworkTransform>();
if (Object.HasStateAuthority)
{
    nt.Teleport(position, rotation);
    GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
}
```

Teleport from the network tick. Called from `Update()`, a coroutine or a UnityEvent, the teleport happens between
ticks and is unreliable: the next tick can overwrite it. Record the request there, and apply it in the tick.

## 4. Recommended NetworkTransform settings

Set these options in prefabs and in scenes, including on the NetworkTransforms you add later. The migration tool
applies them to the whole project with one button (step 6 of the tool).

- **Forecast Physics: off**, on **every** NetworkTransform. With Forecast on, other players see a local physics
  prediction of the object instead of its real path: a grabbed or thrown object looks floaty and drifts away from the
  hand that holds it. With Forecast off, other players see exactly the movement of the player who owns the object.
- **Config Flags: Disable Shared Mode Interpolation**, only on objects grabbed with XR Grab Interactable
  **Movement Type: Kinematic**. Without it, the owner's copy of the object is redrawn between network updates. That
  fights the Kinematic grab, which moves the object outside the network update: the object stutters, for the owner and
  for the other players. Objects with the other movement types (Instantaneous, Velocity Tracking) do not need it.
  In the scene or prefab file, `m_MovementType` is `0` Velocity Tracking, `1` Kinematic, `2` Instantaneous; the name of
  an object can mislead (the Community Modules "Networked Grabable Kinematic Example" is Instantaneous).
  This option only changes what the owner sees. Other players still receive the same updates and still see smooth
  movement.

If your own scripts move a networked object in `Update()` instead of `FixedUpdateNetwork()` and it stutters for its
owner, the same option fixes it.

## 5. Grabbable network objects: keep them at the root of the scene

NetworkTransform syncs the position **relative to the parent**; NetworkRigidbody3D synced it in world space.
XRGrabInteractable removes the parent of an object while it is grabbed. If a grabbable network object sits under a
parent that is moved, rotated or scaled, other players see it with an offset while you hold it.

- With Community Modules 4.0, nothing to do: NetworkGrabbable has a **Detach From Parent** option (on by default) that
  moves the object to the root of the scene when the world loads, keeping its world position.
- With your own grab scripts: keep grabbable network objects at the root of the scene, or move them to the root
  (`transform.SetParent(null, true)`) before the network starts.

## 6. Fix custom network scripts

### Namespaces (Photon Realtime 5)

| Fusion 2.0 | Fusion 2.1 |
|---|---|
| `using ExitGames.Client.Photon;` | `using Photon.Client;` |
| `using Fusion.Photon.Realtime;` | `using Photon.Realtime;` |
| asmdef reference `PhotonRealtime` | `Photon.Realtime` |
| `DebugLevel` | `LogLevel` (Photon.Client) |
| `using Fusion.Addons.Physics;` | remove it (see step 3) |

### Reliable data uses ReadOnlySpan<byte>

Before:

```csharp
public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data)
{
    var bytes = data.Array;
}
```

After:

```csharp
public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data)
{
    var bytes = data.ToArray(); // only if you need to keep the data
}
```

`SendReliableDataToPlayer(player, key, byte[])` still compiles, because `byte[]` converts to `ReadOnlySpan<byte>`.

### State authority calls return bool

Before:

```csharp
Object.RequestStateAuthority();
```

After:

```csharp
if (!Object.RequestStateAuthority())
    Debug.LogWarning("State authority request was not sent");
```

### NetworkObject flags are baked as V2

Fusion 2.1 bakes NetworkObject flags with version `V2`. Scripts that assign a fixed value undo that:

Before:

```csharp
networkObject.Flags = NetworkObjectFlags.V1 | NetworkObjectFlags.AllowStateAuthorityOverride;
```

After (add the flag you need, keep the rest):

```csharp
networkObject.Flags |= NetworkObjectFlags.AllowStateAuthorityOverride;
```

### Behaviour changes to check

- When the owner leaves, objects that are not destroyed now have `StateAuthority == PlayerRef.None`.
- Only objects with **Allow State Authority Override** can be taken over with `RequestStateAuthority()`.
- **Destroy When State Authority Leaves** no longer depends on that flag.
- `PlayerRef.MasterClient` was renamed. Use `Runner.IsSharedModeMasterClient` or `Runner.GetMasterClient()` instead.
- `NetworkObject.SendPriority` is obsolete. Remove it, or use `NetworkObject.SetPriority(player, priority)`.
- Version defines: use `FUSION_2_1_OR_NEWER` instead of `FUSION_2_0_*`.

## 7. Reimport, test and re-upload

Fusion 2.1 uses a new prefab format, but prefabs are re-baked automatically on import.

1. Let Unity finish importing.
2. Run **Tools → Fusion → Rebuild Prefab Table**.
3. Check the Console and fix any errors with steps 3 to 6.

Test with 2 clients (Editor + build, or two builds):

- [ ] No errors in the Console after entering the world
- [ ] Objects appear for both clients and nothing disappears on spawn
- [ ] Movement and physics are smooth for the owner and on the other client (also Kinematic grabs)
- [ ] A grabbed object follows the hand exactly on the other client, with no offset (also for objects that had a parent)
- [ ] Grabbing or ownership transfer works
- [ ] The first player leaves, and objects stay or despawn as expected for the second player

If an object held with XR Grab Interactable **Velocity Tracking** or **Kinematic** still stutters for its owner at
the physics rate (with or without networking), this is XR Interaction Toolkit, not Fusion: put its renderers on a
child without colliders and assign that child to the grab's **Predicted Visuals Transform**. Community Modules 4.0
grab examples are set up this way.

Then re-upload the world.

Questions or something not covered here? Ask on the official Somnium Space Discord and tag **@denevraut**, the reference
person for this migration. Please include your world name and the Console errors.
