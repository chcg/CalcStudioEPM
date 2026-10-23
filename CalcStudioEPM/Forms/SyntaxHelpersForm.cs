using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using CalcStudioEPM.Config;
using CalcStudioEPM.Epm;

namespace CalcStudioEPM.Forms
{
    /// <summary>
    /// Editor for the Groovy syntax helpers offered by the Variables window. Each helper
    /// has a default template plus any number of variable-type-specific templates.
    /// On Save the list is written to syntax_helpers.json and exposed via <see cref="Helpers"/>.
    /// </summary>
    public class SyntaxHelpersForm : PluginForm
    {
        private class TypeOption
        {
            public string Key   { get; set; }
            public string Label { get; set; }
        }

        private ListBox      _list;
        private TextBox      _txtName, _txtTemplate, _txtPreview;
        private DataGridView _grid;
        private Button       _btnRemove, _btnUp, _btnDown;
        private bool         _loading;

        private readonly List<SyntaxHelper> _helpers;
        private readonly List<TypeOption>   _types;

        public List<SyntaxHelper> Helpers => _helpers;

        /// <param name="extraTypes">Variable types seen in the connection, added to the known list.</param>
        public SyntaxHelpersForm(List<SyntaxHelper> helpers, IEnumerable<string> extraTypes = null)
        {
            // Work on copies so Cancel leaves the caller's list untouched
            _helpers = helpers.Select(h => h.Clone()).ToList();

            // Type choices: Calc Manager's known types, the connection's types, and any
            // type already used by a helper (so nothing saved earlier becomes unselectable)
            var keys = new List<string>(SyntaxHelpers.KnownTypes);
            foreach (string t in (extraTypes ?? Enumerable.Empty<string>())
                                 .Concat(_helpers.SelectMany(h => h.TypeTemplates.Keys)))
                if (!string.IsNullOrWhiteSpace(t) && !keys.Contains(t, StringComparer.OrdinalIgnoreCase))
                    keys.Add(t);
            _types = keys.Select(k => new TypeOption
            {
                Key   = k,
                // "Member", but "Numeric string  (str_as_num)" where the key differs
                Label = string.Equals(VariableEntry.LabelFor(k), k, StringComparison.OrdinalIgnoreCase)
                    ? VariableEntry.LabelFor(k) : $"{VariableEntry.LabelFor(k)}  ({k})",
            }).ToList();

            InitializeComponents();
            RefreshList(0);
        }

        // ------------------------------------------------------------------ //
        //  Layout
        // ------------------------------------------------------------------ //

        private void InitializeComponents()
        {
            Text            = "Groovy syntax helpers";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            MinimizeBox     = false;
            StartPosition   = FormStartPosition.CenterParent;
            ClientSize      = new Size(700, 470);
            AllowResize("SyntaxHelpers");

            // ---- Helper list (left) ----
            _list = new ListBox { Location = new Point(12, 12), Size = new Size(180, 376), IntegralHeight = false,
                                 Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left };
            _list.SelectedIndexChanged += (s, e) => ShowSelected();
            Controls.Add(_list);

            var add    = AddButton("Add",    12,  396, 86, (s, e) => AddHelper());
            _btnRemove = AddButton("Remove", 106, 396, 86, (s, e) => RemoveHelper());
            _btnUp     = AddButton("▲ Up",   12,  428, 86, (s, e) => MoveSelected(-1));
            _btnDown   = AddButton("▼ Down", 106, 428, 86, (s, e) => MoveSelected(+1));
            foreach (var b in new[] { add, _btnRemove, _btnUp, _btnDown })
                b.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;

            // ---- Selected helper (right) ----
            int x = 208, w = 480;
            AddLabel("Name", x, 12);
            _txtName = AddTextBox(x, 30, w);
            _txtName.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            AddLabel("Default template — used for every type without its own template below", x, 62);
            _txtTemplate = AddTextBox(x, 80, w);
            _txtTemplate.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            AddLabel("Type-specific templates", x, 112);
            _grid = new DataGridView
            {
                Location                 = new Point(x, 130),
                Size                     = new Size(w, 150),
                Anchor                   = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                AllowUserToAddRows       = true,
                AllowUserToDeleteRows    = true,
                AllowUserToResizeRows    = false,
                RowHeadersWidth          = 24,
                AutoSizeColumnsMode      = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor          = SystemColors.Window,
                EditMode                 = DataGridViewEditMode.EditOnEnter,
            };
            var typeCol = new DataGridViewComboBoxColumn
            {
                HeaderText    = "Variable type",
                DataSource    = _types,
                DisplayMember = nameof(TypeOption.Label),
                ValueMember   = nameof(TypeOption.Key),
                FillWeight    = 44,
                FlatStyle     = FlatStyle.Flat,
            };
            var tmplCol = new DataGridViewTextBoxColumn
            {
                HeaderText   = "Template",
                FillWeight   = 56,
                DefaultCellStyle = { Font = new Font("Consolas", 9f) },
            };
            _grid.Columns.Add(typeCol);
            _grid.Columns.Add(tmplCol);
            _grid.CellValueChanged += (s, e) => CommitGrid();
            _grid.UserDeletedRow   += (s, e) => CommitGrid();
            // Commit combo choices immediately instead of on cell exit
            _grid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewComboBoxCell)
                    _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _grid.DataError += (s, e) => e.ThrowException = false;
            Controls.Add(_grid);

