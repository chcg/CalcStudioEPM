using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using CalcStudioEPM.Config;
using CalcStudioEPM.Epm;

namespace CalcStudioEPM.Forms
{
    /// <summary>What kind of script the active tab holds — decides the insert syntax.</summary>
    public enum ScriptKind { None, CalcScript, Groovy }

    /// <summary>
    /// Lists the connection's runtime-prompt variables (from the variable dictionary)
    /// and lets the user insert one at the cursor. Sorted by type then usage by default;
    /// clicking a column header re-sorts, and the order is remembered per connection.
    ///
    /// The form only decides WHAT to insert — on OK, read <see cref="InsertText"/> and
    /// <see cref="SelectedVariable"/>; the caller edits the document.
    /// </summary>
    public class VariablesForm : PluginForm
    {
        // Column keys — also what VariablesViewSettings stores
        private static readonly string[] ColumnKeys   = { "Name", "Type", "Dimension", "Prompt", "UsedBy" };
        private static readonly string[] ColumnTitles = { "Name", "Type", "Dimension", "Prompt text", "Used by" };
        private static readonly int[]    ColumnWidths = { 210, 105, 120, 260, 70 };

        private static string _lastHelperName;   // remembered for the session

        private readonly ConnectionConfig     _cfg;
        private readonly VariableDictionary   _dict;
        private readonly ScriptKind           _kind;
        private readonly string               _fileName;
        private List<SyntaxHelper>            _helpers;
        private List<SortKey>                 _sort;

        private TextBox   _txtFilter;
        private Button    _btnHelpers;
        private ListView  _list;
        private ComboBox  _cmbSyntax;
        private TextBox   _txtPreview;
        private Label     _lblNote;
        private Label     _lblCount;
        private Button    _btnInsert;

        public VariableEntry SelectedVariable { get; private set; }
        public string        InsertText       { get; private set; }

        public VariablesForm(ConnectionConfig cfg, VariableDictionary dict, ScriptKind kind, string fileName)
        {
            _cfg      = cfg;
            _dict     = dict;
            _kind     = kind;
            _fileName = fileName;
            _helpers  = SyntaxHelpers.Load();
            _sort     = VariablesViewSettings.LoadSort(cfg?.LocalFolderPath);

            InitializeComponents();
            LoadHelpers();
            Populate();
        }

        // ------------------------------------------------------------------ //
        //  Layout
        // ------------------------------------------------------------------ //

