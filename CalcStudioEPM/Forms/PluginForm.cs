using System;
using System.Drawing;
using System.Windows.Forms;
using CalcStudioEPM.Config;

namespace CalcStudioEPM.Forms
{
    /// <summary>
    /// Base for every plugin dialog.
    ///
    /// Scaling: layouts are written in 96-DPI pixels for Segoe UI 9pt. Before the dialog
    /// is shown, the whole layout is scaled once by the height the font ACTUALLY renders
    /// at (16 px at 100 %, 20 px at 125 %, 24 px at 150 % …). WinForms' own AutoScaleMode
    /// can't be relied on inside Notepad++: it never ran for these code-built dialogs,
    /// and it may be told 96 DPI while text is drawn at 125–150 % — a 100 %-sized layout
    /// with oversized text, i.e. cramped, clipped dialogs.
    ///
    /// Resizing: <see cref="AllowResize"/> makes the dialog resizable, with its laid-out
    /// size (or a given minimum) as the smallest size, and remembers the user's size per
    /// dialog in ui_state.json (stored at 100 % so it carries across scalings).
    /// </summary>
    public class PluginForm : Form
    {
        /// <summary>Line height of Segoe UI 9pt at 96 DPI — what the layouts are written for.</summary>
        private const float DesignFontHeight = 16f;

        private string _sizeKey;
        private bool   _resizeHeight = true;
        private Size   _minimum96;
        private float  _scale = 1f;

        protected PluginForm()
        {
            AutoScaleMode = AutoScaleMode.None;   // we scale ourselves, see OnLoad
            Font          = new Font("Segoe UI", 9f);
        }

        /// <summary>How much the layout is scaled up from 96-DPI pixels.</summary>
        protected float LayoutScale => _scale;

        /// <summary>A 96-DPI length scaled like the layout.</summary>
        protected int Scaled(int pixels96) => (int)Math.Round(pixels96 * LayoutScale);

        /// <summary>A MaximumSize width that doesn't limit (WinForms treats 0 as 0 here).</summary>
        protected const int UnlimitedWidth = 20000;

        /// <summary>
        /// Lets the user resize the dialog. <paramref name="key"/> names it in ui_state.json;
        /// <paramref name="minimum96"/> is the smallest size at 100 % (default: the laid-out
        /// size); <paramref name="height"/> false = width only.
        /// </summary>
        protected void AllowResize(string key, Size? minimum96 = null, bool height = true)
        {
            _sizeKey      = key;
            _resizeHeight = height;
            _minimum96    = minimum96 ?? Size.Empty;
            FormBorderStyle = FormBorderStyle.Sizable;
            SizeGripStyle   = SizeGripStyle.Show;
            MinimizeBox     = false;
        }

        /// <summary>The size the user last left this dialog at (at 100 %), or Empty.</summary>
        protected Size RememberedSize96 =>
            _sizeKey != null && UiState.Load().WindowSizes.TryGetValue(_sizeKey, out int[] wh) && wh?.Length == 2
                ? new Size(wh[0], wh[1]) : Size.Empty;

        protected override void OnLoad(EventArgs e)
        {
            // Scale the 96-DPI layout to the rendered font, once, before anything is shown
            float scale = Font.Height / DesignFontHeight;
            if (Math.Abs(scale - 1f) > 0.01f)
            {
                _scale = scale;
                Scale(new SizeF(scale, scale));
                ScaleListColumns(this, scale);   // Scale() leaves ListView columns alone
            }

            base.OnLoad(e);

            if (_sizeKey != null)
            {
                Size min = _minimum96.IsEmpty
                    ? Size
                    : new Size(Scaled(_minimum96.Width), Scaled(_minimum96.Height));
                MinimumSize = min;

                Size saved = RememberedSize96;
                if (!saved.IsEmpty)
                {
                    Rectangle work = Screen.FromControl(this).WorkingArea;
                    int w = Math.Min(Math.Max(Scaled(saved.Width), min.Width), work.Width);
                    int h = _resizeHeight
                        ? Math.Min(Math.Max(Scaled(saved.Height), min.Height), work.Height)
                        : Height;
                    Size = new Size(w, h);
                }
                if (!_resizeHeight)
                    MaximumSize = new Size(UnlimitedWidth, Height);
            }

            // Centre it at its final size — on its owner (Notepad++) or the screen, as asked. (The
            // centring WinForms does sits inside base.OnLoad and used the size before scaling and
            // the remembered size; reading the position before it gave the window's start
            // position near the top-left.) Then keep it on screen.
            if (StartPosition == FormStartPosition.CenterParent) CenterToParent();
            else if (StartPosition == FormStartPosition.CenterScreen) CenterToScreen();
            Rectangle area = Screen.FromControl(this).WorkingArea;
            int x = Math.Max(area.Left, Math.Min(Left, area.Right - Width));
            int y = Math.Max(area.Top,  Math.Min(Top, area.Bottom - Height));
            Location = new Point(x, y);
        }

        private static void ScaleListColumns(Control parent, float scale)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is ListView list)
                    foreach (ColumnHeader col in list.Columns)
                        col.Width = (int)Math.Round(col.Width * scale);
                ScaleListColumns(c, scale);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            RememberSize();
        }

        /// <summary>Stores the current size (at 100 %) under the dialog's key.</summary>
        protected void RememberSize()
        {
            if (_sizeKey == null || WindowState != FormWindowState.Normal) return;
            float s = LayoutScale;
            var state = UiState.Load();
            state.WindowSizes[_sizeKey] = new[] { (int)Math.Round(Width / s), (int)Math.Round(Height / s) };
            state.Save();
        }
    }
}
