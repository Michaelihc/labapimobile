using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace LabApi.Events.Patches;

/// <summary>
/// Identifies the IL body of a game method, so a patch that replaces or copies a native body only runs on the builds
/// whose body it was written for.
/// </summary>
/// <remarks>
/// Carl Mod servers that report the same game version are not one build: the official distribution of 0.0.4 and the
/// build with the deathmatch module differ in a few methods, and 0.0.5 changes more. A fingerprint covers opcodes, operands
/// (members by declaring type, name and parameter types, branch targets by instruction index) and exception blocks, so it
/// does not depend on metadata tokens. <c>nop</c> instructions are left out (they do nothing, and the server distributors'
/// own assembly patching inserts them), so a body that differs only by them has the same fingerprint. It is computed once
/// per patch class at startup.
/// </remarks>
internal static class NativeBody
{
    /// <summary>
    /// Computes the fingerprint of a method's IL.
    /// </summary>
    /// <param name="method">The method.</param>
    /// <returns>The fingerprint, or <see langword="null"/> when the method is missing or Harmony cannot read its IL.</returns>
    internal static string? Fingerprint(MethodBase? method)
    {
        if (method == null)
        {
            return null;
        }

        List<CodeInstruction> instructions;
        try
        {
            instructions = PatchProcessor.GetOriginalInstructions(method);
        }
        catch (Exception)
        {
            return null;
        }

        // Indices count only instructions other than nop; a label on a nop targets the next instruction.
        Dictionary<Label, int> targets = [];
        int index = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            foreach (Label label in instruction.labels)
            {
                targets[label] = index;
            }

            if (instruction.opcode != OpCodes.Nop)
            {
                index++;
            }
        }

