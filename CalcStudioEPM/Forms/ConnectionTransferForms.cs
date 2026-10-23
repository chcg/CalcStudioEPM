using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using CalcStudioEPM.Config;

namespace CalcStudioEPM.Forms
{
    /// <summary>
    /// Export Connections: pick the connections and, to include their saved passwords, an
    /// encryption key (typed twice). Without a key they are exported without passwords.
    /// </summary>
    public class ExportConnectionsForm : PluginForm
    {
        private readonly CheckedListBox _list;
        private readonly TextBox _txtKey, _txtConfirm;
        private readonly List<ConnectionConfig> _connections;

        public ExportConnectionsForm(List<ConnectionConfig> connections, ConnectionConfig selected)
        {
            _connections = connections;
            Text          = "CalcStudio — Export Connections";
            MaximizeBox   = false;
            MinimizeBox   = false;
            ShowIcon      = false;
            StartPosition = FormStartPosition.CenterParent;
            Size          = new Size(520, 420);
            AllowResize("ExportConnections");

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // 0 caption
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));   // 1 list
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // 2 key
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // 3 confirm
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // 4 note
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));   // 5 buttons

            var caption = new Label { Text = "Connections to export:", AutoSize = true, Margin = new Padding(3, 0, 3, 4) };
            layout.Controls.Add(caption, 0, 0);
            layout.SetColumnSpan(caption, 2);

            _list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
            foreach (ConnectionConfig c in connections)
                _list.Items.Add(Describe(c), selected == null || ReferenceEquals(c, selected) ||
                                             ConfigManager.SameFolder(c.LocalFolderPath, selected.LocalFolderPath));
            layout.Controls.Add(_list, 0, 1);
            layout.SetColumnSpan(_list, 2);

            _txtKey     = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true, Margin = new Padding(3, 8, 3, 3) };
            _txtConfirm = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
            layout.Controls.Add(FieldLabel("Encryption key:", 8), 0, 2);
            layout.Controls.Add(_txtKey, 1, 2);
            layout.Controls.Add(FieldLabel("Confirm key:", 3), 0, 3);
            layout.Controls.Add(_txtConfirm, 1, 3);

            var note = new Label
            {
                Text = "The key encrypts the saved passwords in the file, and is needed again to import them. " +
                       "Leave it empty to export the connections without passwords.",
                AutoSize = true, MaximumSize = new Size(480, 0), ForeColor = SystemColors.GrayText,
                Margin = new Padding(3, 6, 3, 3),
            };
            layout.Controls.Add(note, 0, 4);
            layout.SetColumnSpan(note, 2);
            layout.Resize += (s, e) => note.MaximumSize = new Size(Math.Max(100, layout.ClientSize.Width - 30), 0);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 75 };
            var export = new Button { Text = "Export…", Width = 90 };
            export.Click += OnExport;
            buttons.Controls.AddRange(new Control[] { cancel, export });
            layout.Controls.Add(buttons, 0, 5);
            layout.SetColumnSpan(buttons, 2);

            Controls.Add(layout);
            AcceptButton = export;
            CancelButton = cancel;
        }

        internal static string Describe(ConnectionConfig c) =>
            $"{(string.IsNullOrWhiteSpace(c.Name) ? "(unnamed)" : c.Name)}  —  {c.Application}  —  {c.LocalFolderPath}";

        internal static Label FieldLabel(string text, int top) =>
            new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, top + 3, 6, 3) };

        private void OnExport(object sender, EventArgs e)
        {
            var chosen = _list.CheckedIndices.Cast<int>().Select(i => _connections[i]).ToList();
            if (chosen.Count == 0)
            {
                MessageBox.Show(this, "Tick at least one connection to export.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string key = _txtKey.Text;
            if (key != _txtConfirm.Text)
            {
                MessageBox.Show(this, "The encryption key and its confirmation are different.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (key.Length > 0 && key.Length < 8)
            {
                MessageBox.Show(this, "Use an encryption key of at least 8 characters: it protects real passwords.",
                                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (var dlg = new SaveFileDialog
            {
                Title      = "Export connections",
                Filter     = "CalcStudio connections (*.json)|*.json|All files (*.*)|*.*",
                FileName   = chosen.Count == 1 && !string.IsNullOrWhiteSpace(chosen[0].Name)
                                 ? $"{string.Concat(chosen[0].Name.Split(Path.GetInvalidFileNameChars()))} connection.json"
                                 : "CalcStudio connections.json",
                OverwritePrompt = true,
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    int passwords = ConnectionTransfer.Export(dlg.FileName, chosen, key);
                    int saved = chosen.Count(c => c.SavePassword && !string.IsNullOrEmpty(c.EncryptedPassword));
                    string pw = key.Length == 0 ? "without passwords (no encryption key given)"
                              : passwords == saved ? $"with {passwords} encrypted password(s)"
                              : $"with {passwords} encrypted password(s); {saved - passwords} could not be read and were left out";
                    if (key.Length > 0 && saved == 0) pw = "without passwords (none of them has a saved password)";
                    MessageBox.Show(this, $"Exported {chosen.Count} connection(s) {pw} to:\n\n{dlg.FileName}",
                                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    DialogResult = DialogResult.OK;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Export failed: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }

    /// <summary>
    /// Import Connections from an export file: tick the ones to import, change their local
    /// folders for this PC if needed, and give the encryption key to import the passwords
    /// (empty = without passwords). Folders are checked like on Save: not nested with each
    /// other or with existing connections; the same folder as an existing one replaces it.
    /// </summary>
    public class ImportConnectionsForm : PluginForm
    {
        private readonly ConnectionTransfer.ExportFile _file;
        private readonly DataGridView _grid;
        private readonly TextBox _txtKey;

        /// <summary>The last imported connection's folder (to select it), or null.</summary>
        public string ImportedFolder { get; private set; }
        public int ImportedCount { get; private set; }

        public ImportConnectionsForm(ConnectionTransfer.ExportFile file, string path)
        {
            _file = file;
            Text          = "CalcStudio — Import Connections";
            MaximizeBox   = false;
            MinimizeBox   = false;
            ShowIcon      = false;
            StartPosition = FormStartPosition.CenterParent;
            Size          = new Size(900, 420);
            AllowResize("ImportConnections");

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // 0 caption
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));   // 1 grid
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // 2 browse
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // 3 key
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // 4 note
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));   // 5 buttons

            var caption = new Label
            {
                Text = $"{file.Connections.Count} connection(s) in {Path.GetFileName(path)}, exported {file.ExportedAt.ToLocalTime():g}. " +
                       "Tick the ones to import; the local folder can be changed for this PC.",
                AutoSize = true, MaximumSize = new Size(680, 0), Margin = new Padding(3, 0, 3, 6),
            };
            layout.Controls.Add(caption, 0, 0);
            layout.SetColumnSpan(caption, 2);
            layout.Resize += (s, e) => caption.MaximumSize = new Size(Math.Max(100, layout.ClientSize.Width - 30), 0);

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                EditMode = DataGridViewEditMode.EditOnEnter,
                BackgroundColor = SystemColors.Window,
            };
            _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Import", HeaderText = "Import", FillWeight = 12 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Name", FillWeight = 26 });
            // Shown so the user sees where the (imported or later typed) password will be sent
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Url", HeaderText = "Service URL", FillWeight = 40, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "User", HeaderText = "Username", FillWeight = 22, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "App", HeaderText = "Application", FillWeight = 18, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Password", HeaderText = "Password", FillWeight = 14, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Folder", HeaderText = "Local Folder", FillWeight = 44 });
            foreach (ConnectionTransfer.ExportedConnection c in file.Connections)
                _grid.Rows.Add(true, c.Name, c.ServiceBaseUrl, c.Username, c.Application,
                               string.IsNullOrEmpty(c.Password) ? "—" : "encrypted", c.LocalFolderPath);
            layout.Controls.Add(_grid, 0, 1);
            layout.SetColumnSpan(_grid, 2);

            var browse = new Button { Text = "Change Folder…", AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
            browse.Click += OnBrowse;
            layout.Controls.Add(browse, 0, 2);

            _txtKey = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true, Margin = new Padding(3, 8, 3, 3) };
            layout.Controls.Add(ExportConnectionsForm.FieldLabel("Encryption key:", 8), 0, 3);
            layout.Controls.Add(_txtKey, 1, 3);
            _txtKey.Enabled = file.HasPasswords;

            var note = new Label
            {
                Text = file.HasPasswords
                    ? "Enter the key the file was exported with to import the passwords. Leave it empty to import " +
                      "without passwords: you will be asked for them when they are needed."
                    : "This file has no passwords: you will be asked for them when they are needed.",
                AutoSize = true, MaximumSize = new Size(680, 0), ForeColor = SystemColors.GrayText,
                Margin = new Padding(3, 6, 3, 3),
            };
            layout.Controls.Add(note, 0, 4);
            layout.SetColumnSpan(note, 2);
            layout.Resize += (s, e) => note.MaximumSize = new Size(Math.Max(100, layout.ClientSize.Width - 30), 0);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 75 };
            var import = new Button { Text = "Import", Width = 90 };
            import.Click += OnImport;
            buttons.Controls.AddRange(new Control[] { cancel, import });
            layout.Controls.Add(buttons, 0, 5);
            layout.SetColumnSpan(buttons, 2);

            Controls.Add(layout);
            CancelButton = cancel;
        }

        private void OnBrowse(object sender, EventArgs e)
        {
            int row = _grid.CurrentCell?.RowIndex ?? -1;
            if (row < 0) { MessageBox.Show(this, "Select a connection first.", Text); return; }
            _grid.EndEdit();
            using (var dlg = new FolderBrowserDialog { Description = "Local folder for " + _grid.Rows[row].Cells["Name"].Value, ShowNewFolderButton = true })
            {
                string current = _grid.Rows[row].Cells["Folder"].Value as string;
                if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current)) dlg.SelectedPath = current;
                if (dlg.ShowDialog(this) == DialogResult.OK) _grid.Rows[row].Cells["Folder"].Value = dlg.SelectedPath;
            }
        }

        private void OnImport(object sender, EventArgs e)
        {
            _grid.EndEdit();
            string key = _txtKey.Text;
            if (key.Length > 0 && !ConnectionTransfer.IsKeyCorrect(_file, key))
            {
                MessageBox.Show(this, "That is not the key this file was exported with.\n\n" +
                                      "Check it, or leave it empty to import without passwords.",
                                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _txtKey.Focus(); _txtKey.SelectAll();
                return;
            }

            // The ticked rows, as connections for this PC
            var chosen = new List<ConnectionConfig>();
            try
            {
                for (int i = 0; i < _grid.Rows.Count; i++)
                {
                    DataGridViewRow r = _grid.Rows[i];
                    if (!(r.Cells["Import"].Value is bool b && b)) continue;
                    ConnectionConfig cfg = ConnectionTransfer.ToConfig(_file, _file.Connections[i], key);
                    cfg.Name = ((r.Cells["Name"].Value as string) ?? string.Empty).Trim();
                    cfg.LocalFolderPath = ((r.Cells["Folder"].Value as string) ?? string.Empty).Trim();
                    chosen.Add(cfg);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not read the passwords: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (chosen.Count == 0)
            {
                MessageBox.Show(this, "Tick at least one connection to import.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Folders: set, not nested with existing connections (the same folder replaces
            // that connection) nor with each other
            var existing = ConfigManager.GetAll();
            var replaced = new List<ConnectionConfig>();
            foreach (ConnectionConfig c in chosen)
            {
                string who = string.IsNullOrWhiteSpace(c.Name) ? "(unnamed)" : $"'{c.Name}'";
                string problem = string.IsNullOrWhiteSpace(c.Name) ? "The name is empty."
                    : string.IsNullOrWhiteSpace(c.LocalFolderPath) ? "The local folder is empty."
                    : ConfigManager.FolderConflict(c.LocalFolderPath, ignoreFolder: c.LocalFolderPath, connections: existing)
                      ?? ConfigManager.FolderConflict(c.LocalFolderPath, connections: chosen.Where(o => !ReferenceEquals(o, c)));
                if (problem != null)
                {
                    MessageBox.Show(this, $"Can't import {who}:\n\n{problem}\n\nChange its local folder, or untick it.",
                                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                ConnectionConfig same = existing.FirstOrDefault(x => ConfigManager.SameFolder(x.LocalFolderPath, c.LocalFolderPath));
                if (same != null) replaced.Add(same);
            }
            if (replaced.Count > 0 &&
                MessageBox.Show(this, "These connections use the same local folder and will be replaced:\n\n" +
                                      string.Join("\n", replaced.Select(ExportConnectionsForm.Describe)) + "\n\nContinue?",
                                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            foreach (ConnectionConfig c in chosen)
            {
                ConnectionConfig same = existing.FirstOrDefault(x => ConfigManager.SameFolder(x.LocalFolderPath, c.LocalFolderPath));
                ConfigManager.UpsertConfig(c, replacingFolder: same?.LocalFolderPath);
            }
            ImportedCount  = chosen.Count;
            ImportedFolder = chosen.Last().LocalFolderPath;
            DialogResult   = DialogResult.OK;
        }
    }
}
