using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CalcStudioEPM.Forms
{
    /// <summary>
    /// Red alert shown before a "Deploy CM+HP" that can't reach Planning: the rule calls
    /// templates that only Calculation Manager can inline (design-time prompts, graphical
    /// templates, or templates that were never pulled). OK = deploy to Calculation Manager
    /// only; Cancel = don't deploy.
    /// </summary>
    public class PlanningBlockedForm : PluginForm
    {
        private static readonly Color Red      = Color.FromArgb(0xB0, 0x00, 0x00);
        private static readonly Color RedLight = Color.FromArgb(0xFD, 0xEC, 0xEC);

        /// <param name="forExecute">Deploying before an execution: the run uses Planning's previous version.</param>
        public PlanningBlockedForm(string ruleName, IReadOnlyList<string> blockers, bool forExecute)
        {
            Text            = "ACTION NEEDED — Planning deploy not possible";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            MinimizeBox     = false;
            ShowIcon        = false;
            StartPosition   = FormStartPosition.CenterParent;
            ClientSize      = new Size(560, 372);
            AllowResize("PlanningBlocked");

            // ---- Red banner ----
            var banner = new Panel { Location = new Point(0, 0), Size = new Size(560, 44), BackColor = Red,
                                    Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            banner.Controls.Add(new PictureBox
            {
                Image    = SystemIcons.Error.ToBitmap(),
                Location = new Point(12, 6),
                Size     = new Size(32, 32),
                SizeMode = PictureBoxSizeMode.Zoom,
            });
            banner.Controls.Add(new Label
            {
                Text      = "Complex templates cannot be deployed to Planning (HP)",
                Location  = new Point(52, 0),
                Size      = new Size(500, 44),
                Anchor    = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.White,
                Font      = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            });
            Controls.Add(banner);

            Controls.Add(new Label
            {
                Text = $"'{ruleName}' calls templates that only Calculation Manager can expand when it " +
                       "deploys a rule to Planning. The deploy will only reach Calculation Manager." +
                       (forExecute ? "\nThe execution will then run the version Planning already has." : ""),
                Location = new Point(16, 54),
                Size     = new Size(528, 48),
                Anchor   = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            });

            var list = new TextBox
            {
                Text       = string.Join("\r\n", blockers),
                Location   = new Point(16, 104),
                Size       = new Size(528, 110),
                Anchor     = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Multiline  = true,
                ReadOnly   = true,
                WordWrap   = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor  = SystemColors.Window,
                Font       = new Font("Consolas", 9f),
            };
            Controls.Add(list);

            Controls.Add(new Label
            {
                Text        = "ACTION NEEDED: after the deploy, open Calculation Manager and deploy " +
                              $"'{ruleName}' to Planning.",
                Location    = new Point(16, 226),
                Size        = new Size(528, 58),
                Anchor      = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Padding     = new Padding(8, 6, 8, 6),
                ForeColor   = Red,
                BackColor   = RedLight,
                BorderStyle = BorderStyle.FixedSingle,
                Font        = new Font("Segoe UI", 10f, FontStyle.Bold),
            });

            var ok = new Button
            {
                Text = "Deploy to Calculation Manager only", Location = new Point(224, 330), Size = new Size(232, 28),
                DialogResult = DialogResult.OK, Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            };
            var cancel = new Button
            {
                Text = "Cancel", Location = new Point(464, 330), Size = new Size(80, 28),
                DialogResult = DialogResult.Cancel, Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = cancel;   // a deliberate choice — Enter doesn't deploy
            CancelButton = cancel;
            ActiveControl = cancel;
        }
    }
}
