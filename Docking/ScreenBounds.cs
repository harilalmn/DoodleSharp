using System;
using System.Windows;

namespace DoodleSharp.Docking;

/// <summary>
/// Keeps a restored floating panel reachable when the monitor it was saved on is no longer there.
///
/// <para>
/// Takes the desktop rectangle as a parameter rather than reading <see cref="SystemParameters"/>
/// itself, so the arithmetic is a plain function that can be tested — including the case everyone
/// gets wrong, a secondary monitor placed to the *left* of the primary, which makes the virtual
/// desktop's origin negative.
/// </para>
/// </summary>
internal static class ScreenBounds
{
    /// <summary>How much of a window's title bar must be reachable for it to count as grabbable.</summary>
    private const double MinVisibleWidth = 120;

    /// <summary>Height of the strip that has to be on screen — the caption the user drags.</summary>
    private const double CaptionHeight = 32;

    /// <summary>
    /// True when the window could not be dragged back into view: none of its caption, or only a
    /// sliver of it, lies on the desktop. A two-pixel edge is as stranded as none at all, which is
    /// why this asks for a grabbable width rather than merely testing for intersection.
    /// </summary>
    internal static bool IsStranded(Rect window, Rect desktop)
    {
        if (window.Width <= 0 || window.Height <= 0) return true;
        if (desktop.Width <= 0 || desktop.Height <= 0) return false;   // nothing sane to compare against

        var caption = new Rect(window.Left, window.Top, window.Width, Math.Min(CaptionHeight, window.Height));
        var visible = Rect.Intersect(desktop, caption);

        return visible.IsEmpty
            || visible.Width < Math.Min(MinVisibleWidth, window.Width)
            || visible.Height <= 0;
    }

    /// <summary>
    /// Moves a stranded window back onto the desktop, preserving as much of the user's intent as
    /// possible: it is clamped to the nearest edge rather than centred, so a panel parked on the right
    /// stays on the right. Oversized windows are shrunk to fit first.
    /// </summary>
    internal static Rect ClampToVirtualScreen(Rect window, Rect desktop)
    {
        if (desktop.Width <= 0 || desktop.Height <= 0) return window;
        if (!IsStranded(window, desktop)) return window;

        var width = Math.Min(window.Width, desktop.Width);
        var height = Math.Min(window.Height, desktop.Height);

        var left = Math.Clamp(window.Left, desktop.Left, desktop.Right - width);
        var top = Math.Clamp(window.Top, desktop.Top, desktop.Bottom - height);

        return new Rect(left, top, width, height);
    }

    /// <summary>The whole desktop across every monitor. Uses SystemParameters, never WinForms.</summary>
    internal static Rect VirtualScreen => new(
        SystemParameters.VirtualScreenLeft,
        SystemParameters.VirtualScreenTop,
        SystemParameters.VirtualScreenWidth,
        SystemParameters.VirtualScreenHeight);
}
