using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using CalcStudioEPM.Epm.Validation;

namespace CalcStudioEPM.Forms
{
    /// <summary>
    /// Shows the local Groovy validation results. Two modes:
    ///   Review — from "Validate Groovy Script": the list plus "Edit rules…".
    ///   Gate   — before a deploy: the list plus "Ignore local validation and deploy
    ///            anyway" (unticked); Deploy is only enabled once it is ticked.
    /// Double-clicking an issue closes the window; read <see cref="JumpTo"/> to move the
    /// editor to it (in Gate mode that also cancels the deploy).
    /// </summary>
    public class ValidationForm : PluginForm
    {
        public enum Mode { Review, Gate }

        private readonly Mode     _mode;
        private readonly ListView _list;
        private CheckBox          _chkIgnore;
        private Button            _btnDeploy;

        /// <summary>The issue the user double-clicked, if any.</summary>
        public ValidationIssue JumpTo { get; private set; }

        /// <summary>Review mode: the user asked to edit the rules file.</summary>
        public bool EditRulesRequested { get; private set; }

        public ValidationForm(Mode mode, string fileName, List<ValidationIssue> issues)
        {
            _mode = mode;

            Text          = $"Local Syntax Validation — {fileName}";
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox   = false;
            ShowIcon      = false;
            ClientSize    = new Size(760, 400);
            AllowResize("Validation", minimum96: new Size(560, 300));

            int errors   = issues.Count(i => i.IsError);
            int warnings = issues.Count - errors;
            string counts = Plural(errors, "error") + (warnings > 0 ? ", " + Plural(warnings, "warning") : "");

            // ---- Header ----
            var top = new Panel { Dock = DockStyle.Top, Size = new Size(ClientSize.Width, 52) };
            top.Controls.Add(new PictureBox
            {
                Image    = (errors > 0 ? SystemIcons.Error : SystemIcons.Warning).ToBitmap(),
                Location = new Point(12, 10), Size = new Size(32, 32), SizeMode = PictureBoxSizeMode.Zoom,
            });
            top.Controls.Add(new Label
            {
                Text = mode == Mode.Gate
                    ? $"Local validation found {counts} in {fileName}.\nFix them first (double-click to go to the line), or deploy anyway."
                    : $"{counts} in {fileName}. Double-click an issue to go to its line.",
                Location = new Point(54, 8),
                Size     = new Size(ClientSize.Width - 66, 40),
                Anchor   = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            });

            // ---- Issues ----
            _list = new ListView
            {
                Dock          = DockStyle.Fill,
                View          = View.Details,
                FullRowSelect = true,
                MultiSelect   = false,
                HideSelection = false,
                ShowItemToolTips = true,
            };
            _list.Columns.Add("",        26);
            _list.Columns.Add("Line",    50, HorizontalAlignment.Right);
            _list.Columns.Add("Message", 520);
            _list.Columns.Add("Rule",    150);
            foreach (ValidationIssue issue in issues)
            {
                var item = new ListViewItem(new[]
                {
                    issue.IsError ? "✘" : "⚠", issue.Line.ToString(), issue.Message, issue.RuleId,
                })
                {
                    Tag         = issue,
                    ForeColor   = issue.IsError ? Color.DarkRed : Color.DarkGoldenrod,
                    ToolTipText = issue.Message,
                    UseItemStyleForSubItems = false,
                };
                item.SubItems[2].ForeColor = SystemColors.ControlText;
                item.SubItems[3].ForeColor = SystemColors.GrayText;
                _list.Items.Add(item);
            }
            _list.ItemActivate += (s, e) =>
            {
                if (_list.SelectedItems.Count == 0) return;
                JumpTo       = _list.SelectedItems[0].Tag as ValidationIssue;
                DialogResult = DialogResult.Cancel;   // in Gate mode: don't deploy, go fix it
                Close();
            };

            // ---- Bottom ----
            var bottom = new Panel { Dock = DockStyle.Bottom, Size = new Size(ClientSize.Width, mode == Mode.Gate ? 76 : 44) };
            int buttonsY = bottom.Height - 34;

            if (mode == Mode.Gate)
            {
                _chkIgnore = new CheckBox
                {
                    Text     = "Ignore local validation and deploy anyway",
                    Location = new Point(12, 10),
                    AutoSize = true,
                    Checked  = false,
                };
                _chkIgnore.CheckedChanged += (s, e) => _btnDeploy.Enabled = _chkIgnore.Checked;
                bottom.Controls.Add(_chkIgnore);

                _btnDeploy = AddButton(bottom, "Deploy", 80, ClientSize.Width - 12 - 80 - 8 - 80, buttonsY, DialogResult.OK);
                _btnDeploy.Enabled = false;
                var cancel = AddButton(bottom, "Cancel", 80, ClientSize.Width - 12 - 80, buttonsY, DialogResult.Cancel);
                CancelButton = cancel;
            }
            else
            {
                var edit = AddButton(bottom, "Edit rules…", 100, 12, buttonsY, DialogResult.None);
                edit.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
                edit.Click += (s, e) => { EditRulesRequested = true; DialogResult = DialogResult.Cancel; Close(); };
                var close = AddButton(bottom, "Close", 80, ClientSize.Width - 12 - 80, buttonsY, DialogResult.Cancel);
                CancelButton = close;
            }

            Controls.Add(_list);
            Controls.Add(top);
            Controls.Add(bottom);
        }

        private static Button AddButton(Panel parent, string text, int width, int x, int y, DialogResult result)
        {
            var b = new Button
            {
                Text = text, Size = new Size(width, 26), Location = new Point(x, y),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right, DialogResult = result,
            };
            parent.Controls.Add(b);
            return b;
        }

        private static string Plural(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";
    }
}
