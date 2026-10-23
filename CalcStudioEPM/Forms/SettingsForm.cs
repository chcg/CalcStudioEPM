using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Newtonsoft.Json;
using CalcStudioEPM.Config;

namespace CalcStudioEPM.Forms
{
    /// <summary>
    /// Plugins › CalcStudio for Oracle EPM › Configure… — every setting the plugin keeps, in
    /// one place:
    ///  • General: plugin options and the data the plugin remembers (prompt values, sort
    ///    orders, Planning reminders, dialog sizes, the validation notice);
    ///  • Connections: overview, editing opens the Connections window;
    ///  • Calc Script language: install / reinstall / remove "Calc Script - Essbase";
    ///  • Calc Script validation (calcscript_catalog.json): rules, functions, blocks, commands;
    ///  • Groovy validation (groovy_validation.json): rules, known names, restricted patterns;
    ///  • Syntax helpers: overview, editing opens the Syntax helpers window.
    /// Options, the language checkboxes and both validation files are written on Save;
    /// actions with their own button (Forget, Reinstall …) happen straight away.
    /// </summary>
    public class SettingsForm : PluginForm
    {
        private static readonly string[] Severities = { "error", "warning", "off" };

        private readonly string       _nppSettingsDir;
        private readonly Action<string> _openInNpp;
        private readonly Action<string> _log;

        private CalcScriptCatalog      _catalog;
        private GroovyValidationConfig _groovy;
        private PluginSettings         _settings;
        private string _catalogJsonAtLoad;

        // General
        private Label _lblPrompts, _lblSorts, _lblPending, _lblSizes, _lblNotice;
        // Language / editor
        private CheckBox _chkInstallLanguage, _chkHandleComments, _chkAutoCalc, _chkAutoGroovy;
        private Label    _lblLanguageStatus, _lblOtherLanguages;
        // Connections / helpers
        private ListView _lvConnections, _lvHelpers;
        // Calc Script
        private DataGridView _gridCalcRules, _gridFunctions, _gridBlocks;
        private TextBox      _txtFunctionFilter, _txtCommands, _txtSetOptions, _txtConstants;
        // Groovy
        private DataGridView _gridGroovyRules, _gridRestricted;
        private TextBox      _txtGlobals, _txtGroovyFunctions, _txtKnownApi;

        /// <summary>Saved anything that changes how the plugin behaves (Main re-reads settings).</summary>
        public bool Saved { get; private set; }

        public SettingsForm(string nppSettingsDir, Action<string> openInNpp, Action<string> log)
        {
            _nppSettingsDir = nppSettingsDir;
            _openInNpp      = openInNpp;
            _log            = log;

            Text          = "CalcStudio for Oracle EPM — Configure";
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon      = false;
            MaximizeBox   = true;
            ClientSize    = new Size(880, 600);
            AllowResize("Settings", minimum96: new Size(720, 480));

            BuildLayout();
            LoadAll();
        }

        // ------------------------------------------------------------------ //
        //  Layout
        // ------------------------------------------------------------------ //

