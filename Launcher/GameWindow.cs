using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MechaCommunityMod.Launcher;

internal enum GameWindowResult { Focused, NeedsActivation, TimedOut }

internal static class GameWindow
{
    // Steam creates the game process asynchronously. Keep the requesting launcher
    // alive until Unity has a visible, responsive window; Process.Start alone does
    // not transfer focus to the game.
    internal static async Task<GameWindowResult> WaitAndActivate(CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromMinutes(2))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var processes = Process.GetProcessesByName("Mechabellum");
            try
            {
                foreach (var process in processes)
                {
                    var window = FindWindow(process.Id);
                    if (window == IntPtr.Zero || IsHungAppWindow(window)) continue;
                    if (GetForegroundWindow() == window) return GameWindowResult.Focused;
                    AllowSetForegroundWindow((uint)process.Id);
                    if (IsIconic(window)) ShowWindowAsync(window, 9); // SW_RESTORE
                    SetForegroundWindow(window);
                    // Activation can be asynchronous while Unity processes messages.
                    for (var attempt = 0; attempt < 8; attempt++)
                    {
                        await Task.Delay(125, cancellationToken);
                        if (GetForegroundWindow() == window) return GameWindowResult.Focused;
                    }
                    // If Windows denies activation after the user switches apps,
                    // finish the handoff without repeatedly taking focus.
                    return GameWindowResult.NeedsActivation;
                }
            }
            finally { foreach (var process in processes) process.Dispose(); }
            await Task.Delay(250, cancellationToken);
        }
        return GameWindowResult.TimedOut;
    }

    internal static IntPtr FindWindow(int processId)
    {
        var found = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner != (uint)processId || !IsWindowVisible(window) || GetWindow(window, 4) != IntPtr.Zero)
                return true; // GW_OWNER: skip owned popups and dialogs.
            if (!IsIconic(window) && (!GetClientRect(window, out var bounds) || bounds.Right < 320 || bounds.Bottom < 200))
                return true;
            found = window;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private delegate bool EnumWindowCallback(IntPtr window, IntPtr parameter);
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsHungAppWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out NativeRect bounds);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint processId);
}
