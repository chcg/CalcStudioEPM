using System;
using System.Runtime.InteropServices;
using System.Text;

namespace NppPluginNET
{
    /// <summary>
    /// Ctrl+K / Ctrl+Shift+K / Ctrl+Shift+Q (and Edit › Comment/Uncomment) for Calc Script.
    ///
    /// The "Calc Script - Essbase" user language defines /* */ as a delimiter, not as a
    /// comment, because Notepad++'s UDL lexer misplaces fold points after every comment it
    /// styles. A UDL without comment markers gets no comment commands from Notepad++, so we
    /// catch those commands on Notepad++'s main window and, for .csc files in a user
    /// language, apply /* */ ourselves. Whatever keys the user mapped keep working; for
    /// every other file the command goes to Notepad++ untouched.
    ///
    /// Calc Script has no line comment, so "single line" comments wrap each line in
    /// /* … */ — what Notepad++ itself does for such languages.
    /// </summary>
    public static class CommentCommands
    {
        // Notepad++ menuCmdID.h
        private const int IDM_EDIT_BLOCK_COMMENT     = 42022;   // Toggle Single Line Comment (Ctrl+K)
        private const int IDM_EDIT_STREAM_COMMENT    = 42023;   // Block Comment (Ctrl+Shift+Q)
        private const int IDM_EDIT_BLOCK_COMMENT_SET = 42035;   // Single Line Comment
        private const int IDM_EDIT_BLOCK_UNCOMMENT   = 42036;   // Single Line Uncomment (Ctrl+Shift+K)
        private const int IDM_EDIT_STREAM_UNCOMMENT  = 42047;   // Block Uncomment

        private const string Open = "/*", Close = "*/";

        private static IntPtr       _nppHwnd;
        private static SubclassProc _proc;   // kept alive — called through a native pointer
        private static readonly UIntPtr SubclassId = (UIntPtr)0x45504D32; // "EPM2"

        /// <summary>Hooks the comment commands. Call once on NPPN_READY.</summary>
        public static void Attach(IntPtr nppHwnd, Func<IntPtr> currentScintilla)
        {
            if (_proc != null) return;
            _nppHwnd = nppHwnd;
            _currentScintilla = currentScintilla;
            _proc = WndProc;
            if (!SetWindowSubclass(nppHwnd, _proc, SubclassId, IntPtr.Zero)) _proc = null;
        }

        public static void Detach()
        {
            if (_proc == null) return;
            RemoveWindowSubclass(_nppHwnd, _proc, SubclassId);
            _proc = null;
        }

        private static Func<IntPtr> _currentScintilla;

        private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
                                      UIntPtr id, IntPtr refData)
        {
            try
            {
                if (msg == WM_COMMAND)
                {
                    int cmd = (int)(wParam.ToInt64() & 0xFFFF);
                    if ((cmd == IDM_EDIT_BLOCK_COMMENT || cmd == IDM_EDIT_STREAM_COMMENT ||
                         cmd == IDM_EDIT_BLOCK_COMMENT_SET || cmd == IDM_EDIT_BLOCK_UNCOMMENT ||
                         cmd == IDM_EDIT_STREAM_UNCOMMENT) && AppliesToCurrentFile())
                    {
                        IntPtr sci = _currentScintilla();
                        switch (cmd)
                        {
                            case IDM_EDIT_BLOCK_COMMENT:     LineComments(sci, LineMode.Toggle);    break;
                            case IDM_EDIT_BLOCK_COMMENT_SET: LineComments(sci, LineMode.Comment);   break;
                            case IDM_EDIT_BLOCK_UNCOMMENT:   LineComments(sci, LineMode.Uncomment); break;
                            case IDM_EDIT_STREAM_COMMENT:    StreamComment(sci);                    break;
                            case IDM_EDIT_STREAM_UNCOMMENT:  StreamUncomment(sci);                  break;
                        }
                        return IntPtr.Zero;
                    }
                }
            }
            catch { /* never let an exception escape into Notepad++'s window procedure */ }
            return DefSubclassProc(hWnd, msg, wParam, lParam);
        }

