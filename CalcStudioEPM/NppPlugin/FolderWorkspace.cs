using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;

namespace NppPluginNET
{
    /// <summary>
    /// Adds and removes folders in Notepad++'s "Folder as Workspace" panel.
    ///
    /// Notepad++ has no plugin message for this (Notepad_plus_msgs.h has none, and the
    /// WM_COPYDATA command-line hand-over ignores senders inside its own process). So we
    /// use the two paths Notepad++ itself implements:
    ///
    ///  • Add — a simulated drag-and-drop. WM_DROPFILES → Notepad_plus::dropFiles(), which
    ///    for dropped folders calls launchFileBrowser(folders, "") without fromScratch: the
    ///    folder becomes a new root, existing roots stay, a folder that is already a root is
    ///    a silent no-op, and the panel is shown. Only when config.xml's
    ///    MISC/isFolderDroppedOpenFiles is "no" (the default) — otherwise a drop opens every
    ///    file in the folder, so <see cref="AddFolder"/> refuses.
    ///
    ///  • Remove — the panel's own "Remove" command (IDM_FILEBROWSER_REMOVEROOTFOLDER = 3511)
    ///    on the selected root. Roots are matched by full path, read through the panel's own
    ///    TVN_GETINFOTIP handler (the root tooltip is the root path).
    /// </summary>
    public static class FolderWorkspace
    {
        private const int ID_FILEBROWSERTREEVIEW            = 3531;   // fileBrowser_rc.h
        private const int IDM_FILEBROWSER_REMOVEROOTFOLDER  = 3511;

        /// <summary>Adds <paramref name="folder"/> as a workspace root. False when a drop would open its files instead.</summary>
        public static bool AddFolder(IntPtr nppHwnd, IntPtr editorHwnd, string folder)
        {
            if (DropOpensFilesInsteadOfWorkspace(nppHwnd))
            {
                Log?.Invoke("FolderWorkspace: Notepad++ is set to open a dropped folder's files — not dropping");
                return false;
            }

            string path = Normalize(folder);

            // Drop point: the middle of the editor, in Notepad++ client coordinates —
            // dropFiles() ignores drops that land on no child window.
            var pt = new POINT();
            if (editorHwnd != IntPtr.Zero && GetClientRect(editorHwnd, out RECT rc))
            {
                pt.X = (rc.Left + rc.Right) / 2;
                pt.Y = (rc.Top + rc.Bottom) / 2;
                MapWindowPoints(editorHwnd, nppHwnd, ref pt, 1);
            }

            // HDROP = DROPFILES header + double-NUL-terminated UTF-16 path list.
            // Notepad++ frees it with DragFinish once it has read it.
            byte[] names  = Encoding.Unicode.GetBytes(path + "\0\0");
            int    header = Marshal.SizeOf(typeof(DROPFILES));
            IntPtr hDrop  = GlobalAlloc(GHND, (UIntPtr)(header + names.Length));
            if (hDrop == IntPtr.Zero) return false;

            IntPtr p = GlobalLock(hDrop);
            try
            {
                Marshal.StructureToPtr(new DROPFILES { pFiles = header, pt = pt, fNC = 0, fWide = 1 }, p, false);
                Marshal.Copy(names, 0, p + header, names.Length);
            }
            finally { GlobalUnlock(hDrop); }

            SendMessage(nppHwnd, WM_DROPFILES, hDrop, IntPtr.Zero);
            Log?.Invoke($"FolderWorkspace: dropped '{path}' at ({pt.X},{pt.Y})");
            return true;
        }

        /// <summary>True when <paramref name="folder"/> is currently a root of the panel.</summary>
        public static bool Contains(IntPtr nppHwnd, string folder) => FindRoot(nppHwnd, folder, out _, out _) != IntPtr.Zero;

        /// <summary>Removes <paramref name="folder"/> from the panel. False when it isn't a root (or the panel was never opened).</summary>
        public static bool RemoveFolder(IntPtr nppHwnd, string folder)
        {
            IntPtr root = FindRoot(nppHwnd, folder, out IntPtr tree, out IntPtr dialog);
            if (root == IntPtr.Zero) return false;

            SendMessage(tree, TVM_SELECTITEM, (IntPtr)TVGN_CARET, root);
            SendMessage(dialog, WM_COMMAND, (IntPtr)IDM_FILEBROWSER_REMOVEROOTFOLDER, IntPtr.Zero);
            return FindRoot(nppHwnd, folder, out _, out _) == IntPtr.Zero;
        }

