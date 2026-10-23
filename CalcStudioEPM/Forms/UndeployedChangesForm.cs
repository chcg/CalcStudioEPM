using System.Drawing;
using System.Windows.Forms;

namespace CalcStudioEPM.Forms
{
    /// <summary>
    /// Shown before executing when the editor holds changes that are not deployed.
    /// Unticked (default): run the version deployed in EPM Cloud.
    /// Ticked: deploy the editor's version first, then run it.
    /// </summary>
    public class UndeployedChangesForm : PluginForm
    {
        private readonly CheckBox _chkDeploy;

        public bool DeployFirst => _chkDeploy.Checked;

        public UndeployedChangesForm(string ruleName)
        {
            Text            = "Undeployed changes";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            MinimizeBox     = false;
            ShowIcon        = false;
            StartPosition   = FormStartPosition.CenterParent;
            ClientSize      = new Size(480, 196);
            AllowResize("UndeployedChanges");

            var icon = new PictureBox
            {
                Image    = SystemIcons.Warning.ToBitmap(),
                Location = new Point(16, 16),
                Size     = new Size(32, 32),
                SizeMode = PictureBoxSizeMode.Zoom,
            };
            Controls.Add(icon);

            Controls.Add(new Label
            {
                Text = $"'{ruleName}' has changes that are not deployed to EPM Cloud.\n\n" +
                       "Executing now runs the version deployed in EPM Cloud, with its runtime " +
                       "prompts — not the script in the editor.",
                Location = new Point(60, 16),
                Size     = new Size(404, 92),
                Anchor   = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            });

            _chkDeploy = new CheckBox
            {
                Text     = "Deploy my changes first, then execute them",
                Location = new Point(60, 116),
                AutoSize = true,
                Checked  = false,
                Anchor   = AnchorStyles.Bottom | AnchorStyles.Left,
            };
            Controls.Add(_chkDeploy);

            var ok = new Button
            {
                Text = "Continue", Location = new Point(300, 156), Size = new Size(80, 26),
                DialogResult = DialogResult.OK, Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            };
            var cancel = new Button
            {
                Text = "Cancel", Location = new Point(388, 156), Size = new Size(80, 26),
                DialogResult = DialogResult.Cancel, Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }
    }
}
