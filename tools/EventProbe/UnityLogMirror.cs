using System;
using UnityEngine;

namespace EventProbe;

/// <summary>
/// Copies server console lines into the Unity log (<c>-logFile</c>). With the file console (<c>-key&lt;session&gt;</c>)
/// the console text is otherwise not readable anywhere, including LabAPI loader and event handler errors.
/// </summary>
public sealed class UnityLogMirror : IOutput
{
    public void Print(string text) => Debug.Log(text);

    public void Print(string text, ConsoleColor c) => Debug.Log(text);

    public void Print(string text, ConsoleColor c, Color rgbColor) => Debug.Log(text);
}
