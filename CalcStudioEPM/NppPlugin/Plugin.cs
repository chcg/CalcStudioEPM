using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using NppPluginNET;
using CalcStudioEPM;

// This file provides the unmanaged DLL entry points Notepad++ requires.
// ReSharper disable UnusedMember.Global
namespace CalcStudioEPM
{
    internal class PluginDllEntry
    {
        // Cached once so that getName() always returns the same pointer for
        // the lifetime of the process. Allocating a new string on every call
        // would leak the previous allocation and could return an invalid pointer
        // if NPP stores and dereferences it later.
        private static readonly IntPtr _pluginNamePtr =
            Marshal.StringToHGlobalUni(Main.PluginName);

        [DllExport("isUnicode", CallingConvention = CallingConvention.Winapi)]
        public static bool IsUnicode() => true;

        // Diagnostic log — written during startup so we can see exactly where any crash occurs.
        // Written to %TEMP%\CalcStudioEPM_startup.log — safe to delete at any time.
        private static readonly string _logPath = Path.Combine(
            Path.GetTempPath(), "CalcStudioEPM_startup.log");

        private static void Log(string msg)
        {
            try { File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\r\n"); } catch { }
        }

        [DllExport("setInfo", CallingConvention = CallingConvention.Winapi)]
        public static void SetInfo(NppData notepadPlusData)
        {
            try
            {
                // Clear the log at the start of each NPP session so old entries don't accumulate.
                try { File.WriteAllText(_logPath, $"=== CalcStudioEPM startup {DateTime.Now} ===\r\n"); } catch { }

                Log("setInfo: start");

                // Register assembly resolver before ANY plugin code runs.
                AppDomain.CurrentDomain.AssemblyResolve += ResolvePluginAssembly;
                Log("setInfo: AssemblyResolve registered");

                // EPM Cloud requires TLS 1.2+. Notepad++.exe has no .NET config, so the CLR may
                // start with only the legacy protocols. Added to (not replacing) the process-wide
                // setting, which other .NET plugins share.
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
                    Log("setInfo: TLS set");
                }
                catch (Exception ex)
                {
                    Log("setInfo: TLS 1.3 not available: " + ex.Message);
                    try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
                }

                PluginBase.nppData = notepadPlusData;
                Log("setInfo: nppData set");

                Main.CommandMenuInit();
                Log("setInfo: CommandMenuInit done");
            }
            catch (Exception ex)
            {
                Log("setInfo: UNHANDLED EXCEPTION: " + ex);
            }
        }

        /// <summary>
        /// Resolves assemblies from the same directory as CalcStudioEPM.dll so that
        /// Newtonsoft.Json.dll (and any future dependency) is found even though the
        /// host process base directory is the Notepad++ install folder.
        ///
        /// Uses UnsafeLoadFrom rather than LoadFrom: files delivered via email, a zip
        /// download, or a network share get the Windows "Mark of the Web" flag, and
        /// plain LoadFrom refuses to load such a file under .NET Framework's legacy
        /// CAS sandboxing (NotSupportedException, HRESULT 0x80131515) unless the host
        /// process opts in via a loadFromRemoteSources app.config switch — which we
        /// cannot set for Notepad++.exe. UnsafeLoadFrom bypasses that sandboxing.
        /// </summary>
        private static Assembly ResolvePluginAssembly(object sender, ResolveEventArgs args)
        {
            string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string shortName  = new AssemblyName(args.Name).Name;
            string candidate  = Path.Combine(pluginDir, shortName + ".dll");
            return File.Exists(candidate) ? Assembly.UnsafeLoadFrom(candidate) : null;
        }

        [DllExport("getName", CallingConvention = CallingConvention.Winapi)]
        public static IntPtr GetName() => _pluginNamePtr;

