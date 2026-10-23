using System.Drawing;
using System.Windows.Forms;

namespace CalcStudioEPM.Forms
{
    /// <summary>
    /// Explains that Local Syntax Validation is a simple offline syntax check, not an EPM
    /// validation. "Don't show me again" stays disabled — with a "n/3" counter — for the
    /// first <see cref="ViewsBeforeOptOut"/> showings, and can be ticked from then on.
    /// </summary>
    public class LocalValidationNoticeForm : PluginForm
    {
        public const int ViewsBeforeOptOut = 3;

        private readonly CheckBox _chkDontShow;

        /// <summary>True when the user ticked "Don't show me again".</summary>
        public bool DontShowAgain => _chkDontShow.Enabled && _chkDontShow.Checked;

        /// <param name="viewNumber">1-based count of this showing (including this one).</param>
        public LocalValidationNoticeForm(int viewNumber)
        {
            Text            = "Local Syntax Validation";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            MinimizeBox     = false;
            ShowIcon        = false;
            StartPosition   = FormStartPosition.CenterParent;
            ClientSize      = new Size(500, 200);
            AllowResize("LocalValidationNotice");

            Controls.Add(new PictureBox
            {
                Image    = SystemIcons.Information.ToBitmap(),
                Location = new Point(16, 16),
                Size     = new Size(32, 32),
                SizeMode = PictureBoxSizeMode.Zoom,
            });

            Controls.Add(new Label
            {
                Text =
                    "This is a simple, local syntax check — not an EPM validation.\n\n" +
                    "It runs offline in Notepad++ and catches common mistakes (unclosed blocks " +
                    "and brackets, unknown functions, wrong number of parameters, undeclared " +
                    "variables…). A script that passes here can still fail EPM Cloud's own " +
                    "validation when you deploy or run it.",
                Location = new Point(60, 16),
                Size     = new Size(424, 100),
                Anchor   = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            });

            // Disabled — with a counter — until the notice has been seen often enough
            bool canOptOut = viewNumber > ViewsBeforeOptOut;
            _chkDontShow = new CheckBox
            {
                Text     = canOptOut
                    ? "Don't show me again"
                    : $"Don't show me again   ({viewNumber}/{ViewsBeforeOptOut})",
                Location = new Point(60, 126),
                AutoSize = true,
                Enabled  = canOptOut,
                Anchor   = AnchorStyles.Bottom | AnchorStyles.Left,
            };
            Controls.Add(_chkDontShow);

            var ok = new Button
            {
                Text = "OK", Location = new Point(404, 160), Size = new Size(80, 26),
                DialogResult = DialogResult.OK, Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            };
            Controls.Add(ok);
            AcceptButton = ok;
            CancelButton = ok;
        }
    }
}