        StringBuilder text = new();
        List<ExceptionBlock> nopBlocks = [];
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Nop)
            {
                // Exception block boundaries on a nop belong to the next instruction.
                nopBlocks.AddRange(instruction.blocks);
                continue;
            }

            text.Append(instruction.opcode.Name);
            AppendOperand(text, instruction.operand, targets);
            AppendBlocks(text, nopBlocks);
            nopBlocks.Clear();
            AppendBlocks(text, instruction.blocks);
            text.Append('\n');
        }

        AppendBlocks(text, nopBlocks);
        return Hash(text.ToString());
    }

    /// <summary>
    /// Identifies which known Carl Mod body a method has.
    /// </summary>
    /// <param name="method">The method.</param>
    /// <param name="standard">The fingerprint of the body in the official 0.0.4 distribution (no deathmatch module).</param>
    /// <param name="deathmatch">The fingerprint of the body in the 0.0.4 build with the deathmatch module.</param>
    /// <param name="version005">
    /// The fingerprint of the Carl Mod 0.0.5 body when it differs from both 0.0.4 bodies; <see langword="null"/> when 0.0.5 has
    /// one of them (it is then identified as that variant).
    /// </param>
    /// <param name="fingerprint">The method's fingerprint, for log messages.</param>
    /// <returns>The variant, or <see cref="BodyVariant.Unknown"/>.</returns>
    internal static BodyVariant Identify(MethodBase? method, string standard, string deathmatch, string? version005, out string? fingerprint)
    {
        fingerprint = Fingerprint(method);
        if (fingerprint == null)
        {
            return BodyVariant.Unknown;
        }

        if (fingerprint == standard)
        {
            return BodyVariant.Standard;
        }

        if (fingerprint == deathmatch)
        {
            return BodyVariant.Deathmatch;
        }

        return fingerprint == version005 ? BodyVariant.Version005 : BodyVariant.Unknown;
    }

    /// <summary>
    /// Identifies which known Carl Mod body a method has, for a method whose body is the same in both 0.0.4 builds.
    /// </summary>
    /// <param name="method">The method.</param>
    /// <param name="carlMod004">The fingerprint of the body in both Carl Mod 0.0.4 builds (identified as <see cref="BodyVariant.Standard"/>).</param>
    /// <param name="version005">The fingerprint of the Carl Mod 0.0.5 body.</param>
    /// <param name="fingerprint">The method's fingerprint, for log messages.</param>
    /// <returns>The variant, or <see cref="BodyVariant.Unknown"/>.</returns>
    internal static BodyVariant Identify(MethodBase? method, string carlMod004, string version005, out string? fingerprint) =>
        Identify(method, carlMod004, carlMod004, version005, out fingerprint);

    /// <summary>
    /// Builds the skip reason for a patch whose target body is not a known one.
    /// </summary>
    /// <param name="method">The target method.</param>
    /// <param name="fingerprint">Its fingerprint.</param>
    /// <param name="consequence">What is unavailable as a result.</param>
    /// <returns>The reason.</returns>
    internal static string UnknownBody(MethodBase? method, string? fingerprint, string consequence) =>
        $"{Describe(method)} differs from the Carl Mod builds LabAPI-Mobile knows (0.0.4, 0.0.5; IL {fingerprint ?? "missing or unreadable"}); {consequence}";

    /// <summary>
    /// Computes a fingerprint of a method's raw IL bytes. Only meaningful for one exact assembly, because the bytes contain
    /// metadata tokens; used for bodies Harmony cannot read.
    /// </summary>
    /// <param name="method">The method.</param>
    /// <returns>The fingerprint, or <see langword="null"/> when the method has no body.</returns>
    internal static string? RawFingerprint(MethodBase? method)
    {
        byte[]? il = method?.GetMethodBody()?.GetILAsByteArray();
        if (il == null)
        {
            return null;
        }

        ulong hash = 14695981039346656037UL;
        foreach (byte b in il)
        {
            hash = (hash ^ b) * 1099511628211UL;
        }

        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Gets a readable name for log messages.
    /// </summary>
    /// <param name="method">The method.</param>
    /// <returns>"Type.Method".</returns>
    internal static string Describe(MethodBase? method) => method == null ? "(missing method)" : method.DeclaringType?.FullName + "." + method.Name;

    private static void AppendOperand(StringBuilder text, object? operand, Dictionary<Label, int> targets)
    {
        switch (operand)
        {
            case null:
                return;
            case MethodBase method:
                text.Append(' ').Append(method.DeclaringType).Append("::").Append(method.Name);
                if (method.IsGenericMethod)
                {
                    text.Append('<');
                    foreach (Type argument in method.GetGenericArguments())
                    {
                        text.Append(argument).Append(',');
                    }

                    text.Append('>');
                }

                text.Append('(');
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    text.Append(parameter.ParameterType).Append(',');
                }

                text.Append(')');
                return;
            case FieldInfo field:
                text.Append(' ').Append(field.DeclaringType).Append("::").Append(field.Name);
                return;
            case Type type:
                text.Append(' ').Append(type);
                return;
            case Label label:
                AppendTarget(text, label, targets);
                return;
            case Label[] labels:
                foreach (Label target in labels)
                {
                    AppendTarget(text, target, targets);
                }

                return;
            case LocalBuilder local:
                text.Append(" V").Append(local.LocalIndex).Append(':').Append(local.LocalType);
                return;
            case string value:
                text.Append(" \"").Append(value).Append('"');
                return;
            case IConvertible value:
                text.Append(' ').Append(value.ToString(CultureInfo.InvariantCulture));
                return;
            default:
                text.Append(' ').Append(operand);
                return;
        }
    }

    private static void AppendBlocks(StringBuilder text, List<ExceptionBlock> blocks)
    {
        foreach (ExceptionBlock block in blocks)
        {
            text.Append(" {").Append(block.blockType).Append(' ').Append(block.catchType).Append('}');
        }
    }

    private static void AppendTarget(StringBuilder text, Label label, Dictionary<Label, int> targets) =>
        text.Append(" L").Append(targets.TryGetValue(label, out int index) ? index : -1);

    private static string Hash(string text)
    {
        // FNV-1a, 64 bit.
        ulong hash = 14695981039346656037UL;
        foreach (char c in text)
        {
            hash = (hash ^ c) * 1099511628211UL;
        }

        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// A known Carl Mod body of a game method.
/// </summary>
internal enum BodyVariant
{
    /// <summary>Not a body LabAPI-Mobile knows.</summary>
    Unknown,

    /// <summary>
    /// The body of the official 0.0.4 server distribution (no deathmatch module); for a method that is the same in both 0.0.4
    /// builds, the body of both.
    /// </summary>
    Standard,

    /// <summary>The body of the 0.0.4 build with the deathmatch module.</summary>
    Deathmatch,

    /// <summary>The body of Carl Mod 0.0.5, where it differs from both 0.0.4 bodies.</summary>
    Version005,
}
