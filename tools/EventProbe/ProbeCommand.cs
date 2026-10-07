using System;
using System.Globalization;
using CommandSystem;
using LabApi.Features.Wrappers;
using UnityEngine;

namespace EventProbe;

/// <summary>
/// <c>probe cancel|allow &lt;Class.Event&gt;</c>, <c>probe cancelled</c>, <c>probe counts</c>,
/// <c>probe where &lt;playerId&gt;</c>, <c>probe tp &lt;playerId&gt; x y z</c>, <c>probe goto &lt;playerId&gt; &lt;GameType&gt; [index] [distance]</c>
/// (moves a test client in front of the n-th scene object of a game component type, for example Scp079Generator or TeslaGate,
/// so features can be reached without driving the touch UI across the map), <c>probe front &lt;movedId&gt; &lt;anchorId&gt; [distance]</c>
/// (moves a player in front of another one, facing it), <c>probe face &lt;id&gt; &lt;targetId&gt; [away]</c> (turns a player towards or away from another one,
/// yaw only), <c>probe deathrole &lt;RoleTypeId|None&gt;</c> (role given instead of Spectator on death), <c>probe patches</c>
/// (LabAPI patch summary: applied count, failed patch classes, patched generic methods), and wrapper checks:
/// <c>probe whoami</c> (<c>Player.Get(sender)</c>), <c>probe maxhp &lt;id&gt; [value]</c> (<c>Player.MaxHealth</c>, <c>Health</c>, <c>LifeId</c>),
/// <c>probe dropammo &lt;id&gt; &lt;ItemType&gt; &lt;amount&gt;</c> (<c>Player.DropAmmo</c>), <c>probe decon [offset]</c> (<c>Decontamination.Offset</c>),
/// <c>probe windows</c> (<c>Window.List</c> against the scene's windows), <c>probe ragdollto &lt;id&gt;</c> (moves the newest ragdoll to
/// where the player's camera points, for SCP-049 resurrections and SCP-049-2 consumption without precise touch aiming),
/// <c>probe equip &lt;id&gt; &lt;ItemType&gt;</c> (gives the item when missing and equips it), <c>probe aimat &lt;id&gt;</c> (moves the player,
/// keeping its view direction, so that the camera ray passes through the newest ragdoll's body; the view must point downwards).
/// Server-side stand-ins for client actions the Carl Mod touch UI cannot perform: <c>probe killby &lt;id&gt; &lt;attackerId&gt;</c>
/// (kills with an SCP damage handler from the attacker), <c>probe cuff &lt;disarmerId&gt; &lt;targetId&gt; [release]</c> (feeds a
/// <c>DisarmMessage</c> from the disarmer's connection to the server handler), <c>probe lunge &lt;scp939Id&gt; &lt;targetId&gt;</c> (focuses the
/// SCP-939 and feeds a lunge hit command for the target to <c>Scp939LungeAbility.ServerProcessCmd</c>), <c>probe atlas &lt;scp106Id&gt;
/// &lt;x&gt; &lt;y&gt; &lt;z&gt;</c> (feeds a Hunter's Atlas destination to <c>Scp106HuntersAtlasAbility.ServerProcessCmd</c>; the emulator renders an
/// empty minimap, so no room can be selected on the client).
/// Registered for the server console and Remote Admin.
/// </summary>
[CommandHandler(typeof(GameConsoleCommandHandler))]
[CommandHandler(typeof(RemoteAdminCommandHandler))]
public sealed class ProbeCommand : ICommand
{
    public string Command => "probe";

    public string[] Aliases => [];

    public string Description => "EventProbe: probe cancel|allow <Class.Event>, probe cancelled, probe counts, probe where <id>, probe tp <id> <x> <y> <z>, probe yaw <id> <degrees>, probe as <id> <RA command>, probe elevator <id>, probe netobj <name>, probe waypoints [refresh], probe toyrevert, probe goto <id> <GameType> [index] [distance], probe front <movedId> <anchorId> [distance], probe face <id> <targetId> [away], probe deathrole <RoleTypeId|None>, probe patches, probe whoami, probe maxhp <id> [value], probe dropammo <id> <ItemType> <amount>, probe decon [offset], probe windows, probe ragdollto <id>, probe equip <id> <ItemType>, probe aimat <id>, probe killby <id> <attackerId>, probe cuff <disarmerId> <targetId> [release], probe lunge <scp939Id> <targetId>, probe atlas <scp106Id> <x> <y> <z>";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        EventProbePlugin? plugin = EventProbePlugin.Instance;
        if (plugin == null)
        {
            response = "EventProbe is not enabled.";
            return false;
        }