        private void BuildLayout()
        {
            var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 4) };
            tabs.TabPages.Add(GeneralPage());
            tabs.TabPages.Add(ConnectionsPage());
            tabs.TabPages.Add(LanguagePage());
            tabs.TabPages.Add(CalcScriptPage());
            tabs.TabPages.Add(GroovyPage());
            tabs.TabPages.Add(HelpersPage());

            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft,
                Height = 44, Padding = new Padding(8, 8, 8, 8),
            };
            var close = new Button { Text = "Close", Size = new Size(90, 28), DialogResult = DialogResult.Cancel };
            var save  = new Button { Text = "Save",  Size = new Size(90, 28) };
            save.Click += (s, e) => { if (SaveAll()) { Saved = true; DialogResult = DialogResult.OK; Close(); } };
            var note = new Label
            {
                Text = "Save writes the options and both validation files. Buttons inside a page act straight away.",
                AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 7, 16, 0),
            };
            bottom.Controls.AddRange(new Control[] { close, save, note });

            Controls.Add(tabs);
            Controls.Add(bottom);
            AcceptButton = save;
            CancelButton = close;
        }

        private static TabPage Page(string title) =>
            new TabPage(title) { Padding = new Padding(10), UseVisualStyleBackColor = true };

        /// <summary>A vertical stack that fills its page.</summary>
        private static TableLayoutPanel Stack(int rows)
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = rows };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            return t;
        }

        private static Label Heading(string text) => new Label
        {
            Text = text, AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Margin = new Padding(0, 6, 0, 4),
        };

        private static Label Note(string text) => new Label
        {
            Text = text, AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 0, 0, 6),
            MaximumSize = new Size(820, 0),
        };

        private static Button Btn(string text, EventHandler click, int width = 150)
        {
            var b = new Button { Text = text, Size = new Size(width, 28), Margin = new Padding(0, 0, 8, 0) };
            b.Click += click;
            return b;
        }

        private static FlowLayoutPanel ButtonRow(params Control[] controls)
        {
            var f = new FlowLayoutPanel
            {
                AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 6, 0, 0),
            };
            f.Controls.AddRange(controls);
            return f;
        }

        // ---- General ----

        private TabPage GeneralPage()
        {
            TabPage page = Page("General");
            var stack = Stack(7);
            for (int i = 0; i < 6; i++) stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            stack.Controls.Add(Heading("Settings files"), 0, 0);
            stack.Controls.Add(ButtonRow(
                Note($"All plugin settings are in {ConfigManager.SettingsDir}"),
                Btn("Open settings folder", (s, e) => OpenFolder(ConfigManager.SettingsDir))), 0, 1);

            stack.Controls.Add(Heading("Autocomplete"), 0, 2);
            _chkAutoCalc = new CheckBox
            {
                Text = "Calc Script (.csc): @ lists functions, SET lists SET options, { lists runtime prompts",
                AutoSize = true, Margin = new Padding(0, 2, 0, 2),
            };
            _chkAutoGroovy = new CheckBox
            {
                Text = "Groovy rules: after a dot, the EPM methods of that object's type, then all others; rtps. lists prompts",
                AutoSize = true, Margin = new Padding(0, 2, 0, 2),
            };
            stack.Controls.Add(_chkAutoCalc, 0, 3);
            stack.Controls.Add(_chkAutoGroovy, 0, 4);

            stack.Controls.Add(Heading("Data CalcStudio remembers"), 0, 5);
            var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            void Row(out Label label, string button, Action action)
            {
                label = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 8, 8) };
                grid.Controls.Add(label);
                grid.Controls.Add(Btn(button, (s, e) => { action(); RefreshGeneral(); }, 170));
            }
            Row(out _lblPrompts, "Forget values", () =>
            {
                if (Confirm("Forget the runtime prompt values remembered for every connection?")) PromptValues.ClearAll();
            });
            Row(out _lblSorts, "Reset sorting", () =>
            {
                if (Confirm("Reset the Variables window sorting of every connection to the default?")) VariablesViewSettings.ClearAll();
            });
            Row(out _lblPending, "Clear reminders", () =>
            {
                if (Confirm("Clear every \"Planning may run an older version\" reminder?\n\nOnly do this when those rules " +
                            "have been deployed to Planning from Calculation Manager.")) PlanningPending.ClearAll();
            });
            Row(out _lblSizes, "Reset sizes", () =>
            {
                var ui = UiState.Load(); ui.WindowSizes.Clear(); ui.Save();
            });
            Row(out _lblNotice, "Show it again", () =>
            {
                var ui = UiState.Load(); ui.LocalValidationNoticeDismissed = false; ui.LocalValidationNoticeCount = 0; ui.Save();
            });
            stack.Controls.Add(grid, 0, 6);
            page.Controls.Add(stack);
            return page;
        }

        private void RefreshGeneral()
        {
            _lblPrompts.Text = $"Runtime prompt values entered before an execution: {PromptValues.Count()} remembered";
            _lblSorts.Text   = $"Variables window sort order: remembered for {VariablesViewSettings.Count()} connection(s)";
            _lblPending.Text = $"Rules deployed to Calculation Manager only (Execute warns about them): {PlanningPending.Count()}";
            var ui = UiState.Load();
            _lblSizes.Text   = $"Dialog sizes you changed: {ui.WindowSizes.Count} remembered";
            _lblNotice.Text  = ui.LocalValidationNoticeDismissed
                ? "Local Syntax Validation notice: hidden (\"Don't show me again\")"
                : $"Local Syntax Validation notice: shown {ui.LocalValidationNoticeCount} time(s)";
        }

        // ---- Connections ----

        private TabPage ConnectionsPage()
        {
            TabPage page = Page("Connections");
            var stack = Stack(3);
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            stack.Controls.Add(Note("Connections are stored in connections.json, passwords encrypted for your Windows account."), 0, 0);
            _lvConnections = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
            _lvConnections.Columns.Add("Name", 130);
            _lvConnections.Columns.Add("Service URL", 240);
            _lvConnections.Columns.Add("Application", 90);
            _lvConnections.Columns.Add("Username", 160);
            _lvConnections.Columns.Add("Password", 80);
            _lvConnections.Columns.Add("Default deploy", 110);
            _lvConnections.Columns.Add("Local folder", 200);
            _lvConnections.DoubleClick += (s, e) => EditConnections();
            stack.Controls.Add(_lvConnections, 0, 1);
            stack.Controls.Add(ButtonRow(Btn("Edit connections…", (s, e) => EditConnections())), 0, 2);
            page.Controls.Add(stack);
            return page;
        }

        private void RefreshConnections()
        {
            _lvConnections.Items.Clear();
            ConnectionConfig active = ConfigManager.GetActive();
            foreach (ConnectionConfig c in ConfigManager.GetAll())
            {
                bool isActive = active != null && string.Equals(active.LocalFolderPath, c.LocalFolderPath, StringComparison.OrdinalIgnoreCase);
                _lvConnections.Items.Add(new ListViewItem(new[]
                {
                    (c.Name ?? "") + (isActive ? "  (active)" : ""), c.ServiceBaseUrl, c.Application, c.Username,
                    Credentials.IsSaved(c) ? "saved" : "asked",
                    DeployMethodNames.Short(c.DefaultDeployMethod), c.LocalFolderPath,
                }));
            }
        }

        private void EditConnections()
        {
            using (var f = new ConfigForm()) f.ShowDialog(this);
            RefreshConnections();
        }

        // ---- Calc Script language ----

        private TabPage LanguagePage()
        {
            TabPage page = Page("Calc Script language");
            var stack = Stack(9);
            for (int i = 0; i < 8; i++) stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            stack.Controls.Add(Heading(CalcScriptLanguage.Name), 0, 0);
            stack.Controls.Add(Note(
                "A Notepad++ user language for .csc files, generated from the Calc Script validation catalog: " +
                "functions, commands, SET options, member names, runtime prompts, comments and FIX / IF / LOOP " +
                "folding, with a dark mode version Notepad++ picks automatically."), 0, 1);

            _chkInstallLanguage = new CheckBox
            {
                Text = "Install the language when Notepad++ starts, if it is missing",
                AutoSize = true, Margin = new Padding(0, 6, 0, 0),
            };
            stack.Controls.Add(_chkInstallLanguage, 0, 2);
            stack.Controls.Add(Note("Untick to keep your own .csc language: CalcStudio then never writes the language files."), 0, 3);

            _chkHandleComments = new CheckBox
            {
                Text = "Handle Ctrl+K / Ctrl+Shift+K / Ctrl+Shift+Q (comment / uncomment) for .csc files",
                AutoSize = true, Margin = new Padding(0, 6, 0, 0),
            };
            stack.Controls.Add(_chkHandleComments, 0, 4);
            stack.Controls.Add(Note("The language defines /* */ as a delimiter (it keeps folding correct), so Notepad++ has no " +
                                    "comment markers for it; CalcStudio applies them instead."), 0, 5);

            _lblLanguageStatus = new Label { AutoSize = true, Margin = new Padding(0, 10, 0, 4), MaximumSize = new Size(820, 0) };
            stack.Controls.Add(_lblLanguageStatus, 0, 6);
            _lblOtherLanguages = new Label
            {
                AutoSize = true, ForeColor = Color.DarkOrange, Margin = new Padding(0, 0, 0, 4), MaximumSize = new Size(820, 0),
            };
            stack.Controls.Add(_lblOtherLanguages, 0, 7);

            stack.Controls.Add(ButtonRow(
                Btn("Reinstall now (overwrite)", (s, e) => ReinstallLanguage(), 190),
                Btn("Remove", (s, e) => RemoveLanguage(), 100),
                Btn("Open folder", (s, e) => OpenFolder(Path.Combine(_nppSettingsDir ?? "", "userDefineLangs")), 110)), 0, 8);
            page.Controls.Add(stack);
            return page;
        }

        private void RefreshLanguage()
        {
            var f = CalcScriptLanguage.Files(_nppSettingsDir);
            string state = f.LightExists && f.DarkExists ? "installed (light and dark mode)"
                         : f.LightExists ? "installed (light only — dark mode file missing)"
                         : f.DarkExists  ? "installed (dark only — light mode file missing)"
                         : "not installed";
            _lblLanguageStatus.Text = $"Status: {state}\nFolder: {Path.GetDirectoryName(f.Light)}\n" +
                                      "Notepad++ reads user languages when it starts: restart it after a change here.";
            List<string> others = CalcScriptLanguage.OtherLanguagesForCsc(_nppSettingsDir);
            _lblOtherLanguages.Text = others.Count == 0 ? "" :
                "Also set for .csc files: " + string.Join(", ", others.Select(n => $"'{n}'")) +
                ". Remove \"csc\" from their extension (Language › User Defined Language › Define your language…) " +
                $"so Notepad++ uses {CalcScriptLanguage.Name}, or untick the install option above to keep yours.";
        }

        private void ReinstallLanguage()
        {
            if (!Confirm($"Write '{CalcScriptLanguage.Name}' again from the current catalog?\n\n" +
                         "This replaces the language files, including changes made to them in Notepad++'s " +
                         "User Defined Language dialog. Restart Notepad++ afterwards.")) return;
            try { CalcScriptLanguage.Reinstall(_nppSettingsDir, _log); }
            catch (Exception ex) { Error("Could not write the language files: " + ex.Message); }
            RefreshLanguage();
        }

        private void RemoveLanguage()
        {
            if (!Confirm($"Delete the '{CalcScriptLanguage.Name}' language files?\n\n" +
                         "Untick \"Install the language when Notepad++ starts\" as well, or it is installed again " +
                         "at the next start. Restart Notepad++ afterwards.")) return;
            try { CalcScriptLanguage.Remove(_nppSettingsDir, _log); }
            catch (Exception ex) { Error("Could not delete the language files: " + ex.Message); }
            RefreshLanguage();
        }

        // ---- Calc Script validation ----

        private TabPage CalcScriptPage()
        {
            TabPage page = Page("Calc Script validation");
            var inner = new TabControl { Dock = DockStyle.Fill };

            // Rules
            var rules = Page("Rules");
            _gridCalcRules = RulesGrid();
            rules.Controls.Add(_gridCalcRules);
            inner.TabPages.Add(rules);

            // Functions
            var functions = Page("Functions");
            var fStack = Stack(2);
            fStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            fStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            _txtFunctionFilter = new TextBox { Width = 260, Margin = new Padding(0, 0, 8, 6) };
            _txtFunctionFilter.TextChanged += (s, e) => FilterFunctions();
            fStack.Controls.Add(ButtonRow(new Label { Text = "Filter:", AutoSize = true, Margin = new Padding(0, 4, 4, 0) },
                _txtFunctionFilter,
                Note("Min / Max = number of parameters; Max -1 = any number. Add rows at the bottom, select a row and press Delete to remove it.")), 0, 0);
            _gridFunctions = Grid();
            _gridFunctions.Columns.Add(TextCol("Name", 22));
            _gridFunctions.Columns.Add(TextCol("Min", 6));
            _gridFunctions.Columns.Add(TextCol("Max", 6));
            _gridFunctions.Columns.Add(TextCol("Syntax", 50));
            _gridFunctions.Columns.Add(TextCol("Source", 16));
            fStack.Controls.Add(_gridFunctions, 0, 1);
            functions.Controls.Add(fStack);
            inner.TabPages.Add(functions);

            // Blocks
            var blocks = Page("Blocks");
            var bStack = Stack(2);
            bStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            bStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            bStack.Controls.Add(Note("Commands that open a block. Middle words are separated by spaces (ELSEIF ELSE). " +
                                     "They also drive folding in the Calc Script language (Reinstall it after changing them)."), 0, 0);
            _gridBlocks = Grid();
            _gridBlocks.Columns.Add(TextCol("Opens with", 25));
            _gridBlocks.Columns.Add(TextCol("Middle words", 30));
            _gridBlocks.Columns.Add(TextCol("Closes with", 25));
            _gridBlocks.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Only inside a member block", FillWeight = 20 });
            bStack.Controls.Add(_gridBlocks, 0, 1);
            blocks.Controls.Add(bStack);
            inner.TabPages.Add(blocks);

            // Commands, SET options, constants
            var words = Page("Commands and SET options");
            var w = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2 };
            for (int i = 0; i < 3; i++) w.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
            w.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            w.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            w.Controls.Add(Note("Commands (one per line)"), 0, 0);
            w.Controls.Add(Note("SET options (one per line)"), 1, 0);
            w.Controls.Add(Note("Constants: @-names used as values"), 2, 0);
            _txtCommands   = WordBox(); w.Controls.Add(_txtCommands, 0, 1);
            _txtSetOptions = WordBox(); w.Controls.Add(_txtSetOptions, 1, 1);
            _txtConstants  = WordBox(); w.Controls.Add(_txtConstants, 2, 1);
            words.Controls.Add(w);
            inner.TabPages.Add(words);

            var stack = Stack(2);
            stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.Controls.Add(inner, 0, 0);
            stack.Controls.Add(ButtonRow(
                Btn("Restore defaults", (s, e) =>
                {
                    if (Confirm("Replace the Calc Script rules, functions, blocks and words with the defaults? " +
                                "(Written when you click Save.)")) ShowCatalog(CalcScriptCatalog.Defaults());
                }),
                Btn("Open file in Notepad++", (s, e) => OpenFile(CalcScriptCatalog.FilePath), 170)), 0, 1);
            page.Controls.Add(stack);
            return page;
        }

        // ---- Groovy validation ----

        private TabPage GroovyPage()
        {
            TabPage page = Page("Groovy validation");
            var inner = new TabControl { Dock = DockStyle.Fill };

            var rules = Page("Rules");
            _gridGroovyRules = RulesGrid();
            rules.Controls.Add(_gridGroovyRules);
            inner.TabPages.Add(rules);

            var names = Page("Known names");
            var n = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2 };
            for (int c = 0; c < 3; c++) n.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
            n.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            n.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            n.Controls.Add(Note("Names that exist without a declaration (one per line)"), 0, 0);
            n.Controls.Add(Note("Functions callable without a receiver (one per line)"), 1, 0);
            n.Controls.Add(Note("Known API members missing from Oracle's reference: Type.member or *.member"), 2, 0);
            _txtGlobals         = WordBox(); n.Controls.Add(_txtGlobals, 0, 1);
            _txtGroovyFunctions = WordBox(); n.Controls.Add(_txtGroovyFunctions, 1, 1);
            _txtKnownApi        = WordBox(); n.Controls.Add(_txtKnownApi, 2, 1);
            names.Controls.Add(n);
            inner.TabPages.Add(names);

            var restricted = Page("Restricted code");
            var rStack = Stack(2);
            rStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            rStack.Controls.Add(Note("Regular expressions matched against code (not strings or comments), with the message shown."), 0, 0);
            _gridRestricted = Grid();
            _gridRestricted.Columns.Add(TextCol("Pattern (regex)", 35));
            _gridRestricted.Columns.Add(TextCol("Message", 50));
            _gridRestricted.Columns.Add(SeverityCol(15));
            rStack.Controls.Add(_gridRestricted, 0, 1);
            restricted.Controls.Add(rStack);
            inner.TabPages.Add(restricted);

            var stack = Stack(2);
            stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.Controls.Add(inner, 0, 0);
            stack.Controls.Add(ButtonRow(
                Btn("Restore defaults", (s, e) =>
                {
                    if (Confirm("Replace the Groovy rules, known names and restricted code with the defaults? " +
                                "(Written when you click Save.)")) ShowGroovy(GroovyValidationConfig.Defaults());
                }),
                Btn("Open file in Notepad++", (s, e) => OpenFile(GroovyValidationConfig.FilePath), 170)), 0, 1);
            page.Controls.Add(stack);
            return page;
        }

        // ---- Syntax helpers ----

        private TabPage HelpersPage()
        {
            TabPage page = Page("Syntax helpers");
            var stack = Stack(3);
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.Controls.Add(Note("How the Variables window inserts a variable into a Groovy rule. {name} is the variable name."), 0, 0);
            _lvHelpers = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
            _lvHelpers.Columns.Add("Helper", 160);
            _lvHelpers.Columns.Add("Default template", 260);
            _lvHelpers.Columns.Add("Type-specific templates", 380);
            _lvHelpers.DoubleClick += (s, e) => EditHelpers();
            stack.Controls.Add(_lvHelpers, 0, 1);
            stack.Controls.Add(ButtonRow(Btn("Edit syntax helpers…", (s, e) => EditHelpers())), 0, 2);
            page.Controls.Add(stack);
            return page;
        }

        private void RefreshHelpers()
        {
            _lvHelpers.Items.Clear();
            foreach (SyntaxHelper h in SyntaxHelpers.Load())
                _lvHelpers.Items.Add(new ListViewItem(new[]
                {
                    h.Name, h.Template,
                    string.Join(",  ", (h.TypeTemplates ?? new Dictionary<string, string>()).Select(kv => $"{kv.Key}: {kv.Value}")),
                }));
        }

        private void EditHelpers()
        {
            using (var f = new SyntaxHelpersForm(SyntaxHelpers.Load())) f.ShowDialog(this);   // saves itself
            RefreshHelpers();
        }

        // ------------------------------------------------------------------ //
        //  Grid helpers
        // ------------------------------------------------------------------ //

        private static DataGridView Grid() => new DataGridView
        {
            Dock                  = DockStyle.Fill,
            AllowUserToAddRows    = true,
            AllowUserToDeleteRows = true,
            AllowUserToResizeRows = false,
            AutoSizeColumnsMode   = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersWidth       = 28,
            BackgroundColor       = SystemColors.Window,
            EditMode              = DataGridViewEditMode.EditOnEnter,
        };

        private static DataGridViewTextBoxColumn TextCol(string title, float weight) =>
            new DataGridViewTextBoxColumn { HeaderText = title, FillWeight = weight };

        private static DataGridViewComboBoxColumn SeverityCol(float weight)
        {
            var c = new DataGridViewComboBoxColumn { HeaderText = "Severity", FillWeight = weight, FlatStyle = FlatStyle.Flat };
            c.Items.AddRange(Severities.Cast<object>().ToArray());
            return c;
        }

        /// <summary>Rule id (read-only) · what it checks (read-only) · severity.</summary>
        private static DataGridView RulesGrid()
        {
            DataGridView g = Grid();
            g.AllowUserToAddRows = false;
            g.AllowUserToDeleteRows = false;
            g.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Rule", FillWeight = 28, ReadOnly = true });
            g.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "What it checks", FillWeight = 57, ReadOnly = true });
            g.Columns.Add(SeverityCol(15));
            g.DataError += (s, e) => e.ThrowException = false;
            return g;
        }

        private static TextBox WordBox() => new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true,
            Font = new Font("Consolas", 9.5f), WordWrap = false,
        };

        private static void FillRules(DataGridView g, Dictionary<string, string> rules, Dictionary<string, string> descriptions)
        {
            g.Rows.Clear();
            foreach (var kv in rules.OrderBy(r => r.Key))
            {
                string sev = Severities.Contains((kv.Value ?? "").Trim().ToLowerInvariant()) ? kv.Value.Trim().ToLowerInvariant() : "warning";
                g.Rows.Add(kv.Key, descriptions.TryGetValue(kv.Key, out string d) ? d : "", sev);
            }
        }

        private static Dictionary<string, string> ReadRules(DataGridView g)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DataGridViewRow r in g.Rows)
                if (!r.IsNewRow && r.Cells[0].Value is string id)
                    d[id] = (r.Cells[2].Value as string) ?? "warning";
            return d;
        }

        private static string Cell(DataGridViewRow r, int i) => (r.Cells[i].Value?.ToString() ?? "").Trim();

        private static List<string> Lines(TextBox t) =>
            t.Lines.Select(l => l.Trim()).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        private static void SetLines(TextBox t, IEnumerable<string> items) =>
            t.Text = string.Join(Environment.NewLine, items ?? Enumerable.Empty<string>());

        // ------------------------------------------------------------------ //
        //  Load / show
        // ------------------------------------------------------------------ //

        private void LoadAll()
        {
            _settings = PluginSettings.Load();
            _chkInstallLanguage.Checked = _settings.InstallCalcScriptLanguage;
            _chkHandleComments.Checked  = _settings.HandleCalcScriptComments;
            _chkAutoCalc.Checked        = _settings.AutoCompleteCalcScript;
            _chkAutoGroovy.Checked      = _settings.AutoCompleteGroovy;

            _catalog = CalcScriptCatalog.Load();
            ShowCatalog(_catalog);
            _catalogJsonAtLoad = JsonConvert.SerializeObject(ReadCatalog(out _));
            _groovy = GroovyValidationConfig.Load();
            ShowGroovy(_groovy);

            RefreshGeneral();
            RefreshConnections();
            RefreshLanguage();
            RefreshHelpers();
        }

        private void ShowCatalog(CalcScriptCatalog c)
        {
            FillRules(_gridCalcRules, c.Rules, CalcScriptCatalog.RuleDescriptions);

            _gridFunctions.Rows.Clear();
            foreach (CalcFunction f in c.Functions.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                _gridFunctions.Rows.Add(f.Name, f.MinArgs, f.MaxArgs, f.Syntax, f.Source);
            FilterFunctions();

            _gridBlocks.Rows.Clear();
            foreach (CalcBlock b in c.Blocks)
                _gridBlocks.Rows.Add(b.Open, string.Join(" ", b.Middle ?? new List<string>()), b.Close, b.RequiresMemberBlock);

            SetLines(_txtCommands, c.Commands);
            SetLines(_txtSetOptions, c.SetOptions.Distinct(StringComparer.OrdinalIgnoreCase));
            SetLines(_txtConstants, c.Constants);
        }

        private void ShowGroovy(GroovyValidationConfig g)
        {
            FillRules(_gridGroovyRules, g.Rules, GroovyValidationConfig.RuleDescriptions);
            SetLines(_txtGlobals, g.KnownGlobals);
            SetLines(_txtGroovyFunctions, g.KnownFunctions);
            SetLines(_txtKnownApi, g.KnownApiMembers);
            _gridRestricted.Rows.Clear();
            foreach (RestrictedPattern p in g.Restricted)
                _gridRestricted.Rows.Add(p.Pattern, p.Message,
                    Severities.Contains((p.Severity ?? "").ToLowerInvariant()) ? p.Severity.ToLowerInvariant() : "warning");
        }

        private void FilterFunctions()
        {
            string f = _txtFunctionFilter.Text.Trim();
            _gridFunctions.CurrentCell = null;
            foreach (DataGridViewRow r in _gridFunctions.Rows)
                if (!r.IsNewRow)
                    r.Visible = f.Length == 0 ||
                                Cell(r, 0).IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                Cell(r, 3).IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                Cell(r, 4).IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ------------------------------------------------------------------ //
        //  Save
        // ------------------------------------------------------------------ //

        private bool SaveAll()
        {
            foreach (var g in new[] { _gridCalcRules, _gridFunctions, _gridBlocks, _gridGroovyRules, _gridRestricted })
                g.EndEdit();

            // ---- Calc Script catalog ----
            CalcScriptCatalog catalog = ReadCatalog(out string problem);
            if (catalog == null) return Invalid(problem);
            _catalog = catalog;

            // ---- Groovy ----
            var restricted = new List<RestrictedPattern>();
            foreach (DataGridViewRow r in _gridRestricted.Rows)
            {
                if (r.IsNewRow || Cell(r, 0).Length == 0) continue;
                try { _ = new Regex(Cell(r, 0)); }
                catch (ArgumentException ex) { return Invalid($"Restricted pattern {Cell(r, 0)} is not a valid regular expression:\n{ex.Message}"); }
                restricted.Add(new RestrictedPattern
                {
                    Pattern = Cell(r, 0), Message = Cell(r, 1), Severity = (r.Cells[2].Value as string) ?? "warning",
                });
            }
            _groovy.Rules          = ReadRules(_gridGroovyRules);
            _groovy.KnownGlobals   = Lines(_txtGlobals);
            _groovy.KnownFunctions = Lines(_txtGroovyFunctions);
            _groovy.KnownApiMembers = Lines(_txtKnownApi);
            _groovy.Restricted     = restricted;

            // ---- Options ----
            _settings.InstallCalcScriptLanguage = _chkInstallLanguage.Checked;
            _settings.HandleCalcScriptComments  = _chkHandleComments.Checked;
            _settings.AutoCompleteCalcScript    = _chkAutoCalc.Checked;
            _settings.AutoCompleteGroovy        = _chkAutoGroovy.Checked;

            try
            {
                CalcScriptCatalog.Save(_catalog);
                GroovyValidationConfig.Save(_groovy);
                PluginSettings.Save(_settings);
            }
            catch (Exception ex)
            {
                return Invalid("Could not save: " + ex.Message);
            }
            _log?.Invoke("Configure: settings saved");

            // The language is generated from the catalog: offer to bring it up to date
            var files = CalcScriptLanguage.Files(_nppSettingsDir);
            if (JsonConvert.SerializeObject(_catalog) != _catalogJsonAtLoad && (files.LightExists || files.DarkExists) &&
                MessageBox.Show(this,
                    $"The Calc Script catalog changed. Update the '{CalcScriptLanguage.Name}' language with it?\n\n" +
                    "This rewrites the language files (replacing changes made in Notepad++'s User Defined Language " +
                    "dialog). Restart Notepad++ to see the result.",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                try { CalcScriptLanguage.Reinstall(_nppSettingsDir, _log); }
                catch (Exception ex) { Error("Could not write the language files: " + ex.Message); }
            }
            return true;
        }

        /// <summary>The catalog as the grids show it; null (and <paramref name="problem"/>) when invalid.
        /// Also used right after loading, as the baseline that tells whether anything changed.</summary>
        private CalcScriptCatalog ReadCatalog(out string problem)
        {
            problem = null;
            _gridFunctions.EndEdit();
            _gridBlocks.EndEdit();
            _gridCalcRules.EndEdit();
            var functions = new List<CalcFunction>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DataGridViewRow r in _gridFunctions.Rows)
            {
                if (r.IsNewRow) continue;
                string name = Cell(r, 0);
                if (name.Length == 0) continue;
                if (!name.StartsWith("@")) name = "@" + name;
                if (!int.TryParse(Cell(r, 1), out int min) || !int.TryParse(Cell(r, 2), out int max) ||
                    min < 0 || (max != -1 && max < min))
                { problem = $"Function {name}: Min must be 0 or more, and Max must be -1 (any number) or at least Min."; return null; }
                if (!seen.Add(name)) { problem = $"Function {name} is listed twice."; return null; }
                functions.Add(new CalcFunction
                {
                    Name = name, MinArgs = min, MaxArgs = max,
                    Syntax = Cell(r, 3), Source = Cell(r, 4).Length > 0 ? Cell(r, 4) : "custom",
                });
            }

            var blocks = new List<CalcBlock>();
            foreach (DataGridViewRow r in _gridBlocks.Rows)
            {
                if (r.IsNewRow || (Cell(r, 0).Length == 0 && Cell(r, 2).Length == 0)) continue;
                if (Cell(r, 0).Length == 0 || Cell(r, 2).Length == 0)
                { problem = "Every block needs the word that opens it and the word that closes it."; return null; }
                blocks.Add(new CalcBlock
                {
                    Open   = Cell(r, 0).ToUpperInvariant(),
                    Close  = Cell(r, 2).ToUpperInvariant(),
                    Middle = Cell(r, 1).Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
                                       .Select(m => m.ToUpperInvariant()).ToList(),
                    RequiresMemberBlock = r.Cells[3].Value is bool b && b,
                });
            }

            return new CalcScriptCatalog
            {
                Rules      = ReadRules(_gridCalcRules),
                Functions  = functions,
                Blocks     = blocks,
                Commands   = Lines(_txtCommands).Select(x => x.ToUpperInvariant()).ToList(),
                SetOptions = Lines(_txtSetOptions).Select(x => x.ToUpperInvariant()).ToList(),
                Constants  = Lines(_txtConstants).Select(x => x.ToUpperInvariant()).ToList(),
            };
        }

        // ------------------------------------------------------------------ //
        //  Small helpers
        // ------------------------------------------------------------------ //

        private bool Confirm(string message) =>
            MessageBox.Show(this, message, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

        private void Error(string message) =>
            MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);

        private bool Invalid(string message)
        {
            MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        private void OpenFile(string path)
        {
            if (!File.Exists(path)) { Error("The file does not exist yet: " + path); return; }
            if (Confirm("Open the file in Notepad++ and close this window?\n\nUnsaved changes here are discarded."))
            {
                _openInNpp?.Invoke(path);
                Close();
            }
        }

        private static void OpenFolder(string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                Process.Start("explorer.exe", "\"" + folder + "\"");
            }
            catch { }
        }
    }
}