        /// <summary>Full paths of all current workspace roots (empty when the panel was never opened).</summary>
        public static List<string> Roots(IntPtr nppHwnd)
        {
            var result = new List<string>();
            IntPtr tree = FindTree(nppHwnd);
            if (tree == IntPtr.Zero) return result;
            IntPtr dialog = GetParent(tree);
            for (IntPtr item = SendMessage(tree, TVM_GETNEXTITEM, (IntPtr)TVGN_ROOT, IntPtr.Zero);
                 item != IntPtr.Zero;
                 item = SendMessage(tree, TVM_GETNEXTITEM, (IntPtr)TVGN_NEXT, item))
            {
                string p = RootPath(tree, dialog, item);
                if (!string.IsNullOrEmpty(p)) result.Add(p);
            }
            return result;
        }

        // ------------------------------------------------------------------ //

        private static IntPtr FindRoot(IntPtr nppHwnd, string folder, out IntPtr tree, out IntPtr dialog)
        {
            tree   = FindTree(nppHwnd);
            dialog = tree == IntPtr.Zero ? IntPtr.Zero : GetParent(tree);
            if (tree == IntPtr.Zero) return IntPtr.Zero;

            string wanted     = Normalize(folder);
            string wantedName = Path.GetFileName(wanted);
            IntPtr byName     = IntPtr.Zero;
            int    nameHits   = 0;

            for (IntPtr item = SendMessage(tree, TVM_GETNEXTITEM, (IntPtr)TVGN_ROOT, IntPtr.Zero);
                 item != IntPtr.Zero;
                 item = SendMessage(tree, TVM_GETNEXTITEM, (IntPtr)TVGN_NEXT, item))
            {
                string path  = RootPath(tree, dialog, item);
                string label = ItemLabel(tree, item);
                Log?.Invoke($"FolderWorkspace: root label='{label}' path='{path}'");

                // Exact match on the full path (from the panel's tooltip)
                if (!string.IsNullOrEmpty(path) &&
                    string.Equals(Normalize(path), wanted, StringComparison.OrdinalIgnoreCase))
                    return item;

                // Fallback when the tooltip gives nothing: the folder name, if unambiguous
                if (string.IsNullOrEmpty(path) &&
                    string.Equals(label, wantedName, StringComparison.OrdinalIgnoreCase))
                {
                    byName = item;
                    nameHits++;
                }
            }
            if (nameHits == 1) return byName;
            if (nameHits > 1) Log?.Invoke($"FolderWorkspace: {nameHits} roots are named '{wantedName}' — not guessing which");
            return IntPtr.Zero;
        }

        /// <summary>Optional diagnostics sink (the plugin log).</summary>
        public static Action<string> Log;

