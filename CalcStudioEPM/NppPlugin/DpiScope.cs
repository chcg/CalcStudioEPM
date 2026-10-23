using System;
using System.Runtime.InteropServices;

namespace NppPluginNET
{
    /// <summary>
    /// Runs our WinForms dialogs as system-DPI-aware while Notepad++ itself stays
    /// per-monitor aware.
    ///
    /// Notepad++ (8.7+) is per-monitor DPI aware, so windows created on its UI thread are
    /// too. .NET Framework WinForms can't follow per-monitor DPI changes when hosted in
    /// another exe: it lays a dialog out for one DPI, and when the dialog lands on a
    /// monitor with a different scale factor Windows resizes the window while the text
    /// stays the same size — squashed, clipped, unreadable dialogs on mixed-DPI setups.
    ///
    /// Windows take the DPI awareness of the thread at the moment they're created, so we
    /// switch the thread to SYSTEM_AWARE for the duration of a plugin command and switch
    /// it back afterwards. Our dialogs are then laid out at the system DPI (AutoScaleMode.Dpi)
    /// and Windows scales the whole window uniformly on monitors with other scale factors.
    /// No-op on Windows versions without per-thread DPI contexts (before Windows 10 1607).
    /// </summary>
    public sealed class DpiScope : IDisposable
    {
        private static readonly IntPtr DPI_AWARENESS_CONTEXT_SYSTEM_AWARE = new IntPtr(-2);

        private IntPtr _previous;

        public static DpiScope SystemAware() => new DpiScope(DPI_AWARENESS_CONTEXT_SYSTEM_AWARE);

        private DpiScope(IntPtr context)
        {
            try { _previous = SetThreadDpiAwarenessContext(context); }
            catch (EntryPointNotFoundException) { _previous = IntPtr.Zero; }
        }

        public void Dispose()
        {
            if (_previous == IntPtr.Zero) return;
            try { SetThreadDpiAwarenessContext(_previous); }
            catch (EntryPointNotFoundException) { }
            _previous = IntPtr.Zero;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);
    }
}