        string verb = arguments.Count > 0 ? arguments.At(0).ToLowerInvariant() : string.Empty;
        switch (verb)
        {
            case "cassiedummy":
            {
                ReferenceHub dummy = ServerDummy.Spawn(PlayerRoles.RoleTypeId.ClassD, Vector3.zero, Quaternion.identity);
                try
                {
                    Player dummyPlayer = Player.Get(dummy);
                    Mirror.NetworkServer.SetClientReady(dummy.connectionToClient);
                    bool emptyRejected = !Announcer.MessageTo(dummyPlayer, "");
                    dummy.connectionToClient.isReady = false;
                    bool unreadyRejected = !Announcer.MessageTo(dummyPlayer, "attention", glitchScale: 0f);
                    dummy.connectionToClient.isReady = true;
                    bool sent = Announcer.MessageTo(dummyPlayer, "attention all personnel", glitchScale: 0f);
                    response = $"Private CASSIE dummy: sent={sent}, emptyRejected={emptyRejected}, unreadyRejected={unreadyRejected}";
                    LabApi.Features.Console.Logger.Raw("[PROBE] " + response, ConsoleColor.Cyan);
                    return sent && emptyRejected && unreadyRejected;
                }
                finally
                {
                    dummy.connectionToClient.Disconnect();
                }
            }
            case "cassie" when arguments.Count > 2 && TryGetPlayer(arguments.At(1), out Player? recipient):
            {
                string words = string.Join(" ", arguments.Array!, arguments.Offset + 2, arguments.Count - 2);
                bool sent = Announcer.MessageTo(recipient!, words, glitchScale: 0f);
                response = $"Private CASSIE to #{recipient!.PlayerId}: sent={sent}, words={words}";
                LabApi.Features.Console.Logger.Raw("[PROBE] " + response, ConsoleColor.Cyan);
                return sent;
            }
            case "cancel" or "allow" when arguments.Count > 1:
                return plugin.SetCancel(arguments.At(1), verb == "cancel", out response);
            case "cancelled":
                response = plugin.Describe(countsOnly: false);
                LabApi.Features.Console.Logger.Raw("[PROBE] cancelled:\n" + response, ConsoleColor.Cyan);
                return true;
            case "counts":
                response = plugin.Describe(countsOnly: true);
                LabApi.Features.Console.Logger.Raw("[PROBE] counts:\n" + response, ConsoleColor.Cyan);
                return true;
            case "where" when arguments.Count > 1 && TryGetPlayer(arguments.At(1), out Player? player):
                response = $"{player!.Nickname}: position {player.Position} room {player.Room?.Name.ToString() ?? "none"}";
                LabApi.Features.Console.Logger.Raw("[PROBE] " + response, ConsoleColor.Cyan);
                return true;
            case "tp" when arguments.Count > 4 && TryGetPlayer(arguments.At(1), out Player? player)
                && TryParse(arguments.At(2), out float x) && TryParse(arguments.At(3), out float y) && TryParse(arguments.At(4), out float z):
                player!.Position = new Vector3(x, y, z);
                response = $"Moved {player.Nickname} to {player.Position}";
                return true;
            case "yaw" when arguments.Count > 2 && TryGetPlayer(arguments.At(1), out Player? player) && TryParse(arguments.At(2), out float yaw):
                // Yaw only: the client accepts no pitch override, so keep the camera pitch untouched on the client.
                player!.Rotation = Quaternion.Euler(0f, yaw, 0f);
                response = $"Turned {player.Nickname} to yaw {yaw:0.#}";
                break;
            case "as" when arguments.Count > 2 && TryGetPlayer(arguments.At(1), out Player? actor):
            {
                // Runs a Remote Admin command as the player (commands that need a player sender, e.g. ProjectMER's
                // select/position, cannot run from the server console). The RA reply also goes to the player's client.
                string query = string.Join(" ", arguments.Array!, arguments.Offset + 2, arguments.Count - 2);
                string reply = Server.RunCommand(query.StartsWith("/", StringComparison.Ordinal) ? query : "/" + query,
                    new RemoteAdmin.PlayerCommandSender(actor!.ReferenceHub));
                response = $"as {actor.Nickname}: {query} -> {reply}";
                break;
            }
            case "goto" when arguments.Count > 2 && TryGetPlayer(arguments.At(1), out Player? player):
                return Goto(player!, arguments, out response);
            case "front" when arguments.Count > 2 && TryGetPlayer(arguments.At(1), out Player? moved) && TryGetPlayer(arguments.At(2), out Player? anchor):
            {
                float distance = arguments.Count > 3 && TryParse(arguments.At(3), out float d) ? d : 1.5f;
                Vector3 forward = anchor!.Camera.forward;
                forward.y = 0f;
                forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : anchor.GameObject!.transform.forward;
                moved!.Position = anchor.Position + (forward * distance);
                Face(moved, anchor);
                response = $"Moved {moved.Nickname} {distance:0.0} m in front of {anchor.Nickname} to {moved.Position}";
                LabApi.Features.Console.Logger.Raw("[PROBE] " + response, ConsoleColor.Cyan);
                return true;
            }
            case "face" when arguments.Count > 2 && TryGetPlayer(arguments.At(1), out Player? turned) && TryGetPlayer(arguments.At(2), out Player? target):
                bool away = arguments.Count > 3 && arguments.At(3).Equals("away", StringComparison.OrdinalIgnoreCase);
                Face(turned!, target!, away);
                response = $"Turned {turned!.Nickname} {(away ? "away from" : "towards")} {target!.Nickname}";
                LabApi.Features.Console.Logger.Raw("[PROBE] " + response, ConsoleColor.Cyan);
                return true;
            case "patches":
                response = DescribePatches();
                LabApi.Features.Console.Logger.Raw("[PROBE] patches: " + response, ConsoleColor.Cyan);
                return true;
            case "deathrole" when arguments.Count > 1 && Enum.TryParse(arguments.At(1), true, out PlayerRoles.RoleTypeId role):
                plugin.Config.DeathRole = role;
                response = $"DeathRole = {role}";
                LabApi.Features.Console.Logger.Raw("[PROBE] " + response, ConsoleColor.Cyan);
                return true;
            case "whoami":
                response = Player.TryGet(sender, out Player? self)
                    ? $"Player.Get(sender) = {self!.Nickname} (#{self.PlayerId}, {self.UserId}); sender {sender.GetType().Name}"
                    : $"Player.Get(sender) = null; sender {sender.GetType().Name}";
                break;
            case "maxhp" when arguments.Count > 1 && TryGetPlayer(arguments.At(1), out Player? hpPlayer):
                if (arguments.Count > 2 && TryParse(arguments.At(2), out float maxHealth))
                    hpPlayer!.MaxHealth = maxHealth;
                response = $"{hpPlayer!.Nickname} (#{hpPlayer.PlayerId}, {hpPlayer.Role}): MaxHealth {hpPlayer.MaxHealth:0.#}, Health {hpPlayer.Health:0.#}, LifeId {hpPlayer.LifeId}";
                break;
            case "dropammo" when arguments.Count > 3 && TryGetPlayer(arguments.At(1), out Player? ammoPlayer)
                && Enum.TryParse(arguments.At(2), true, out ItemType ammoType) && ushort.TryParse(arguments.At(3), out ushort amount):
            {
                int pickups = 0;
                int total = 0;
                foreach (AmmoPickup pickup in ammoPlayer!.DropAmmo(ammoType, amount))
                {
                    pickups++;
                    total += pickup.Ammo;
                }

                response = $"DropAmmo({ammoType}, {amount}) returned {pickups} pickups with {total} rounds; {ammoPlayer.Nickname} has {ammoPlayer.GetAmmo(ammoType)} left";
                break;
            }
            case "decon":
                if (arguments.Count > 1 && TryParse(arguments.At(1), out float offset))
                    Decontamination.Offset = offset;
                LightContainmentZoneDecontamination.DecontaminationController controller = LightContainmentZoneDecontamination.DecontaminationController.Singleton;
                response = $"Offset {Decontamination.Offset:0.##}, RoundStartTime {controller.RoundStartTime:0.###}, TimeOffset {controller.TimeOffset:0.##}, "
                    + $"NetworkTime {Mirror.NetworkTime.time:0.##}, ServerTime {Decontamination.ServerTime:0.##}";
                break;
            case "ragdollto" when arguments.Count > 1 && TryGetPlayer(arguments.At(1), out Player? looker):
            {
                Ragdoll? newest = null;
                foreach (Ragdoll ragdoll in Ragdoll.List)
                {
                    if (!ragdoll.IsDestroyed && (newest == null || ragdoll.Base.Info.ExistenceTime < newest.Base.Info.ExistenceTime))
                        newest = ragdoll;
                }

                Transform eye = looker!.Camera;
                if (newest == null)
                {
                    response = "No ragdoll.";
                    break;
                }

                Vector3 point = Physics.Raycast(eye.position, eye.forward, out RaycastHit hit, 4f, 1)
                    ? hit.point
                    : eye.position + (eye.forward * 1.5f);
                newest.Position = point + (Vector3.up * 0.15f);
                response = $"Moved ragdoll of {newest.Nickname} ({newest.Role}, {newest.Base.Info.ExistenceTime:0.0} s old) to {point} in front of {looker.Nickname}";
                break;
            }
            case "equip" when arguments.Count > 2 && TryGetPlayer(arguments.At(1), out Player? holder)
                && Enum.TryParse(arguments.At(2), true, out ItemType equipType):
            {
                Item? item = null;
                foreach (Item owned in holder!.Items)
                {
                    if (owned.Type == equipType)
                    {
                        item = owned;
                        break;
                    }
                }

                item ??= holder.AddItem(equipType);
                if (item != null)
                    holder.CurrentItem = item;
                response = $"{holder.Nickname} holds {holder.CurrentItem?.Type.ToString() ?? "nothing"}";
                break;
            }
            case "aimat" when arguments.Count > 1 && TryGetPlayer(arguments.At(1), out Player? aimer):
            {
                Ragdoll? target = null;
                foreach (Ragdoll ragdoll in Ragdoll.List)
                {
                    if (!ragdoll.IsDestroyed && (target == null || ragdoll.Base.Info.ExistenceTime < target.Base.Info.ExistenceTime))
                        target = ragdoll;
                }

                if (target == null)
                {
                    response = "No ragdoll.";
                    break;
                }

                Vector3 center = Vector3.zero;
                float lowest = float.MaxValue;
                int bodies = 0;
                foreach (Rigidbody body in target.Base.GetComponentsInChildren<Rigidbody>())
                {
                    center += body.position;
                    lowest = Mathf.Min(lowest, body.position.y);
                    bodies++;
                }

                center = bodies > 0 ? center / bodies : target.Base.transform.position;
                Transform eye = aimer!.Camera;
                Vector3 forward = eye.forward;
                if (forward.y > -0.2f)
                {
                    response = $"{aimer.Nickname} must look down (forward.y = {forward.y:0.00}).";
                    break;
                }

                // Keep the eye height above the ragdoll's floor that the player has above its own floor.
                float eyeAboveFloor = Physics.Raycast(aimer.Position, Vector3.down, out RaycastHit floor, 5f, 1)
                    ? eye.position.y - floor.point.y
                    : 1.6f;
                float along = (eyeAboveFloor - (center.y - lowest)) / -forward.y;
                Vector3 eyeTarget = center - (forward * along);
                Vector3 eyeOffset = eye.position - aimer.Position;
                aimer.Position = eyeTarget - eyeOffset;
                response = $"Moved {aimer.Nickname} to {aimer.Position} to look at the ragdoll of {target.Nickname} at {center} ({target.Base.Info.ExistenceTime:0.0} s old, {bodies} bodies)";
                break;
            }
            case "killby" when arguments.Count > 2 && TryGetPlayer(arguments.At(1), out Player? victim) && TryGetPlayer(arguments.At(2), out Player? killer):
            {
                bool killed = victim!.ReferenceHub.playerStats.DealDamage(new PlayerStatsSystem.ScpDamageHandler(killer!.ReferenceHub, PlayerStatsSystem.DeathTranslations.Unknown));
                response = $"Killed {victim.Nickname} as {killer.Nickname}: {killed}";
                break;
            }
            case "cuff" when arguments.Count > 2 && TryGetPlayer(arguments.At(1), out Player? disarmer) && TryGetPlayer(arguments.At(2), out Player? cuffed):
            {
                bool release = arguments.Count > 3 && arguments.At(3).Equals("release", StringComparison.OrdinalIgnoreCase);
                System.Reflection.MethodInfo handler = typeof(InventorySystem.Disarming.DisarmingHandlers).GetMethod("ServerProcessDisarmMessage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
                handler.Invoke(null, [disarmer!.Connection, new InventorySystem.Disarming.DisarmMessage(cuffed!.ReferenceHub, !release, false)]);
                response = $"{(release ? "Release" : "Cuff")} request from {disarmer.Nickname} for {cuffed.Nickname}: target disarmed = {cuffed.IsDisarmed}";
                break;
            }
            case "lunge" when arguments.Count > 2 && TryGetPlayer(arguments.At(1), out Player? lunger) && TryGetPlayer(arguments.At(2), out Player? lungeTarget):
            {
                if (lunger!.RoleBase is not PlayerRoles.PlayableScps.Scp939.Scp939Role scp939
                    || !scp939.SubroutineModule.TryGetSubroutine(out PlayerRoles.PlayableScps.Scp939.Scp939LungeAbility lunge)
                    || !scp939.SubroutineModule.TryGetSubroutine(out PlayerRoles.PlayableScps.Scp939.Scp939FocusAbility focus))
                {
                    response = $"{lunger.Nickname} is not SCP-939.";
                    break;
                }

                // TargetState's setter records the frozen rotation the lunge angle check compares against.
                typeof(PlayerRoles.PlayableScps.Scp939.Scp939FocusAbility).GetProperty("TargetState")!.GetSetMethod(true)!.Invoke(focus, [true]);
                focus.State = 1f;
                Mirror.NetworkWriter writer = new();
                RelativePositioning.RelativePositionSerialization.WriteRelativePosition(writer, new RelativePositioning.RelativePosition(lunger.Position));
                Utils.Networking.ReferenceHubReaderWriter.WriteReferenceHub(writer, lungeTarget!.ReferenceHub);
                RelativePositioning.RelativePositionSerialization.WriteRelativePosition(writer, new RelativePositioning.RelativePosition(lungeTarget.Position));
                lunge.ServerProcessCmd(new Mirror.NetworkReader(writer.ToArraySegment()));
                response = $"Lunge command for {lunger.Nickname} on {lungeTarget.Nickname}: lunge state {lunge.State}, target health {lungeTarget.Health:0.#}";
                break;
            }
            case "atlas" when arguments.Count > 4 && TryGetPlayer(arguments.At(1), out Player? hunter)
                && TryParse(arguments.At(2), out float ax) && TryParse(arguments.At(3), out float ay) && TryParse(arguments.At(4), out float az):
            {
                if (hunter!.RoleBase is not PlayerRoles.PlayableScps.Scp106.Scp106Role scp106
                    || !scp106.SubroutineModule.TryGetSubroutine(out PlayerRoles.PlayableScps.Scp106.Scp106HuntersAtlasAbility atlas))
                {
                    response = $"{hunter.Nickname} is not SCP-106.";
                    break;
                }

                Mirror.NetworkWriter writer = new();
                RelativePositioning.RelativePositionSerialization.WriteRelativePosition(writer, new RelativePositioning.RelativePosition(new Vector3(ax, ay, az)));
                Mirror.NetworkWriterExtensions.WriteShort(writer, 0);
                Mirror.NetworkWriterExtensions.WriteShort(writer, 0);
                atlas.ServerProcessCmd(new Mirror.NetworkReader(writer.ToArraySegment()));
                response = $"Hunter's Atlas command for {hunter.Nickname} to ({ax:0.0}, {ay:0.0}, {az:0.0}): submerged = {atlas.IsSubmerged}";
                break;
            }
            case "windows":
                response = $"Window.List {Window.List.Count}, BreakableWindow objects {UnityEngine.Object.FindObjectsByType<BreakableWindow>(FindObjectsSortMode.None).Length}";
                break;
            case "toyrevert":
            {
                // Creates a primitive spawned at once, then sets its colour and flags in the same frame, and reports one
                // second later whether the server object kept them (the host client must not revert them).
                PrimitiveObjectToy toy = PrimitiveObjectToy.Create(new Vector3(0f, -500f, 0f), Quaternion.identity, Vector3.one, null, true);
                toy.Color = Color.red;
                toy.IsStatic = false;
                MEC.Timing.CallDelayed(1f, () =>
                {
                    string result = $"toyrevert: color {toy.Base.MaterialColor} (set red), static {toy.Base.IsStatic} (set False)";
                    LabApi.Features.Console.Logger.Raw("[PROBE] " + result, ConsoleColor.Cyan);
                    toy.Destroy();
                });
                response = "toyrevert: created; result in 1 s";
                break;
            }
            case "elevator" when arguments.Count > 1 && TryGetPlayer(arguments.At(1), out Player? rider):
            {
                // Sends the elevator nearest to the player to its next floor (the same server call as the chamber's
                // button), for repeatable rides without aiming at the panel.
                Elevator? nearest = null;
                float best = float.MaxValue;
                foreach (Elevator elevator in Elevator.List)
                {
                    float distance = (elevator.Base.transform.position - rider!.Position).sqrMagnitude;
                    if (distance < best)
                    {
                        best = distance;
                        nearest = elevator;
                    }
                }

                if (nearest == null)
                {
                    response = "No elevator.";
                    return false;
                }

                nearest.SendToNextFloor();
                response = $"Sent {nearest.Group} elevator (chamber at {nearest.Base.transform.position}, {Mathf.Sqrt(best):0.0} m from {rider!.Nickname}) to its next floor";
                break;
            }
            case "netobj" when arguments.Count > 1:
                response = DescribeNetObjects(arguments.At(1));
                break;
            case "waypoints":
                response = DescribeWaypoints(arguments.Count > 1 && arguments.At(1).Equals("refresh", StringComparison.OrdinalIgnoreCase));
                break;
            default:
                response = "Usage: " + Description;
                return false;
        }

        LabApi.Features.Console.Logger.Raw("[PROBE] " + response, ConsoleColor.Cyan);
        return true;
    }

