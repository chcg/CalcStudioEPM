using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CalcStudioEPM.Forms
{
    /// <summary>
    /// Shared look and feel for long-running EPM operations (Pull, Deploy, Execute): a
    /// short, friendly checklist of stages, a summary when the work ends, an optional
    /// "Response" box (what the operation returned, expanded when shown), and the full
    /// technical log behind "Show details" (opened automatically on failure).
    ///
    /// The sections are stacked top to bottom in one grid. The window is resizable: its
    /// width always, its height when the details log or the Response box is open — that
    /// box takes the extra height. With both closed the window fits its content exactly.
    /// The width is remembered between runs.
    ///
    /// Derived forms supply the step texts, run the operation in <see cref="RunAsync"/>
    /// and describe the outcome in <see cref="OnSucceeded"/> / <see cref="OnFailed"/>.
    /// </summary>
    public abstract class StepProgressForm<TResult> : PluginForm
    {
        protected enum StepState { Pending, Running, Done, Warning, Failed }

        // 100 % sizes; PluginForm scales them
        private const int FormWidth    = 560;
        private const int MinWidth     = 440;
        private const int LogHeight    = 240;
        private const int ResponseHeight = 140;
        private const int MinBoxHeight = 80;

        // ------------------------------------------------------------------ //
        //  Controls
        // ------------------------------------------------------------------ //

        private TableLayoutPanel _grid;
        private Label       _lblHeadline;
        private Label[]     _stepIcons;
        private Label[]     _stepTexts;
        private Label       _lblSummary;
        private Label       _lblAlert;        // red "ACTION NEEDED" box; hidden until SetAlert
        private LinkLabel   _lnkResponse;     // null unless the form has a Response box
        private TextBox     _txtResponse;
        private LinkLabel   _lnkDetails;
        private Button      _btnClose;
        private RichTextBox _rtbLog;
        private Font        _stepFont;
        private Font        _stepFontBold;

        // Grid rows
        private const int RowResponseBox = 5;
        private const int RowLog         = 7;

        // ------------------------------------------------------------------ //
        //  Fields
        // ------------------------------------------------------------------ //

        private readonly string[] _steps;
        private readonly string   _failHeadline;

        private static readonly string[] SpinnerFrames =
            { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };

        private int  _spinnerIdx;
        private System.Windows.Forms.Timer _spinnerTimer;
        private bool _finished;
        private bool _loaded;      // sized and scaled; FitHeight may run
        private bool _fitting;     // FitHeight is resizing the window
        private int  _lastWidth;
        // Height an open box gets in the natural layout (the grid resizes docked boxes
        // itself, so their Height can't be read back reliably)
        private readonly System.Collections.Generic.Dictionary<Control, int> _boxHeight =
            new System.Collections.Generic.Dictionary<Control, int>();

        /// <summary>True once the operation completed without throwing.</summary>
        public bool Succeeded { get; private set; }
        private int  _currentStep = -1;
        private StepState[] _stepStates;
        /// <summary>Rows that run alongside the others (see <see cref="ParallelStatus"/>), and
        /// those of them that have finished (their state is final).</summary>
        private readonly System.Collections.Generic.HashSet<int> _parallel = new System.Collections.Generic.HashSet<int>();
        private readonly System.Collections.Generic.HashSet<int> _parallelDone = new System.Collections.Generic.HashSet<int>();

        // ------------------------------------------------------------------ //
        //  Constructor
        // ------------------------------------------------------------------ //

        /// <param name="withResponse">Adds a "Response" box, hidden until <see cref="SetResponse"/>.</param>
        protected StepProgressForm(string title, string headline, string failHeadline, string[] steps,
                                   bool withResponse = false)
        {
            _steps        = steps;
            _failHeadline = failHeadline;
            InitializeComponents(title, headline, withResponse);
        }

        // ------------------------------------------------------------------ //
        //  To implement
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Runs the operation on a worker thread. Detailed lines go to
        /// <paramref name="log"/>; stage changes go through a reporter from
        /// <see cref="StepProgress{TStep}"/>. Throw to signal failure.
        /// </summary>
        protected abstract Task<TResult> RunAsync(IProgress<string> log);

        /// <summary>
        /// Called on the UI thread after success, with every step already marked done.
        /// Set the headline and summary, and downgrade steps to warnings if needed.
        /// </summary>
        protected abstract void OnSucceeded(TResult result);

        /// <summary>Called on the UI thread after the failure state is shown; optional.</summary>
        protected virtual void OnFailed(Exception ex) { }

        /// <summary>The row to mark as failed; by default the step that was running.</summary>
        protected virtual int FailedStep(Exception ex, int currentStep) => currentStep;

        // ------------------------------------------------------------------ //
        //  Layout
        // ------------------------------------------------------------------ //

        private void InitializeComponents(string title, string headline, bool withResponse)
        {
            Text          = title;
            MaximizeBox   = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon      = false;
            _stepFont     = Font;
            _stepFontBold = new Font(Font, FontStyle.Bold);
            ClientSize    = new Size(FormWidth, 300);   // height is fitted on load
            // Width remembered; the height is managed by FitHeight
            AllowResize("Progress", minimum96: new Size(MinWidth, 0), height: false);

            _grid = new TableLayoutPanel
            {
                Dock        = DockStyle.Fill,
                ColumnCount = 1,
                RowCount    = 8,
                Padding     = new Padding(12, 8, 12, 10),
            };
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 8; i++)
                _grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(_grid);

            // ---- 0: headline ----
            _lblHeadline = new Label
            {
                Text         = headline,
                Dock         = DockStyle.Fill,
                Height       = 26,
                TextAlign    = ContentAlignment.MiddleLeft,
                Font         = new Font("Segoe UI", 10f, FontStyle.Bold),
                AutoEllipsis = true,
                Margin       = Padding.Empty,
            };
            _grid.Controls.Add(_lblHeadline, 0, 0);

            // ---- 1: step checklist ----
            var steps = new TableLayoutPanel
            {
                Dock         = DockStyle.Fill,
                ColumnCount  = 2,
                RowCount     = _steps.Length,
                AutoSize     = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin       = new Padding(4, 4, 0, 0),
            };
            steps.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28f));
            steps.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _stepIcons = new Label[_steps.Length];
            _stepTexts = new Label[_steps.Length];
            for (int i = 0; i < _steps.Length; i++)
            {
                steps.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
                _stepIcons[i] = new Label
                {
                    Dock      = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font      = new Font("Segoe UI", 11f),
                    Margin    = Padding.Empty,
                };
                _stepTexts[i] = new Label
                {
                    Text         = _steps[i],
                    Dock         = DockStyle.Fill,
                    TextAlign    = ContentAlignment.MiddleLeft,
                    AutoEllipsis = true,
                    Margin       = Padding.Empty,
                };
                steps.Controls.Add(_stepIcons[i], 0, i);
                steps.Controls.Add(_stepTexts[i], 1, i);
                SetStepState(i, StepState.Pending);
            }
            _grid.Controls.Add(steps, 0, 1);

            // ---- 2: summary (wraps to the window width) ----
            _lblSummary = new Label
            {
                AutoSize    = true,
                Anchor      = AnchorStyles.Left | AnchorStyles.Right,
                MinimumSize = new Size(0, 36),
                Margin      = new Padding(0, 8, 0, 4),
            };
            _grid.Controls.Add(_lblSummary, 0, 2);

            // ---- 3: alert (hidden until SetAlert) ----
            _lblAlert = new Label
            {
                AutoSize    = true,
                Anchor      = AnchorStyles.Left | AnchorStyles.Right,
                Margin      = new Padding(0, 2, 0, 6),
                Padding     = new Padding(8, 6, 8, 6),
                ForeColor   = Color.FromArgb(0xB0, 0x00, 0x00),
                BackColor   = Color.FromArgb(0xFD, 0xEC, 0xEC),
                BorderStyle = BorderStyle.FixedSingle,
                Font        = new Font(Font, FontStyle.Bold),
                Visible     = false,
            };
            _grid.Controls.Add(_lblAlert, 0, 3);

            // ---- 4/5: Response (optional; hidden until there is something to show) ----
            if (withResponse)
            {
                _lnkResponse = new LinkLabel
                {
                    Text = "Response ▴", AutoSize = true, Anchor = AnchorStyles.Left,
                    Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 4, 0, 2),
                    Visible = false,
                };
                _lnkResponse.LinkClicked += (s, e) => SetResponseVisible(!_txtResponse.Visible);
                _grid.Controls.Add(_lnkResponse, 0, 4);

                _txtResponse = new TextBox
                {
                    Dock       = DockStyle.Fill,
                    Height     = ResponseHeight,
                    Margin     = new Padding(0, 0, 0, 4),
                    Multiline  = true,
                    ReadOnly   = true,
                    WordWrap   = true,
                    ScrollBars = ScrollBars.Vertical,
                    Font       = new Font("Consolas", 9.5f),
                    BackColor  = SystemColors.Window,
                    Visible    = false,
                };
                _grid.Controls.Add(_txtResponse, 0, RowResponseBox);
            }

            // ---- 6: details toggle + Close ----
            var bar = new TableLayoutPanel
            {
                Dock         = DockStyle.Fill,
                ColumnCount  = 2,
                RowCount     = 1,
                AutoSize     = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin       = new Padding(0, 6, 0, 6),
            };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _lnkDetails = new LinkLabel
            {
                Text = "Show details ▾", AutoSize = true, Anchor = AnchorStyles.Left, Margin = Padding.Empty,
            };
            _lnkDetails.LinkClicked += (s, e) => SetDetailsVisible(!_rtbLog.Visible);
            bar.Controls.Add(_lnkDetails, 0, 0);
            _btnClose = new Button
            {
                Text    = "Close",
                Size    = new Size(80, 26),
                Margin  = Padding.Empty,
                Enabled = false, // enabled only once the work finishes
            };
            _btnClose.Click += (s, e) => Close();
            bar.Controls.Add(_btnClose, 1, 0);
            _grid.Controls.Add(bar, 0, 6);

            // ---- 7: detailed log (hidden until "Show details") ----
            _rtbLog = new RichTextBox
            {
                Dock        = DockStyle.Fill,
                Height      = LogHeight,
                Margin      = Padding.Empty,
                ReadOnly    = true,
                ScrollBars  = RichTextBoxScrollBars.Vertical,
                Font        = new Font("Consolas", 8.5f),
                BackColor   = SystemColors.Window,
                BorderStyle = BorderStyle.FixedSingle,
                Visible     = false,
            };
            _grid.Controls.Add(_rtbLog, 0, RowLog);

            CancelButton = _btnClose;
        }

        /// <summary>
        /// Sizes the window to its content. The open box (log first, else Response) takes
        /// any extra height and the user may make it taller; with neither open the height
        /// is fixed and only the width can change.
        /// </summary>
        private void FitHeight(bool opened = false)
        {
            if (!_loaded || _fitting) return;
            _fitting = true;
            try
            {
                Control fill = _rtbLog.Visible ? _rtbLog
                             : _txtResponse != null && _txtResponse.Visible ? (Control)_txtResponse
                             : null;

                // Natural height: every row at its own size, an open box at its current height
                // (a multiline box's "preferred" height is meaningless, so pin it)
                PinBoxRow(RowResponseBox, _txtResponse);
                PinBoxRow(RowLog, _rtbLog);
                int natural = _grid.GetPreferredSize(new Size(ClientSize.Width, 0)).Height;

                MinimumSize = Size.Empty;
                MaximumSize = Size.Empty;
                int frame = Height - ClientSize.Height;
                int minW  = Scaled(MinWidth);

                if (fill == null)
                {
                    ClientSize  = new Size(ClientSize.Width, natural);
                    MinimumSize = new Size(minW, Height);
                    MaximumSize = new Size(UnlimitedWidth, Height);   // width only
                }
                else
                {
                    int withoutBox = natural - BoxHeight(fill) - fill.Margin.Vertical;
                    int height = opened ? natural : Math.Max(ClientSize.Height, withoutBox + Scaled(MinBoxHeight));
                    Rectangle work = Screen.FromControl(this).WorkingArea;
                    height = Math.Min(height, work.Height - frame);
                    _grid.RowStyles[fill == _rtbLog ? RowLog : RowResponseBox].SizeType = SizeType.Percent;
                    _grid.RowStyles[fill == _rtbLog ? RowLog : RowResponseBox].Height   = 100f;
                    ClientSize  = new Size(ClientSize.Width, height);
                    MinimumSize = new Size(minW, withoutBox + Scaled(MinBoxHeight) + frame);
                    if (Bottom > work.Bottom) Top = Math.Max(work.Top, work.Bottom - Height);
                }
            }
            finally
            {
                _fitting = false;
            }
        }

        private int BoxHeight(Control box) =>
            _boxHeight.TryGetValue(box, out int h) ? h
            : Scaled(box == _rtbLog ? LogHeight : ResponseHeight);

        private void PinBoxRow(int row, Control box)
        {
            RowStyle style = _grid.RowStyles[row];
            if (box != null && box.Visible)
            {
                style.SizeType = SizeType.Absolute;
                style.Height   = BoxHeight(box) + box.Margin.Vertical;
            }
            else
            {
                style.SizeType = SizeType.AutoSize;
            }
        }

        // ------------------------------------------------------------------ //
        //  Form lifecycle
        // ------------------------------------------------------------------ //

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);            // scales the layout, restores the remembered width
            _loaded    = true;
            _lastWidth = Width;
            FitHeight();
            StartSpinner();
            BeginRun();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            // Wrapped texts change height with the width — refit a fixed-height window
            if (_loaded && !_fitting && Width != _lastWidth)
            {
                _lastWidth = Width;
                bool boxOpen = _rtbLog.Visible || (_txtResponse != null && _txtResponse.Visible);
                if (!boxOpen) FitHeight();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_finished && e.CloseReason == CloseReason.UserClosing)
                e.Cancel = true;   // block close while running
            else
                base.OnFormClosing(e);
        }

        private void SetDetailsVisible(bool visible)
        {
            if (visible == _rtbLog.Visible) return;
            if (!visible) ShrinkBox(_rtbLog, LogHeight);
            else          _boxHeight[_rtbLog] = Scaled(LogHeight);   // opens at its normal height
            _rtbLog.Visible  = visible;
            _lnkDetails.Text = visible ? "Hide details ▴" : "Show details ▾";
            FitHeight(opened: visible);
            if (visible)
            {
                _rtbLog.SelectionStart = _rtbLog.TextLength;
                _rtbLog.ScrollToCaret();
            }
        }

        private void SetResponseVisible(bool visible)
        {
            if (_txtResponse == null || visible == _txtResponse.Visible) return;
            if (!visible) ShrinkBox(_txtResponse, ResponseHeight);
            else          _boxHeight[_txtResponse] = Scaled(ResponseHeight);
            _txtResponse.Visible = visible;
            _lnkResponse.Text    = visible ? "Response ▴" : "Response ▾";
            FitHeight(opened: visible);
        }

        /// <summary>Before closing a box: give its height back, so the window shrinks by
        /// what the box took (FitHeight then settles the rest).</summary>
        private void ShrinkBox(Control box, int normal96)
        {
            _boxHeight[box] = Scaled(normal96);
            if (!_loaded) return;
            _fitting = true;
            try
            {
                MinimumSize = Size.Empty;
                MaximumSize = Size.Empty;
                int taken = box.Height + box.Margin.Vertical;
                ClientSize = new Size(ClientSize.Width, Math.Max(Scaled(100), ClientSize.Height - taken));
            }
            finally
            {
                _fitting = false;
            }
        }

        // ------------------------------------------------------------------ //
        //  Run
        // ------------------------------------------------------------------ //

        private void BeginRun()
        {
            var log = new ActionProgress<string>(AppendLog);

            Task.Run(async () =>
            {
                try
                {
                    TResult result = await RunAsync(log).ConfigureAwait(false);
                    InvokeUI(() => Succeed(result));
                }
                catch (Exception ex)
                {
                    InvokeUI(() => Fail(ex));
                }
            });
        }

        private void Succeed(TResult result)
        {
            _finished = true;
            Succeeded = true;
            for (int i = 0; i < _steps.Length; i++)
                if (!_parallelDone.Contains(i)) SetStepState(i, StepState.Done);
            StopSpinnerWhenIdle();

            OnSucceeded(result);

            _btnClose.Enabled = true;
            _btnClose.Focus();
        }

        private void Fail(Exception ex)
        {
            _finished = true;
            AppendLog("ERROR: " + ex.Message);

            int failed = Math.Max(FailedStep(ex, _currentStep), 0);
            SetStepState(failed, StepState.Failed);

            SetHeadline(_failHeadline, Color.Red);
            // Whole message on one flowing line; the label wraps to two lines and
            // ellipsizes the rest — the full text is in the details log.
            string reason = System.Text.RegularExpressions.Regex
                .Replace(ex.Message ?? "", @"\s+", " ").Trim();
            SetSummary($"Stopped at \"{_steps[failed]}\": {reason}");

            _btnClose.Enabled = true;
            SetDetailsVisible(true);
            OnFailed(ex);
            StopSpinnerWhenIdle();   // a parallel row may still be running: it keeps its spinner
        }

        // ------------------------------------------------------------------ //
        //  Helpers for derived forms
        // ------------------------------------------------------------------ //

        /// <summary>
        /// A thread-safe reporter that marks the step at <c>toIndex(step)</c> as running
        /// (and every earlier one as done) each time the operation reports a stage.
        /// </summary>
        protected IProgress<TStep> StepProgress<TStep>(Func<TStep, int> toIndex) =>
            new ActionProgress<TStep>(step => OnStepStarted(toIndex(step)));

        /// <summary>
        /// A thread-safe reporter for a row that runs alongside the others: it shows the row
        /// as running, with each reported text as its live status. The sequential steps
        /// never mark it done; <see cref="FinishParallelStep"/> does. It keeps updating after
        /// the operation has finished or failed, and is ignored once the window is closed.
        /// Call it from the constructor: the row gets room for two lines (before scaling).
        /// </summary>
        protected IProgress<string> ParallelStatus(int i)
        {
            _parallel.Add(i);
            if (_stepTexts[i].Parent is TableLayoutPanel rows && i < rows.RowStyles.Count)
                rows.RowStyles[i].Height = 42f;   // the live status wraps instead of being cut off
            _stepTexts[i].AutoEllipsis = false;
            return new ActionProgress<string>(text => OnUI(() =>
            {
                if (_parallelDone.Contains(i)) return;
                SetStepState(i, StepState.Running, text);
                if (_spinnerTimer == null) StartSpinner();
            }));
        }

        /// <summary>Gives a parallel row its final state (thread-safe).</summary>
        protected void FinishParallelStep(int i, StepState state, string suffix) => OnUI(() =>
        {
            _parallel.Add(i);
            _parallelDone.Add(i);
            SetStepState(i, state, suffix);
            if (_finished) StopSpinnerWhenIdle();
        });

        protected void SetHeadline(string text, Color color)
        {
            _lblHeadline.Text      = text;
            _lblHeadline.ForeColor = color;
        }

        protected void SetSummary(string text)
        {
            _lblSummary.Text = text;
            FitHeight();
        }

        /// <summary>Shows a red alert box under the summary; null hides it.</summary>
        protected void SetAlert(string text)
        {
            _lblAlert.Text    = text ?? "";
            _lblAlert.Visible = !string.IsNullOrEmpty(text);
            FitHeight();
        }

        /// <summary>Opens the details log.</summary>
        protected void ShowDetails() => SetDetailsVisible(true);

        /// <summary>
        /// Fills the Response box and shows it expanded. Only for forms constructed
        /// with <c>withResponse: true</c>.
        /// </summary>
        protected void SetResponse(string text)
        {
            if (_txtResponse == null) return;
            _txtResponse.Text = string.IsNullOrEmpty(text)
                ? "(no response)"
                : text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
            _txtResponse.Select(0, 0);
            _lnkResponse.Visible = true;
            if (_txtResponse.Visible) FitHeight();
            else SetResponseVisible(true);
        }

        protected void SetStepState(int i, StepState state, string suffix = null)
        {
            if (_stepStates == null) _stepStates = new StepState[_steps.Length];
            _stepStates[i] = state;
            Label icon = _stepIcons[i];
            switch (state)
            {
                case StepState.Pending: icon.Text = "○"; icon.ForeColor = SystemColors.GrayText; break;
                case StepState.Running: icon.Text = SpinnerFrames[_spinnerIdx]; icon.ForeColor = Color.SteelBlue; break;
                case StepState.Done:    icon.Text = "✔"; icon.ForeColor = Color.DarkGreen;  break;
                case StepState.Warning: icon.Text = "⚠"; icon.ForeColor = Color.DarkOrange; break;
                case StepState.Failed:  icon.Text = "✘"; icon.ForeColor = Color.Red;        break;
            }

            Label text = _stepTexts[i];
            text.ForeColor = state == StepState.Pending ? SystemColors.GrayText : SystemColors.ControlText;
            text.Font      = state == StepState.Running ? _stepFontBold : _stepFont;
            if (suffix != null)
                text.Text = _steps[i] + " — " + suffix;
        }

        // ------------------------------------------------------------------ //
        //  Step checklist internals
        // ------------------------------------------------------------------ //

        private void StartSpinner()
        {
            _spinnerTimer = new System.Windows.Forms.Timer { Interval = 80 };
            _spinnerTimer.Tick += (s, e) =>
            {
                if (_stepStates == null) return;
                _spinnerIdx = (_spinnerIdx + 1) % SpinnerFrames.Length;
                for (int i = 0; i < _stepStates.Length; i++)   // every running row, parallel ones too
                    if (_stepStates[i] == StepState.Running) _stepIcons[i].Text = SpinnerFrames[_spinnerIdx];
            };
            _spinnerTimer.Start();
        }

        /// <summary>Stops the spinner unless a row is still running.</summary>
        private void StopSpinnerWhenIdle()
        {
            if (_stepStates == null || Array.IndexOf(_stepStates, StepState.Running) < 0) StopSpinner();
        }

        private void StopSpinner()
        {
            _spinnerTimer?.Stop();
            _spinnerTimer?.Dispose();
            _spinnerTimer = null;
        }

        private void OnStepStarted(int idx)
        {
            if (InvokeRequired)
            {
                OnUI(() => OnStepStarted(idx));
                return;
            }
            if (_finished || idx < 0 || idx >= _steps.Length) return;

            for (int i = Math.Max(_currentStep, 0); i < idx; i++)
                if (!_parallel.Contains(i)) SetStepState(i, StepState.Done);

            _currentStep = idx;
            // Waiting on a parallel row: it keeps its own (live or final) state
            if (!_parallel.Contains(idx)) SetStepState(idx, StepState.Running);
        }

        // ------------------------------------------------------------------ //
        //  Log + threading helpers
        // ------------------------------------------------------------------ //

        private void AppendLog(string message)
        {
            if (InvokeRequired)
            {
                OnUI(() => AppendLog(message));
                return;
            }
            if (IsDisposed) return;

            string m       = message.TrimStart();   // some warnings are indented
            bool isError   = m.StartsWith("ERROR",  StringComparison.OrdinalIgnoreCase)
                          || m.StartsWith("[WARN]", StringComparison.OrdinalIgnoreCase)
                          || m.StartsWith("[ERROR]",StringComparison.OrdinalIgnoreCase);
            bool isSuccess = m.StartsWith("Pull complete", StringComparison.OrdinalIgnoreCase)
                          || m.StartsWith("Scripts ready", StringComparison.OrdinalIgnoreCase)
                          || (m.StartsWith("Rule '") && m.Contains("deployed"));
            bool isOutput  = m.StartsWith("»") || m.StartsWith("────");   // rule output block

            Color c = isError   ? Color.DarkRed
                    : isSuccess ? Color.DarkGreen
                    : isOutput  ? Color.DarkBlue
                    :             SystemColors.ControlText;

            int    start = _rtbLog.TextLength;
            string line  = $"[{DateTime.Now:HH:mm:ss}] {message}\n";
            _rtbLog.AppendText(line);
            _rtbLog.Select(start, line.Length);
            _rtbLog.SelectionColor = c;
            _rtbLog.SelectionLength = 0;
            if (_rtbLog.Visible) _rtbLog.ScrollToCaret();
        }

        /// <summary>
        /// Runs <paramref name="action"/> on the UI thread, or not at all once the window is
        /// closed (a parallel download may outlive it).
        /// </summary>
        private void OnUI(Action action)
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (!InvokeRequired) { action(); return; }
            try { BeginInvoke(action); }
            catch (InvalidOperationException) { /* closed meanwhile */ }
        }

        private void InvokeUI(Action action)
        {
            if (IsHandleCreated)
            {
                if (InvokeRequired) BeginInvoke(action);
                else                action();
            }
        }

        /// <summary>
        /// Calls the handler synchronously on the reporting thread; the handlers above
        /// marshal to the UI themselves, so log lines and step changes stay in order.
        /// </summary>
        private sealed class ActionProgress<T> : IProgress<T>
        {
            private readonly Action<T> _handler;
            public ActionProgress(Action<T> handler) { _handler = handler; }
            public void Report(T value) => _handler(value);
        }
    }
}
