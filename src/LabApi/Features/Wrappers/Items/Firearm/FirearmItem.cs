using Generators;
using GameCore;
using InventorySystem;
using InventorySystem.Items.Firearms;
using InventorySystem.Items.Firearms.Attachments;
using InventorySystem.Items.Firearms.Attachments.Components;
using InventorySystem.Items.Firearms.BasicMessages;
using InventorySystem.Items.Firearms.Modules;
using LabApi.Events.Patches.ItemsFirearms;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;
using Utils.Networking;
using Logger = LabApi.Features.Console.Logger;

namespace LabApi.Features.Wrappers;

/// <summary>
/// The wrapper representing <see cref="Firearm"/>.<para/>
/// The Carl Mod firearm keeps its whole state in one <see cref="FirearmStatus"/>: a single ammo count (chambered rounds included),
/// status flags (cocked, chambered, magazine inserted, flashlight) and the attachments code.
/// This wrapper exposes the official LabAPI view of that state: <see cref="StoredAmmo"/> is the ammo container and <see cref="ChamberedAmmo"/> the chamber.
/// </summary>
public class FirearmItem : Item
{
    /// <summary>
    /// Contains all the handlers for constructing wrappers for the associated base game types.
    /// </summary>
    private static readonly Dictionary<ItemType, Func<Firearm, FirearmItem>> TypeWrappers = [];

    /// <summary>
    /// Contains all the cached firearm items, accessible through their <see cref="Firearm"/>.
    /// </summary>
    public static new Dictionary<Firearm, FirearmItem> Dictionary { get; } = [];

    /// <summary>
    /// A reference to all instances of <see cref="FirearmItem"/>.
    /// </summary>
    public static new IReadOnlyCollection<FirearmItem> List => Dictionary.Values;

    /// <summary>
    /// Gets the firearm item wrapper from the <see cref="Dictionary"/> or creates a new one if it doesn't exist and the provided <see cref="Firearm"/> was not null.
    /// </summary>
    /// <param name="firearm">The <see cref="Base"/> of the item.</param>
    /// <returns>The requested item or null.</returns>
    [return: NotNullIfNotNull(nameof(firearm))]
    public static FirearmItem? Get(Firearm? firearm)
    {
        if (firearm == null)
        {
            return null;
        }

        return Dictionary.TryGetValue(firearm, out FirearmItem item) ? item : CreateFirearmWrapper(firearm);
    }

    /// <summary>
    /// Creates a firearm wrapper or it's subtype.
    /// </summary>
    /// <param name="firearm">The base game firearm.</param>
    /// <returns>Firearm wrapper object.</returns>
    internal static FirearmItem CreateFirearmWrapper(Firearm firearm)
    {
        if (!TypeWrappers.TryGetValue(firearm.ItemTypeId, out Func<Firearm, FirearmItem> ctor))
        {
            return new FirearmItem(firearm);
        }

        return ctor(firearm);
    }

    /// <summary>
    /// Initializes the <see cref="FirearmItem"/> class by registering derived wrappers.
    /// </summary>
    [InitializeWrapper]
    internal static void InitializeFirearmWrappers()
    {
        Register(ItemType.ParticleDisruptor, (x) => new ParticleDisruptorItem((ParticleDisruptor)x));
        Register(ItemType.GunRevolver, (x) => new RevolverFirearm(x));
        Register(ItemType.GunShotgun, (x) => new ShotgunFirearm(x));
    }

    /// <summary>
    /// A private method to handle the addition of wrapper handlers.
    /// </summary>
    /// <param name="itemType">Item type of the target firearm.</param>
    /// <param name="constructor">A handler to construct the wrapper with the base game instance.</param>
    private static void Register(ItemType itemType, Func<Firearm, FirearmItem> constructor)
    {
        TypeWrappers.Add(itemType, constructor);
    }

    /// <summary>
    /// An internal constructor to prevent external instantiation.
    /// </summary>
    /// <param name="firearm">The base <see cref="Firearm"/> object.</param>
    internal FirearmItem(Firearm firearm)
        : base(firearm)
    {
        Base = firearm;

        if (CanCache)
        {
            Dictionary.Add(firearm, this);
        }
    }

    /// <summary>
    /// The base <see cref="Firearm"/> object.
    /// </summary>
    public new Firearm Base { get; }

