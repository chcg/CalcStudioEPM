using System;
using System.Runtime.InteropServices;
using System.Text;

namespace NppPluginNET
{
    /// <summary>
    /// Text access on a Scintilla window from inside Notepad++'s process (pointers can be
    /// passed straight through), in the document's own encoding.
    /// </summary>
    internal static class ScintillaText
    {
        public static Encoding EncodingOf(IntPtr sci) =>
            Send(sci, SCI_GETCODEPAGE) == 65001 ? new UTF8Encoding(false) : Encoding.Default;

        public static int Send(IntPtr sci, uint msg, long w = 0, long l = 0) =>
            SendMessage(sci, msg, (IntPtr)w, (IntPtr)l).ToInt32();

        /// <summary>Text between two byte positions.</summary>
        public static string GetRange(IntPtr sci, int start, int end, Encoding enc = null)
        {
            if (end <= start) return "";
            enc = enc ?? EncodingOf(sci);
            int len = end - start;
            IntPtr buf = Marshal.AllocHGlobal(len + 1);
            try
            {
                var tr = new Sci_TextRangeFull { cpMin = (IntPtr)start, cpMax = (IntPtr)end, lpstrText = buf };
                if (SendMessage(sci, SCI_GETTEXTRANGEFULL, IntPtr.Zero, ref tr).ToInt64() != len)
                {
                    // Notepad++ before 8.4.8 (Scintilla < 5.3): the 32-bit variant
                    var old = new Sci_TextRange { cpMin = start, cpMax = end, lpstrText = buf };
                    SendMessage(sci, SCI_GETTEXTRANGE, IntPtr.Zero, ref old);
                }
                var bytes = new byte[len];
                Marshal.Copy(buf, bytes, 0, len);
                return enc.GetString(bytes);
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        /// <summary>Replaces [start, end) with <paramref name="text"/>.</summary>
        public static void Replace(IntPtr sci, int start, int end, string text, Encoding enc = null)
        {
            byte[] bytes = (enc ?? EncodingOf(sci)).GetBytes(text);
            Send(sci, SCI_SETTARGETRANGE, start, end);
            WithBytes(bytes, p => SendMessage(sci, SCI_REPLACETARGET, (IntPtr)bytes.Length, p));
        }

        /// <summary>Sends a message whose lParam is a NUL-terminated string in the document encoding.</summary>
        public static void SendString(IntPtr sci, uint msg, long w, string text, Encoding enc = null)
        {
            byte[] bytes = (enc ?? EncodingOf(sci)).GetBytes(text ?? "");
            var z = new byte[bytes.Length + 1];
            Buffer.BlockCopy(bytes, 0, z, 0, bytes.Length);
            WithBytes(z, p => SendMessage(sci, msg, (IntPtr)w, p));
        }

        private static void WithBytes(byte[] bytes, Action<IntPtr> use)
        {
            IntPtr buf = Marshal.AllocHGlobal(Math.Max(1, bytes.Length));
            try
            {
                Marshal.Copy(bytes, 0, buf, bytes.Length);
                use(buf);
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        /// <summary>A NUL-terminated string Scintilla handed us (e.g. a notification's text).</summary>
        public static string FromPointer(IntPtr p, Encoding enc)
        {
            if (p == IntPtr.Zero) return "";
            int len = 0;
            while (Marshal.ReadByte(p, len) != 0) len++;
            var bytes = new byte[len];
            Marshal.Copy(p, bytes, 0, len);
            return enc.GetString(bytes);
        }

        public const uint SCI_GETCODEPAGE      = 2137;
        public const uint SCI_SETTARGETRANGE   = 2686;
        public const uint SCI_REPLACETARGET    = 2194;
        public const uint SCI_GETTEXTRANGEFULL = 2039;
        public const uint SCI_GETTEXTRANGE     = 2162;

        [StructLayout(LayoutKind.Sequential)]
        private struct Sci_TextRangeFull { public IntPtr cpMin; public IntPtr cpMax; public IntPtr lpstrText; }

        [StructLayout(LayoutKind.Sequential)]
        private struct Sci_TextRange { public int cpMin; public int cpMax; public IntPtr lpstrText; }

        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, ref Sci_TextRangeFull l);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, ref Sci_TextRange l);
    }
}