    private static string DescribePatches()
    {
        System.Text.StringBuilder sb = new();
        sb.Append(LabApi.Events.Patches.PatchManager.AppliedPatchCount).Append(" applied, ")
            .Append(LabApi.Events.Patches.PatchManager.FailedPatches.Count).Append(" failed");
        foreach (System.Collections.Generic.KeyValuePair<Type, string> failed in LabApi.Events.Patches.PatchManager.FailedPatches)
            sb.Append("\n  failed ").Append(failed.Key.Name).Append(": ").Append(failed.Value);

        int methods = 0;
        foreach (System.Reflection.MethodBase method in LabApi.Events.Patches.PatchManager.Harmony.GetPatchedMethods())
        {
            methods++;
            if (method.DeclaringType is { IsGenericType: true } || method.IsGenericMethod)
                sb.Append("\n  GENERIC ").Append(method.DeclaringType).Append('.').Append(method.Name);
        }

        sb.Append(", ").Append(methods).Append(" patched methods");
        return sb.ToString();
    }

    /// <summary>
    /// Lists the server's net waypoints (door <c>NetIdWaypoint</c>s) in netId order with the waypoint id each one has, and
    /// flags ids that differ from what a client computes (32 + rank by netId among the waypoints it has). With
    /// <c>refresh</c>, requests the game's renumbering on the next frame.
    /// </summary>
    private static string DescribeWaypoints(bool refresh)
    {
        const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        Type type = typeof(RelativePositioning.NetIdWaypoint);
        var all = (System.Collections.Generic.HashSet<RelativePositioning.NetIdWaypoint>)type.GetField("AllNetWaypoints", Any)!.GetValue(null);
        System.Reflection.FieldInfo target = type.GetField("_targetNetId", Any)!;
        System.Reflection.FieldInfo id = typeof(RelativePositioning.WaypointBase).GetField("_id", Any)!;
        var list = new System.Collections.Generic.List<(uint NetId, byte Id, string Name)>();
        foreach (RelativePositioning.NetIdWaypoint waypoint in all)
        {
            var identity = (Mirror.NetworkIdentity)target.GetValue(waypoint);
            list.Add((identity != null ? identity.netId : 0u, (byte)id.GetValue(waypoint), waypoint.name));
        }

        list.Sort(static (a, b) => a.NetId.CompareTo(b.NetId));
        System.Text.StringBuilder sb = new();
        int mismatches = 0;
        for (int i = 0; i < list.Count; i++)
        {
            byte expected = unchecked((byte)(32 + i));
            if (list[i].Id != expected)
            {
                mismatches++;
                if (mismatches <= 12)
                    sb.Append("\n  netId ").Append(list[i].NetId).Append(' ').Append(list[i].Name).Append(": id ").Append(list[i].Id).Append(", compact ").Append(expected);
            }
        }

        string head = $"{list.Count} net waypoints, netIds {(list.Count > 0 ? list[0].NetId : 0)}..{(list.Count > 0 ? list[list.Count - 1].NetId : 0)}, {mismatches} ids differ from the compact numbering a joining client computes";
        if (refresh)
        {
            type.GetField("_refreshNextFrame", Any)!.SetValue(null, true);
            foreach (RelativePositioning.NetIdWaypoint waypoint in all)
            {
                waypoint.enabled = true;
                break;
            }

            head += "; renumbering requested";
        }

        return head + sb;
    }