            var hint = new Label
            {
                Text      = "{name} is replaced by the variable name. Add a row per type that needs " +
                            "different syntax; select a row and press Delete to remove it.",
                Location  = new Point(x, 284),
                Size      = new Size(w, 32),
                Anchor    = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                ForeColor = SystemColors.GrayText,
            };
            Controls.Add(hint);

            AddLabel("Preview for variable RTP_Year", x, 318).Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _txtPreview = new TextBox
            {
                Location   = new Point(x, 336), Size = new Size(w, 84), Multiline = true,
                Anchor     = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                ReadOnly   = true, ScrollBars = ScrollBars.Both, WordWrap = false,
                Font       = new Font("Consolas", 9f),
            };
            Controls.Add(_txtPreview);

            _txtName.TextChanged     += (s, e) => UpdateSelected();
            _txtTemplate.TextChanged += (s, e) => UpdateSelected();

            var restore = AddButton("Restore defaults", x, 428, 120, (s, e) =>
            {
                _helpers.Clear();
                _helpers.AddRange(SyntaxHelpers.Defaults());
                RefreshList(0);
            });

            var ok     = AddButton("Save",   520, 428, 80, (s, e) => Save());
            var cancel = AddButton("Cancel", 608, 428, 80, (s, e) => { DialogResult = DialogResult.Cancel; Close(); });
            restore.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            ok.Anchor      = AnchorStyles.Bottom | AnchorStyles.Right;
            cancel.Anchor  = AnchorStyles.Bottom | AnchorStyles.Right;
            AcceptButton = ok;
            CancelButton = cancel;
        }

        // ------------------------------------------------------------------ //
        //  List handling
        // ------------------------------------------------------------------ //

        private void RefreshList(int select)
        {
            _loading = true;
            _list.Items.Clear();
            foreach (var h in _helpers) _list.Items.Add(h);
            _loading = false;
            if (_helpers.Count > 0)
                _list.SelectedIndex = Math.Max(0, Math.Min(select, _helpers.Count - 1));
            else
                ShowSelected();
        }

        private SyntaxHelper Selected =>
            _list.SelectedIndex >= 0 && _list.SelectedIndex < _helpers.Count ? _helpers[_list.SelectedIndex] : null;

        /// <summary>
        /// The helper whose values are in the editor fields right now. Edits always go to
        /// this one — during a selection change the list already points at the next helper.
        /// </summary>
        private SyntaxHelper _shown;

        private void ShowSelected()
        {
            if (_loading) return;

            // Flush a half-finished grid edit into the helper being left
            if (_grid.IsCurrentCellInEditMode) _grid.EndEdit();
            CommitGrid();

            SyntaxHelper h = _shown = Selected;

            _loading = true;
            _txtName.Text     = h?.Name ?? "";
            _txtTemplate.Text = h?.Template ?? "";
            _grid.Rows.Clear();
            if (h != null)
                foreach (var kv in h.TypeTemplates)
                    _grid.Rows.Add(KeyFor(kv.Key), kv.Value);
            _loading = false;

            bool any = h != null;
            _txtName.Enabled = _txtTemplate.Enabled = _grid.Enabled = any;
            _btnRemove.Enabled = any;
            _btnUp.Enabled     = any && _list.SelectedIndex > 0;
            _btnDown.Enabled   = any && _list.SelectedIndex < _helpers.Count - 1;
            UpdatePreview();
        }

        /// <summary>Maps a stored type to the exact key in the combo's list (case-insensitive).</summary>
        private string KeyFor(string type) =>
            _types.FirstOrDefault(t => string.Equals(t.Key, type, StringComparison.OrdinalIgnoreCase))?.Key ?? type;

