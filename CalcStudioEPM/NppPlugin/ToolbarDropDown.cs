using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace NppPluginNET
{
    /// <summary>
    /// Gives our toolbar buttons a split drop-down arrow — the same BTNS_DROPDOWN
    /// style Notepad++ uses for its own "Show All Characters" button.
    ///
    /// Notepad++ has no plugin API for this (plugin buttons are always plain
    /// BTNS_BUTTON), so we work at the Win32 level:
    ///  • set BTNS_DROPDOWN on our buttons inside NPP's ToolbarWindow32;
    ///  • subclass NPP's main window — the toolbar's notification parent — to catch
    ///    TBN_DROPDOWN for our buttons and show their menus. Every other message,
    ///    including NPP's own TBN_DROPDOWN, passes straight through.
    ///
    /// A button can also get a separator just before it, so our group is visually
    /// split from the buttons of plugins that loaded before us.
    ///
    /// NPP destroys and recreates its toolbar whenever the icon set or size changes
    /// (ToolBar::reset), which drops all of that. The subclass notices the cached
    /// toolbar handle has died and re-applies everything on the new toolbar.
    /// </summary>
    public static class ToolbarDropDown
    {
        public struct Item
        {
            public string Text;
            public bool   Checked;
            public Item(string text, bool isChecked) { Text = text; Checked = isChecked; }
        }

        private sealed class Registration
        {
            public int CmdId;
            public Func<List<Item>> GetItems;
            public Action<int>      OnPick;
            public string EmptyText;
            public bool   SeparatorBefore;
        }

        private static readonly List<Registration> _registrations = new List<Registration>();
        private static IntPtr _nppHwnd;
        private static IntPtr _toolbar;          // NPP toolbar holding our buttons; Zero = not found
        private static readonly Dictionary<int, bool> _enabled = new Dictionary<int, bool>();   // cmdId → wanted state

        private static SubclassProc _subclassProc; // kept alive — NPP calls it via a native pointer
        private static uint _reapplyMsg;
        private static bool _reapplyPending;
        private static bool _nppWantsItemDraw;   // NPP asked for item-stage custom draw this paint
        private static readonly UIntPtr SubclassId = (UIntPtr)0x45504D31; // "EPM1"

        /// <summary>
        /// Adds the drop-down arrow to the toolbar button with <paramref name="cmdId"/>.
        /// Must be called on NPP's UI thread once the toolbar exists (NPPN_READY); call
        /// it once per button.
        /// <paramref name="getItems"/> builds the menu each time the arrow is clicked;
        /// <paramref name="onPick"/> receives the index of the chosen item.
        /// </summary>
        public static void Attach(IntPtr nppHwnd, int cmdId, Func<List<Item>> getItems,
                                  Action<int> onPick, string emptyText, bool separatorBefore)
        {
            if (cmdId <= 0 || _registrations.Exists(r => r.CmdId == cmdId)) return;

            if (_subclassProc == null)
            {
                _nppHwnd    = nppHwnd;
                _reapplyMsg = RegisterWindowMessage("CalcStudioEPM.ToolbarDropDown.Reapply");
                _subclassProc = SubclassWndProc;
                if (!SetWindowSubclass(_nppHwnd, _subclassProc, SubclassId, IntPtr.Zero))
                {
                    _subclassProc = null;
                    return;
                }
            }

            _registrations.Add(new Registration
            {
                CmdId           = cmdId,
                GetItems        = getItems,
                OnPick          = onPick,
                EmptyText       = emptyText,
                SeparatorBefore = separatorBefore,
            });
            ApplyCustomizations();
        }

        /// <summary>Removes the main-window subclass. Call on NPPN_SHUTDOWN.</summary>
        public static void Detach()
        {
            if (_subclassProc == null) return;
            RemoveWindowSubclass(_nppHwnd, _subclassProc, SubclassId);
            _subclassProc = null;
            _registrations.Clear();
        }

        private static IntPtr SubclassWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
                                              UIntPtr idSubclass, IntPtr refData)
        {
            try
            {
                if (msg == WM_NOTIFY && lParam != IntPtr.Zero)
                {
                    // NMHDR: hwndFrom, idFrom (both pointer-sized), then code.
                    // NMTOOLBAR.iItem follows the (padded) NMHDR.
                    IntPtr from = Marshal.ReadIntPtr(lParam);
                    int    code = Marshal.ReadInt32(lParam, 2 * IntPtr.Size);

                    if (code == NM_CUSTOMDRAW && from == _toolbar && _toolbar != IntPtr.Zero)
                        return CustomDraw(hWnd, msg, wParam, lParam);

                    if (code == TBN_DROPDOWN && from == _toolbar && _toolbar != IntPtr.Zero)
                    {
                        int item = Marshal.ReadInt32(lParam, 3 * IntPtr.Size);
                        Registration reg = _registrations.Find(r => r.CmdId == item);
                        if (reg != null)
                        {
                            ShowMenu(reg);
                            return (IntPtr)TBDDRET_DEFAULT;
                        }
                    }

                    // Toolbar was recreated (icon set/size change) — re-apply once the
                    // current message is done, so we never restyle mid-rebuild or mid-paint.
                    if (_toolbar != IntPtr.Zero && !_reapplyPending && !IsWindow(_toolbar))
                    {
                        _reapplyPending = true;
                        PostMessage(hWnd, _reapplyMsg, IntPtr.Zero, IntPtr.Zero);
                    }
                }
                else if (msg == _reapplyMsg && _reapplyMsg != 0)
                {
                    _reapplyPending = false;
                    ApplyCustomizations();
                    return IntPtr.Zero;
                }
            }
            catch { /* never let an exception escape into NPP's window procedure */ }

            return DefSubclassProc(hWnd, msg, wParam, lParam);
        }

        /// <summary>
        /// Redraws the drop-down arrow of our buttons as a crisp triangle. comctl32 draws
        /// it as a Marlett font glyph, which ClearType renders with coloured fringes
        /// (very visible on the dark toolbar). Everything else is left to NPP: its own
        /// custom draw (dark mode colours, hot/checked backgrounds) runs first, and item
        /// notifications it didn't ask for are not passed to it — its item stage applies
        /// dark colours unconditionally.
        /// </summary>
        private static IntPtr CustomDraw(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            var cd = (NMCUSTOMDRAW)Marshal.PtrToStructure(lParam, typeof(NMCUSTOMDRAW));
            switch (cd.dwDrawStage)
            {
                case CDDS_PREPAINT:
                {
                    long lr = DefSubclassProc(hWnd, msg, wParam, lParam).ToInt64();
                    _nppWantsItemDraw = (lr & CDRF_NOTIFYITEMDRAW) != 0;
                    return (IntPtr)(lr | CDRF_NOTIFYITEMDRAW);
                }
                case CDDS_ITEMPREPAINT:
                {
                    long lr = _nppWantsItemDraw ? DefSubclassProc(hWnd, msg, wParam, lParam).ToInt64() : CDRF_DODEFAULT;
                    if (_registrations.Exists(r => r.CmdId == cd.dwItemSpec.ToInt64()))
                        lr |= CDRF_NOTIFYPOSTPAINT;
                    return (IntPtr)lr;
                }
                case CDDS_ITEMPOSTPAINT:
                    if (_registrations.Exists(r => r.CmdId == cd.dwItemSpec.ToInt64()))
                    {
                        DrawArrow(cd.hdc, (int)cd.dwItemSpec.ToInt64(), (cd.uItemState & CDIS_DISABLED) != 0);
                        return IntPtr.Zero;
                    }
                    break;
            }
            return _nppWantsItemDraw || (cd.dwDrawStage & CDDS_ITEM) == 0
                ? DefSubclassProc(hWnd, msg, wParam, lParam)
                : IntPtr.Zero;
        }

        private static void DrawArrow(IntPtr hdc, int cmdId, bool disabled)
        {
            int index = SendMessage(_toolbar, TB_COMMANDTOINDEX, (IntPtr)cmdId, IntPtr.Zero).ToInt32();
            var drop = new RECT();
            if (index < 0 || SendMessage(_toolbar, TB_GETITEMDROPDOWNRECT, (IntPtr)index, ref drop) == IntPtr.Zero)
                return;

            int dpi = 96;
            try { dpi = (int)GetDpiForWindow(_toolbar); } catch (EntryPointNotFoundException) { }
            if (dpi <= 0) dpi = 96;
            int h = Math.Max(3, (4 * dpi + 48) / 96);   // triangle height; width = 2h - 1
            int cx = (drop.left + drop.right) / 2;
            int cy = (drop.top + drop.bottom) / 2;

            // Cover comctl32's glyph with the button background right beside it (plain,
            // hot or pressed — whatever was just painted there), inside the hot border.
            int bg = GetPixel(hdc, drop.left + 2, cy);
            if (bg == CLR_INVALID) return;
            var erase = new RECT
            {
                left   = drop.left + 2,
                right  = drop.right - 2,
                top    = Math.Max(drop.top + 2, cy - h - 2),
                bottom = Math.Min(drop.bottom - 2, cy + h + 2),
            };
            FillSolid(hdc, erase, bg);

            // Same colours NPP's own arrow uses: light text on the dark toolbar, black on the light one
            int r = bg & 0xFF, g = (bg >> 8) & 0xFF, b = (bg >> 16) & 0xFF;
            bool darkBg = r * 299 + g * 587 + b * 114 < 128000;
            int fg = darkBg ? (disabled ? 0x808080 : 0xE0E0E0)
                            : (disabled ? 0xA0A0A0 : 0x000000);

            int top = cy - h / 2;
            for (int i = 0; i < h; i++)
                FillSolid(hdc, new RECT { left = cx - (h - 1) + i, right = cx + h - i, top = top + i, bottom = top + i + 1 }, fg);
        }

        private static void FillSolid(IntPtr hdc, RECT rc, int colorRef)
        {
            IntPtr brush = CreateSolidBrush(colorRef);
            FillRect(hdc, ref rc, brush);
            DeleteObject(brush);
        }

        /// <summary>
        /// Enables or greys out one of our toolbar buttons; kept when NPP recreates the
        /// toolbar (e.g. after an icon-set change).
        /// </summary>
        public static void SetEnabled(int cmdId, bool enabled)
        {
            _enabled[cmdId] = enabled;
            if (_toolbar == IntPtr.Zero) _toolbar = FindToolbar(cmdId);
            if (_toolbar != IntPtr.Zero)
                SendMessage(_toolbar, TB_ENABLEBUTTON, (IntPtr)cmdId, (IntPtr)(enabled ? 1 : 0));
        }

        /// <summary>
        /// Swaps new images into NPP's live toolbar for the given buttons, in place of the
        /// bitmaps NPP added when it built the toolbar (TB_ADDBITMAP copies them, so later
        /// changes to the source bitmaps don't show until it rebuilds). Only meaningful in
        /// "Standard icons" mode, where the toolbar's image list holds those bitmaps.
        /// </summary>
        public static void ReplaceButtonImages(IEnumerable<(int CmdId, IntPtr HBitmap)> images)
        {
            if (_nppHwnd == IntPtr.Zero) return;
            foreach (var (cmdId, hbmp) in images)
            {
                if (_toolbar == IntPtr.Zero || !IsWindow(_toolbar)) _toolbar = FindToolbar(cmdId);
                if (_toolbar == IntPtr.Zero) return;

                IntPtr himl = SendMessage(_toolbar, TB_GETIMAGELIST, IntPtr.Zero, IntPtr.Zero);
                var info = new TBBUTTONINFO
                {
                    cbSize = Marshal.SizeOf(typeof(TBBUTTONINFO)),
                    dwMask = TBIF_IMAGE,
                };
                if (himl == IntPtr.Zero
                    || SendMessage(_toolbar, TB_GETBUTTONINFOW, (IntPtr)cmdId, ref info).ToInt64() < 0
                    || info.iImage < 0)
                    continue;
                ImageList_Replace(himl, info.iImage, hbmp, IntPtr.Zero);
            }
            if (_toolbar != IntPtr.Zero) InvalidateRect(_toolbar, IntPtr.Zero, true);
        }

        private static void ApplyCustomizations()
        {
            if (_registrations.Count == 0) return;
            _toolbar = FindToolbar(_registrations[0].CmdId);
            if (_toolbar == IntPtr.Zero) return; // stops the recreate-watch until next Attach
            foreach (var kv in _enabled)
                SendMessage(_toolbar, TB_ENABLEBUTTON, (IntPtr)kv.Key, (IntPtr)(kv.Value ? 1 : 0));

            // NPP already sets TBSTYLE_EX_DRAWDDARROWS for its own drop-down button;
            // OR it in anyway so the arrow is drawn even if that ever changes.
            IntPtr ex = SendMessage(_toolbar, TB_GETEXTENDEDSTYLE, IntPtr.Zero, IntPtr.Zero);
            if ((ex.ToInt64() & TBSTYLE_EX_DRAWDDARROWS) == 0)
                SendMessage(_toolbar, TB_SETEXTENDEDSTYLE, IntPtr.Zero,
                    (IntPtr)(ex.ToInt64() | TBSTYLE_EX_DRAWDDARROWS));

            foreach (Registration reg in _registrations)
            {
                if (reg.SeparatorBefore)
                    InsertSeparatorBefore(reg.CmdId);

                var info = new TBBUTTONINFO
                {
                    cbSize = Marshal.SizeOf(typeof(TBBUTTONINFO)),
                    dwMask = TBIF_STYLE,
                };
                if (SendMessage(_toolbar, TB_GETBUTTONINFOW, (IntPtr)reg.CmdId, ref info).ToInt64() < 0)
                    continue;
                if ((info.fsStyle & BTNS_DROPDOWN) != 0)
                    continue;

                info.fsStyle |= BTNS_DROPDOWN;
                SendMessage(_toolbar, TB_SETBUTTONINFOW, (IntPtr)reg.CmdId, ref info);
            }
        }

        /// <summary>
        /// Inserts a separator left of the button unless one is already there — e.g.
        /// NPP's own separator when no other plugin added buttons before ours.
        /// </summary>
        private static void InsertSeparatorBefore(int cmdId)
        {
            int index = SendMessage(_toolbar, TB_COMMANDTOINDEX, (IntPtr)cmdId, IntPtr.Zero).ToInt32();
            if (index <= 0) return;

            var prev = new TBBUTTON();
            if (SendMessage(_toolbar, TB_GETBUTTON, (IntPtr)(index - 1), ref prev) == IntPtr.Zero)
                return;
            if ((prev.fsStyle & BTNS_SEP) != 0)
                return;

            var sep = new TBBUTTON { fsStyle = BTNS_SEP };
            SendMessage(_toolbar, TB_INSERTBUTTONW, (IntPtr)index, ref sep);
        }

        /// <summary>
        /// NPP's main toolbar: a ToolbarWindow32 hosted in a ReBar that contains our
        /// command ID. (Docked panels have their own toolbars; the ReBar check skips them.)
        /// </summary>
        private static IntPtr FindToolbar(int cmdId)
        {
            IntPtr found = IntPtr.Zero;
            var cls = new StringBuilder(64);
            Win32.EnumChildWindows(_nppHwnd, (hwnd, _) =>
            {
                cls.Clear();
                Win32.GetClassName(hwnd, cls, cls.Capacity);
                if (cls.ToString() != "ToolbarWindow32") return true;

                cls.Clear();
                Win32.GetClassName(GetParent(hwnd), cls, cls.Capacity);
                if (cls.ToString() != "ReBarWindow32") return true;

                if (SendMessage(hwnd, TB_COMMANDTOINDEX, (IntPtr)cmdId, IntPtr.Zero).ToInt64() < 0)
                    return true;

                found = hwnd;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        private static void ShowMenu(Registration reg)
        {
            List<Item> items = reg.GetItems?.Invoke() ?? new List<Item>();

            IntPtr hMenu = CreatePopupMenu();
            if (hMenu == IntPtr.Zero) return;
            try
            {
                if (items.Count == 0)
                    AppendMenu(hMenu, MF_STRING | MF_GRAYED, (UIntPtr)1, reg.EmptyText);

                for (int i = 0; i < items.Count; i++)
                {
                    // '&' would otherwise be swallowed as a mnemonic marker
                    string text = (items[i].Text ?? string.Empty).Replace("&", "&&");
                    uint flags = MF_STRING | (items[i].Checked ? MF_CHECKED : 0u);
                    AppendMenu(hMenu, flags, (UIntPtr)(i + 1), text);
                }

                // Drop the menu down from the button's bottom-left corner, like NPP's own.
                var rc = new RECT();
                SendMessage(_toolbar, TB_GETRECT, (IntPtr)reg.CmdId, ref rc);
                MapWindowPoints(_toolbar, IntPtr.Zero, ref rc, 2);
                var tpm = new TPMPARAMS { cbSize = Marshal.SizeOf(typeof(TPMPARAMS)), rcExclude = rc };

                int pick = TrackPopupMenuEx(hMenu,
                    TPM_LEFTALIGN | TPM_TOPALIGN | TPM_VERTICAL | TPM_RETURNCMD,
                    rc.left, rc.bottom, _nppHwnd, ref tpm);

                if (pick > 0 && pick <= items.Count)
                    reg.OnPick?.Invoke(pick - 1);
            }
            finally
            {
                DestroyMenu(hMenu);
            }
        }

        // ------------------------------------------------------------------ //
        //  Win32 interop
        // ------------------------------------------------------------------ //

        private const uint WM_NOTIFY = 0x004E;
        private const int  TBN_DROPDOWN    = -710;   // TBN_FIRST - 10
        private const int  TBDDRET_DEFAULT = 0;
        private const int  NM_CUSTOMDRAW   = -12;

        private const int  CDDS_PREPAINT       = 0x00001;
        private const int  CDDS_ITEM           = 0x10000;
        private const int  CDDS_ITEMPREPAINT   = CDDS_ITEM | CDDS_PREPAINT;
        private const int  CDDS_ITEMPOSTPAINT  = CDDS_ITEM | 0x00002;
        private const long CDRF_DODEFAULT      = 0x00;
        private const long CDRF_NOTIFYPOSTPAINT = 0x10;
        private const long CDRF_NOTIFYITEMDRAW = 0x20;
        private const uint CDIS_DISABLED       = 0x0004;
        private const int  CLR_INVALID         = -1;

        private const uint TB_ENABLEBUTTON     = 0x0400 + 1;
        private const uint TB_GETBUTTON        = 0x0400 + 23;
        private const uint TB_COMMANDTOINDEX   = 0x0400 + 25;
        private const uint TB_INSERTBUTTONW    = 0x0400 + 67;
        private const uint TB_GETRECT          = 0x0400 + 51;
        private const uint TB_SETEXTENDEDSTYLE = 0x0400 + 84;
        private const uint TB_GETEXTENDEDSTYLE = 0x0400 + 85;
        private const uint TB_GETITEMDROPDOWNRECT = 0x0400 + 103;
        private const uint TB_GETBUTTONINFOW   = 0x0400 + 63;
        private const uint TB_SETBUTTONINFOW   = 0x0400 + 64;
        private const uint TB_GETIMAGELIST     = 0x0400 + 49;
        private const uint TBIF_IMAGE          = 0x0001;
        private const uint TBIF_STYLE          = 0x0008;
        private const byte BTNS_SEP            = 0x01;
        private const byte BTNS_DROPDOWN       = 0x08;
        private const long TBSTYLE_EX_DRAWDDARROWS = 0x0001;

        private const uint MF_STRING  = 0x0000;
        private const uint MF_GRAYED  = 0x0001;
        private const uint MF_CHECKED = 0x0008;
        private const uint TPM_LEFTALIGN  = 0x0000;
        private const uint TPM_TOPALIGN   = 0x0000;
        private const uint TPM_VERTICAL   = 0x0040;
        private const uint TPM_RETURNCMD  = 0x0100;

        private delegate IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
                                             UIntPtr idSubclass, IntPtr refData);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct TPMPARAMS { public int cbSize; public RECT rcExclude; }

        [StructLayout(LayoutKind.Sequential)]
        private struct NMHDR { public IntPtr hwndFrom; public IntPtr idFrom; public int code; }

        // NMHDR must be nested, not flattened: it is padded to pointer alignment (24 bytes
        // on x64), so dwDrawStage starts after that padding.
        [StructLayout(LayoutKind.Sequential)]
        private struct NMCUSTOMDRAW
        {
            public NMHDR  hdr;
            public int    dwDrawStage;
            public IntPtr hdc;
            public RECT   rc;
            public IntPtr dwItemSpec;
            public uint   uItemState;
            public IntPtr lItemlParam;
        }

        // bReserved[6] (x64) / [2] (x86) is covered by the automatic padding before dwData.
        [StructLayout(LayoutKind.Sequential)]
        private struct TBBUTTON
        {
            public int    iBitmap;
            public int    idCommand;
            public byte   fsState;
            public byte   fsStyle;
            public IntPtr dwData;
            public IntPtr iString;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct TBBUTTONINFO
        {
            public int    cbSize;
            public uint   dwMask;
            public int    idCommand;
            public int    iImage;
            public byte   fsState;
            public byte   fsStyle;
            public ushort cx;
            public IntPtr lParam;
            public IntPtr pszText;
            public int    cchText;
        }

        [DllImport("comctl32.dll")]
        private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass,
                                                     UIntPtr uIdSubclass, IntPtr dwRefData);
        [DllImport("comctl32.dll")]
        private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass,
                                                        UIntPtr uIdSubclass);
        [DllImport("comctl32.dll")]
        private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, ref TBBUTTONINFO lParam);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, ref RECT lParam);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, ref TBBUTTON lParam);
        [DllImport("user32.dll")] private static extern bool   PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool   IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int    MapWindowPoints(IntPtr from, IntPtr to, ref RECT pts, int count);
        [DllImport("user32.dll")] private static extern uint   GetDpiForWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool   InvalidateRect(IntPtr hWnd, IntPtr rect, bool erase);
        [DllImport("comctl32.dll")] private static extern bool ImageList_Replace(IntPtr himl, int i, IntPtr hbmImage, IntPtr hbmMask);
        [DllImport("user32.dll")] private static extern int    FillRect(IntPtr hdc, ref RECT rc, IntPtr brush);
        [DllImport("gdi32.dll")]  private static extern int    GetPixel(IntPtr hdc, int x, int y);
        [DllImport("gdi32.dll")]  private static extern IntPtr CreateSolidBrush(int colorRef);
        [DllImport("gdi32.dll")]  private static extern bool   DeleteObject(IntPtr obj);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll")] private static extern bool   DestroyMenu(IntPtr hMenu);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool AppendMenu(IntPtr hMenu, uint flags, UIntPtr id, string text);
        [DllImport("user32.dll")]
        private static extern int TrackPopupMenuEx(IntPtr hMenu, uint flags, int x, int y, IntPtr hWnd, ref TPMPARAMS tpm);
    }
}
