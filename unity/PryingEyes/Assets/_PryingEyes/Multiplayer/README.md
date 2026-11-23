# Multiplayer: the first co-op draft (frozen)

This folder is ChatGPT's first draft of the co-op: seats, a FishNet adapter and a probe scene with capsule avatars, written in an isolated
worktree (branch `feature/multiplayer`) and imported as it was in commit `36e0a3a` (12 November 2026). It has not been edited since.
**It is not the co-op the game plays.** That is `unity/PryingEyes/Assets/_PryingEyes/Scripts/Coop/`, described in
[docs/multiplayer/README.md](../../../../../docs/multiplayer/README.md).

**Status: frozen** (task T20 of [the modular plan](../../../../../docs/engineering/modular-roadmap.md#decisions-that-are-the-maintainers)).
Nothing here is extended, and it is not kept up to date with the game beyond compiling. Change a file only when the game forces it: the four
files of the first table, and the calls into the game listed under [What it costs](#what-it-costs-while-it-stays). Removing any of it is the
maintainer's decision; whatever is removed stays in git history.

## What the game uses: the admission of a co-op session

`Scripts/Coop/FishNetCoopNet.cs` builds a `FishNetSeatAuthenticator` when a session starts and gives it `CoopProtocol.ContentRevision`. It
decides who may join: same revision, a free seat, a reply within 15 seconds. It needs three files of `Core`.

| File | Lines | What it does |
| --- | --- | --- |
| `FishNet/FishNetSeatAuthenticator.cs` | 97 | the FishNet `Authenticator`: challenge, reply, time limit (compiled only with `PRYINGEYES_FISHNET`, that is Standalone) |
| `Core/AdmissionGate.cs` | 59 | host side: one reply per challenge, deadline, capacity |
| `Core/SessionAdmission.cs` | 40 | the token: protocol byte, seats, content revision |
| `Core/SessionRoster.cs` | 79 | peers and their seats (`PlayerSeat`), capacity |

`Core/PryingEyes.Multiplayer.Core.asmdef` makes `Core` one engine-free assembly. Move these files together, each with its `.meta`, and run
the co-op scenarios (`tools/coop/run_all.ps1`) after any change. `CoopProtocol.ContentRevision` must stay 1 to 64 characters of
`A-Z a-z 0-9 . _ -`: `SessionAdmission` throws otherwise, when the session starts.

## What only the draft uses

17 C# files (1,695 lines) and five assets. Nothing in `Scripts/`, `Editor/`, the packages, the scenes of the game or `ProjectSettings` names them.

| Group | Files | What |
| --- | --- | --- |
| Pure rules | `Core/SeatCommand.cs`, `Core/SplitLayout.cs` | commands that carry intent only; one viewport per seat |
| Local seats | `LocalSeatInput.cs`, `LocalSeatView.cs` | input and camera of a seat |
| Probe | `FishNet/FishNetSessionLauncher.cs`, `FishNetSeatAvatar.cs`, `FishNetWorldState.cs`, `FishNetTowerObjectives.cs`, `FishNetProceduralFloor.cs`, `FishNetProbeMenu.cs` | host and join, avatars, the shared floor and objectives |
| Smoke tests | `FishNet/FishNetLocalSmoke.cs`, `FishNetNetworkSmoke.cs` | `-mpSmokeLocal`, `-mpSmokeNetwork host` or `client` |
| Floor | `Procedural/NetworkFloorPlan.cs` | the real generator, a SHA-256 digest of a plan, spawn candidates |
| Editor | `FishNet/Editor/*.cs` (4 files) | create the probe (a menu item), build its player, run the smoke test |
| Assets | `GeneratedProbe 1/` | `MultiplayerProbe.unity`, `SeatAvatar.prefab`, `ProceduralWorld.prefab`, `ProbePrefabs.asset`, `AvatarBody.mat` |

## What else names this folder

- `tools/multiplayer-check/`: a lab without the engine (`MultiplayerCheck.csproj`: 287 assertions on `Core`, `NetworkFloorPlan.cs`,
  `SampleWorkspaceGuard.cs`, `PlayerPerks.cs` and `Punti.cs`), a second program (`Packets`), nine compile checks against Unity's libraries and
  FishNet's sources, and `Run-NetworkSmoke.ps1`. **It has not compiled since 15 November**: `PlanAnnex.cs` is missing from the include lists of
  `MultiplayerCheck.csproj` and `unity-compile/UnityModules.csproj`. With that file added to a copy of the first, the 287 assertions pass. The
  FishNet checks need a `tools/multiplayer-check/.dependencies` folder that no script prepares. None of it is in the gate.
- `Assets/DefaultPrefabObjects.asset`, FishNet's list of prefabs, lists `SeatAvatar.prefab` and `ProceduralWorld.prefab`.
- `.gitignore` has a block for the outputs of `tools/multiplayer-check/`.

## What it costs while it stays

- It compiles into the game (`FishNet/` on Standalone only, the rest on every platform) and into the editor. Its three `NetworkBehaviour`
  classes are the only ones in the game.
- Two `[RuntimeInitializeOnLoadMethod]` hooks run at every start of the game and return at once unless the command line has `-mpSmokeLocal`
  or `-mpSmokeNetwork`.
- The editor tools cannot run: `SampleWorkspaceGuard` allows them only in a linked worktree on the branch `feature/multiplayer`, and no such
  branch exists in this repository. The probe scene cannot be regenerated, and `Run-NetworkSmoke.ps1` has no player to run.
- It calls into the game: `PlanGen.Generate` and `PlanGen.UseGrid` (`NetworkFloorPlan.cs`), `FloorBuilder.Build`
  (`FishNetProceduralFloor.cs`), `ProcKit` (the editor tools). A change to those signatures has to keep this folder compiling, or the game
  does not build.

## If the co-op is ever rewritten

Three things in the draft are absent from the game's co-op and worth reading before designing again: a host-side barrier on the floor
(`SessionRoster.BeginFloor` with a SHA-256 digest of the plan, `NetworkFloorPlan.Digest`, that every peer must acknowledge; the game only logs
`FloorDirector.FloorSignature()` and a test compares it); commands that carry intent only (`SeatCommand.Sanitized`, sequence numbers that
survive rollover); several local seats on one connection, which the game does not combine with the network. The game solved the rest another
way: one `CoopPacket` and puppets that each machine builds for itself.