        [DllExport("getFuncsArray", CallingConvention = CallingConvention.Winapi)]
        public static IntPtr GetFuncsArray(ref int nbF)
        {
            try
            {
                Log($"getFuncsArray: count={PluginBase._funcItems.Items.Count}");
                nbF = PluginBase._funcItems.Items.Count;
                return PluginBase._funcItems.NativePointer;
            }
            catch (Exception ex)
            {
                Log("getFuncsArray: FAILED: " + ex);
                nbF = 0;
                return IntPtr.Zero;
            }
        }

        [DllExport("messageProc", CallingConvention = CallingConvention.Winapi)]
        public static uint MessageProc(uint msg, IntPtr wParam, IntPtr lParam) => 1;

        // HandleProcessCorruptedStateExceptions lets us catch AccessViolationException
        // (an SEH exception from native code) — without it, AVs bypass catch{} and kill NPP.
        [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
        [DllExport("beNotified", CallingConvention = CallingConvention.Winapi)]
        public static void BeNotified(IntPtr notifyCode)
        {
            // A managed exception that escapes a DllExport terminates the host process.
            // Wrap everything so NPP is always safe even if plugin code throws.
            try
            {
                if (notifyCode == IntPtr.Zero) return;

                // Read 'code' at the correct native offset, bypassing C# struct layout mismatch.
                // Native NMHDR layout on x64: hwndFrom(8) + idFrom(8, UINT_PTR) + code(4) → offset 16
                // C# struct keeps idFrom as uint(4) so Marshal.PtrToStructure places code at offset 12.
                // Reading directly from the pointer at offset (2 * IntPtr.Size) gives the true value.
                uint code = (uint)Marshal.ReadInt32(notifyCode, 2 * IntPtr.Size);

                // Only log NPP notification codes (1000–1099) — Scintilla codes fire constantly
                // and logging every one causes severe overhead / exit lag.
                if (code >= 1000 && code < 1100)
                    Log($"beNotified: code={code}");

                // NPPN_TBMODIFICATION (1002) is the only window in which toolbar icons
                // can be added: NPP sends it just before it creates the toolbar, and
                // ToolBar::registerDynBtn ignores anything registered after that.
                // This is the same hook JsonTools, MarkdownViewer++ and ComparePlus use.
                if (code == (uint)NppMsg.NPPN_TBMODIFICATION)
                {
                    // NPP writes the real Windows command ID for each of our menu
                    // entries directly into the native FuncItem buffer it got from
                    // getFuncsArray — but never back into our managed copy. Refresh
                    // the managed copy from that buffer before SetToolBarIcon reads
                    // _cmdID, otherwise every lookup sees the placeholder value (0).
                    PluginBase._funcItems.RefreshItems();
                    Log("beNotified: NPPN_TBMODIFICATION — calling SetToolBarIcon");
                    try { Main.SetToolBarIcon(); Log("beNotified: SetToolBarIcon done"); }
                    catch (Exception ex) { Log("beNotified: SetToolBarIcon FAILED: " + ex); }
                }

                if (code == (uint)NppMsg.NPPN_READY)
                {
                    try { Main.OnNppReady(); } catch { }
                }

                if (code == (uint)NppMsg.NPPN_DARKMODECHANGED)
                {
                    try { Main.OnDarkModeChanged(); } catch (Exception ex) { Log("beNotified: OnDarkModeChanged FAILED: " + ex); }
                }

                if (code == (uint)NppMsg.NPPN_SHUTDOWN)
                {
                    try { Main.PluginCleanUp(); } catch { }
                }

                // Autocomplete reads its fields at their native offsets (ScNotification's layout
                // doesn't match x64), so it gets the raw pointer
                if (code >= 2000 && code < 2100)
                    AutoComplete.OnScintillaNotification(code, notifyCode);

                try
                {
                    var nc = (ScNotification)Marshal.PtrToStructure(notifyCode, typeof(ScNotification));
                    nc.nmhdr.code = code;   // patch with correctly-read value before dispatch
                    Main.OnNotification(nc);
                }
                catch { }
            }
            catch { }
        }
    }
}