    /// <summary>
    /// Lists spawned network objects whose name contains a text: netId, observers, transform and, for admin toys, the
    /// static flag, transform SyncVars, behaviour state and (primitives) colour.
    /// </summary>
    private static string DescribeNetObjects(string nameContains)
    {
        System.Text.StringBuilder sb = new();
        int count = 0;
        foreach (Mirror.NetworkIdentity identity in Mirror.NetworkServer.spawned.Values)
        {
            if (identity == null || identity.name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            if (++count > 20)
                continue;

            sb.Append("\n  netId ").Append(identity.netId).Append(' ').Append(identity.name).Append(" observers [");
            foreach (int connectionId in identity.observers.Keys)
                sb.Append(connectionId).Append(' ');
            sb.Append("] pos ").Append(identity.transform.position.ToString("F2")).Append(" scale ").Append(identity.transform.localScale.ToString("F2"));
            if (identity.TryGetComponent(out AdminToys.AdminToyBase toy))
            {
                sb.Append(" static ").Append(toy.IsStatic).Append(" enabled ").Append(toy.enabled).Append(" syncPos ").Append(toy.Position.ToString("F2"))
                    .Append(" syncScale ").Append(toy.Scale.ToString("F2")).Append(" smoothing ").Append(toy.MovementSmoothing);
                if (toy is AdminToys.PrimitiveObjectToy primitive)
                    sb.Append(" color ").Append(primitive.MaterialColor);
            }
        }

        return $"{count} spawned objects named *{nameContains}*" + sb;
    }

    private static void Face(Player turned, Player target, bool away = false)
    {
        Vector3 direction = away ? turned.Position - target.Position : target.Position - turned.Position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.0001f)
            turned.Rotation = Quaternion.LookRotation(direction);
    }

