using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using CalcStudioEPM.Epm;

namespace CalcStudioEPM.Forms
{
    /// <summary>
    /// Asks for the values of a rule's runtime prompts before executing it. Each field is
    /// pre-filled with the value last used for that variable on this connection, or else
    /// the default stored on the rule.
    /// </summary>
    public class RtpForm : PluginForm
    {
        private readonly List<RuntimePrompt> _prompts;
        private readonly List<TextBox>       _boxes = new List<TextBox>();

        /// <summary>Variable name → value, filled when the user clicks Execute.</summary>
        public Dictionary<string, string> RtpValues { get; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public RtpForm(List<RuntimePrompt> prompts, string ruleName, IDictionary<string, string> lastValues)
        {
            _prompts = prompts ?? new List<RuntimePrompt>();

            Text            = $"Runtime prompts — {ruleName}";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            MinimizeBox     = false;
            ShowIcon        = false;
            StartPosition   = FormStartPosition.CenterParent;

            const int width = 520, rowH = 52, maxVisibleRows = 8;

            Controls.Add(new Label
            {
                Text     = "Enter a value for each runtime prompt, then click Execute.",
                Location = new Point(12, 12),
                AutoSize = true,
            });

            // Scrolls when a rule has many prompts
            var rows = new Panel
            {
                Location   = new Point(0, 36),
                Size       = new Size(width, Math.Min(_prompts.Count, maxVisibleRows) * rowH + 4),
                AutoScroll = true,
            };
            Controls.Add(rows);

            var hintColor = SystemColors.GrayText;
            for (int i = 0; i < _prompts.Count; i++)
            {
                RuntimePrompt p = _prompts[i];
                int y = i * rowH;

                rows.Controls.Add(new Label
                {
                    Text         = p.PromptText,
                    Location     = new Point(12, y + 2),
                    Size         = new Size(200, 18),
                    Font         = new Font(Font, FontStyle.Bold),
                    AutoEllipsis = true,
                });
                rows.Controls.Add(new Label
                {
                    Text         = $"{p.Name} · {p.TypeLabel}" +
                                   (string.IsNullOrEmpty(p.Dimension) ? "" : $" · {p.Dimension}"),
                    Location     = new Point(12, y + 20),
                    Size         = new Size(200, 18),
                    ForeColor    = hintColor,
                    AutoEllipsis = true,
                });

                string value = lastValues != null && lastValues.TryGetValue(p.Name, out string last)
                    ? last
                    : p.DefaultValue ?? "";
                var box = new TextBox
                {
                    Location = new Point(220, y + 10),
                    Width    = width - 220 - 28,
                    Text     = value,
                    Anchor   = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                };
                _boxes.Add(box);
                rows.Controls.Add(box);
            }

            int buttonsY = rows.Bottom + 10;
            var execute = new Button { Text = "Execute", Location = new Point(width - 184, buttonsY), Size = new Size(80, 26) };
            var cancel  = new Button { Text = "Cancel",  Location = new Point(width - 96,  buttonsY), Size = new Size(80, 26),
                                       DialogResult = DialogResult.Cancel };
            execute.Click += (s, e) =>
            {
                for (int i = 0; i < _prompts.Count; i++)
                    RtpValues[_prompts[i].Name] = _boxes[i].Text.Trim();
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(execute);
            Controls.Add(cancel);
            AcceptButton = execute;
            CancelButton = cancel;

            ClientSize = new Size(width, buttonsY + 26 + 12);

            // Anchors after the final size, so they keep these distances when resized
            rows.Anchor    = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            execute.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            cancel.Anchor  = AnchorStyles.Bottom | AnchorStyles.Right;
            AllowResize("RuntimePrompts");
        }
    }
}
