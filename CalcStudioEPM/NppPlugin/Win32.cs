using System;
using System.Runtime.InteropServices;
using System.Text;

namespace NppPluginNET
{
    public class Win32
    {
        // SIZE_T is pointer-sized — UIntPtr matches on both x86 and x64.
        [DllImport("kernel32")]
        public static extern void MoveMemory(IntPtr dst, IntPtr src, UIntPtr size);

        // WPARAM and LPARAM are both pointer-sized on 64-bit Windows.
        // Using IntPtr avoids silent truncation of the upper 32 bits.
        // LRESULT (return) is likewise pointer-sized.

        [DllImport("user32")]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32")]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, ref ToolbarIcons lParam);

        [DllImport("user32")]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, ref ToolbarIconsWithDarkMode lParam);

        [DllImport("user32", CharSet = CharSet.Auto)]
        public static extern bool SetWindowText(IntPtr hWnd, string text);

        [DllImport("user32", CharSet = CharSet.Auto)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxLength);

        [DllImport("user32", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, StringBuilder lParam);

        // Returns the dialog control ID of a child window
        [DllImport("user32")]
        public static extern int GetDlgCtrlID(IntPtr hWnd);

        // ------------------------------------------------------------------ //
        //  Menu helpers — used by "Open as Workspace" to locate the command ID
        // ------------------------------------------------------------------ //

        [DllImport("user32")]
        public static extern IntPtr GetMenu(IntPtr hWnd);

        [DllImport("user32")]
        public static extern IntPtr GetSubMenu(IntPtr hMenu, int nPos);

        [DllImport("user32")]
        public static extern int GetMenuItemCount(IntPtr hMenu);

        [DllImport("user32")]
        public static extern int GetMenuItemID(IntPtr hMenu, int nPos);

        [DllImport("user32", CharSet = CharSet.Unicode)]
        public static extern int GetMenuString(IntPtr hMenu, uint uIDItem,
            StringBuilder lpString, int nMaxCount, uint uFlag);

        // PostMessage (async): returns before NPP processes the message.
        // Use instead of SendMessage when triggering a modal dialog from a
        // plugin command handler — SendMessage would deadlock.
        [DllImport("user32")]
        public static extern bool PostMessage(IntPtr hWnd, uint Msg,
            IntPtr wParam, IntPtr lParam);

        // ------------------------------------------------------------------ //
        //  Window enumeration — used to find child windows
        // ------------------------------------------------------------------ //

        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32")]
        public static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ToolbarIcons
    {
        public IntPtr hToolbarBmp;
        public IntPtr hToolbarIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ToolbarIconsWithDarkMode
    {
        public IntPtr hToolbarBmp;
        public IntPtr hToolbarIcon;
        public IntPtr hToolbarIconDarkMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NMHDR
    {
        public IntPtr hwndFrom;
        public uint   idFrom;   // Kept as uint to preserve ScNotification total marshal size.
        public uint   code;     // BeNotified reads this field via Marshal.ReadInt32 at the
                                // correct native offset (2 * IntPtr.Size) to bypass the
                                // 4-byte vs 8-byte UINT_PTR mismatch on x64.
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ScNotification
    {
        public NMHDR nmhdr;
        public IntPtr position;
        public int ch;
        public int modifiers;
        public int modificationType;
        public IntPtr text;
        public int length;
        public int linesAdded;
        public int message;
        public IntPtr wParam;
        public IntPtr lParam;
        public int line;
        public int foldLevelNow;
        public int foldLevelPrev;
        public int margin;
        public int listType;
        public int x;
        public int y;
        public int token;
        public int annotationLinesAdded;
        public bool updated;
        public bool listCompletionMethod;
        public int characterSource;
    }
}
