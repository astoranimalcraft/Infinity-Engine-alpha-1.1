// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Runtime.InteropServices;

namespace Infinity.DIK;

/// <summary>Buttons shown by a native message box.</summary>
public enum MessageBoxButtons
{
    Ok = 0x00000000,
    OkCancel = 0x00000001,
    AbortRetryIgnore = 0x00000002,
    YesNoCancel = 0x00000003,
    YesNo = 0x00000004,
    RetryCancel = 0x00000005,
}

/// <summary>Icon shown by a native message box.</summary>
public enum MessageBoxIcon
{
    None = 0,
    Error = 1,
    Question = 2,
    Warning = 3,
    Information = 4,
}

/// <summary>The button the player pressed in a native message box.</summary>
public enum MessageBoxResult
{
    None = 0,
    Ok = 1,
    Cancel = 2,
    Abort = 3,
    Retry = 4,
    Ignore = 5,
    Yes = 6,
    No = 7,
    TryAgain = 10,
    Continue = 11,
}

/// <summary>
/// Native desktop dialogs. On Windows this calls user32's MessageBoxW and blocks until the player answers,
/// which is what a startup notice or a fatal-error prompt needs. Other platforms write the message to the
/// console and return <see cref="MessageBoxResult.None"/> instead of throwing.
/// </summary>
public static class DialogTools
{
    /// <summary>True on Windows, where native dialogs are available.</summary>
    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>
    /// Shows a modal message box. Pass the game window handle as <paramref name="owner"/> to keep the dialog
    /// above the game; zero is fine for a startup message before the window is ready.
    /// </summary>
    public static MessageBoxResult Show(
        string text,
        string caption = "Infinity Engine",
        MessageBoxButtons buttons = MessageBoxButtons.Ok,
        MessageBoxIcon icon = MessageBoxIcon.Information,
        nint owner = 0)
        => Show(text, caption, buttons, icon, requiredClicks: 1, owner);

    /// <summary>Shows a message box whose affirmative button must be clicked <paramref name="requiredClicks"/> times.</summary>
    public static MessageBoxResult Show(
        string text,
        string caption,
        MessageBoxButtons buttons,
        MessageBoxIcon icon,
        int requiredClicks,
        nint owner = 0)
    {
        if (!IsSupported)
        {
            Console.WriteLine($"[{caption}] {text}");
            return MessageBoxResult.None;
        }

        if (requiredClicks <= 1)
            return ShowOnce(text, caption, buttons, icon, owner);

        MessageBoxResult result = MessageBoxResult.None;
        for (int click = 1; click <= requiredClicks; click++)
        {
            string progress = $"\n\nClique {click} de {requiredClicks} para confirmar.";
            result = ShowOnce(text + progress, caption, buttons, icon, owner);

            // Only the affirmative button counts toward the gate. Cancel, No, Retry, etc. dismiss at once.
            bool affirmative = result is MessageBoxResult.Ok or MessageBoxResult.Yes or MessageBoxResult.Retry
                or MessageBoxResult.TryAgain or MessageBoxResult.Continue;
            if (!affirmative)
                return result;
        }

        return result;
    }

    private static MessageBoxResult ShowOnce(
        string text,
        string caption,
        MessageBoxButtons buttons,
        MessageBoxIcon icon,
        nint owner)
    {
        uint type = (uint)buttons | ((uint)icon << 4);
        return (MessageBoxResult)MessageBoxW(owner, text, caption, type);
    }

    /// <summary>
    /// Shows a warning with a Yes/No choice. The affirmative answer must be clicked
    /// <paramref name="requiredClicks"/> times, which makes an accidental single click harmless.
    /// </summary>
    public static bool ConfirmWarning(
        string text,
        string caption = "Infinity Engine - Alerta",
        nint owner = 0,
        int requiredClicks = 2)
        => Show(text, caption, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, requiredClicks, owner) == MessageBoxResult.Yes;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(nint hwnd, string text, string caption, uint type);
}