        private void InitializeComponents()
        {
            string conn = string.IsNullOrWhiteSpace(_cfg?.Name) ? "EPM Cloud" : _cfg.Name;
            Text          = $"Variables — {conn}";
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox   = false;
            ShowIcon      = false;
            ClientSize    = new Size(820, 520);
            AllowResize("Variables", minimum96: new Size(640, 400));

            // ---- Top bar: filter + syntax helpers ----
            // Panels get the full client width up front: right-anchored children are
            // positioned against it, and a default-width (200 px) panel would push them
            // off-screen once docking stretches it.
            var top = new Panel { Dock = DockStyle.Top, Size = new Size(ClientSize.Width, 40) };
            top.Controls.Add(new Label { Text = "Filter:", Location = new Point(12, 12), AutoSize = true });
            _txtFilter = new TextBox { Location = new Point(56, 9), Width = 240 };
            _txtFilter.TextChanged += (s, e) => Populate();
            top.Controls.Add(_txtFilter);

            _lblCount = new Label
            {
                Location = new Point(308, 12), AutoSize = true, ForeColor = SystemColors.GrayText,
            };
            top.Controls.Add(_lblCount);

            _btnHelpers = new Button
            {
                Text = "Groovy syntax helpers…", Width = 170, Height = 26,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };
            _btnHelpers.Location = new Point(ClientSize.Width - 12 - _btnHelpers.Width, 7);
            _btnHelpers.Click += (s, e) => EditHelpers();
            top.Controls.Add(_btnHelpers);

            // ---- Variable list ----
            _list = new ListView
            {
                Dock             = DockStyle.Fill,
                View             = View.Details,
                FullRowSelect    = true,
                MultiSelect      = false,
                HideSelection    = false,
                ShowItemToolTips = true,
            };
            for (int i = 0; i < ColumnKeys.Length; i++)
                _list.Columns.Add(ColumnTitles[i], ColumnWidths[i],
                    ColumnKeys[i] == "UsedBy" ? HorizontalAlignment.Right : HorizontalAlignment.Left);
            _list.ColumnClick          += (s, e) => SortBy(ColumnKeys[e.Column]);
            _list.SelectedIndexChanged += (s, e) => UpdatePreview();
            _list.ItemActivate         += (s, e) => Insert();   // double-click / Enter

            // ---- Bottom: insert syntax + preview + buttons ----
            var bottom = new Panel { Dock = DockStyle.Bottom, Size = new Size(ClientSize.Width, 96) };

            bottom.Controls.Add(new Label { Text = "Insert as:", Location = new Point(12, 14), AutoSize = true });
            _cmbSyntax = new ComboBox
            {
                Location = new Point(80, 10), Width = 200, DropDownStyle = ComboBoxStyle.DropDownList,
            };
            _cmbSyntax.SelectedIndexChanged += (s, e) => UpdatePreview();
            bottom.Controls.Add(_cmbSyntax);

            _txtPreview = new TextBox
            {
                Location = new Point(292, 10), ReadOnly = true, Font = new Font("Consolas", 9.5f),
                Anchor   = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };
            _txtPreview.Width = ClientSize.Width - 12 - _txtPreview.Left;
            bottom.Controls.Add(_txtPreview);

            _lblNote = new Label
            {
                Location  = new Point(12, 44), ForeColor = SystemColors.GrayText,
                Anchor    = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                AutoEllipsis = true,
            };
            _lblNote.Size = new Size(ClientSize.Width - 24, 18);
            bottom.Controls.Add(_lblNote);

            var btnClose = new Button
            {
                Text = "Close", Size = new Size(80, 26),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right, DialogResult = DialogResult.Cancel,
            };
            btnClose.Location = new Point(ClientSize.Width - 12 - btnClose.Width, 64);
            bottom.Controls.Add(btnClose);

            _btnInsert = new Button
            {
                Text = "Insert", Size = new Size(80, 26), Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            };
            _btnInsert.Location = new Point(btnClose.Left - 8 - _btnInsert.Width, 64);
            _btnInsert.Click += (s, e) => Insert();
            bottom.Controls.Add(_btnInsert);

            Controls.Add(_list);
            Controls.Add(top);
            Controls.Add(bottom);

            AcceptButton = _btnInsert;
            CancelButton = btnClose;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _txtFilter.Focus();
        }

        // ------------------------------------------------------------------ //
        //  Syntax options
        // ------------------------------------------------------------------ //

        private void LoadHelpers()
        {
            _cmbSyntax.Items.Clear();
            switch (_kind)
            {
                case ScriptKind.CalcScript:
                    _cmbSyntax.Items.Add("Calc script  {name}");
                    _cmbSyntax.SelectedIndex = 0;
                    _cmbSyntax.Enabled = false;
                    break;

                case ScriptKind.Groovy:
                    foreach (var h in _helpers) _cmbSyntax.Items.Add(h);
                    int i = _helpers.FindIndex(h => h.Name == _lastHelperName);
                    _cmbSyntax.SelectedIndex = i >= 0 ? i : 0;
                    _cmbSyntax.Enabled = true;
                    break;

                default:
                    _cmbSyntax.Enabled = false;
                    break;
            }
            _btnHelpers.Enabled = true;
            UpdatePreview();
        }

        private void EditHelpers()
        {
            using (var f = new SyntaxHelpersForm(_helpers, _dict.Variables.Select(v => v.Type)))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                _helpers = f.Helpers;
            }
            LoadHelpers();
        }

        // ------------------------------------------------------------------ //
        //  List + sorting
        // ------------------------------------------------------------------ //

        private void Populate()
        {
            string keep   = (_list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as VariableEntry : null)?.Name;
            string filter = _txtFilter.Text.Trim();

            IEnumerable<VariableEntry> rows = _dict.Variables;
            if (filter.Length > 0)
                rows = rows.Where(v => Contains(v.Name, filter) || Contains(v.PromptText, filter) ||
                                       Contains(v.Dimension, filter) || Contains(v.TypeLabel, filter));

            var sorted = rows.ToList();
            sorted.Sort(Compare);

            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (VariableEntry v in sorted)
            {
                var item = new ListViewItem(new[]
                {
                    v.Name, v.TypeLabel, v.Dimension ?? "", v.PromptText ?? "", v.UsedBy.ToString(),
                }) { Tag = v, ToolTipText = UsedInTooltip(v) };
                _list.Items.Add(item);
                if (v.Name == keep) item.Selected = true;
            }
            _list.EndUpdate();

            if (_list.SelectedItems.Count == 0 && _list.Items.Count > 0)
                _list.Items[0].Selected = true;
            _list.SelectedItems.Cast<ListViewItem>().FirstOrDefault()?.EnsureVisible();

            _lblCount.Text = filter.Length > 0
                ? $"{sorted.Count} of {_dict.Variables.Count} variables"
                : $"{_dict.Variables.Count} variables";
            UpdateHeaders();
            UpdatePreview();
        }