        /// <summary>
        /// The panel's tree view (created the first time the panel is shown): the
        /// SysTreeView32 inside the "File Browser" dialog. Notepad++ creates the tree with
        /// control ID 0 rather than ID_FILEBROWSERTREEVIEW, so the dialog is the reliable anchor.
        /// </summary>
        private static IntPtr FindTree(IntPtr nppHwnd)
        {
            IntPtr found = IntPtr.Zero;
            var cls  = new StringBuilder(32);
            var text = new StringBuilder(64);
            EnumChildWindows(nppHwnd, (h, _) =>
            {
                cls.Clear();
                GetClassName(h, cls, cls.Capacity);
                if (cls.ToString() != "SysTreeView32") return true;

                IntPtr parent = GetParent(h);
                text.Clear();
                GetWindowText(parent, text, text.Capacity);
                if (GetDlgCtrlID(h) == ID_FILEBROWSERTREEVIEW || text.ToString() == "File Browser")
                {
                    found = h;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            Log?.Invoke($"FolderWorkspace: panel tree {(found == IntPtr.Zero ? "not found (panel never opened?)" : "found")}");
            return found;
        }

        /// <summary>The root item's label — the folder name only.</summary>
        private static string ItemLabel(IntPtr tree, IntPtr item)
        {
            IntPtr buf = Marshal.AllocHGlobal(520 * 2);
            try
            {
                var tvi = new TVITEMW { mask = TVIF_TEXT, hItem = item, pszText = buf, cchTextMax = 520 };
                return SendMessage(tree, TVM_GETITEMW, IntPtr.Zero, ref tvi) == IntPtr.Zero
                    ? null : Marshal.PtrToStringUni(buf);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        /// <summary>
        /// Asks the panel for a root item's tooltip, which it builds from the root's stored
        /// full path — the same text you see when hovering over the root.
        /// </summary>
        private static string RootPath(IntPtr tree, IntPtr dialog, IntPtr item)
        {
            var tvi = new TVITEMW { mask = TVIF_PARAM, hItem = item };
            if (SendMessage(tree, TVM_GETITEMW, IntPtr.Zero, ref tvi) == IntPtr.Zero) return null;

            var tip = new NMTVGETINFOTIPW
            {
                hdr        = new NMHDR { hwndFrom = tree, idFrom = (UIntPtr)ID_FILEBROWSERTREEVIEW, code = TVN_GETINFOTIPW },
                pszText    = IntPtr.Zero,
                cchTextMax = 0,
                hItem      = item,
                lParam     = tvi.lParam,
            };
            IntPtr pTip = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NMTVGETINFOTIPW)));
            try
            {
                Marshal.StructureToPtr(tip, pTip, false);
                SendMessage(dialog, WM_NOTIFY, (IntPtr)ID_FILEBROWSERTREEVIEW, pTip);
                tip = (NMTVGETINFOTIPW)Marshal.PtrToStructure(pTip, typeof(NMTVGETINFOTIPW));
                return tip.pszText == IntPtr.Zero ? null
                     : tip.cchTextMax > 0 ? Marshal.PtrToStringUni(tip.pszText, tip.cchTextMax)
                     : Marshal.PtrToStringUni(tip.pszText);
            }
            finally { Marshal.FreeHGlobal(pTip); }
        }

        /// <summary>
        /// Notepad++ setting "Open all files of folder instead of launching Folder as
        /// Workspace on folder dropping" — config.xml GUIConfig MISC/isFolderDroppedOpenFiles.
        /// </summary>
        public static bool DropOpensFilesInsteadOfWorkspace(IntPtr nppHwnd)
        {
            try
            {
                string configXml = Path.Combine(SettingsDir(nppHwnd), "config.xml");
                if (!File.Exists(configXml)) return false;
                var doc = new XmlDocument();
                doc.Load(configXml);
                var misc = doc.SelectSingleNode("/NotepadPlus/GUIConfigs/GUIConfig[@name='MISC']") as XmlElement;
                return string.Equals(misc?.GetAttribute("isFolderDroppedOpenFiles"), "yes", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>Notepad++'s settings folder (NPPM_GETNPPSETTINGSDIRPATH), else %AppData%\Notepad++.</summary>
        internal static string SettingsDir(IntPtr nppHwnd)
        {
            var sb = new StringBuilder(1024);
            if (SendMessage(nppHwnd, NPPM_GETNPPSETTINGSDIRPATH, (IntPtr)sb.Capacity, sb) != IntPtr.Zero &&
                sb.Length > 0 && Directory.Exists(sb.ToString()))
                return sb.ToString();
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Notepad++");
        }

        private static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            try { path = Path.GetFullPath(path); } catch { }
            return path.TrimEnd('\\', '/');
        }

        // ------------------------------------------------------------------ //
        //  Win32
        // ------------------------------------------------------------------ //

        private const uint WM_NOTIFY      = 0x004E;
        private const uint WM_COMMAND     = 0x0111;
        private const uint WM_DROPFILES   = 0x0233;
        private const uint NPPM_GETNPPSETTINGSDIRPATH = 2024 + 119;
        private const uint TVM_GETNEXTITEM = 0x110A;
        private const uint TVM_SELECTITEM  = 0x110B;
        private const uint TVM_GETITEMW    = 0x113E;
        private const int  TVGN_ROOT  = 0x0, TVGN_NEXT = 0x1, TVGN_CARET = 0x9;
        private const uint TVIF_PARAM = 0x4;
        private const uint TVIF_TEXT  = 0x1;
        private const int  TVN_GETINFOTIPW = -400 - 14;
        private const uint GHND = 0x0042;   // GMEM_MOVEABLE | GMEM_ZEROINIT

        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct RECT  { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct DROPFILES { public int pFiles; public POINT pt; public int fNC; public int fWide; }

        [StructLayout(LayoutKind.Sequential)]
        private struct NMHDR { public IntPtr hwndFrom; public UIntPtr idFrom; public int code; }

        [StructLayout(LayoutKind.Sequential)]
        private struct NMTVGETINFOTIPW
        {
            public NMHDR  hdr;
            public IntPtr pszText;
            public int    cchTextMax;
            public IntPtr hItem;
            public IntPtr lParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TVITEMW
        {
            public uint   mask;
            public IntPtr hItem;
            public uint   state, stateMask;
            public IntPtr pszText;
            public int    cchTextMax, iImage, iSelectedImage, cChildren;
            public IntPtr lParam;
        }

        private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32")] private static extern bool   EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr lParam);
        [DllImport("user32")] private static extern int    GetDlgCtrlID(IntPtr hwnd);
        [DllImport("user32")] private static extern IntPtr GetParent(IntPtr hwnd);
        [DllImport("user32", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder cls, int max);
        [DllImport("user32", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
        [DllImport("user32")] private static extern bool   GetClientRect(IntPtr hwnd, out RECT rc);
        [DllImport("user32")] private static extern int    MapWindowPoints(IntPtr from, IntPtr to, ref POINT pt, int count);
        [DllImport("user32")] private static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32")] private static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, ref TVITEMW lParam);
        [DllImport("user32", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, StringBuilder lParam);
        [DllImport("kernel32")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
        [DllImport("kernel32")] private static extern IntPtr GlobalLock(IntPtr h);
        [DllImport("kernel32")] private static extern bool   GlobalUnlock(IntPtr h);
    }
}