        /// <summary>A .csc file shown in a user-defined language (Calc Script - Essbase).</summary>
        private static bool AppliesToCurrentFile()
        {
            if (!CalcStudioEPM.Config.PluginSettings.Current.HandleCalcScriptComments) return false;
            int lang = 0;
            SendMessage(_nppHwnd, NPPM_GETCURRENTLANGTYPE, IntPtr.Zero, ref lang);
            if (lang != L_USER) return false;

            var ext = new StringBuilder(260);
            SendMessage(_nppHwnd, NPPM_GETEXTPART, (IntPtr)ext.Capacity, ext);
            return string.Equals(ext.ToString(), ".csc", StringComparison.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------------------ //
        //  Line comments: each line wrapped in /* … */ after its indentation
        // ------------------------------------------------------------------ //

        private enum LineMode { Toggle, Comment, Uncomment }

        private static void LineComments(IntPtr sci, LineMode mode)
        {
            Encoding enc = EncodingOf(sci);
            int selStart = Sci(sci, SCI_GETSELECTIONSTART), selEnd = Sci(sci, SCI_GETSELECTIONEND);
            int first = Sci(sci, SCI_LINEFROMPOSITION, selStart);
            int last  = Sci(sci, SCI_LINEFROMPOSITION, selEnd);
            // A selection ending at the very start of a line doesn't include that line
            if (last > first && Sci(sci, SCI_POSITIONFROMLINE, last) == selEnd) last--;

            Sci(sci, SCI_BEGINUNDOACTION);
            try
            {
                for (int line = last; line >= first; line--)   // bottom-up: earlier positions stay valid
                {
                    int start = Sci(sci, SCI_POSITIONFROMLINE, line);
                    int end   = Sci(sci, SCI_GETLINEENDPOSITION, line);
                    string text = GetRange(sci, start, end, enc);
                    string body = text.TrimStart(' ', '\t');
                    if (body.Trim().Length == 0) continue;        // blank lines stay blank
                    string indent = text.Substring(0, text.Length - body.Length);

                    bool commented = IsWrapped(body);
                    if (mode == LineMode.Comment || (mode == LineMode.Toggle && !commented))
                        Replace(sci, start, end, indent + Open + " " + body.TrimEnd() + " " + Close, enc);
                    else if (commented)
                        Replace(sci, start, end, indent + Unwrap(body), enc);
                }
            }
            finally
            {
                Sci(sci, SCI_ENDUNDOACTION);
            }

            if (first != last)   // keep the processed lines selected, as Notepad++ does
                Sci(sci, SCI_SETSEL, Sci(sci, SCI_POSITIONFROMLINE, first), Sci(sci, SCI_GETLINEENDPOSITION, last));
        }

        // ------------------------------------------------------------------ //
        //  Block comments: the selection (or the current line) in one /* … */
        // ------------------------------------------------------------------ //

        private static void StreamComment(IntPtr sci)
        {
            Encoding enc = EncodingOf(sci);
            int start = Sci(sci, SCI_GETSELECTIONSTART), end = Sci(sci, SCI_GETSELECTIONEND);
            if (start == end) { LineComments(sci, LineMode.Comment); return; }

            Sci(sci, SCI_BEGINUNDOACTION);
            try
            {
                Insert(sci, end, " " + Close, enc);
                Insert(sci, start, Open + " ", enc);
            }
            finally
            {
                Sci(sci, SCI_ENDUNDOACTION);
            }
        }

        private static void StreamUncomment(IntPtr sci)
        {
            Encoding enc = EncodingOf(sci);
            int start = Sci(sci, SCI_GETSELECTIONSTART), end = Sci(sci, SCI_GETSELECTIONEND);
            if (start == end) { LineComments(sci, LineMode.Uncomment); return; }

            string text = GetRange(sci, start, end, enc);
            string trimmed = text.Trim();
            if (!IsWrapped(trimmed)) return;
            int lead = text.Length - text.TrimStart().Length;
            int trail = text.Length - text.TrimEnd().Length;
            string result = text.Substring(0, lead) + Unwrap(trimmed) + text.Substring(text.Length - trail);
            Sci(sci, SCI_BEGINUNDOACTION);
            try { Replace(sci, start, end, result, enc); }
            finally { Sci(sci, SCI_ENDUNDOACTION); }
        }

        private static bool IsWrapped(string s)
        {
            string t = s.TrimEnd();
            return t.Length >= 4 && t.StartsWith(Open, StringComparison.Ordinal) && t.EndsWith(Close, StringComparison.Ordinal);
        }

        /// <summary>"/* x */" → "x" (one space next to each marker is part of the marker).</summary>
        private static string Unwrap(string s)
        {
            string t = s.TrimEnd();
            t = t.Substring(Open.Length, t.Length - Open.Length - Close.Length);
            if (t.StartsWith(" ")) t = t.Substring(1);
            if (t.EndsWith(" "))   t = t.Substring(0, t.Length - 1);
            return t;
        }

        // ------------------------------------------------------------------ //
        //  Scintilla helpers (in Notepad++'s process: pointers can be passed)
        // ------------------------------------------------------------------ //

        private static Encoding EncodingOf(IntPtr sci) => ScintillaText.EncodingOf(sci);

        private static string GetRange(IntPtr sci, int start, int end, Encoding enc) =>
            ScintillaText.GetRange(sci, start, end, enc);

        private static void Replace(IntPtr sci, int start, int end, string text, Encoding enc) =>
            ScintillaText.Replace(sci, start, end, text, enc);

        private static void Insert(IntPtr sci, int pos, string text, Encoding enc) => Replace(sci, pos, pos, text, enc);

        private static int Sci(IntPtr sci, uint msg, int w = 0, int l = 0) =>
            SendMessage(sci, msg, (IntPtr)w, (IntPtr)l).ToInt32();

        // ------------------------------------------------------------------ //
        //  Interop
        // ------------------------------------------------------------------ //

        private const uint WM_COMMAND = 0x0111;
        private const uint NPPM_GETCURRENTLANGTYPE = 2024 + 5;
        private const uint NPPM_GETEXTPART         = 4024 + 5;
        private const int  L_USER = 15;

        private const uint SCI_GETSELECTIONSTART  = 2143;
        private const uint SCI_GETSELECTIONEND    = 2145;
        private const uint SCI_LINEFROMPOSITION   = 2166;
        private const uint SCI_POSITIONFROMLINE   = 2167;
        private const uint SCI_GETLINEENDPOSITION = 2136;
        private const uint SCI_SETSEL             = 2160;
        private const uint SCI_BEGINUNDOACTION    = 2078;
        private const uint SCI_ENDUNDOACTION      = 2079;

        private delegate IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
                                             UIntPtr idSubclass, IntPtr refData);

        [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(IntPtr h, SubclassProc p, UIntPtr id, IntPtr data);
        [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(IntPtr h, SubclassProc p, UIntPtr id);
        [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, ref int l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, StringBuilder l);
    }
}