        private int Compare(VariableEntry a, VariableEntry b)
        {
            foreach (SortKey k in _sort)
            {
                int c = k.Column == "UsedBy"
                    ? a.UsedBy.CompareTo(b.UsedBy)
                    : string.Compare(Field(a, k.Column), Field(b, k.Column), StringComparison.OrdinalIgnoreCase);
                if (c != 0) return k.Ascending ? c : -c;
            }
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }

        private static string Field(VariableEntry v, string column)
        {
            switch (column)
            {
                case "Type":      return v.TypeLabel;
                case "Dimension": return v.Dimension ?? "";
                case "Prompt":    return v.PromptText ?? "";
                default:          return v.Name;
            }
        }

        /// <summary>
        /// Clicked column becomes the primary sort key (clicking it again flips the
        /// direction); the previous keys become tie-breakers. Saved per connection.
        /// </summary>
        private void SortBy(string column)
        {
            if (_sort.Count > 0 && _sort[0].Column == column)
                _sort[0].Ascending = !_sort[0].Ascending;
            else
            {
                _sort.RemoveAll(k => k.Column == column);
                _sort.Insert(0, new SortKey { Column = column, Ascending = column != "UsedBy" });
                if (_sort.Count > 3) _sort.RemoveRange(3, _sort.Count - 3);
            }
            VariablesViewSettings.SaveSort(_cfg?.LocalFolderPath, _sort);
            Populate();
        }

        private void UpdateHeaders()
        {
            for (int i = 0; i < ColumnKeys.Length; i++)
            {
                string arrow = _sort.Count > 0 && _sort[0].Column == ColumnKeys[i]
                    ? (_sort[0].Ascending ? " ▲" : " ▼")
                    : "";
                _list.Columns[i].Text = ColumnTitles[i] + arrow;
            }
        }

        private static string UsedInTooltip(VariableEntry v)
        {
            const int max = 12;
            var names = v.UsedIn.Take(max).ToList();
            string more = v.UsedIn.Count > max ? $"\n… and {v.UsedIn.Count - max} more" : "";
            return $"Used in {v.UsedBy}:\n" + string.Join("\n", names) + more;
        }

        private static bool Contains(string s, string part) =>
            !string.IsNullOrEmpty(s) && s.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;

        // ------------------------------------------------------------------ //
        //  Preview + insert
        // ------------------------------------------------------------------ //

        private VariableEntry Current =>
            _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as VariableEntry : null;

        private string BuildInsertText(VariableEntry v)
        {
            if (v == null) return null;
            switch (_kind)
            {
                case ScriptKind.CalcScript: return "{" + v.Name + "}";
                case ScriptKind.Groovy:     return (_cmbSyntax.SelectedItem as SyntaxHelper)?.Format(v.Name, v.Type);
                default:                    return null;
            }
        }

        private void UpdatePreview()
        {
            VariableEntry v = Current;
            _txtPreview.Text = BuildInsertText(v) ?? "";

            if (_kind == ScriptKind.None)
                _lblNote.Text = "Open a .csc (Calc script) or .js (Groovy) rule to insert variables.";
            else if (_kind == ScriptKind.Groovy && v != null)
                _lblNote.Text = $"Also adds {{{v.Name}}} to the /*RTPS: … */ header of {_fileName}.";
            else if (v != null)
                _lblNote.Text = $"Inserts at the cursor in {_fileName}. Deploy adds the prompt to the rule.";
            else
                _lblNote.Text = "";

            _btnInsert.Enabled = _kind != ScriptKind.None && v != null && !string.IsNullOrEmpty(_txtPreview.Text);
        }

        private void Insert()
        {
            if (!_btnInsert.Enabled) return;
            SelectedVariable = Current;
            InsertText       = _txtPreview.Text;
            if (_cmbSyntax.SelectedItem is SyntaxHelper h) _lastHelperName = h.Name;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
