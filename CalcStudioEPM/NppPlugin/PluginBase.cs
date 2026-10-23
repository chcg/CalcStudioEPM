using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace NppPluginNET
{
    [StructLayout(LayoutKind.Sequential)]
    public struct NppData
    {
        public IntPtr _nppHandle;
        public IntPtr _scintillaMainHandle;
        public IntPtr _scintillaSecondHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct FuncItem
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string _itemName;
        public IntPtr _pFunc;
        public int _cmdID;
        public bool _init2Check;
        public IntPtr _pShKey;
    }

    public class FuncItems : IDisposable
    {
        readonly List<FuncItem> _funcItems = new List<FuncItem>();
        int _sizeFuncItem = Marshal.SizeOf(typeof(FuncItem));
        IntPtr _nativePointer;
        bool _disposed = false;

        public void Add(FuncItem funcItem)
        {
            int oldSize = _funcItems.Count * _sizeFuncItem;
            _funcItems.Add(funcItem);
            int newSize = _funcItems.Count * _sizeFuncItem;
            IntPtr newPointer = Marshal.AllocHGlobal(newSize);

            if (_nativePointer != IntPtr.Zero)
            {
                Win32.MoveMemory(newPointer, _nativePointer, (UIntPtr)oldSize);
                Marshal.FreeHGlobal(_nativePointer);
            }
            IntPtr ptrToLast = new IntPtr(newPointer.ToInt64() + oldSize);
            IntPtr pinned = Marshal.AllocHGlobal(_sizeFuncItem);
            Marshal.StructureToPtr(funcItem, pinned, false);
            Win32.MoveMemory(ptrToLast, pinned, (UIntPtr)_sizeFuncItem);
            Marshal.FreeHGlobal(pinned);
            _nativePointer = newPointer;
        }

        public IntPtr NativePointer => _nativePointer;
        public List<FuncItem> Items => _funcItems;

        /// <summary>
        /// Re-reads each <see cref="FuncItem"/> from the native buffer NPP owns and
        /// updates our managed copy.  Must be called after NPP has had a chance to
        /// assign real command IDs (NPPN_TBMODIFICATION / NPPN_READY) and before any
        /// code reads <see cref="FuncItem._cmdID"/> — NPP writes the assigned Windows
        /// command ID directly into the native buffer returned by getFuncsArray, but
        /// never back into our managed List&lt;FuncItem&gt;, so without this the
        /// managed copy's _cmdID stays stuck at the placeholder value we sent (0).
        /// </summary>
        public void RefreshItems()
        {
            for (int i = 0; i < _funcItems.Count; i++)
            {
                IntPtr ptrPos = new IntPtr(_nativePointer.ToInt64() + i * _sizeFuncItem);
                _funcItems[i] = (FuncItem)Marshal.PtrToStructure(ptrPos, typeof(FuncItem));
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_nativePointer != IntPtr.Zero) Marshal.FreeHGlobal(_nativePointer);
                _disposed = true;
            }
        }
    }

    public static class PluginBase
    {
        public static NppData nppData;
        public static FuncItems _funcItems = new FuncItems();

        public static void SetCommand(int index, string commandName, Action functionPointer, bool checkOnInit = false)
        {
            var fi = new FuncItem
            {
                _itemName = commandName,
                _pFunc = functionPointer == null ? IntPtr.Zero : Marshal.GetFunctionPointerForDelegate(functionPointer),
                _cmdID = 0,
                _init2Check = checkOnInit,
                _pShKey = IntPtr.Zero
            };
            _funcItems.Add(fi);
        }
    }

    /// <summary>
    /// Notepad++ messages and notifications, per Notepad_plus_msgs.h.
    /// Values cross-checked against the NppMsg enum compiled into JsonTools.dll and
    /// MarkdownViewerPlusPlus.dll (both built on the kbilsted NppPlugin template).
    /// </summary>
    public enum NppMsg : uint
    {
        // NPPMSG = WM_USER + 1000 = 0x0400 + 1000 = 2024
        NPPM_SETSTATUSBAR               = 2024 + 24,   // 2048 — wParam=section, lParam=TCHAR*
        NPPM_SAVECURRENTFILE            = 2024 + 38,   // 2062 — save current document
        NPPM_SAVEFILE                   = 2024 + 94,   // 2118 — lParam=TCHAR* full path; returns TRUE when saved
        NPPM_GETCURRENTSCINTILLA        = 2024 + 4,    // 2028 — lParam=int* (0=main view, 1=secondary)
        NPPM_ADDTOOLBARICON             = 2024 + 41,   // 2065 — legacy (NPPM_ADDTOOLBARICON_DEPRECATED)
        NPPM_GETFULLPATHFROMBUFFERID    = 2024 + 58,   // 2082 — wParam=BufferID, lParam=TCHAR*
        NPPM_GETCURRENTBUFFERID         = 2024 + 60,   // 2084 — returns current BufferID
        NPPM_GETBUFFERLANGTYPE          = 2024 + 64,   // 2088 — wParam=BufferID → returns LangType
        NPPM_DOOPEN                     = 2024 + 77,   // 2101 — lParam=TCHAR* path
        NPPM_GETLANGUAGENAME            = 2024 + 83,   // 2107 — wParam=LangType, lParam=TCHAR* (out)
        NPPM_ADDTOOLBARICON_FORDARKMODE = 2024 + 101,  // 2125 — NPP 8.0+; only valid during NPPN_TBMODIFICATION
        NPPM_ISDARKMODEENABLED          = 2024 + 107,  // 2131 — NPP 8.4.1+; returns TRUE when dark mode is on
        NPPM_GETDARKMODECOLORS          = 2024 + 108,  // 2132 — wParam=sizeof(NppDarkMode::Colors), lParam=Colors*
        NPPM_GETTOOLBARICONSETCHOICE    = 2024 + 118,  // 2142 — NPP 8.7+; 0..3 = Fluent variants, 4 = Standard icons

        // RUNCOMMAND_USER = WM_USER + 3000 = 4024 — "variable" getters, wParam=buffer len, lParam=TCHAR*
        NPPM_GETFULLCURRENTPATH         = 4024 + 1,    // 4025
        NPPM_GETCURRENTDIRECTORY        = 4024 + 2,    // 4026
        NPPM_GETFILENAME                = 4024 + 3,    // 4027
        NPPM_GETNAMEPART                = 4024 + 4,    // 4028
        NPPM_GETEXTPART                 = 4024 + 5,    // 4029
        NPPM_GETCURRENTWORD             = 4024 + 6,    // 4030
        NPPM_GETNPPDIRECTORY            = 4024 + 7,    // 4031
        NPPM_GETCURRENTLINE             = 4024 + 8,    // 4032
        NPPM_GETCURRENTCOLUMN           = 4024 + 9,    // 4033
        NPPM_GETFILENAMEATCURSOR        = 4024 + 11,   // 4035

        // Notification codes (sent via WM_NOTIFY nmhdr.code), NPPN_FIRST = 1000
        NPPN_READY                      = 1000 + 1,    // 1001 — NPP fully initialized (toolbar already built)
        NPPN_TBMODIFICATION             = 1000 + 2,    // 1002 — the ONLY moment toolbar icons can be added
        NPPN_FILEOPENED                 = 1000 + 4,    // 1004
        NPPN_FILEBEFOREOPEN             = 1000 + 6,    // 1006
        NPPN_FILESAVED                  = 1000 + 8,    // 1008
        NPPN_SHUTDOWN                   = 1000 + 9,    // 1009
        NPPN_BUFFERACTIVATED            = 1000 + 10,   // 1010 — idFrom = new BufferID
        NPPN_LANGCHANGED                = 1000 + 11,   // 1011 — language manually changed
        NPPN_DARKMODECHANGED            = 1000 + 27,   // 1027 — sent after NPP has re-themed (and rebuilt its toolbar)
    }

    public enum SciMsg : uint
    {
        SCI_GETTEXT         = 2182,
        SCI_GETTEXTLENGTH   = 2183,
        SCI_GETLENGTH       = 2006,
        SCI_GETEOLMODE      = 2030,   // 0 = CRLF, 1 = CR, 2 = LF
        SCI_BEGINUNDOACTION = 2078,
        SCI_ENDUNDOACTION   = 2079,
        SCI_GETCODEPAGE     = 2137,   // 65001 = UTF-8
        SCI_GETMODIFY       = 2159,   // non-zero = document has unsaved changes
        SCI_REPLACESEL      = 2170,   // lParam = char* (replaces selection / inserts at caret)
        SCI_REPLACETARGET   = 2194,   // wParam = length, lParam = char*
        SCI_GOTOPOS         = 2025,   // wParam = position (moves caret, scrolls into view)
        SCI_POSITIONFROMLINE = 2167,  // wParam = line (0-based) → position of its start
        SCI_ENSUREVISIBLEENFORCEPOLICY = 2234, // wParam = line — unfolds and scrolls to it
        SCI_GRABFOCUS       = 2400,
        SCI_POSITIONRELATIVE = 2670,  // wParam = position, lParam = characters to move
        SCI_SETTARGETRANGE  = 2686,   // wParam = start, lParam = end (byte positions)
    }
}
