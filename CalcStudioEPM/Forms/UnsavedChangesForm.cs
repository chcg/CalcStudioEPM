using System.Drawing;
using System.Windows.Forms;

namespace CalcStudioEPM.Forms
{
    /// <summary>
    /// Shown by Deploy Active Tab when the tab has unsaved changes:
    /// "Save and deploy" (OK) or "Cancel".
    /// </summary>
    public class UnsavedChangesForm : PluginForm
    {
        public UnsavedChangesForm(string fileName)
        {
            Text            = "Unsaved changes";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            MinimizeBox     = false;
            ShowIcon        = false;
            StartPosition   = FormStartPosition.CenterParent;
            ClientSize      = new Size(440, 136);
            AllowResize("UnsavedChanges");

            Controls.Add(new PictureBox
            {
                Image    = SystemIcons.Warning.ToBitmap(),
                Location = new Point(16, 16),
                Size     = new Size(32, 32),
                SizeMode = PictureBoxSizeMode.Zoom,
            });

            Controls.Add(new Label
            {
                Text     = $"'{fileName}' has unsaved changes.\n\n" +
                           "Save it and deploy the saved version to EPM Cloud?",
                Location = new Point(60, 16),
                Size     = new Size(364, 64),
                Anchor   = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            });

            var save = new Button
            {
                Text = "Save and deploy", Location = new Point(224, 96), Size = new Size(120, 26),
                DialogResult = DialogResult.OK, Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            };
            var cancel = new Button
            {
                Text = "Cancel", Location = new Point(352, 96), Size = new Size(76, 26),
                DialogResult = DialogResult.Cancel, Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            };
            Controls.Add(save);
            Controls.Add(cancel);
            AcceptButton = save;
            CancelButton = cancel;
        }
    }
}
