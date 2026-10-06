# Compatibility with official LabAPI 1.1.7

LabAPIMobile keeps the namespaces, type names and member signatures of official LabAPI 1.1.7 wherever the Carl Mod
server (SL 13.1-13.2 game code) can support them, so most plugins port by recompiling against this `LabApi.dll`.
This page lists what differs. Anything not listed works as in official LabAPI.

The ProjectMER port and its compatibility list are in [projectmer-mobile](https://github.com/Michaelihc/projectmer-mobile).

States used in the tables:

- **supported**: same behaviour as official.
- **adapted**: same purpose, but a type or signature follows the fork (for example `KeycardPermissions` instead of
  `DoorPermissionFlags`), or a member is read-only.
- **approximated**: available, with a behaviour difference described in the notes.
- **absent**: removed because the game feature does not exist in Carl Mod. Nothing is left as a throwing stub.

Every event is raised by a Harmony patch in `src/LabApi/Events/Patches/<Area>/`, applied by `PatchManager` from
`PluginLoader.Initialize()` (189 patch classes; on both known server builds 187 apply and the two damage fallbacks
described under [Carl Mod server builds](#carl-mod-server-builds) stay inactive). A patch checks `<Handler>.Has<Event>`
first and leaves the game method untouched while the event has no subscribers.

## Contents

- [Carl Mod server builds](#carl-mod-server-builds)
- [Core: loader, events, permissions, admin toys](#core-loader-events-permissions-admin-toys)
- [Admin: authentication, moderation, Remote Admin, commands, server wrappers](#admin-authentication-moderation-remote-admin-commands-server-wrappers)
- [Player: connection, damage, roles, movement, player and ragdoll wrappers](#player-connection-damage-roles-movement-player-and-ragdoll-wrappers)
- [Items: firearms](#items-firearms)
- [Items (general): inventory, pickups, searching, usables, throwables, grenades](#items-general-inventory-pickups-searching-usables-throwables-grenades)
- [Facility: doors, rooms, elevators, structures, hazards, SCP-914](#facility-doors-rooms-elevators-structures-hazards-scp-914)
- [Round, respawn, CASSIE, decontamination and warhead](#round-respawn-cassie-decontamination-and-warhead)
- [SCP role events](#scp-role-events)

## Carl Mod server builds

Two Windows server builds report game version 0.0.4. One `LabApi.dll` supports both:

| Build | Recognised by | Game behaviour that differs |
| --- | --- | --- |
| Official server distribution | no `CarlModExtras.dll` in `Carl Mod_Data/Managed` | The standard 13.x behaviour described on this page: disarming needs a held item that allows it (a firearm), and a respawn wave plays its entry animation before it spawns. |
| Build with the deathmatch module | `CarlModExtras.dll` (`CarlModExtras.DmFun`) and deathmatch members in Assembly-CSharp (`PlayerStats.DmCleanup`) | Extra configs: `deathmatch` (direct respawns 5 s after death, no natural round end, kill broadcasts), timed `funmode` loadouts, `auto_cleanup`, `death_no_drop`, `infinite_ammo`, `infinite_stamina`, `lock_gate_ab`. Extra chat: RA `help <text>` and client `.s <text>`; RA `wiki` grants the `wiki_group` group. The RA commands `mtf` and `ci` replace `stare` and `096state`. Any held item allows disarming. A respawn wave spawns as soon as its team is chosen. |

LabAPI-Mobile keeps each build's own behaviour:

- `LabApi.dll` has no reference to `CarlModExtras`. The module's members are looked up once at startup; on a build
  without the module they are simply absent.
- Patches that copy a native body whose code differs between the builds (`PlayerStats.DealDamage` / `KillPlayer`,
  `CommandProcessor.ProcessQuery`, `QueryProcessor.ProcessGameConsoleQuery`, `InventoryItemProvider.ServerGrantLoadout`
  and the `RoundSummary` coroutine) identify the target's IL at startup and run the copy written for that body. The
  respawn team selection events are inserted after whichever call picks the team (`RespawnTokensManager.DominatingTeam`
  or `DmFun.ChooseTeam`).
- The startup log names the build (`[PATCHES] Game build: ...`) and ends with
  `Applied N patch classes in X ms (F failed, S skipped for this server build)`; on both 0.0.4 builds that is 187
  applied, 0 failed, 0 skipped.

Other builds (including Carl Mod 0.0.5) are not tested. On a build whose bodies are not the known ones:

- A patch that copies an unknown body is not applied: the game's own code runs, the events it raised are unavailable,
  and the log names the patch, the method, its IL fingerprint and the missing events (`[PATCHES] <Patch> not applied: ...`).
  This applies to the RA and client console command events, the loadout events and the round ending events.
- The damage events are still raised, around the unchanged game code (`PlayerDamageFallbackPatch` on `DealDamage`,
  `PlayerDeathFallbackPatch` on `KillPlayer`): Hurting runs before the role's damage processing, Hurt after the damage
  (for a lethal hit just before Dying), Dying after the game's own death callbacks (`OnAnyPlayerDied`, which also counts
  the kill), and cancelling Dying skips `KillPlayer`, leaving the player alive at 0 HP. When `KillPlayer` itself cannot
  be patched, Dying and Death are not raised.
- An SCP-2176 without its own `ServerFuseEnd` raises ProjectileExploding from the shared grenade fuse end.

## Core: loader, events, permissions, admin toys

### Loader and infrastructure

| Item | State | Notes |
| --- | --- | --- |
| `PluginLoader.Initialize` | supported | Called from `ServerStatic.Awake` by the installer. Order: LabAPI config, `PatchManager.ApplyAll`, wrapper initializers, LabAPI commands, dependencies, plugins, build info, default permissions provider, `ServerEvents.PluginsEnabled`. Each wrapper initializer is isolated by try/catch so one failing game hook does not stop plugin loading. |
| `PathManager` | adapted | Data folder is `<appdata>/SCP Secret Laboratory/LabAPI-Mobile/` (plugins, dependencies, configs), never the official `LabAPI` folder. `hoster_policy.txt` with `gamedir_for_configs: true` still selects `./AppData`. |
| YAML (`YamlConfigParser`, custom converters) | supported | Built against the fork's YamlDotNet 18. Converters implement the 3-argument `ReadYaml`/`WriteYaml` interface members and keep the official 2-argument overloads. |
| `CommandLoader` | supported | `[CommandHandler]` registration for RA, server console and client console. The server console handler lives on the `GameCore.Console` instance in Carl Mod; it is attached when the console wakes if it does not exist yet. |
| `refreshcommands` (fork-only) | supported | After the game clears and rebuilds `RemoteAdminCommandHandler`/`GameConsoleCommandHandler`, LabAPI and plugin `[CommandHandler]` commands are registered again. Commands registered manually without the attribute are not restored. |
| `EventManager` | supported | Each subscriber runs in its own try/catch as in official. The subscriber array is cached per event args type and rebuilt only when the subscriber set changes, so invoking does not allocate. Errors name the failing subscriber. |
| `Has<Event>` | supported | Generated per event; patches check it before allocating args. |
| Mirror host spawn payloads | adapted | The server is a Mirror host, and its own client deserializes every spawn message it receives into the server's object one frame later (`NetworkClient.OnHostClientSpawn`), with the state of the moment the message was sent. That would revert SyncVars written right after spawning on the server: a toy from `PrimitiveObjectToy.Create(..., networkSpawn: true)` given a colour and `IsStatic = false` in the same frame would be white and static again a frame later (and the next broadcast could send that to clients). `HostSpawnPayloadPatch` (`Events/Patches/Internal/`) re-serializes the current state into the host's message, so host spawn hooks still run, with current values. |
| `DefaultPermissionsProvider` | supported | Group key comes from `config_remoteadmin` `Members` (`PermissionsHandler._members`), like official `Player.PermissionsGroupName`. Unknown or unassigned users use `default`. |
| `Plugin.IsTransparent`, transparent-modded flag | absent | Carl Mod has no transparently-modded server flag. |
| `LabApiProperties` | supported | Also accepts an informational version without a `+commit` suffix. |
| `IScp914ItemProcessor` | adapted | Carl Mod has no `Scp914Result`: `UpgradeItem` returns `ItemBase?`, `UpgradePickup` returns `ItemPickupBase?` (the single result its game processors return). `MoveVector` is computed from the controller's output and intake chambers. |
| `Features/Audio/AudioTransmitter` | absent | Needs speaker toys and `AudioMessage`, which Carl Mod lacks. |
| Event arg interfaces `IObjectiveEvent`, `IInteractableEvent`, `IScp127ItemEvent` | absent | No objectives, interactable toys or SCP-127. |

### Events

| Event | State | Notes |
| --- | --- | --- |
| `ServerEvents.PluginsEnabled` | supported | Raised by `PluginLoader.Initialize`. |
| `PlayerEvents.DamagingShootingTarget` / `DamagedShootingTarget` | supported | Around `ShootingTarget.Damage`; only attacker damage with a live attacker. Cancelling returns `false` and sends no hit feedback. As in official, a changed `DamageHandler` is not fed back. |
| `PlayerEvents.InteractingShootingTarget` / `InteractedShootingTarget` | supported | Around `ShootingTarget.ServerInteract`; only for players with `FacilityManagement`. Cancelling skips every button action. |
| `PlayerEvents.InteractedToy`, `SearchingToy`, `SearchedToy`, `SearchToyAborted` | absent | No `InvisibleInteractableToy`. |

### Admin toy wrappers

Only `PrimitiveObjectToy`, `LightSourceToy` and `ShootingTarget` exist in Carl Mod.

| Wrapper / member | State | Notes |
| --- | --- | --- |
| `AdminToy` cache (`List`, `Get`) | adapted | No `AdminToyBase.OnAdded`/`OnRemoved`: wrappers are added when a toy is network spawned (or created through a wrapper) and removed when it is destroyed (a prefix on `NetworkIdentity.OnDestroy`, which empties the identity's behaviour list as it runs). Unspawned toys instantiated outside the wrappers appear only after `Get`. |
| `AdminToy.Create*` / spawning | adapted | Prefab found in `NetworkClient.prefabs`. New toys start static; the first wrapper `Position`/`Rotation`/`Scale`/`Parent` change after spawn makes them dynamic unless the plugin set `IsStatic` itself. Moving a static toy through `Transform` directly needs `IsStatic = false`. Spawning does not send the pre-spawn SyncVar writes again. |
| `Position`, `Rotation`, `Scale` | adapted | Setters also write the SyncVars (unchanged values are not dirtied), because Carl Mod only syncs dynamic toys each frame. Rotation is sent as a low-precision quaternion. |
| `Parent` | approximated | Server-side only: clients have no toy parenting and receive the world position/rotation and local scale while the toy is dynamic. A parented static toy spawns at its local coordinates. |
| `IsStatic`, `MovementSmoothing`, `SyncInterval`, `Spawn`, `Destroy` | supported | |
| `PrimitiveObjectToy.Type`, `Color` | supported | |
| `PrimitiveObjectToy.Flags` | approximated | No flags SyncVar; the client adds a mesh collider only when a component of the `Scale` SyncVar is positive, and static toys keep the transform of their spawn message. Static toys: positive transform scale, `Scale` SyncVar positive with `Collidable` and negated without it. Dynamic toys render the SyncVar: positive scale with `Collidable`, otherwise an all-negative scale plus a 180° turn about local X, which looks the same for every Unity primitive (including the one-sided Plane and Quad). Without `Visible` the color is sent with zero alpha (the primitive still renders, transparent). Changing `Collidable` on a spawned toy respawns it; switching `IsStatic` re-encodes the transform. A respawned toy spawned with `Visibility.ForceHidden` keeps its observers (plugins that pick observers per player, such as ProjectMER, keep control); other toys go to every ready player, as on a first spawn. |
| `PrimitiveObjectToy.Rotation`, `Scale` | adapted | Return the requested values. Negative (mirrored) scale components are kept exactly: they become a 180° rotation, plus a local X mirror that no primitive shows. The toy's `Transform` holds the encoded rotation and scale, so move such toys through the wrapper. |
| `LightSourceToy.Intensity`, `Range`, `Color` | supported | Values reach the client unchanged, but Carl Mod renders with Unity's built-in pipeline while official SL uses HDRP: 1 to 2 is a normal light on Carl Mod, and intensities tuned for official SL (often 20 to 100) light everything white. |
| `LightSourceToy.ShadowType` | approximated | Only on/off is synced; any value other than `None` renders as `Soft`. |
| `LightSourceToy.ShadowStrength`, `Type`, `Shape`, `SpotAngle`, `InnerSpotAngle` | absent | Not synced by Carl Mod's light toy; the light type and shape are fixed by its prefab. |
| `ShootingTargetToy` (`IsGlobal`, `Create`) | supported | `Create` spawns the first target prefab found, as in official. |
| `CameraToy`, `CapybaraToy`, `InteractableToy`, `SpeakerToy`, `TextToy`, `WaypointToy`, `SpawnableCullingParent` | absent | Toy types not in Carl Mod. |

## Admin: authentication, moderation, Remote Admin, commands, server wrappers

Carl Mod has no central authentication: clients send a device user ID and answer a server challenge. Several
admin handlers are patched with a prefix that runs the fork's own logic with the events inserted (only while the
event has subscribers; otherwise the game method runs untouched). Patches live in `src/LabApi/Events/Patches/Admin/`.

### Events

| Event | State | Notes |
| --- | --- | --- |
| `PlayerEvents.PreAuthenticating` / `PreAuthenticated` | approximated | In `CustomLiteNetLib4MirrorTransport.ProcessConnectionRequest` after the version, challenge and ban checks. `UserId` is the Carl Mod device ID. `Expiration`, `Flags`, `Region` and `Signature` are removed (no central auth tokens). `CanJoin` starts `true` because the fork does not check slots, whitelist or reserved slots at preauth; setting it `false` rejects as server full. `ForceReject` is ignored (the fork's LiteNetLib has no force reject). |
| `PlayerEvents.Kicking` / `Kicked` | supported | `BanPlayer.KickUser(ReferenceHub, ICommandSender, string)`, which every kick uses. |
| `PlayerEvents.Banning` / `Banned` | approximated | `BanPlayer.BanUser(ReferenceHub, ICommandSender, string, long)`. Fires after the fork's checks (positive duration, no staff bypass, valid device ID). `PlayerId` is the device ID. `Banned` fires after the disconnect is issued and only when the ban succeeded. Offline (`Footprint`) bans do not exist in the fork. |
| `ServerEvents.BanIssuing` / `BanIssued` | supported | `BanHandler.IssueBan` when no active ban with the same ID exists. Bans the fork rejects (invalid ID, localhost IP) never raise it. |
| `ServerEvents.BanUpdating` / `BanUpdated` | approximated | `BanHandler.IssueBan` when an active ban with the same ID exists. The fork rewrites the entry in place, so the nested Revoking/Revoked/Issuing/Issued events official SL raises during an update are not raised, except Revoking/Revoked for the old entry when a handler changes `BanType`. |
| `ServerEvents.BanRevoking` / `BanRevoked` | supported | `BanHandler.RemoveBan`. `BanDetails` is `null` when no active ban matches. |
| `PlayerEvents.Muting` / `Muted`, `Unmuting` / `Unmuted` | supported | Per target of the `mute`, `imute`, `unmute`, `iunmute` RA commands. |
| `PlayerEvents.ReportingCheater` / `ReportedCheater`, `ReportingPlayer` / `ReportedPlayer` | absent | Carl Mod has no `CheaterReport` (no player reporting). |
| `PlayerEvents.TogglingNoclip` / `ToggledNoclip` | supported | `FpcNoclipToggleMessage.ProcessMessage`. As in official, `TogglingNoclip` also fires without permission with `IsAllowed` preset to `false`. |
| `PlayerEvents.RequestingRaPlayerList` / `RequestedRaPlayerList`, `RaPlayerListAddingPlayer` / `RaPlayerListAddedPlayer` | approximated | `RaPlayerList.ReceiveData`. The fork list has no muted or hidden-spectator badge: `IsMuted` reports the mute state but is not rendered. `PlayerSorting.Class` sorts by player ID, as in the fork. |
| `PlayerEvents.RequestingRaPlayersInfo` / `RequestedRaPlayersInfo`, `RequestingRaPlayerInfo` / `RequestedRaPlayerInfo` | supported | `RaPlayer.ReceiveData`. Text and clipboard format are the fork's ("User ID / Device ID"); empty clipboard builders are not sent. The sensitive-data permission is checked after the Requesting events, as in official. |
| `PlayerEvents.RequestedCustomRaInfo` | supported | `RaPlayer.ReceiveData` when the selection matches no player (the fork otherwise ignores such requests). |
| `PlayerEvents.ChangingBadgeVisibility` / `ChangedBadgeVisibility` | approximated | The fork's game console `hidetag`/`showtag`/`globaltag` (`CharacterClassManager` commands) and RA `hidetag`/`showtag`. `NewVisibility` is `true` when showing and `false` when hiding (official always passes `false`). |
| `PlayerEvents.ChangingNickname` / `ChangedNickname` | supported | `NicknameSync.DisplayName` setter. |
| `PlayerEvents.GroupChanging` / `GroupChanged` | supported | `ServerRoles.SetGroup`; `GroupChanged` fires on every completed call. |
| `PlayerEvents.UsingIntercom` | supported | `Intercom.CheckPlayer`; cancelling makes the speaker check fail. |
| `PlayerEvents.UsedIntercom` | approximated | When the intercom state goes from in-use to cooldown (state setter, no per-frame hook), so the RA intercom timeout command also raises it. |
| `ServerEvents.CommandExecuting` / `CommandExecuted` | approximated | RA (`CommandProcessor.ProcessQuery`), server console (`GameCore.Console.TypeCommand`) and client console (`QueryProcessor.ProcessGameConsoleQuery`). Not raised for the fork's built-in `ServerConsole` keywords (`FORCESTART`, `STOPNEXTROUND`, `RESTARTNEXTROUND`, `CONFIG`, `IDLE`...), or, on the deathmatch build, its `help <text>`/`wiki` RA queries and `.s` chat command. For client commands a changed `Response` is sent (official sends the original). |
| `ServerEvents.SendingAdminChat` / `SentAdminChat` | approximated | Admin chat is an RA query starting with `@` in the fork. `Message` excludes the leading `@`; the fork appends ` ~<nickname>` after the event and relays it as a broadcast-style RA reply. No rich-text sanitising or length cap is added. |
| `ServerEvents.Shutdown` | supported | `Shutdown.Quit`, once; raised just before `Shutdown.OnQuit` instead of after it. |

### Wrappers

| Wrapper / member | State | Notes |
| --- | --- | --- |
| `Server` (general, bans, kicks, restart, shutdown, broadcasts, limits, command handlers) | supported | `GameConsoleCommandHandler` is the `GameCore.Console.Singleton` instance handler. |
| `Server.ServerListName`, `PlayerListNameRefreshRate` | adapted | Backed by the fork's private `ServerConsole._serverName` and `PlayerList._refreshRate`. |
| `Server.AchievementsEnabled` | absent | No achievement toggle in Carl Mod. |
| `Server.SendAdminChatMessage` | approximated | Sends `@<message>` through `QueryProcessor.TargetReply` to admins with admin chat or global RA access. `isSilent` is ignored: the Carl Mod client always shows admin chat as a broadcast. |
| `Whitelist` | approximated | `WhitelistEnabled` maps to `ServerConsole.WhiteListEnabled` (reset on config reload). The fork never enforces the whitelist on join; plugins can do it from `PreAuthenticating`. |
| `ReservedSlots` | approximated | The fork loads the list but never consults it on join. `HasReservedSlot` only checks the list (no offline-mode bypass). |

## Player: connection, damage, roles, movement, player and ragdoll wrappers

Patches live in `src/LabApi/Events/Patches/Player/` (namespace `LabApi.Events.Patches.Players`, so it does not
shadow the `Player` wrapper inside other patch namespaces) and `src/LabApi/Events/Patches/Internal/PlayerLifecycle.cs`.
Where the fork method has to raise an event mid-body, a prefix replays the Carl Mod body with the events inserted, and
only while one of the events has subscribers; otherwise the game method runs untouched. Per-frame and per-packet paths
(voice, movement state, jump, visibility, room tracking, max-health override) cost a static subscriber check when nobody
listens.

### Events

| Event | State | Notes |
| --- | --- | --- |
| `PlayerEvents.Joined` | approximated | No central authentication: raised when a remote client's first nickname is accepted (`NicknameSync.UserCode_CmdSetNick__String`, the official offline-mode path). Its user ID is the Carl Mod device ID assigned at connection. Not raised for rejected names (null, too long, unprintable), the host or `ServerDummy` dummies (official dummies do not raise it either). |
| `PlayerEvents.Left` | supported | `ReferenceHub.OnDestroy`, every non-host hub, before the wrapper is removed. |
| `PlayerEvents.SendingVoiceMessage` / `ReceivingVoiceMessage` | supported | `VoiceTransceiver.ServerReceiveMessage`. Keeps the fork's mute check (only exact `LocalRegular`/`GlobalRegular` flags block) and its hear-yourself rule. |
| `PlayerEvents.UpdatingEffect` / `UpdatedEffect` | approximated | `StatusEffectBase.ForceIntensity` on the server. `UpdatedEffect` runs after the effect's enable/disable callbacks instead of before them. If a handler sets `Intensity` to the current value, nothing is synced (the fork returns early). |
| `PlayerEvents.Hurting` / `Hurt` / `Dying` / `Death` | supported | `PlayerStats.DealDamage`, same order as official: Hurting before `ApplyDamage`, Hurt after it, Dying before `OnAnyPlayerDied`, Death after `KillPlayer` (ragdoll, item drop, spectator role, and on the deathmatch build its broadcasts). Cancelling Dying leaves the player alive at 0 HP, as in official. Carl Mod has no spawn-protection damage check in `DealDamage`. Official distribution: `DealDamage` is replaced only while a damage event has subscribers, and the replacement calls the game's own `KillPlayer`. Deathmatch build: `DealDamage` is always replaced, also without subscribers: that build's `KillPlayer` branches into the middle of an instruction when the player is not a spectator after `ServerSetRole(Spectator, Died)` and Harmony cannot patch it, so the replacement runs a corrected copy (ragdoll, item drop unless `death_no_drop`, spectator role, console message, the deathmatch broadcasts, `SpectatorRole.ServerSetData` only for a spectator). Unknown builds: see [Carl Mod server builds](#carl-mod-server-builds). |
| `PlayerEvents.ChangingRole` / `ChangedRole` | approximated | `PlayerRoleManager.ServerSetRole`; cancellation and `NewRole`/`ChangeReason`/`SpawnFlags` changes apply as in official, also on death (`ChangeReason.Died`: the player keeps the old role at 0 HP, or gets the new role). Carl Mod sends the role to clients on the next frame, so `ChangedRole` runs before clients receive it. |
| `PlayerEvents.Cuffing` / `Cuffed` / `Uncuffing` / `Uncuffed` | supported | `DisarmingHandlers.ServerProcessDisarmMessage`. An SCP releasing a target raises `Uncuffing` with `IsAllowed = false` and `CanUnDetainAsScp = false` and is always refused; the fork then resends the cuff list to that SCP. |
| `PlayerEvents.ReceivingLoadout` / `ReceivedLoadout` | supported | `InventoryItemProvider.ServerGrantLoadout`. As in official, `InventoryReset` changes are ignored. On the deathmatch build its fun-mode loadout hook still runs for roles with a defined inventory. |
| `PlayerEvents.Spawning` / `Spawned` | supported | The fork positions players from an anonymous `PlayerRoleManager.OnRoleChanged` handler in `RoleSpawnpointManager`. It runs for every role change after the `None` role a player starts with, including the round-start assignment; only roles with a spawnpoint handler raise the events, as in official. |
| `PlayerEvents.Jumped` | approximated | `FpcMotor.UpdateGrounded` when the server simulates a requested jump (SCP-939 lunges included). No jump multiplier: `JumpStrength` is the role's jump speed. There is no server-forced jump. |
| `PlayerEvents.MovementStateChanged` | supported | `FpcSyncData.TryApply` when a client movement message changes the state. |
| `PlayerEvents.Escaping` / `Escaped` | approximated | `Escape.ServerHandlePlayer`, every frame while an FPC player is inside the escape sphere (radius 12.5 around `Escape.WorldPos`); `EscapeZone` is that sphere's bounding box. `EscapeScenarioType.Custom` does not exist. Respawn tokens for the final scenario are granted after `Escaping`, as the fork grants them. |
| `PlayerEvents.SpawningRagdoll` / `SpawnedRagdoll` | supported | `RagdollManager.ServerSpawnRagdoll`. As in official, a changed `DamageHandler` is not fed back. |
| `PlayerEvents.PlacingBlood` / `PlacedBlood` | approximated | `StandardHitregBase.PlaceBloodDecal` (fork firearms send blood as a `GunHitMessage`, human targets only). Changed positions re-aim the decal from `RaycastStart` towards `HitPosition`. |
| `PlayerEvents.ReceivedAchievement` | approximated | `AchievementHandlerBase.ServerAchieve` for remote clients. The fork has no `AllowAchievements` switch. |
| `PlayerEvents.RoomChanged` / `ZoneChanged` | approximated | The fork has no `CurrentRoomPlayerCache`; LabAPI revalidates every player once per frame while either event has subscribers (camera position for camera roles; room grid cell, then a raycast up and down like official `RoomUtils.TryGetRoom`; dead players have no room). When the first subscriber appears, current rooms are recorded without raising events. `ZoneChanged` needs a room on both sides, as in official. |
| `PlayerEvents.ChangedSpectator` | approximated | Server handler of `SpectatedNetIdSyncMessage`. The fork accepts every target (no spectatable check), so the event follows every change. |
| `PlayerEvents.ValidatedVisibility` | supported | `FpcServerPositionDistributor.WriteAll`, for every non-host FPC target. Making a target invisible sends it as hidden, as the fork does for its own visibility rules. |
| `PlayerEvents.EnteringPocketDimension` / `EnteredPocketDimension` | approximated | Carl Mod has no `PocketCorroding`; `Corroding.Enabled` captures the position and moves the player into the pocket dimension. Cancelling leaves the effect active without teleporting, as in official. |
| `PlayerEvents.LeavingPocketDimension` / `LeftPocketDimension` | approximated | `PocketDimensionTeleport.OnTriggerEnter`. A successful exit applies the fork's exit (Disabled 10 s, removes Corroding, LarryFriend achievement, pocket regeneration); there is no `Traumatized` effect on exit. |
| `ScpEvents.HumeShieldBroken` | supported | `DynamicHumeShieldController.OnHsValueChanged` when the shield drops to 0 on a controller with a break sound. |

### Player wrapper

| Member | State | Notes |
| --- | --- | --- |
| `List`, `Dictionary`, `Get`/`TryGet` (hub, GameObject, NetworkIdentity, net ID, command sender) | supported | Dictionary lookups. A command sender resolves through `PlayerCommandSender.ReferenceHub` (official compares user IDs; Carl Mod device IDs are client-supplied and not unique); other senders have no player. As official, `List` includes the host; use `ReadyList`/`GetAll`/`Count` for real players and dummies. |
| `TryGet(int playerId)`, `TryGet(string userId)` | supported | Player ID through the game's ID dictionary; user ID through a cache with a non-allocating fallback scan. |
| `Count`, `NonVerifiedCount` | supported | Counted without allocating. |
| `ConnectionsCount` | supported | `LiteNetLib4MirrorCore.Host.PeersCount`. |
| `ValidateCustomInfo` | approximated | Checks `Misc.PlayerCustomInfoRegex` only: the Carl Mod client escapes all rich text in custom info. |
| `IsDummy`, `DummyList` | approximated | Dummies are `ServerDummy.Spawn` hubs (`ServerDummyConnection`). |
| `UserId`, `IsReady`, `DoNotTrack`, `IsGlobalModerator`, `IsNorthwoodStaff` | adapted | From `CharacterClassManager` (device user ID, instance mode) and `ServerRoles` (`DoNotTrack`, `RaEverywhere`, `Staff`). |
| `LifeId` | approximated | No role life identifier in the fork; LabAPI assigns a new increasing value on every role initialization. |
| `MaxHealth` | approximated | Setting it overrides `HealthStat.MaxValue` on the server until the next role change; the override is gone before the new role's health is set. The client still sizes its health bar to the role's default maximum. |
| `MaxHumeShield` | adapted | Read-only: the fork derives it from the role's shield-over-health curve. |
| `ArtificialHealth`, `MaxArtificialHealth` | supported | Clears the AHP processes / sets the AHP maximum directly. |
| `Position`, `Move` | supported | Server position override. |
| `Rotation`, `LookRotation`, `Rotate` | approximated | The fork's override message carries only a horizontal delta: yaw is applied, pitch is ignored. |
| `CachedRoom` | approximated | Resolved on each call with the same lookup as `RoomChanged` (grid cell, then a raycast up and down). |
| `GetRoleVisibilityFor` | approximated | Applies `IObfuscatedRole` only; the fork has no distance or visibility based role masking. |
| `AddItem(ItemType)`, `GiveCandy(CandyKindID)`, `GiveRandomCandy()` | adapted | No `ItemAddReason` in the fork, so the reason parameter is absent. Candy follows the fork's RA candy command (adds a bag when missing). |
| `DropAmmo` | supported | Runs the fork's ammo drop with `DroppingAmmo` / `DroppedAmmo`, like a game drop, and returns the spawned pickups (the fork method returns only a bool). |
| `Gravity`, `Scale`, `Jump`, `Emotion`, `IsSpectatable` | absent | No per-player gravity, player scale, forced jump, emotions or spectatable-visibility manager in the fork. |

### Ragdoll wrapper

| Member | State | Notes |
| --- | --- | --- |
| `SpawnRagdoll(RoleTypeId, Vector3, Quaternion, DamageHandlerBase, string, ReferenceHub?)` | adapted | No ragdoll scale or serial in the fork's `RagdollData`, so those parameters are absent. Spawns the role's ragdoll prefab like the fork does. |
| `Role`, `Nickname`, `DamageHandler` | supported | Rewrites the synchronized `RagdollData`. |
| `Position`, `Rotation` | approximated | Clients read the pose only when the ragdoll spawns, so setting either respawns the ragdoll for observers (same server object). |
| `IsConsumed`, `IsRevivableBy`, `Destroy` | supported | |
| `Scale`, `Serial`, `Freeze`, `UnFreeze` | absent | No ragdoll scale/serial sync and no freeze RPC in the fork. |

## Items: firearms

Carl Mod uses the pre-14.0 firearm system: one `FirearmStatus` per firearm (a single ammo count that includes
chambered rounds, flags `Cocked`/`Chambered`/`MagazineInserted`/`FlashlightEnabled`, and the attachments code),
fixed modules (`IAmmoManagerModule`, `IActionModule`, `IAdsModule`, `IHitregModule`) and one request handler
(`FirearmBasicMessagesHandler`). Patches live in `src/LabApi/Events/Patches/ItemsFirearms/`.

### Events

| Event | State | Notes |
| --- | --- | --- |
| `ShootingWeapon` / `ShotWeapon` | approximated | Around `FirearmBasicMessagesHandler.ServerShotReceived`, once per shot request (a shotgun request covers all barrels and pellets). `ShootingWeapon` fires only when a side-effect-free copy of the action module's checks passes (ammo, cocked/chambered, modules ready, trigger/pump timing); the automatic fire-rate check still runs after it. Cancelling skips the shot and corrects the owner's prediction (`RefusedShotMessage` for automatics, shotgun resync). `ShotWeapon` fires after hit registration when ammo was consumed. The disruptor fires at the end of its charge-up (fork sends the shot then), not at trigger press. |
| `DryFiringWeapon` / `DryFiredWeapon` | supported | `RequestType.Dryfire`, only when the action module would authorize the dry fire. Cancelling sends nothing (shotgun is resynced). |
| `ReloadingWeapon` / `UnloadingWeapon` | approximated | `RequestType.Reload`/`Unload`, cancellable. `IsAllowed` starts `true`: the fork validates inside `ServerTryReload`, so requests the fork then rejects still raise the event and plugins cannot force a rejected reload. Not raised for the disruptor (its reload request is a sound cue). |
| `ReloadedWeapon` / `UnloadedWeapon` | approximated | Raised when the ammo manager returns to standby after an approved reload/unload (any source: request, auto-chamber on equip, `FirearmItem.Reload()`), or when the firearm is holstered mid-action. Shotgun: after the 0.5 s post-action cooldown. |
| `AimedWeapon` | supported | `RequestType.AdsIn`/`AdsOut`, after `ServerAds` is set. |
| `AimingWeapon` | absent | Commented out in official LabAPI too. |
| `TogglingWeaponFlashlight` / `ToggledWeaponFlashlight` | supported | `RequestType.ToggleFlashlight` on firearms with a flashlight attachment; `NewState` is applied. No toggle cooldown (fork has none). |
| `ChangingAttachments` / `ChangedAttachments` | supported | `AttachmentsServerHandler.ServerReceiveChangeRequest`, after the workstation/spectator gate. `NewAttachments` replaces the requested code. |
| `SendingAttachmentsPrefs` / `SentAttachmentsPrefs` | supported | `AttachmentsServerHandler.ServerReceivePreference`. `NewAttachments` is stored; `FirearmType` changes are ignored, as in official. |
| `PlacingBulletHole` / `PlacedBulletHole` | approximated | `StandardHitregBase.PlaceBulletholeDecal`, per impact. The fork sends raycast start and direction and the client finds the surface, so a changed `HitPosition` becomes the direction from `RaycastStart`. `DecalType` (`Knife.DeferredDecals.DecalPoolType`) is informational: `Buckshot` for shotgun pellets, otherwise `Bullet`. |
| `SendingHitmarker` / `SentHitmarker` | approximated | `Hitmarker.SendHitmarker(ReferenceHub, float)`, every remote hitmarker (all sources, not only firearms). The fork message has only a size: `PlayAudio`, `PlayedAudio` and `Hitmarker` (`HitmarkerType`) are absent. `Size` and `Player` can be changed. |
| `CheckedHitmarker` | absent | No hitmarker permission check (`IHitmarkerPreventer`) in the fork. |
| `SpinningRevolver` / `SpinnedRevolver` | absent | No revolver roulette. |
| `ToggledDisruptorFiringMode` | absent | No disruptor firing-mode selector. |

### Wrappers

| Wrapper / member | State | Notes |
| --- | --- | --- |
| `FirearmItem` (cache, `Get`, `List`, `Base`, weight/length, `AmmoType`, `Firerate`, `Attachments`, `ActiveAttachments`, `AvailableAttachmentsNames`, attachment code validation) | supported | `Firerate` is the action module's `CyclicRate`. `BaseWeight` returns the base weight (official returns the base length). |
| `FirearmItem.StoredAmmo` / `ChamberedAmmo` / `MaxAmmo` | adapted | Views over the single ammo count: stored = total minus chambered; `MaxAmmo` is the container capacity (fork max minus chamber). Setting `ChamberedAmmo` on an automatic adds/removes the chambered round and the `Chambered` flag. Changes resync through `FirearmStatus`. |
| `FirearmItem.ChamberMax` | adapted | Read-only: chamber size is fixed when modules are created. |
| `FirearmItem.Cocked`, `MagazineInserted`, `FlashlightEnabled`, `AttachmentsCode` | supported | Status flags / attachments code. Removing the magazine returns its rounds to the owner's inventory. Shotguns have no magazine. |
| `FirearmItem.OpenBolt` | approximated | `true` for automatics with chamber size 0. |
| `FirearmItem.IsReloading` / `IsUnloading` / `IsReloadingOrUnloading` | approximated | Tracked from approved reload/unload starts until the ammo manager is idle or the firearm is holstered. |
| `FirearmItem.CanReload` / `CanUnload` | approximated | Copies of the fork ammo managers' server checks. |
| `FirearmItem.Reload()` / `Unload()` | adapted | Only while equipped (fork reloads run on the equipped firearm's animator); clients are notified with a `RequestMessage`. |
| `FirearmItem.BoltLocked`, `FirearmItem.Modules` | absent | No separate bolt-lock state; no 14.x `ModuleBase` modules. |
| `RevolverFirearm` `Cocked`, `StoredAmmo` | supported | `Cocked` is the double-action hammer. |
| `RevolverFirearm.ChamberedAmmo` | adapted | Read-only: 1 while any round is loaded. |
| `RevolverFirearm.Chambers`, `SetChamberStatus`, `Rotate`, `TrySpin` | absent | No per-chamber cylinder state or roulette. |
| `ShotgunFirearm` `Cocked`, `CockedChambers`, `ChamberedAmmo` | supported | Pump-action hammers and barrels; the owner is resynced. `StoredAmmo` is the tube. |
| `ShotgunFirearm.ChamberMax` | adapted | Read-only barrel count. |
| `ShotgunFirearm.Pump(int)` | approximated | Runs the fork pump (live barrel rounds return to inventory) after `shotsFired * 0.5` s; no client pump animation. |
| `ParticleDisruptorItem` (cache, `Get`, `OpenBolt`) | supported | All remaining shots are `StoredAmmo`; no separate chamber. |
| `ParticleDisruptorItem.Destroy()` | approximated | Removes the item from the owner's inventory without a destroy animation. |
| `ParticleDisruptorItem.FiringState`, `SingleShotMode` | absent | No 14.x disruptor action states or firing-mode selector. |
| `Scp127Firearm` | absent | No SCP-127. |
| `FirearmPickup.AttachmentCode` | supported | Reads/writes the pickup's `FirearmStatus` SyncVar (only dirtied when changed). |

## Items (general): inventory, pickups, searching, usables, throwables, grenades

Carl Mod runs the pre-14.0 inventory: no `ItemAddReason`, no `AllowDropping`, no keycard inspection or custom
keycards, the old Micro-HID and jailbird, the flashlight as the only toggleable light, and grenades whose fuse end
lives in `EffectGrenade`. Event patches live in `src/LabApi/Events/Patches/ItemsGeneral/`. Unless noted, each patch
is a prefix that runs the fork's own method body with the official event points while the event has subscribers,
and leaves the game method untouched otherwise.

### Events

| Event | State | Notes |
| --- | --- | --- |
| `PlayerEvents.ChangingItem` / `ChangedItem` | supported | `Inventory.ServerSelectItem`, after the holster/equip checks. |
| `PlayerEvents.DroppingItem` / `DroppedItem` | supported | `Inventory.UserCode_CmdDropItem__UInt16__Boolean`. The fork allows dropping any item that can be holstered. `Throw` changes are applied. |
| `PlayerEvents.ThrowingItem` / `ThrewItem` | supported | Same method. A denied throw puts the item back into the inventory and destroys the pickup, as in official. |
| `PlayerEvents.DroppingAmmo` / `DroppedAmmo` | supported | `InventoryExtensions.ServerDropAmmo`. As in official, `Type`/`Amount` changes are ignored. `DroppedAmmo` fires per pickup after it is spawned with its amount. |
| `PlayerEvents.SearchingPickup` | approximated | `SearchCoordinator.ReceiveRequestUnsafe` prefix, before the fork validates the request (official: `PickupSearchCompletor.ValidateStart`). Denying rejects the request like a failed validation. |
| `PlayerEvents.SearchingAmmo` / `SearchingArmor` | approximated | Postfix of the same method, once the ammo/armor search passed validation (official: inside the completor's `ValidateStart`). Denying discards the session. |
| `PlayerEvents.SearchedAmmo` / `SearchedArmor` | supported | Declared but never raised, as in official SL 14.2.7. |
| `PlayerEvents.SearchedPickup` | supported | First thing in every completor's `Complete` (item, ammo, armor, SCP-244, SCP-330). |
| `PlayerEvents.PickingUpItem` / `PickedUpItem` | supported | `ItemSearchCompletor.Complete` and `Scp244SearchCompletor.Complete`. A denied pickup is released (`InUse` cleared). |
| `PlayerEvents.PickingUpAmmo` / `PickedUpAmmo` | supported | `AmmoSearchCompletor.Complete`; `AmmoAmount` changes are applied. |
| `PlayerEvents.PickingUpArmor` / `PickedUpArmor` | supported | `ArmorSearchCompletor.Complete`. |
| `PlayerEvents.PickingUpScp330` / `PickedUpScp330` | supported | `Scp330SearchCompletor.Complete`. |
| `PlayerEvents.ThrowingProjectile` / `ThrewProjectile` | approximated | `ThrowableItem.ServerProcessThrowConfirmation`; `ProjectileSettings`/`FullForce` changes are applied. The fork has no throw cancellation message: a denied throw resets the server throw state, plays the cancel cue for other players and holsters the item (it stays in the inventory). A confirmation for an item that was already thrown raises nothing, so a denial never undoes a completed throw. |
| `PlayerEvents.InspectingItem` / `InspectedItem` | approximated | Jailbird (`JailbirdItem.ServerProcessCmd`) and firearms (`FirearmBasicMessagesHandler.ServerRequestReceived`, `Inspect` request). Firearm inspection is client-side in the fork: denying only stops the relay to spectators. The fork Micro-HID has no inspect. |
| `PlayerEvents.InspectingKeycard` / `InspectedKeycard` | absent | Carl Mod keycards cannot be inspected. |
| `PlayerEvents.UsingItem` | supported | `UsableItemsController.ServerReceivedStatus` (start request) and `Scp330NetworkHandler.ServerSelectMessageReceived` (candy selection). |
| `PlayerEvents.ItemUsageEffectsApplying` / `UsedItem` | supported | `UsableItemsController.Update`; `ContinueProcess` is honoured. |
| `PlayerEvents.CancellingUsingItem` / `CancelledUsingItem` | supported | `UsableItemsController.ServerReceivedStatus` (cancel request). |
| `PlayerEvents.TogglingRadio` / `ToggledRadio` | supported | `RadioItem.ServerProcessCmd`. |
| `PlayerEvents.ChangingRadioRange` / `ChangedRadioRange` | supported | `RadioItem.ServerProcessCmd`, also for the fork's mobile `IncreaseRange`/`DecreaseRange` commands when the range changes. `Range` changes are applied. |
| `PlayerEvents.UsingRadio` / `UsedRadio` | supported | `RadioItem.Update` (per frame while enabled); uses the fork's drain formula. `Drain` changes are applied. |
| `PlayerEvents.ProcessingJailbirdMessage` / `ProcessedJailbirdMessage` | approximated | `JailbirdItem.ServerProcessCmd`. The fork has no interaction blockers, so `AllowAttack`/`AllowInspect` start `true`. |
| `PlayerEvents.TogglingFlashlight` / `ToggledFlashlight` | supported | `FlashlightNetworkHandler.ServerProcessMessage`. |
| `PlayerEvents.FlippingCoin` / `FlippedCoin` | supported | `Coin.ServerProcessCmd`. The fork coin has no interaction blockers. |
| `PlayerEvents.ProcessingScp1509Message` / `ProcessedScp1509Message`, `Scp1509Resurrecting` / `Scp1509Resurrected` | absent | No SCP-1509. |
| `PlayerEvents.DetectedByScp1344` | absent | No SCP-1344. |
| `ServerEvents.PickupCreated` / `PickupDestroyed` | supported | Raised by the `Pickup` wrapper from `ItemPickupBase.OnPickupAdded`/`OnPickupDestroyed` (pickup `Start`/`OnDestroy`). |
| `ServerEvents.ItemSpawning` / `ItemSpawned` | approximated | `ItemDistributor.CreatePickup`. Raised before the pickup is instantiated (official instantiates first): a denied spawn creates nothing and a changed `ItemType` is spawned instead. |
| `ServerEvents.ProjectileExploding` | supported | `EffectGrenade.ServerFuseEnd` (explosive, flash, SCP-018) and `Scp2176Projectile.ServerFuseEnd` before it shatters (on a build whose SCP-2176 has no own `ServerFuseEnd`, from `EffectGrenade.ServerFuseEnd`). `Player`/`Position` changes are applied. |
| `ServerEvents.ProjectileExploded` | supported | Postfix of `EffectGrenade.ServerFuseEnd`, after the explosion (for SCP-2176 after it shattered). |
| `ServerEvents.ExplosionSpawning` / `ExplosionSpawned` | approximated | `ExplosionGrenade.Explode` (grenades, SCP-018, disruptor, jailbird, pink candy). The `ExplosionType` argument is removed: the fork's explosions carry no type. `Position`, `Settings`, `Player` and `DestroyDoors` changes are applied. |

### Wrappers

| Wrapper / member | State | Notes |
| --- | --- | --- |
| `Item` (`Get`, `TryGet`, `Dictionary`, `List`, `GetAll`) | supported | Tracked through `InventoryExtensions.OnItemAdded`/`OnItemRemoved` (the fork has no `ItemBase.OnItemAdded`/`OnItemRemoved`) plus `ReferenceHub.OnPlayerRemoved` for items destroyed with their player. Wrapper factories resolve fork subclasses through their base types (e.g. `AutomaticFirearm` to `Firearm`). |
| `Item.AddReason` | absent | No `ItemAddReason` in the fork. |
| `Item.CanEquip` / `CanHolster` | adapted | `IEquipDequipModifier` checks (`CanEquip()`/`CanHolster()`). |
| `Item.CanDrop` | approximated | Same as `CanHolster`: the fork has no separate drop restriction. |
| `Pickup` (`Get`, `Create`, `Dictionary`, `SerialCache`, ...) | supported | `Weight` maps to `PickupSyncInfo.Weight`. `Position`/`Rotation` move the transform and resync `PickupSyncInfo`. |
| `Pickup.PhysicsModule` | adapted | Typed `IPickupPhysicsModule`. `Rigidbody` is always available. |
| `Pickup.PickupStandardPhysics` | absent | No `PickupStandardPhysics` in the fork. |
| `KeycardItem.Permissions` | adapted | Fork `KeycardPermissions` flags. |
| `KeycardItem.Levels`, custom keycard factories (`CreateCustomKeycard*`, `CreateCustomCard`) | absent | No keycard levels or customizable keycards. |
| `LightItem`, `FlashlightItem` | adapted | `LightItem` wraps the fork's `InventorySystem.Items.Flashlight.FlashlightItem`, the only toggleable light. |
| `LanternItem` | absent | No lantern. |
| `MicroHIDItem` | approximated | `Energy`, `WindUpProgress` (`Readiness`), `PhaseElapsed`, `IsPrimaryHeld` (fire input), `IsSecondaryHeld` (prime input). `Phase` is typed `HidState`. `BaseEnergyManager`, `BaseInputSyncModule`, `BaseBrokenSyncModule`, `BaseCycleController`, `IsBroken`, `FiringMode` and `TryGetSoundEmissionRange` are absent (no module system, broken state, firing modes or sound emission). |
| `MicroHIDPickup` | approximated | Only `Energy`; the fork pickup stores no cycle state (`BaseCycleController`, `Phase`, `FiringMode`, `WindUpProgress`, `PhaseElapsed` absent). |
| `JailbirdItem` | approximated | `TotalChargesPerformed`, `IsCharging`, `Reset()` (clears the server counters and broken flag; a client already shown the depleted/broken alert keeps it). `WearState` is absent (no wear states). |
| `JailbirdPickup` | approximated | `TotalDamageDealt`, `TotalChargesPerformed`. `WearState` is absent. |
| `UsableItem.IsUsing` setter | adapted | Feeds a status request through `UsableItemsController.ServerReceivedStatus` (no `ServerEmulateMessage` in the fork). |
| `UsableItem.TryGetSoundEmissionRange` | absent | No item sound emission in the fork. |
| `Scp1576Item.TransmitterList` | adapted | `Scp1576Item.ValidatedTransmitters` (no SCP-1576 status effect). |
| `TimedGrenadeProjectile.RemainingTime` | adapted | Seconds until `_fuseDeadline`; setting it moves the synced fuse deadline. |
| `Scp2176Projectile.LockdownDuration` | approximated | Read-only: the fork compiles the duration as a constant. |
| `Scp018Projectile.PlayBounceSound` | adapted | Fork `RpcMakeSound`. |
| `ExplosiveGrenadeProjectile`, `FlashbangProjectile` settings | adapted | Backed by the fork's private serialized fields. |
| `AmmoItem`, `BodyArmorItem`, `CoinItem`, `RadioItem`, `ThrowableItem`, consumables, `Scp244Item`, `Scp268Item`, `Scp330Item`, `Scp1853Item` and their pickups | supported | |
| `Scp1344Item`, `Scp1509Item`, `Scp1509Pickup`, `SnowballItem`, `SnowballProjectile`, `MarshmallowItem`, `AntiScp207Item`, `Scp021JItem`, `Scp2536Projectile`, `FlybyDetectorProjectile`, `SingleTrajectoryProjectile` | absent | The items do not exist in the fork's `ItemType`. |

## Facility: doors, rooms, elevators, structures, hazards, SCP-914

Carl Mod uses pre-14.0 map generation (`RoomIdentifier` with `RoomName`/`RoomShape`/`FacilityZone`), `KeycardPermissions`
instead of `DoorPermissionFlags`, `FlickerableLightController` instead of `RoomLightController`, local elevator
chambers driven by `ElevatorManager` sync messages, and the old SCP-914 processors. Event patches live in
`src/LabApi/Events/Patches/Facility/`; wrapper cache hooks in `src/LabApi/Events/Patches/Internal/Facility*.cs`.

Most interaction events use a prefix that runs the fork's own method body with the events inserted, only while one of
the events has subscribers; otherwise the game method runs untouched. Sequence events (checkpoint, elevator) and
`DoorLockChanged` are transpilers that redirect the fork's field stores or insert one call, so they cost nothing per frame.

### Events

| Event | State | Notes |
| --- | --- | --- |
| `PlayerEvents.InteractingDoor` / `InteractedDoor` | supported | `DoorVariant.ServerInteract`. A locked door first raises `InteractingDoor` with `CanOpen = false` (official `TryResolveLock`); allowing it with `CanOpen = true` bypasses the lock. SCP-079 always passes the permission check, as in the fork. |
| `PlayerEvents.InteractingElevator` / `InteractedElevator` | approximated | `ElevatorManager.ServerReceiveMessage` (clients request a floor by message). Fires for a player in range of a panel of the requested chamber; `IsAllowed` starts as "chamber ready and unlocked (or bypass)". The requested floor is the client's, not always the next one. An allowed request on a moving chamber is forced. |
| `PlayerEvents.InteractingGenerator` / `InteractedGenerator` and the `Opening`, `Opened`, `Closing`, `Closed`, `Unlocking`, `Unlocked`, `Activating`, `Activated`, `Deactivating`, `Deactivated` `Generator` events | supported | `Scp079Generator.ServerInteract`. Fork rules kept: no denied cooldown, humans or an active lever operate the switch. `PlayDeniedAnimation` sends the fork's flagless denied RPC. |
| `PlayerEvents.InteractingLocker` / `InteractedLocker` | supported | `Locker.ServerInteract`. The denied RPC carries no permission flags. |
| `PlayerEvents.InteractingScp330` / `InteractedScp330` | approximated | `Scp330Interobject.ServerInteract`. Fork rules: humans only, 0.1 s cooldown, hands severed on the third candy in one life. A full bag shows the fork's overload hint and raises nothing (official checks `CanAddCandy` first too). |
| `PlayerEvents.DamagingWindow` / `DamagedWindow` | supported | `BreakableWindow.Damage` with an attacker damage handler. |
| `PlayerEvents.TriggeringTesla` / `TriggeredTesla`, `IdlingTesla` / `IdledTesla` | supported | `TeslaGateController.FixedUpdate` (fork loop with the events). |
| `PlayerEvents.EnteringHazard` / `EnteredHazard`, `LeavingHazard` / `LeftHazard` | approximated | `EnvironmentalHazard.OnEnter/OnExit` and the sinkhole/tantrum overrides. Fork amnestic clouds have no `OnEnter` filter, so the events also fire for SCPs and every FPC role entering a cloud. |
| `PlayerEvents.StayingInHazard` | supported | `EnvironmentalHazard.UpdateTargets`; the fork method runs untouched unless the event has subscribers. |
| `PlayerEvents.UnlockingWarheadButton` / `UnlockedWarheadButton` | approximated | `PlayerInteract.CmdSwitchAWButton` (the surface button cover). `IsAllowed` starts as the keycard/bypass check; there is no denied RPC or cooldown in the fork. |
| `PlayerEvents.InteractingWarheadLever` / `InteractedWarheadLever` | supported | `PlayerInteract.CmdUsePanel` lever operation. |
| `ServerEvents.GeneratorActivating` / `GeneratorActivated` | supported | `Scp079Generator.ServerUpdate` on the frame the countdown completes; cancelling keeps the generator waiting, as in official. |
| `ServerEvents.ElevatorSequenceChanged` | supported | Every `ElevatorChamber._curSequence` change. `ElevatorSequence` is the fork's enum (`DoorClosing`, `MovingAway`, `Arriving`, `DoorOpening`, `Ready`). |
| `ServerEvents.BlastDoorChanging` / `BlastDoorChanged` | supported | `BlastDoor.SetClosed` (warhead detonation). `NewState` is "open" as in official. Hook replays that do not change the state raise nothing. |
| `ServerEvents.RoomLightChanged` | approximated | `FlickerableLightController.SetLights` when `LightsEnabled` changes (flicker start/end, wrapper). |
| `ServerEvents.RoomColorChanged` | approximated | `FlickerableLightController.WarheadLightColor`/`WarheadLightOverride` setters; reports the effective override color (`Color.clear` when the override is off). |
| `ServerEvents.DoorLockChanged` | supported | `DoorVariant.Update` right after `LockChanged`, like official. |
| `ServerEvents.DoorDamaging` / `DoorDamaged` | supported | `BreakableDoor.ServerDamage`. |
| `ServerEvents.DoorRepairing` / `DoorRepaired` | absent | Carl Mod breakable doors cannot be repaired. |
| `ServerEvents.CheckpointDoorSequenceChanging` / `CheckpointDoorSequenceChanged` | approximated | Every `CheckpointDoor._currentSequence` store in `UpdateSequence`. Uses the fork enum `CheckpointDoor.CheckpointSequenceStage` (`Idle`, `Granted`, `Open`, `Closing`). Cancelling skips only the store, so the fork repeats that transition's side effects next frame. |
| `ServerEvents.MapGenerating` | approximated | `SeedSynchronizer.Start`. `Seed` can be changed; `IsAllowed = false` keeps the original seed because Carl Mod clients generate the facility themselves. |
| `ServerEvents.MapGenerated` | supported | Handler on `SeedSynchronizer.OnMapGenerated`, after rooms and doors are registered. |
| `Scp914Events.KnobChanging` / `KnobChanged`, `Activating` / `Activated` | supported | `Scp914Controller.ServerInteract`. |
| `Scp914Events.ProcessingPlayer` / `ProcessedPlayer`, `ProcessingInventoryItem` / `ProcessedInventoryItem` | supported | `Scp914Upgrader.ProcessPlayer`. The fork's own `OnPlayerProcess`/item hooks still run. A kept item (fork processors return `null`) is reported as the result. |
| `Scp914Events.ProcessingPickup` / `ProcessedPickup` | supported | `Scp914Upgrader.ProcessPickup`. `NewPosition` is passed to the fork processor, so changing it moves the output. |

### Wrappers

| Wrapper / member | State | Notes |
| --- | --- | --- |
| `Door`, `Gate`, `Timed173Gate`, `ElevatorDoor`, `NonInteractableDoor`, `DummyDoor`, `BulkheadDoor` | supported | Cache filled by `DoorVariant.Start/OnDestroy` patches. `Permissions` is `KeycardPermissions`. `NameTag` comes from `DoorNametagExtension`. `ElevatorDoor.Group` is `ElevatorManager.ElevatorGroup`. |
| `DoorName` | adapted | Official values keep their numbers. Carl Mod name tags with no official equivalent are appended: `HczHidLeft` (`HID_LEFT`), `HczHidRight` (`HID_RIGHT`), `HczCheckpointB` (`CHECKPOINT_EZ_HCZ_B`), `HczServersBottom` (`SERVERS_BOTTOM`). `HID` maps to `HczHidChamber`. |
| `Gate.Is106Passable`, `BreakableDoor.Is106Passable` | adapted | Read-only (always passable in the fork). `NonInteractableDoor.Is106Passable` stays settable. |
| `BreakableDoor.IsBroken`, `CheckpointDoor.IsBroken` | adapted | Setting `true` breaks; `false` does nothing (no repair). |
| `BreakableDoor.TryRepair` | absent | No door repair in Carl Mod. |
| `BulkheadDoor.Crusher`, `DoorCrusher` | absent | No `DoorCrusherExtension`. |
| `CheckpointDoor` | approximated | `SequenceState` uses `CheckpointSequenceStage` and raises the sequence events; `OpenTime`/`WarningTime` map to the fork timers; `MaxHealth`/`Health` average the breakable sub doors. `SequenceController` and `PlayWarningSound` are absent (no controller, warning sound is client-side). |
| `Room` | supported | `RoomIdentifier.OnAdded/OnRemoved`. `TryGetRoomAtPosition` uses the coordinate cache (`RoomIdUtils.RoomAtPositionRaycasts(pos, false)`): no allocation, raycasts only when the cell is empty. |
| `Room.ConnectedRooms`, `AdjacentRooms` | approximated | Rooms that share a door with this room (Carl Mod stores no connections), plus elevator floors. |
| `Room.LightController`, `AllLightControllers`, `GetClosestLightController` | adapted | From `FlickerableLightController.Instances` whose room is this room. |
| `LightsController` | adapted | Base is `FlickerableLightController`. `OverrideLightsColor` is the warhead light color while its override is on; `Color.clear` turns the override off. `Room` is `null` before map generation. |
| `Camera` | supported | Cache from `Scp079Camera.Awake` / `Scp079InteractableBase.OnDestroy` patches. |
| `Elevator` | approximated | Destinations go through `ElevatorManager.TrySetDestination`; `NextDestination(Level)` wraps like the in-game panel; `LockAllDoors` uses `ServerChangeLock(AdminCommand)`; `CurrentSequence` is the fork enum. `WorldSpaceRelativeBounds` and `DynamicAdminLock` are absent. |
| `Structure`, `Locker`, `StandardLocker`, `LargeLocker`, `RifleRackLocker`, `WallCabinet`, `PedestalLocker`, `Workstation`, `LockerChamber` | supported | Cache from `StructurePositionSync.Start` plus a destroy notifier (no structure lifecycle methods in the fork), so ProjectMER-spawned lockers and workstations are wrapped. `Room` is looked up from the position. Permissions are `KeycardPermissions`. Unknown locker types fall back to `Locker`. |
| `LockerChamber.PlayDeniedSound`, `PedestalLocker.PlayDeniedSound`, `Generator.PlayerDeniedBeep` | adapted | No parameter: the fork's denied RPCs carry no permission flags. |
| `ExperimentalWeaponLocker`, `MicroPedestal` | absent | No such structures in Carl Mod. |
| `Generator` | approximated | `TotalActivationTime`/`TotalDeactivationTime` change the server timers only (not SyncVars in the fork, the client gauge keeps its prefab value). `RemainingTime` set moves the countdown. |
| `Window` | supported | Added from the window's `Start` (a server-side component added in `BreakableWindow.Awake`, the fork window's only lifecycle method) and removed by a destroy notifier. |
| `Hazard`, `SinkholeHazard`, `TantrumHazard`, `DecayableHazard` | adapted | Cache from `EnvironmentalHazard.Start/OnDestroy`. `IsActive`, `DecaySpeed` and `LiveDuration` are read-only in the fork. A spawned sinkhole is still subject to the server's `sinkhole_spawn_chance`. |
| `AmnesticCloudHazard` | approximated | `State` is the fork's `CloudState`. `Spawn` derives `MaxDistance` from the hold-time curve; setting `Owner` to an SCP-939 links the cloud to its abilities. |
| `Tesla` | approximated | Cache from `TeslaGate.Start` plus a destroy notifier. The fork never sets `TeslaGate.Room`, so `Room` is looked up from the position. |
| `PocketDimension` | approximated | The pocket effect is `Corroding`. `ForceExit`/`ForceKill` reproduce the fork teleport branches. `Min/MaxPocketItemTriggerDelay` are read-only. Exit pose members are absent (the fork picks exits from whitelisted doors). |
| `PocketItem`, `PocketTeleport` | approximated | Pocket items are synchronized with the item manager on access (no add/remove events); teleports are collected after map generation. |
| `Scp914` (room helper, processors) | adapted | Statics map to the controller's private fields; setting `IsUpgrading` starts the fork sequence. Processor adapters use the fork processor signatures. |
| `Map` | approximated | `DefaultEscapeZone` is the bounding box of the fork's escape sphere; `EscapeZones`, `AddEscapeZone`, `RemoveEscapeZone` are absent (fixed escape area). `GetRandomLocker` and zone-filtered `GetRandomPickup` are implemented. |

## Round, respawn, CASSIE, decontamination and warhead

Carl Mod has the SL 13.x round flow: `RoundSummary` checks the round from a MEC coroutine, `RespawnManager`
selects one of two waves (`SpawnableTeamType.NineTailedFox` / `ChaosInsurgency`) on a single shared timer and
spawns it after its entry animation (the deathmatch build spawns it in the same frame), respawn tokens are a zero-sum
share between both teams, and CASSIE is `NineTailedFoxAnnouncer` fed by `RespawnEffectsController.PlayCassieAnnouncement`
RPCs. On the deathmatch build, CarlModExtras adds a `deathmatch` config (with a timed "fun mode") that blocks natural
round ending and respawns dead players directly.
Patches live in `src/LabApi/Events/Patches/Round/` (namespace `LabApi.Events.Patches.Rounds`).

### Events

| Event | State | Notes |
| --- | --- | --- |
| `ServerEvents.WaitingForPlayers` | supported | Host `CharacterClassManager.Init` coroutine, where the fork logs "Waiting for players...". As officially, it can fire before the map is generated. |
| `ServerEvents.RoundStarting` | supported | Prefix of `CharacterClassManager.ForceRoundStart`; cancelling returns `false`. |
| `ServerEvents.RoundStarted` | supported | Host `Init` coroutine right after `NetworkRoundStarted = true`, before roles are assigned. Exactly once per round: the fork's `CharacterClassManager.OnRoundStarted` also fires from the `RpcRoundStarted` receive path and is not used. |
| `ServerEvents.RoundRestarted` | supported | Prefix of `RoundRestart.InitiateRoundRestart` (server only). |
| `ServerEvents.RoundEndingConditionsCheck` | approximated | The `RoundSummary` coroutine is replaced by an equivalent that keeps the fork rules: checked every 2.5 s after a 15 s grace, only when the kill count changed since the last check, ends at 30 min (overtime). A vetoed end is re-checked on the next cycle without waiting for a kill. On the deathmatch build with `deathmatch` enabled the check runs every 2.5 s only while this event has subscribers and starts with `CanEnd = false`; setting it to `true` ends the deathmatch round normally. `RoundLock` and `KeepRoundOnOne` skip the check as officially. |
| `ServerEvents.RoundEnding` | supported | After the conditions check, with the fork's leading-team rules (no flamingos). Cancelling keeps the round running and re-checks on the next cycle. `LeadingTeam` is applied. |
| `ServerEvents.RoundEnded` | supported | 1.5 s after `RoundEnding`, before the summary RPC; `ShowSummary = false` skips the summary screen. |
| `ServerEvents.WaveTeamSelecting` / `WaveTeamSelected` | approximated | At `RespawnManager.Update` team selection, right after the game picks the team (`RespawnTokensManager.DominatingTeam`, or `DmFun.ChooseTeam` on the deathmatch build), also from `RespawnWave.InitiateRespawn`. `Wave` is the fork's `SpawnableTeamHandlerBase` (use `RespawnWave.Base`). Cancelling restarts the respawn cooldown instead of retrying every frame. Not raised by `InstantRespawn` / RA force spawns (as officially). |
| `ServerEvents.WaveRespawning` / `WaveRespawned` | supported | `RespawnManager.Spawn` is replaced with the same steps while either event has subscribers. `Roles` edits are applied; cancelling spawns nobody and keeps tokens. Not raised when nobody can spawn (for example a forced spawn without spectators), as officially. The deathmatch build's direct respawns (`DmDirectRespawn`, not a wave) do not raise them. |
| `ServerEvents.CassieAnnouncing` / `CassieAnnounced` | supported | Around `RespawnEffectsController.PlayCassieAnnouncement` (the LabAPI 1.0 site; SL 14.2.7 no longer raises them). Covers every server announcement, including glitched SCP terminations and MTF entrances. `CustomSubtitles` is sent in the SL 13.x translated format (`subtitle<size=0> words </size><split>`) with subtitles on. |
| `ServerEvents.CassieQueuingScpTermination` / `CassieQueuedScpTermination` | supported | Prefix of `NineTailedFoxAnnouncer.AnnounceScpTermination`, only when a new termination is queued; deaths merged into a waiting announcement (same text) raise nothing. |
| `ServerEvents.LczDecontaminationAnnounced` | supported | After `DecontaminationController.UpdateTime` advances a non-final phase. |
| `ServerEvents.LczDecontaminationStarting` / `LczDecontaminationStarted` | supported | Around `DecontaminationController.FinishDecontamination` (final phase and `ForceDecontamination`). |
| `WarheadEvents.Starting` / `Started` | supported | Around `AlphaWarheadController.StartDetonation`. `IsAutomatic`, `SuppressSubtitles`, `Player` and `WarheadState` (scenario and start time) are applied. `Started` fires after the start subtitles. |
| `WarheadEvents.Stopping` / `Stopped` | supported | Around `CancelDetonation(ReferenceHub)`. As officially, `WarheadState` changes are overwritten by the cancellation. |
| `WarheadEvents.Detonating` / `Detonated` | supported | Around `AlphaWarheadController.Detonate`; a changed `Player` becomes the triggering footprint. Cancelling retries every frame, as officially. |
| `ServerEvents.DeadmanSequenceActivating` / `DeadmanSequenceActivated` | absent | No Deadman Switch. |
| `ServerEvents.ModifyingFactionInfluence` / `ModifiedFactionInfluence` | absent | No faction influence (see `RespawnWave.Influence`). |
| `ServerEvents.AchievingMilestone` / `AchievedMilestone` | absent | No milestones. |
| `ObjectiveEvents` (all) | absent | No objectives (14.x); handler class and argument types removed. |

### Wrappers

| Member | State | Notes |
| --- | --- | --- |
| `Round` | supported | `IsRoundEnded` reads `RoundSummary._roundEnded`. `End(force)` works through the replaced round coroutine (the fork's own `ForceEnd` would stall the round). `CanRoundEnd` is also `false` while `deathmatch` is enabled on the deathmatch build. |
| `Round.ExtraTargets` | absent | No SCP target counter. `ScpTargetsAmount` counts Foundation staff and enemies only. |
| `Announcer` / obsolete `Cassie` | approximated | Backed by `NineTailedFoxAnnouncer`. `IsSpeaking` reads the host's local queue. `AllLines` is `NineTailedFoxAnnouncer.VoiceLine[]`; `CollectionNames`/`IsValid` use voice line names. `CalculateDuration(string, bool, float)` is the fork's calculation (not obsolete). `Message`: `priority` ignored (clients play in arrival order), `glitchScale` adds `.G`/`JAM_` words with the official chances (doubled after detonation), custom subtitles as in `CassieAnnouncing`. `ConvertNumber` uses the fork's number words. |
| `Announcer.LineDatabase`, `Message(CassieTtsPayload, ...)`, `CalculateDuration(..., CassiePlaybackModifiers ...)` | absent | No 14.2 CASSIE line database, payloads or playback modifiers. |
| `Decontamination` | approximated | `Offset` shifts the synchronized round start time because the fork does not sync its time offset; an offset set before the timer starts is applied when it starts. The start time is network time since the server started and must stay above 0 (the fork stops the timer otherwise), so the part of a positive offset beyond that goes to the controller's server-only `TimeOffset`: server phases follow the full offset, client timers and announcement audio lag by that part. |
| `Decontamination.ElevatorsText` | absent | The elevator text is client-side and not synchronized. |
| `Warhead` | approximated | `WarheadScenarioType` (`Start`, `Resume`) is a LabAPI enum mapped to `AlphaWarheadSyncInfo.ResumeScenario`. `IsAuthorized` is the outside panel's `keycardEntered`; `BaseNukesitePanel` is `AlphaWarheadOutsitePanel.nukeside`. |
| `Warhead.ForceCountdownToggle`, `DeadManSwitchRemaining`, `DeadManSwitchMaxTime`, `DeadmanSwitchScenario` | absent | No Deadman Switch. |
| `Warhead.OpenBlastDoors` | absent | Blast doors only animate closing; they cannot reopen for connected clients. |
| `RespawnWaves.PrimaryMtfWave` / `PrimaryChaosWave` | approximated | Wrap `NineTailedFoxSpawnHandler` / `ChaosInsurgencySpawnHandler`; `Get(SpawnableTeamHandlerBase)` and `Get(SpawnableTeamType)`. |
| `RespawnWave.TimeLeft`, `TimePassed` | approximated | The single `RespawnManager` timer, shared by both waves. |
| `RespawnWave.Influence` | approximated | The team's SL 13.x respawn token share (0-100, zero-sum; the larger share is selected next). Setting it uses `RespawnTokensManager.ForceTeamDominance`. |
| `RespawnWave.MaxWaveSize` | approximated | Absolute player count (`maximum_MTF_respawn_amount` / `maximum_CI_respawn_amount`); a set value lasts until the config is reloaded. |
| `RespawnWave.AnimationTime` | approximated | Arrival effect length; the fork spawns in the selection frame. |
| `RespawnWave.InitiateRespawn` / `InstantRespawn` / `PlayRespawnEffect` | supported | Selection events + arrival effects + `ForceSpawnTeam` / `ForceSpawnTeam` / selection effects only. |
| `RespawnWave.PlayAnnouncement` | approximated | MTF: entrance announcement for the latest unit name (counts SCPs itself). Chaos: nothing (no CASSIE announcement in the fork). |
| `RespawnWave.RespawnTokens`, `AdditionalSecondsPerSpawn`, `PausedTime`, `IsForcefullyPaused`, `TryGetCurrentMilestone` | absent | No per-wave tokens, timers, pausing or milestones. |
| `MtfWave.SergeantsPercentage` / `CaptainsPercentage`, `ChaosWave.LogicerPercent` / `ShotgunPercent` | absent | Fixed wave composition in the fork. |
| `MiniRespawnWave`, `MiniMtfWave`, `MiniChaosWave` | absent | No mini waves. |

## SCP role events

Carl Mod runs the 13.x SCP implementations (`PlayerRoles.PlayableScps.*`). Every event below is raised by a
Harmony patch in `src/LabApi/Events/Patches/Scps/`. Patches do nothing unless the event has subscribers; with
subscribers, most re-run the fork method body with the official event points, so cancellation and argument changes
feed back as in official LabAPI.

### Absent

| Handler | State | Notes |
| --- | --- | --- |
| `Scp3114Events` (all 10 events) | absent | Carl Mod has no SCP-3114. |
| `Scp127Events` (all 6 events) | absent | Carl Mod has no SCP-127 firearm. |

### SCP-049 / SCP-049-2

| Event | State | Notes |
| --- | --- | --- |
| `Scp049Events.Attacking` / `Attacked` | approximated | `InstantKill` and `CooldownTime` are applied. `IsSenseTarget` is reported (sense target of the attacker) but has no effect when changed: the fork attack RPC carries no sense flag. Cancelling skips cooldown, damage and RPC. |
| `Scp049Events.UsingDoctorsCall` / `UsedDoctorsCall` | supported | |
| `Scp049Events.UsingSense` / `UsedSense` | supported | Changing `Target` re-targets the sense. |
| `Scp049Events.SenseLostTarget` / `SenseKilledTarget` | supported | |
| `Scp049Events.StartingResurrection` | supported | When `CanResurrect` stays false the fork's specific error code is shown instead of always `TargetInvalid`. |
| `Scp049Events.ResurrectingBody` / `ResurrectedBody` | supported | Fork values (100 hume shield for sense kills, always `Scp0492`). Changing `Target` revives that player. |
| `Scp0492Events.StartingConsumingCorpse` | supported | `Error` uses the fork's `ConsumeError` values. |
| `Scp0492Events.StartedConsumingCorpse` | supported | Observed at the server dispatch of subroutine commands (`SubroutineMessage.Apply`): the fork does not override `ServerProcessCmd`, and the shared `RagdollAbilityBase<T>` method is not patched. |
| `Scp0492Events.ConsumingCorpse` / `ConsumedCorpse` | supported | `HealAmount`, `AddToConsumedRagdollList`, `HealIfAlreadyConsumed` are applied. Not raised when the ragdoll vanished before completion (the fork heals nothing then). |

### SCP-079

| Event | State | Notes |
| --- | --- | --- |
| `ChangingCamera` / `ChangedCamera` | supported | Spectator switch-state messages raise nothing, as officially. |
| `Pinging` / `Pinged` | supported | A `PingType` outside the fork's 7 processors cancels the ping. |
| `GainingExperience` / `GainedExperience` | approximated | Raised from `Scp079RewardManager.GrantExp`. The fork passes no subject role: `Subject` is always `RoleTypeId.None` and changing it has no effect. RA `addexperience` bypasses the event, as officially. |
| `LevelingUp` / `LeveledUp` | supported | Same order as official, including that cancelling does not undo the per-level increments. |
| `BlackingOutRoom` / `BlackedOutRoom` | supported | The fork-only "restore lights in a room 079 blacked out" path raises nothing. |
| `BlackingOutZone` / `BlackedOutZone` | supported | |
| `LockingDoor` / `LockedDoor` | supported | The fork lets 079 hold several door locks at once. |
| `UnlockingDoor` / `UnlockedDoor` | approximated | Raised once per door for manual unlocks, automatic unlocks (door no longer valid, aux depleted, signal lost), the fork-only "release all locks" ability and lockdown takeover of a 079-locked door. A cancelled automatic unlock is retried (and raised) every frame, as official. |
| `LockingDownRoom` / `LockedDownRoom` | supported | |
| `CancellingRoomLockdown` / `CancelledRoomLockdown` | supported | Also raised on role reset, as official. |
| `UsingTesla` / `UsedTesla` | supported | |
| `Recontaining` / `Recontained` | supported | |

### SCP-096

| Event | State | Notes |
| --- | --- | --- |
| `AddingTarget` / `AddedTarget` | supported | |
| `ChangingState` / `ChangedState` | supported | Hooked on the `RageState` setter; the reset to `Docile` when the role is pooled raises nothing, as official. |
| `Charging` / `Charged` | supported | |
| `Enraging` / `Enraged` | supported | `InitialDuration` is applied. |
| `PryingGate` / `PriedGate` | supported | |
| `TryingNotToCry` / `TriedNotToCry`, `StartCrying` / `StartedCrying` | approximated | The fork has no hume shield regeneration boost while trying not to cry; only the ability state is gated. |

### SCP-106

The fork has the 13.x SCP-106: stalking means submerging, and one hit damages the target and sends it to the
pocket dimension.

| Event | State | Notes |
| --- | --- | --- |
| `TeleportingPlayer` / `TeleportedPlayer` | approximated | Raised for every successful hit, before the damage. Cancelling cancels the whole hit (damage, vigor reward, capture); there is no separate first-hit corrosion stage. |
| `ChangingStalkMode` / `ChangedStalkMode` | approximated | Stalk is the 13.x submerged stalk (`Scp106StalkAbility.IsActive`). Covers toggling and the automatic exit at zero vigor; the role reset raises nothing. |
| `ChangingSubmersionStatus` / `ChangedSubmersionStatus` | approximated | The fork derives the sinkhole state on the server and every client from the stalk and Hunter's Atlas abilities, so `ChangingSubmersionStatus` is raised where an ability changes its submerged state (stalk toggle and automatic exit, Hunter's Atlas submerge and emerge) when that flips the sinkhole state; cancelling refuses the ability change. Stalk: after `ChangingStalkMode`, and a refusal also refuses the stalk. Hunter's Atlas: teleport and emerge are one step in the fork, so a refused emerge keeps SCP-106 submerged at its origin and is raised again every frame; the teleport happens once the emerge is allowed. `ChangedSubmersionStatus` reports the sinkhole state change. |
| `ChangingVigor` / `ChangedVigor` | supported | Raised for attack reward, Hunter's Atlas cost, stalk drain and regeneration (per frame while changing, as official). `Value` is stored as given; reads are clamped to 0-1 as in the fork. |
| `UsingHunterAtlas` / `UsedHunterAtlas` | supported | `DestinationPosition` is applied. |

### SCP-173

| Event | State | Notes |
| --- | --- | --- |
| `AddingObserver` / `AddedObserver`, `RemovingObserver` / `RemovedObserver` | supported | Observers are humans in the fork (official: enemies). |
| `BreakneckSpeedChanging` / `BreakneckSpeedChanged` | supported | |
| `CreatingTantrum` / `CreatedTantrum` | supported | |
| `PlayingSound` / `PlayedSound` | supported | `SoundId` change is applied. |
| `Snapping` / `Snapped` | supported | As official, a changed `Target` is reported by `Snapped` but the hit still uses the client's raycast. |
| `Teleporting` / `Teleported` | supported | `Position` is applied. |

### SCP-939

| Event | State | Notes |
| --- | --- | --- |
| `Attacking` / `Attacked` (claw) | approximated | The fork claw hits every targeted player in range for 40 each (no primary/secondary split). One event per player; `Damage` and `Target` are applied. Hooked on the 939-only `Scp939ClawAbility.ServerProcessCmd`: the shared `ScpAttackAbilityBase<T>` method is not patched, so SCP-049-2 attacks are unaffected. |
| `Attacking` / `Attacked` (lunge) | supported | Primary 120, secondary 30. A cancelled secondary hit only skips that player (official aborts the rest of the lunge). |
| `Lunging` / `Lunged` | supported | Cancelling the lunge trigger also cancels its hit. Role reset to `None` raises nothing. |
| `Focused` | supported | |
| `CreatingAmnesticCloud` / `CreatedAmnesticCloud` | supported | |
| `MimickingEnvironment` / `MimickedEnvironment` | approximated | The fork picks a sound by (category, option). `SelectedSequence` / `PlayedSequence` is the category-major flat index into `EnvironmentalMimicry.Categories`; setting it selects that pair. |
