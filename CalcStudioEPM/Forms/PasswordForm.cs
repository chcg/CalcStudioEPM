using System;
using System.Drawing;
using System.Windows.Forms;
using CalcStudioEPM.Config;

namespace CalcStudioEPM.Forms
{
    /// <summary>
    /// Asks for the password of a connection that doesn't save it. The user chooses how long
    /// to keep it: for this action only, for this Notepad++ session (encrypted in memory), or
    /// saved on the connection so they are not asked again.
    ///
    /// Use <see cref="Ensure"/>: it returns at once when a password is already available.
    /// The layout sizes itself to its text, so nothing is cropped at any display scaling.
    /// </summary>
    public class PasswordForm : PluginForm
    {
        public enum Keep { ThisAction, Session, Connection }

        private const int ContentWidth = 440;

        private readonly TextBox     _txtPassword;
        private readonly RadioButton _optAction;
        private readonly RadioButton _optSession;
        private readonly RadioButton _optSave;

        public string Password => _txtPassword.Text;

        public Keep Choice =>
            _optSave    != null && _optSave.Checked    ? Keep.Connection :
            _optSession != null && _optSession.Checked ? Keep.Session    : Keep.ThisAction;

        /// <summary>
        /// Makes sure <paramref name="cfg"/> has a password (saved, for this session, or for
        /// the current action), asking the user if not. False = the user cancelled.
        /// </summary>
        /// <param name="allowSave">Offer "save on the connection" (the Connections window
        /// has its own "Save password" box, so it passes false).</param>
        public static bool Ensure(IWin32Window owner, ConnectionConfig cfg, bool allowSave = true)
        {
            if (cfg == null) return false;
            if (Credentials.IsAvailable(cfg)) return true;

            using (var dlg = new PasswordForm(cfg, allowSave))
            {
                if (dlg.ShowDialog(owner) != DialogResult.OK) return false;
                RememberChoice(cfg, dlg.Choice);

                switch (dlg.Choice)
                {
                    case Keep.Connection:
                        cfg.SavePassword      = true;
                        cfg.EncryptedPassword = ConfigManager.EncryptPassword(dlg.Password);
                        ConfigManager.UpsertConfig(cfg);
                        break;
                    case Keep.Session:
                        Credentials.RememberForSession(cfg, dlg.Password);
                        break;
                    default:
                        Credentials.UseForThisAction(cfg, dlg.Password);
                        break;
                }
                return true;
            }
        }

        /// <summary>Identifies the connection in <see cref="UiState.PasswordKeep"/>: its local
        /// folder, or its URL and user while it has none (a new connection being tested).</summary>
        private static string KeyOf(ConnectionConfig cfg) =>
            (!string.IsNullOrWhiteSpace(cfg.LocalFolderPath)
                ? cfg.LocalFolderPath.Trim().TrimEnd('\\', '/')
                : $"{cfg.ServiceBaseUrl}|{cfg.Username}").ToLowerInvariant();

        private static Keep LastChoice(ConnectionConfig cfg) =>
            UiState.Load().PasswordKeep.TryGetValue(KeyOf(cfg), out string s) && Enum.TryParse(s, out Keep k) ? k : Keep.Session;

        private static void RememberChoice(ConnectionConfig cfg, Keep choice)
        {
            UiState state = UiState.Load();
            state.PasswordKeep[KeyOf(cfg)] = choice.ToString();
            state.Save();
        }

        private PasswordForm(ConnectionConfig cfg, bool allowSave)
        {
            string conn = string.IsNullOrWhiteSpace(cfg.Name) ? cfg.ServiceBaseUrl : cfg.Name;
            Text            = "EPM password — " + conn;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            MinimizeBox     = false;
            ShowIcon        = false;
            StartPosition   = FormStartPosition.CenterParent;

            // The window follows its content
            AutoSize     = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            // [icon] | everything else, stacked top-down
            var outer = new TableLayoutPanel
            {
                ColumnCount  = 2,
                AutoSize     = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding      = new Padding(12, 14, 14, 12),
                Location     = new Point(0, 0),
            };
            outer.Controls.Add(new PictureBox
            {
                Image    = SystemIcons.Shield.ToBitmap(),
                Size     = new Size(32, 32),
                SizeMode = PictureBoxSizeMode.Zoom,
                Margin   = new Padding(0, 0, 12, 0),
            }, 0, 0);

            var stack = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents  = false,
                AutoSize      = true,
                AutoSizeMode  = AutoSizeMode.GrowAndShrink,
                Margin        = Padding.Empty,
            };
            outer.Controls.Add(stack, 1, 0);

            stack.Controls.Add(WrappingLabel(
                $"Enter the EPM Cloud password of '{cfg.Username}' for the connection '{conn}'.",
                new Padding(0, 0, 0, 10)));

            // Password: [__________]
            var row = new TableLayoutPanel
            {
                ColumnCount = 2,
                Size        = new Size(ContentWidth, 28),
                Margin      = new Padding(0, 0, 0, 12),
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            row.Controls.Add(new Label
            {
                Text     = "Password:",
                AutoSize = true,
                Anchor   = AnchorStyles.Left,
                Margin   = new Padding(0, 0, 8, 0),
            }, 0, 0);
            _txtPassword = new TextBox
            {
                UseSystemPasswordChar = true,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = Padding.Empty,
            };
            row.Controls.Add(_txtPassword, 1, 0);
            stack.Controls.Add(row);

            stack.Controls.Add(WrappingLabel("Keep the password:", new Padding(0, 0, 0, 4)));
            _optAction  = Option("For this action only — ask again next time");
            _optSession = Option("For this Notepad++ session (kept encrypted in memory)");
            stack.Controls.Add(_optAction);
            stack.Controls.Add(_optSession);
            if (allowSave)
            {
                _optSave = Option("Saved on the connection (encrypted) — don't ask again");
                stack.Controls.Add(_optSave);
            }

            // Start on this connection's last choice (this session the first time)
            Keep last = LastChoice(cfg);
            (last == Keep.ThisAction ? _optAction : last == Keep.Connection && _optSave != null ? _optSave : _optSession)
                .Checked = true;

            // Buttons, right-aligned
            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Size          = new Size(ContentWidth, 30),
                Margin        = new Padding(0, 14, 0, 0),
            };
            var cancel = new Button
            {
                Text = "Cancel", AutoSize = true, MinimumSize = new Size(80, 26),
                DialogResult = DialogResult.Cancel, Margin = Padding.Empty,
            };
            var ok = new Button
            {
                Text = "OK", AutoSize = true, MinimumSize = new Size(80, 26),
                DialogResult = DialogResult.OK, Enabled = false, Margin = new Padding(0, 0, 8, 0),
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            stack.Controls.Add(buttons);

            _txtPassword.TextChanged += (s, e) => ok.Enabled = _txtPassword.Text.Length > 0;

            Controls.Add(outer);
            AcceptButton  = ok;
            CancelButton  = cancel;
            ActiveControl = _txtPassword;
        }

        private static Label WrappingLabel(string text, Padding margin) =>
            new Label
            {
                Text        = text,
                AutoSize    = true,
                MaximumSize = new Size(ContentWidth, 0),
                Margin      = margin,
            };

        private static RadioButton Option(string text) =>
            new RadioButton
            {
                Text     = text,   // radio buttons don't wrap: the window widens instead
                AutoSize = true,
                Margin   = new Padding(0, 0, 0, 4),
            };
    }
}