    /// <summary>
    /// Gets the total firearm's weight including attachments in kilograms.
    /// </summary>
    public new float Weight => Base.Weight;

    /// <summary>
    /// Gets the total length of this firearm in inches.
    /// </summary>
    public float Length => Base.Length;

    /// <summary>
    /// Gets the weight of the firearm in kilograms without any attachments.
    /// </summary>
    public float BaseWeight => Base.BaseWeight;

    /// <summary>
    /// Gets the length of the firearm in inches without any attachments.
    /// </summary>
    public float BaseLength => Base.BaseLength;

    /// <summary>
    /// Gets whether the player is currently reloading this firearm.
    /// </summary>
    public bool IsReloading => FirearmReloadTracker.Get(Base) == FirearmReloadTracker.Reloading;

    /// <summary>
    /// Gets whether the player is currently unloading this firearm.
    /// </summary>
    public bool IsUnloading => FirearmReloadTracker.Get(Base) == FirearmReloadTracker.Unloading;

    /// <summary>
    /// Gets whether the player is either reloading or unloading this firearm.
    /// </summary>
    public bool IsReloadingOrUnloading => FirearmReloadTracker.Get(Base) != FirearmReloadTracker.None;

    /// <summary>
    /// Gets whether the player can reload this firearm.
    /// </summary>
    /// <remarks>
    /// Mirrors the server checks of the firearm's ammo manager without starting the reload.
    /// </remarks>
    public bool CanReload
    {
        get
        {
            if (IsReloadingOrUnloading || !ModulesIdle)
            {
                return false;
            }

            FirearmStatus status = Base.Status;
            switch (AmmoManagerModule)
            {
                case AutomaticAmmoManager automatic:
                    if (status.Ammo >= automatic.MaxAmmo && status.Flags.HasFlagFast(FirearmStatusFlags.Cocked) && status.Flags.HasFlagFast(FirearmStatusFlags.Chambered))
                    {
                        return false;
                    }

                    return status.Ammo != 0 || ReserveAmmo >= Mathf.Max(1, automatic._chamberSize);
                case ClipLoadedInternalMagAmmoManager clipLoaded:
                    return status.Ammo < clipLoaded.MaxAmmo && ReserveAmmo > 0;
                case TubularMagazineAmmoManager tubular:
                    return status.Ammo < tubular.MaxAmmo && ReserveAmmo > 0;
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// Gets whether the player can unload this firearm.
    /// </summary>
    /// <remarks>
    /// Mirrors the server checks of the firearm's ammo manager without starting the unload.
    /// </remarks>
    public bool CanUnload
    {
        get
        {
            if (IsReloadingOrUnloading || !ModulesIdle)
            {
                return false;
            }

            FirearmStatus status = Base.Status;
            return AmmoManagerModule switch
            {
                AutomaticAmmoManager or TubularMagazineAmmoManager => status.Ammo > 0,
                ClipLoadedInternalMagAmmoManager => status.Ammo > 0 || status.Flags.HasFlagFast(FirearmStatusFlags.MagazineInserted),
                _ => false,
            };
        }
    }

    /// <summary>
    /// Gets the firearm's ammo type.
    /// </summary>
    /// <remarks>
    /// May be <see cref="ItemType.None"/> if the firearm item is no longer valid or this firearm is <see cref="ParticleDisruptorItem"/>.
    /// </remarks>
    public ItemType AmmoType => IsDestroyed ? ItemType.None : Base.AmmoType;

    /// <summary>
    /// Gets or sets whether the firearm's hammer is cocked.
    /// </summary>
    /// <remarks>
    /// Every automatic firearm requires <see cref="Cocked"/> to be <see langword="true"/> and a chambered round to be fired.
    /// </remarks>
    public virtual bool Cocked
    {
        get => Base.Status.Flags.HasFlagFast(FirearmStatusFlags.Cocked);
        set => SetFlag(FirearmStatusFlags.Cocked, value);
    }

    /// <summary>
    /// Gets if the firearm fires from an open bolt, that means the chambers are not capable of storing any ammo in <see cref="ChamberedAmmo"/>.
    /// </summary>
    public virtual bool OpenBolt => AmmoManagerModule is AutomaticAmmoManager { _chamberSize: 0 };

    /// <summary>
    /// Gets the firerate with current attachment's modifiers applied.
    /// </summary>
    public float Firerate
    {
        get
        {
            IActionModule action = ActionModule;
            return action != null ? action.CyclicRate : 0;
        }
    }

    /// <summary>
    /// Gets or sets the attachments code for this firearm.<br/>
    /// Attachments code is a binary representation of <see cref="Attachments"/> which are enabled/disabled.<br/>
    /// For validation, see <see cref="ValidateAttachmentsCode(uint)"/>.
    /// </summary>
    public uint AttachmentsCode
    {
        get => Base.GetCurrentAttachmentsCode();
        set
        {
            uint code = Base.ValidateAttachmentsCode(value);
            FirearmStatus status = Base.Status;
            Base.ApplyAttachmentsCode(code, false);
            Base.Status = new FirearmStatus(status.Ammo, status.Flags, code);
        }
    }

    /// <summary>
    /// Gets or sets whether this firearm has inserted magazine.<br/>
    /// An empty magazine is inserted when set to <see langword="true"/>.<br/>
    /// Remaining ammo from the magazine is inserted back into player's inventory when removed.
    /// </summary>
    /// <remarks>
    /// Firearms with no external or internal magazine will always return <see langword="false"/>.
    /// </remarks>
    public virtual bool MagazineInserted
    {
        get => HasMagazine && Base.Status.Flags.HasFlagFast(FirearmStatusFlags.MagazineInserted);
        set
        {
            if (!HasMagazine)
            {
                Logger.Error($"Unable to set {nameof(MagazineInserted)} as this firearm has no magazine");
                return;
            }

            FirearmStatus status = Base.Status;
            if (status.Flags.HasFlagFast(FirearmStatusFlags.MagazineInserted) == value)
            {
                return;
            }

            if (value)
            {
                Base.Status = new FirearmStatus(status.Ammo, status.Flags | FirearmStatusFlags.MagazineInserted, status.Attachments);
                return;
            }

            int chambered = ChamberedAmmo;
            int toReturn = status.Ammo - chambered;
            if (toReturn > 0 && Base.Owner != null && AmmoType != ItemType.None)
            {
                Base.OwnerInventory.ServerAddAmmo(AmmoType, toReturn);
            }

            Base.Status = new FirearmStatus((byte)chambered, status.Flags & ~FirearmStatusFlags.MagazineInserted, status.Attachments);
        }
    }

    /// <summary>
    /// Gets or sets the stored ammo in a <b>ammo container</b> for this firearm.
    /// </summary>
    /// <remarks>
    /// Ammo in magazine beyond <see cref="MaxAmmo"/> when pickup up this firearm again from the ground is added back to player's inventory.
    /// </remarks>
    public virtual int StoredAmmo
    {
        get => Mathf.Max(0, Base.Status.Ammo - ChamberedAmmo);
        set => SetTotalAmmo(value + ChamberedAmmo);
    }

    /// <summary>
    /// Gets the maximum ammo the firearm can have in its <b>ammo container</b>. Attachment modifiers are taken into account when calculating it.
    /// </summary>
    public int MaxAmmo
    {
        get
        {
            return AmmoManagerModule switch
            {
                null => 0,
                AutomaticAmmoManager automatic => automatic.MaxAmmo - automatic.ChamberedAmount,
                TubularMagazineAmmoManager tubular => tubular.MaxAmmo - (Base.Status.Flags.HasFlagFast(FirearmStatusFlags.Cocked) ? tubular.ChamberedRounds : 0),
                IAmmoManagerModule module => module.MaxAmmo,
            };
        }
    }

    /// <summary>
    /// Gets or sets the current ammo in the chamber.
    /// <para><see cref="OpenBolt"/> firearms do not use this value and take ammo directly from it's ammo container.</para>
    /// </summary>
    /// <remarks>
    /// The fork counts the chambered round inside the firearm's single ammo count, flagged as chambered.
    /// Setting this value adds or removes the chambered round(s) and keeps <see cref="StoredAmmo"/> unchanged.
    /// </remarks>
    public virtual int ChamberedAmmo
    {
        get
        {
            if (AmmoManagerModule is not AutomaticAmmoManager automatic)
            {
                return 0;
            }

            return Mathf.Min(Base.Status.Ammo, automatic.ChamberedAmount);
        }

        set
        {
            if (AmmoManagerModule is not AutomaticAmmoManager automatic || automatic._chamberSize == 0)
            {
                Logger.Error($"Unable to set {nameof(ChamberedAmmo)} as this firearm has no chamber");
                return;
            }

            int stored = StoredAmmo;
            bool chambered = value > 0;
            FirearmStatus status = Base.Status;
            FirearmStatusFlags flags = chambered ? status.Flags | FirearmStatusFlags.Chambered : status.Flags & ~FirearmStatusFlags.Chambered;
            int total = stored + (chambered ? automatic._chamberSize : 0);
            Base.Status = new FirearmStatus((byte)Mathf.Clamp(total, 0, byte.MaxValue), flags, status.Attachments);
        }
    }

    /// <summary>
    /// Gets the maximum ammo in chamber.
    /// </summary>
    /// <remarks>
    /// Read-only: the fork fixes the chamber size when the firearm's modules are created.
    /// </remarks>
    public virtual int ChamberMax => AmmoManagerModule is AutomaticAmmoManager automatic ? automatic._chamberSize : 0;

    /// <summary>
    /// Gets or sets whether the firearm's flashlight attachment is enabled and is emitting light.
    /// </summary>
    public bool FlashlightEnabled
    {
        get => Base.Status.Flags.HasFlagFast(FirearmStatusFlags.FlashlightEnabled) && Base.HasAdvantageFlag(AttachmentDescriptiveAdvantages.Flashlight);
        set
        {
            if (!Base.HasAdvantageFlag(AttachmentDescriptiveAdvantages.Flashlight))
            {
                return;
            }

            SetFlag(FirearmStatusFlags.FlashlightEnabled, value);
        }
    }

    /// <summary>
    /// All attachment used by this firearm.<br/>
    /// <b>Set the attachments status using <see cref="AttachmentsCode"/></b>
    /// </summary>
    public Attachment[] Attachments => Base.Attachments;

    /// <summary>
    /// Gets all available attachments names of this firearms.
    /// </summary>
    public IEnumerable<AttachmentName> AvailableAttachmentsNames
    {
        get
        {
            foreach (Attachment attachment in Attachments)
            {
                yield return attachment.Name;
            }
        }
    }

    /// <summary>
    /// Gets all enabled attachments of this firearm.
    /// </summary>
    public IEnumerable<Attachment> ActiveAttachments
    {
        get
        {
            foreach (Attachment attachment in Attachments)
            {
                if (attachment.IsEnabled)
                {
                    yield return attachment;
                }
            }
        }
    }

    /// <summary>
    /// Module for the firearm's ammo (magazine, cylinder, tube) and reloading.
    /// </summary>
    protected IAmmoManagerModule AmmoManagerModule => Base.AmmoManagerModule;

    /// <summary>
    /// Module for firearm's action (trigger, hammer, chamber).
    /// </summary>
    protected IActionModule ActionModule => Base.ActionModule;

    /// <summary>
    /// Gets whether the firearm uses a magazine that can be inserted and removed.
    /// </summary>
    protected virtual bool HasMagazine => AmmoManagerModule is AutomaticAmmoManager or ClipLoadedInternalMagAmmoManager;

    private bool ModulesIdle
    {
        get
        {
            IEquipperModule equipper = Base.EquipperModule;
            IActionModule action = ActionModule;
            IAmmoManagerModule ammo = AmmoManagerModule;
            return equipper != null && equipper.Standby && action != null && action.Standby && ammo != null && ammo.Standby;
        }
    }

    private int ReserveAmmo
    {
        get
        {
            if (ConfigFile.ServerConfig.GetBool("infinite_ammo"))
            {
                return byte.MaxValue;
            }

            Inventory? inventory = Base.Owner != null ? Base.OwnerInventory : null;
            return inventory != null ? inventory.GetCurAmmo(Base.AmmoType) : 0;
        }
    }

    /// <summary>
    /// Gets whether the provided attachments code is valid and can be applied.
    /// </summary>
    /// <param name="code">The code to validate.</param>
    /// <returns>Whether the code is valid and can be applied.</returns>
    public bool CheckAttachmentsCode(uint code) => ValidateAttachmentsCode(code) == code;

    /// <summary>
    /// Gets whether the provided attachment names are valid together and only 1 belongs to each category.
    /// </summary>
    /// <param name="attachments">Attachment names.</param>
    /// <returns>Whether the attachments can be applied together.</returns>
    public bool CheckAttachmentsCode(params AttachmentName[] attachments)
    {
        uint rawCode = GetCodeFromAttachmentNamesRaw(attachments);

        return rawCode == ValidateAttachmentsCode(rawCode);
    }

    /// <summary>
    /// Gets validated attachments code.
    /// Validation of the code is following:
    /// <list type="bullet">
    /// <item>Only 1 attachments from the same <see cref="AttachmentSlot"/> is applied. If multiple ones are enabled, only the first one is selected.</item>
    /// <item><see cref="AttachmentSlot"/> without any attachents assigned sets the first one to enabled.</item>
    /// </list>
    /// </summary>
    /// <param name="code">The code to be validated.</param>
    /// <returns>Validated code with missing attachments added for category and only 1 attachment per category selected.</returns>
    public uint ValidateAttachmentsCode(uint code) => Base.ValidateAttachmentsCode(code);

    /// <inheritdoc cref="ValidateAttachmentsCode(uint)"/>
    /// <param name="attachments">Array of attachment names to be applied and validated.</param>
    public uint ValidateAttachmentsCode(params AttachmentName[] attachments)
    {
        return ValidateAttachmentsCode(GetCodeFromAttachmentNamesRaw(attachments));
    }

    /// <summary>
    /// Gets attachments code from <see cref="AttachmentName"/>s. This value is NOT validated.
    /// </summary>
    /// <param name="attachments">Attachment names.</param>
    /// <returns>Unchecked attachments code.</returns>
    public uint GetCodeFromAttachmentNamesRaw(AttachmentName[] attachments)
    {
        uint resultCode = 0;

        uint bin = 1;
        foreach (Attachment attachment in Attachments)
        {
            if (Array.IndexOf(attachments, attachment.Name) >= 0)
            {
                resultCode += bin;
            }

            bin *= 2;
        }

        return resultCode;
    }

    /// <summary>
    /// Reloads the firearm if <see cref="CanReload"/> is <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// The firearm must be equipped: the fork drives reloads through the equipped firearm's animator.
    /// </remarks>
    /// <returns>Whether the player started to reload.</returns>
    public bool Reload() => ServerRequestAmmoAction(RequestType.Reload);

    /// <summary>
    /// Unloads the firearm if <see cref="CanUnload"/> is <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// The firearm must be equipped: the fork drives unloads through the equipped firearm's animator.
    /// </remarks>
    /// <returns>Whether the player started the unload.</returns>
    public bool Unload() => ServerRequestAmmoAction(RequestType.Unload);

    /// <summary>
    /// An internal method to remove itself from the cache when the base object is destroyed.
    /// </summary>
    internal override void OnRemove()
    {
        base.OnRemove();
        Dictionary.Remove(Base);
    }

    /// <summary>
    /// Replaces the firearm's total ammo count, keeping flags and attachments.
    /// </summary>
    /// <param name="total">The new total, clamped to a byte.</param>
    protected void SetTotalAmmo(int total)
    {
        FirearmStatus status = Base.Status;
        Base.Status = new FirearmStatus((byte)Mathf.Clamp(total, 0, byte.MaxValue), status.Flags, status.Attachments);
    }

    /// <summary>
    /// Sets or clears one status flag; the fork resends the status to clients when it changes.
    /// </summary>
    /// <param name="flag">The flag.</param>
    /// <param name="value">Whether the flag is set.</param>
    protected void SetFlag(FirearmStatusFlags flag, bool value)
    {
        FirearmStatus status = Base.Status;
        FirearmStatusFlags flags = value ? status.Flags | flag : status.Flags & ~flag;
        if (flags != status.Flags)
        {
            Base.Status = new FirearmStatus(status.Ammo, flags, status.Attachments);
        }
    }

    private bool ServerRequestAmmoAction(RequestType request)
    {
        IAmmoManagerModule module = AmmoManagerModule;
        if (module == null)
        {
            Logger.Error($"Unable to {(request == RequestType.Reload ? "reload" : "unload")} this firearm as its {nameof(IAmmoManagerModule)} is null");
            return false;
        }

        if (!Base.IsEquipped)
        {
            return false;
        }

        bool started = request == RequestType.Reload ? module.ServerTryReload() : module.ServerTryUnload();
        if (started)
        {
            new RequestMessage(Serial, request).SendToAuthenticated();
        }

        return started;
    }
}
