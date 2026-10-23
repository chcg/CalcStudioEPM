using System;
using System.Drawing;
using System.Windows.Forms;

namespace CalcStudioEPM.Forms
{
    /// <summary>
    /// A message with its own buttons (a MessageBox only has OK / Yes / No…). Sizes itself to
    /// its text; <see cref="Choice"/> is the index of the button clicked (the cancel button's
    /// index when the window is closed).
    /// </summary>
    public class ChoiceForm : PluginForm
    {
        public int Choice { get; private set; }

        /// <param name="buttons">Labels, left to right.</param>
        /// <param name="defaultButton">Index of the button Enter clicks.</param>
        /// <param name="cancelButton">Index of the button Esc / closing the window picks.</param>
        public ChoiceForm(string title, string message, string[] buttons, int defaultButton, int cancelButton, Icon icon = null)
        {
            Choice          = cancelButton;
            Text            = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            MinimizeBox     = false;
            ShowIcon        = false;
            StartPosition   = FormStartPosition.CenterParent;
            AutoSize        = true;
            AutoSizeMode    = AutoSizeMode.GrowAndShrink;

            var outer = new TableLayoutPanel
            {
                ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(14, 16, 16, 12),
            };
            outer.Controls.Add(new PictureBox
            {
                Image = (icon ?? SystemIcons.Question).ToBitmap(), Size = new Size(32, 32),
                SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, 12, 0),
            }, 0, 0);
            outer.Controls.Add(new Label
            {
                Text = message, AutoSize = true, MaximumSize = new Size(420, 0), Margin = new Padding(0, 6, 0, 18),
            }, 1, 0);

            var row = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Right, Margin = Padding.Empty,
            };
            for (int i = buttons.Length - 1; i >= 0; i--)
            {
                int index = i;
                var b = new Button
                {
                    Text = buttons[i], AutoSize = true, MinimumSize = new Size(80, 26),
                    Margin = new Padding(8, 0, 0, 0), Padding = new Padding(6, 0, 6, 0),
                };
                b.Click += (s, e) => { Choice = index; DialogResult = DialogResult.OK; };
                row.Controls.Add(b);
                if (i == defaultButton) AcceptButton = b;
                if (i == cancelButton) { CancelButton = b; b.DialogResult = DialogResult.Cancel; }
            }
            outer.Controls.Add(row, 0, 1);
            outer.SetColumnSpan(row, 2);

            Controls.Add(outer);
            // Enter clicks the focused button: start on the default one
            Shown += (s, e) => (AcceptButton as Button)?.Focus();
        }
    }
}
