using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using CalcStudioEPM.Config;
using CalcStudioEPM.Epm;

namespace CalcStudioEPM.Forms
{
    /// <summary>
    /// Progress dialog for "Execute Deployed Calculation" — optionally deploys the editor's
    /// changes first, then runs the rule in EPM Cloud. What the rule returned goes in the
    /// expanded "Response" box; the full technical log stays behind "Show details".
    /// See <see cref="StepProgressForm{TResult}"/> for the shared look and feel.
    /// </summary>
    public class ExecuteProgressForm : StepProgressForm<ExecuteResult>
    {
        private static readonly string DeployText  = "Deploy your changes to EPM Cloud";
        private static readonly string[] RunTexts =
        {
            "Start the rule in EPM Cloud",        // ExecuteStep.Submit
            "Wait for the rule to finish",        // ExecuteStep.Run
        };

        private readonly ConnectionConfig           _cfg;
        private readonly string                     _planType, _cube, _ruleName;
        private readonly DeployPlan                 _deployPlan;      // null = run what's deployed
        private readonly Dictionary<string, string> _values;
        private DeployResult                        _deployResult;   // set when deploying first

        /// <param name="deployPlan">What to deploy before running; null to run the deployed version.</param>
        public ExecuteProgressForm(ConnectionConfig cfg, string planType, string cube, string ruleName,
                                   DeployPlan deployPlan, Dictionary<string, string> values)
            : base(title:        $"Execute — {ruleName}",
                   headline:     $"Running '{ruleName}' in {ConnLabel(cfg)}…",
                   failHeadline: "Execution failed",
                   steps:        deployPlan != null ? new[] { DeployText }.Concat(RunTexts).ToArray() : RunTexts,
                   withResponse: true)
        {
            _cfg           = cfg;
            _planType      = planType;
            _cube          = cube;
            _ruleName      = ruleName;
            _deployPlan    = deployPlan;
            _values        = values ?? new Dictionary<string, string>();
        }

        private static string ConnLabel(ConnectionConfig cfg) =>
            string.IsNullOrWhiteSpace(cfg?.Name) ? "EPM Cloud" : cfg.Name;

        /// <summary>Row of a step: ExecuteStep.Deploy is only shown when deploying first.</summary>
        private int Row(ExecuteStep s) => _deployPlan != null ? (int)s : (int)s - 1;

        protected override async Task<ExecuteResult> RunAsync(IProgress<string> log)
        {
            IProgress<ExecuteStep> steps = StepProgress<ExecuteStep>(Row);

            if (_deployPlan != null)
            {
                steps.Report(ExecuteStep.Deploy);
                log.Report("──── Deploying your changes ────");
                // The very same deploy as Deploy Active Tab (package, upload, import, clean-up)
                _deployResult = await DeployService
                    .DeployRuleAsync(_cfg, _planType, _cube, _ruleName, _deployPlan, log)
                    .ConfigureAwait(false);
                log.Report("──── Executing ────");
            }

            return await RuleExecutor.ExecuteRuleAsync(_cfg, _ruleName, _values, log, steps)
                                     .ConfigureAwait(false);
        }

        protected override void OnSucceeded(ExecuteResult r)
        {
            if (_deployResult != null)
            {
                bool cleanupWarning = _deployResult.CleanupError != null;
                SetStepState(Row(ExecuteStep.Deploy), cleanupWarning ? StepState.Warning : StepState.Done,
                    (_deployResult.JobId != null ? $"import job {_deployResult.JobId}" : "deployed") +
                    (cleanupWarning ? "; temporary snapshot not removed (see details)" : ""));
            }
            SetStepState(Row(ExecuteStep.Run), StepState.Done, $"{r.StatusText} (job {r.JobId})");

            SetHeadline($"'{_ruleName}' completed", Color.DarkGreen);
            SetSummary($"Job {r.JobId} finished: {r.StatusText}." +
                       (_deployPlan == null ? "" :
                        _deployPlan.IncludesPlanning
                            ? "\nYour changes were deployed to Calculation Manager and Planning before running."
                            : "\nYour changes were deployed to Calculation Manager only; Planning ran its previous version."));
            if (_deployPlan != null && !_deployPlan.IncludesPlanning)
                SetAlert(DeployProgressForm.ActionNeededText(_ruleName, _deployPlan));

            SetResponse(r.Response);
        }

        protected override void OnFailed(Exception ex)
        {
            // The rule ran but failed — what it returned usually explains why
            if (ex is RuleFailedException rf)
                SetResponse(rf.Result.Response);
        }
    }
}