    private static bool TryGetPlayer(string id, out Player? player)
    {
        player = int.TryParse(id, out int playerId) ? Player.Get(playerId) : null;
        return player != null;
    }

    private static bool TryParse(string text, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static bool Goto(Player player, ArraySegment<string> arguments, out string response)
    {
        Type? type = typeof(ReferenceHub).Assembly.GetType(arguments.At(2), false, true);
        if (type == null)
        {
            foreach (Type candidate in typeof(ReferenceHub).Assembly.GetTypes())
            {
                if (string.Equals(candidate.Name, arguments.At(2), StringComparison.OrdinalIgnoreCase) && typeof(Component).IsAssignableFrom(candidate))
                {
                    type = candidate;
                    break;
                }
            }
        }

        if (type == null || !typeof(Component).IsAssignableFrom(type))
        {
            response = $"No component type '{arguments.At(2)}' in Assembly-CSharp.";
            return false;
        }

        UnityEngine.Object[] found = UnityEngine.Object.FindObjectsByType(type, FindObjectsSortMode.InstanceID);
        int index = arguments.Count > 3 && int.TryParse(arguments.At(3), out int i) ? i : 0;
        float distance = arguments.Count > 4 && TryParse(arguments.At(4), out float d) ? d : 1.5f;
        if (index < 0 || index >= found.Length)
        {
            response = $"{found.Length} objects of {type.Name}; index {index} is out of range.";
            return false;
        }

        Transform target = ((Component)found[index]).transform;
        player.Position = target.position + (target.forward * distance) + Vector3.up;
        response = $"Moved {player.Nickname} to {type.Name} #{index} of {found.Length} at {target.position}";
        LabApi.Features.Console.Logger.Raw("[PROBE] " + response, ConsoleColor.Cyan);
        return true;
    }
}