        private void UpdateSelected()
        {
            if (_loading) return;
            SyntaxHelper h = _shown;
            if (h == null) return;
            h.Name     = _txtName.Text;
            h.Template = _txtTemplate.Text;

            // Refresh the list caption without losing the selection
            int i = _helpers.IndexOf(h);
            _loading = true;
            if (i >= 0) _list.Items[i] = h;
            _loading = false;
            UpdatePreview();
        }

        /// <summary>Copies the grid rows into the shown helper's type templates.</summary>
        private void CommitGrid()
        {
            if (_loading) return;
            SyntaxHelper h = _shown;
            if (h == null) return;

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.IsNewRow) continue;
                string type = row.Cells[0].Value as string;
                string tmpl = row.Cells[1].Value as string;
                if (string.IsNullOrWhiteSpace(type)) continue;
                map[type] = tmpl ?? "";   // a duplicate type: the lower row wins (flagged on Save)
            }
            h.TypeTemplates = map;
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            SyntaxHelper h = _shown;
            if (h == null) { _txtPreview.Text = ""; return; }

            var lines = new List<string> { $"{"(any other type)",-26} {h.Format("RTP_Year", "")}" };
            foreach (var kv in h.TypeTemplates.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)))
                lines.Add($"{VariableEntry.LabelFor(kv.Key),-26} {h.Format("RTP_Year", kv.Key)}");
            _txtPreview.Text = string.Join(Environment.NewLine, lines);
        }

        private void AddHelper()
        {
            _helpers.Add(new SyntaxHelper { Name = "New helper", Template = "rtps.{name}" });
            RefreshList(_helpers.Count - 1);
            _txtName.Focus();
            _txtName.SelectAll();
        }

        private void RemoveHelper()
        {
            int i = _list.SelectedIndex;
            if (i < 0) return;
            _helpers.RemoveAt(i);
            RefreshList(i);
        }

        private void MoveSelected(int delta)
        {
            int i = _list.SelectedIndex, j = i + delta;
            if (i < 0 || j < 0 || j >= _helpers.Count) return;
            (_helpers[i], _helpers[j]) = (_helpers[j], _helpers[i]);
            RefreshList(j);
        }

        private void Save()
        {
            _grid.EndEdit();
            CommitGrid();

            string problem = null;
            SyntaxHelper bad = null;
            foreach (SyntaxHelper h in _helpers)
            {
                if (string.IsNullOrWhiteSpace(h.Name) || string.IsNullOrWhiteSpace(h.Template))
                { bad = h; problem = "Every helper needs a name and a default template."; break; }

                var dupes = DuplicateTypes(h);
                if (dupes.Count > 0)
                { bad = h; problem = $"'{h.Name}' has more than one template for: {string.Join(", ", dupes)}."; break; }
            }
            if (_helpers.Count == 0) problem = "Keep at least one helper.";

            if (problem != null)
            {
                if (bad != null) _list.SelectedItem = bad;
                MessageBox.Show(this, problem, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Rows with a type but no template are dropped — they'd only shadow the default
            foreach (SyntaxHelper h in _helpers)
                h.TypeTemplates = h.TypeTemplates
                    .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                    .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

            SyntaxHelpers.Save(_helpers);
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>Types that appear on more than one grid row (only checkable for the selected helper).</summary>
        private List<string> DuplicateTypes(SyntaxHelper h)
        {
            if (h != _shown) return new List<string>();
            return _grid.Rows.Cast<DataGridViewRow>()
                .Where(r => !r.IsNewRow)
                .Select(r => r.Cells[0].Value as string)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => VariableEntry.LabelFor(g.Key))
                .ToList();
        }

        // ------------------------------------------------------------------ //
        //  Small layout helpers
        // ------------------------------------------------------------------ //

        private Label AddLabel(string text, int x, int y)
        {
            var l = new Label { Text = text, Location = new Point(x, y), AutoSize = true };
            Controls.Add(l);
            return l;
        }

        private TextBox AddTextBox(int x, int y, int w)
        {
            var t = new TextBox { Location = new Point(x, y), Size = new Size(w, 23), Font = new Font("Consolas", 9f) };
            Controls.Add(t);
            return t;
        }

        private Button AddButton(string text, int x, int y, int w, EventHandler click)
        {
            var b = new Button { Text = text, Location = new Point(x, y), Size = new Size(w, 26) };
            b.Click += click;
            Controls.Add(b);
            return b;
        }
    }
}
