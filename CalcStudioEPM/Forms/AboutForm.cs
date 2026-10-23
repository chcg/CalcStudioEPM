using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace CalcStudioEPM.Forms
{
    /// <summary>
    /// The About box: what the plugin does, version, author and links, the "not an Oracle
    /// product" disclaimer and the license. Its text lives in <see cref="AboutInfo"/>.
    /// Laid out top to bottom in 96-DPI pixels, each text block measured so nothing clips.
    /// </summary>
    public class AboutForm : PluginForm
    {
        private const int FormWidth = 520;
        private const int Pad       = 16;
        private const int Inner     = FormWidth - 2 * Pad;

        private int _y = Pad;

        public AboutForm()
        {
            Text            = "About " + AboutInfo.ProductName;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            MinimizeBox     = false;
            ShowIcon        = false;
            ShowInTaskbar   = false;
            StartPosition   = FormStartPosition.CenterParent;

            var bold  = new Font(Font, FontStyle.Bold);
            var small = new Font("Segoe UI", 8.25f);

            AddText(AboutInfo.ProductName, new Font("Segoe UI", 14f, FontStyle.Bold));
            AddText($"Version {AboutInfo.Version}   ·   built {AboutInfo.BuildDate}", Font, SystemColors.GrayText);
            _y += 6;
            AddText(AboutInfo.Description, Font);
            AddRule();

            AddText(AboutInfo.Author, bold);
            // Segoe UI Emoji: a plain Segoe UI label draws the note's emoji as a box
            AddText(AboutInfo.AuthorNote, new Font("Segoe UI Emoji", 9f));
            _y += 2;
            AddLinks();
            AddRule();

            var disclaimer = new TextBox
            {
                Text        = AboutInfo.Disclaimer,
                Location    = new Point(Pad, _y),
                Multiline   = true,
                ReadOnly    = true,
                WordWrap    = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor   = SystemColors.Info,
                ForeColor   = SystemColors.InfoText,
                TabStop     = false,
            };
            disclaimer.Size = new Size(Inner, MeasureHeight(AboutInfo.Disclaimer, Font, Inner - 8) + 10);
            Controls.Add(disclaimer);
            _y = disclaimer.Bottom + 10;

            AddText(AboutInfo.LicenseNotice, Font);
            AddLink(AboutInfo.LicenseName, AboutInfo.LicenseUrl, Pad, _y);
            _y += 22;
            AddText(AboutInfo.Copyright + ".  " + AboutInfo.ThirdParty, small, SystemColors.GrayText);

            _y += 8;
            var copy = new Button { Text = "Copy version info", Location = new Point(Pad, _y), Size = new Size(140, 28) };
            copy.Click += (s, e) => CopyVersionInfo();
            var close = new Button
            {
                Text = "Close", Location = new Point(FormWidth - Pad - 90, _y), Size = new Size(90, 28),
                DialogResult = DialogResult.OK,
            };
            Controls.Add(copy);
            Controls.Add(close);
            AcceptButton  = close;
            CancelButton  = close;
            ActiveControl = close;

            ClientSize = new Size(FormWidth, close.Bottom + Pad);
        }

        private void AddText(string text, Font font, Color? color = null)
        {
            int h = MeasureHeight(text, font, Inner);
            Controls.Add(new Label
            {
                Text      = text,
                Font      = font,
                ForeColor = color ?? SystemColors.ControlText,
                Location  = new Point(Pad, _y),
                Size      = new Size(Inner, h),
            });
            _y += h + 4;
        }

        private void AddRule()
        {
            _y += 6;
            Controls.Add(new Label { Location = new Point(Pad, _y), Size = new Size(Inner, 2), BorderStyle = BorderStyle.Fixed3D });
            _y += 12;
        }

        /// <summary>One row of links; entries without a URL are left out.</summary>
        private void AddLinks()
        {
            int x = Pad;
            foreach (var (text, url) in AboutInfo.Links)
            {
                if (string.IsNullOrEmpty(url)) continue;
                int w = MeasureWidth(text, Font) + 6;
                if (x + w > FormWidth - Pad) { x = Pad; _y += 22; }
                AddLink(text, url, x, _y);
                x += w + 14;
            }
            _y += 24;
        }

        private void AddLink(string text, string url, int x, int y)
        {
            var link = new LinkLabel
            {
                Text     = text,
                Location = new Point(x, y),
                Size     = new Size(MeasureWidth(text, Font) + 6, 20),
            };
            new ToolTip().SetToolTip(link, url);
            link.LinkClicked += (s, e) => OpenUrl(url);
            Controls.Add(link);
        }

        /// <summary>Opens an http(s) link in the default browser. The links are our own constants;
        /// the scheme check keeps anything else from being handed to the shell.</summary>
        private static void OpenUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                return;
            try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
            catch (Exception ex)
            {
                MessageBox.Show("Could not open the browser:\n\n" + ex.Message + "\n\n" + uri.AbsoluteUri,
                    AboutInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>Version details to paste into a bug report.</summary>
        private static void CopyVersionInfo()
        {
            string info =
                $"{AboutInfo.ProductName} {AboutInfo.Version} (built {AboutInfo.BuildDate})\r\n" +
                $"Windows {Environment.OSVersion.Version}, {(Environment.Is64BitProcess ? "64" : "32")}-bit process\r\n" +
                $".NET CLR {Environment.Version}";
            try { Clipboard.SetText(info); } catch { /* clipboard busy — nothing useful to do */ }
        }

        // Measured at the rendered DPI, returned in the 96-DPI pixels the layout is written in
        // (PluginForm scales the whole dialog afterwards).
        private float RenderScale => Font.Height / 16f;

        private int MeasureHeight(string text, Font font, int width96)
        {
            float s = RenderScale;
            Size size = TextRenderer.MeasureText(text, font, new Size((int)(width96 * s), int.MaxValue),
                                                 TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            return (int)Math.Ceiling(size.Height / s) + 2;
        }

        private int MeasureWidth(string text, Font font) =>
            (int)Math.Ceiling(TextRenderer.MeasureText(text, font).Width / RenderScale);
    }
}
