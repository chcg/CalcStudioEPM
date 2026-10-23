using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using CalcStudioEPM.Epm;

namespace CalcStudioEPM.Forms
{
    /// <summary>
    /// Before a pull: the files with saved changes that weren't deployed, each with
    /// "Overwrite" (replaced by EPM's version, as a pull always did) or "Keep both" (backed up,
    /// then copied back beside EPM's version as {name}.local.NNN{ext}). The first row selects
    /// one option for every file. Keep both is preselected.
    /// </summary>
    internal class LocalChangesForm : PluginForm
    {
        private const int ColFile = 0, ColFolder = 1, ColOverwrite = 2, ColKeep = 3;

        private readonly List<LocalChanges.Change> _changes;
        private readonly DataGridView _grid;
        private bool _updating;

        public List<LocalChanges.Change> KeepBoth  { get; private set; } = new List<LocalChanges.Change>();
        public List<LocalChanges.Change> Overwrite { get; private set; } = new List<LocalChanges.Change>();

        public LocalChangesForm(string connection, List<LocalChanges.Change> changes)
        {
            _changes = changes;
            Text          = "Pull Calculations — " + connection;
            MaximizeBox   = false;
            MinimizeBox   = false;
            ShowIcon      = false;
            StartPosition = FormStartPosition.CenterParent;
            Size          = new Size(760, 440);
            AllowResize("LocalChanges");

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(10) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));   // the window's width, not the text's
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));

            var intro = new Label
            {
                Text = $"{changes.Count} file(s) have saved changes that weren't deployed, or aren't in EPM. " +
                       "A pull replaces the scripts folder with EPM's version. Choose for each file:",
                AutoSize = true, MaximumSize = new Size(720, 0), Margin = new Padding(3, 0, 3, 8),
            };
            layout.Controls.Add(intro, 0, 0);
            layout.Resize += (s, e) => intro.MaximumSize = new Size(Math.Max(100, layout.ClientSize.Width - 30), 0);

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
                RowHeadersVisible = false, MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = SystemColors.Window,
            };
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "File", ReadOnly = true, FillWeight = 40 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Folder", ReadOnly = true, FillWeight = 45 });
            _grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Overwrite", FillWeight = 12 });
            _grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Keep both", FillWeight = 12 });

            // Row 0: select all
            _grid.Rows.Add("", "Select all", false, true);
            DataGridViewRow all = _grid.Rows[0];
            all.DefaultCellStyle.ForeColor = SystemColors.GrayText;
            all.DefaultCellStyle.Font = new Font(_grid.Font, FontStyle.Italic);
            all.Frozen = true;
            foreach (LocalChanges.Change c in changes)
            {
                int i = _grid.Rows.Add(Path.GetFileName(c.File),
                                       (Path.GetDirectoryName(c.Relative) ?? "") + (c.Reason == "changed" ? "" : $"   ({c.Reason})"),
                                       false, true);
                _grid.Rows[i].Cells[ColFile].ToolTipText = c.Relative;
            }

            // A tick takes effect at once; one choice per row; row 0 sets them all
            _grid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _grid.CellValueChanged += OnCellValueChanged;
            layout.Controls.Add(_grid, 0, 1);

            var note = new Label
            {
                Text = "Overwrite: the file is replaced by EPM's version (as a pull always did).\n" +
                       "Keep both: the file is backed up to .epm\\backup\\, and after the pull it is copied back beside " +
                       "EPM's version as  {name}.local.001{ext}  — open both and compare them.",
                AutoSize = true, MaximumSize = new Size(720, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 8, 3, 3),
            };
            layout.Controls.Add(note, 0, 2);
            layout.Resize += (s, e) => note.MaximumSize = new Size(Math.Max(100, layout.ClientSize.Width - 30), 0);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 75 };
            var pull = new Button { Text = "Pull", Width = 90 };
            pull.Click += (s, e) => { Collect(); DialogResult = DialogResult.OK; };
            buttons.Controls.AddRange(new Control[] { cancel, pull });
            layout.Controls.Add(buttons, 0, 3);

            Controls.Add(layout);
            Shown += (s, e) => _grid.ClearSelection();   // no row highlighted until the user picks one
            AcceptButton = pull;
            CancelButton = cancel;
        }

        private void OnCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_updating || e.RowIndex < 0 || (e.ColumnIndex != ColOverwrite && e.ColumnIndex != ColKeep)) return;
            _updating = true;
            try
            {
                int other = e.ColumnIndex == ColOverwrite ? ColKeep : ColOverwrite;
                bool on = (bool)(_grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value ?? false);
                if (e.RowIndex == 0)
                {
                    // Select all: every file gets this option (unticking it picks the other one)
                    int col = on ? e.ColumnIndex : other;
                    for (int r = 1; r < _grid.Rows.Count; r++) Set(r, col);
                }
                else
                {
                    Set(e.RowIndex, on ? e.ColumnIndex : other);
                }
                // The select-all row shows an option ticked only when every file has it
                bool allOverwrite = Enumerable.Range(1, _grid.Rows.Count - 1).All(r => (bool)_grid.Rows[r].Cells[ColOverwrite].Value);
                bool allKeep      = Enumerable.Range(1, _grid.Rows.Count - 1).All(r => (bool)_grid.Rows[r].Cells[ColKeep].Value);
                _grid.Rows[0].Cells[ColOverwrite].Value = allOverwrite;
                _grid.Rows[0].Cells[ColKeep].Value = allKeep;
            }
            finally { _updating = false; }
        }

        private void Set(int row, int col)
        {
            _grid.Rows[row].Cells[col].Value = true;
            _grid.Rows[row].Cells[col == ColOverwrite ? ColKeep : ColOverwrite].Value = false;
        }

        private void Collect()
        {
            _grid.EndEdit();
            KeepBoth.Clear(); Overwrite.Clear();
            for (int i = 0; i < _changes.Count; i++)
                ((bool)_grid.Rows[i + 1].Cells[ColKeep].Value ? KeepBoth : Overwrite).Add(_changes[i]);
        }
    }
}
