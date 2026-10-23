using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using CalcStudioEPM.Config;

namespace CalcStudioEPM.Epm
{
    /// <summary>User-facing stages of an execution, reported as each one starts.</summary>
    public enum ExecuteStep { Deploy, Submit, Run }

    /// <summary>Outcome of a rule run.</summary>
    public sealed class ExecuteResult
    {
        public long   JobId;
        public string StatusText;
        /// <summary>The job's "details" text exactly as EPM returned it.</summary>
        public string Details;
        /// <summary>
        /// What the rule returned — its log messages (e.g. Groovy println output) with
        /// EPM's "Log messages :" label removed. Empty when the rule logged nothing.
        /// </summary>
        public string Response;
    }

    /// <summary>The rule ran but did not finish successfully; carries what it returned.</summary>
    public sealed class RuleFailedException : EpmException
    {
        public ExecuteResult Result { get; }
        public RuleFailedException(string message, ExecuteResult result) : base(message) { Result = result; }
    }

    /// <summary>
    /// Runs a deployed business rule as a Planning RULES job:
    ///   POST …/applications/{app}/jobs  { jobType: "RULES", jobName, parameters: {prompt: value} }
    ///   GET  …/jobs/{jobId}           until status is no longer -1 (in progress)
    /// The rule's output comes back in the job's "details" field
    /// ("Log messages : \n…"). The job-details endpoint is not used: EPM answers
    /// 400 "No details can be displayed for the type of job selected" for rules.
    /// </summary>
    public static class RuleExecutor
    {
        private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(30);

        private static readonly Regex LogMessagesLabel =
            new Regex(@"^\s*Log messages\s*:[ \t]*\n?", RegexOptions.IgnoreCase);

        public static async Task<ExecuteResult> ExecuteRuleAsync(
            ConnectionConfig cfg,
            string ruleName,
            Dictionary<string, string> rtpValues,
            IProgress<string> log,
            IProgress<ExecuteStep> steps = null)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            if (string.IsNullOrWhiteSpace(ruleName))
                throw new ArgumentException("Rule name must not be empty.", nameof(ruleName));

            var parameters = new Dictionary<string, string>(
                rtpValues ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            var result = new ExecuteResult();

            using (var client = new EpmClient(cfg))
            {
                // ---------------------------------------------------------- //
                //  Submit
                // ---------------------------------------------------------- //
                steps?.Report(ExecuteStep.Submit);
                log?.Report($"Submitting RULES job '{ruleName}'…");
                foreach (var kv in parameters)
                    log?.Report($"  {kv.Key} = {kv.Value}");

                var body = new { jobType = "RULES", jobName = ruleName, parameters };
                JObject submittedRaw;
                try
                {
                    submittedRaw = await client.PostAsync<object, JObject>("jobs", body).ConfigureAwait(false);
                }
                catch (EpmException ex)
                {
                    throw new EpmException($"EPM Cloud did not accept the rule: {ex.Message}", ex);
                }
                if (submittedRaw == null)
                    throw new EpmException("EPM Cloud returned an empty response to the job submission.");

                log?.Report("  Response: " + submittedRaw.ToString(Formatting.None));
                PlanningJobResponse job = submittedRaw.ToObject<PlanningJobResponse>();
                result.JobId = job.JobId;
                log?.Report($"Rule job {job.JobId} submitted — {job.StatusText}.");

                // ---------------------------------------------------------- //
                //  Wait for it to finish
                // ---------------------------------------------------------- //
                steps?.Report(ExecuteStep.Run);
                DateTime deadline   = DateTime.UtcNow + RunTimeout;
                string   lastStatus = job.StatusText;
                string   finalJson  = null;

                while (job.IsRunning && job.JobId > 0)
                {
                    if (DateTime.UtcNow > deadline)
                        throw new EpmException(
                            $"Rule job {job.JobId} did not finish within {RunTimeout.TotalMinutes:F0} minutes. " +
                            "It may still be running — check the Jobs console in EPM Cloud.");

                    await Task.Delay(EpmClient.JobPollInterval).ConfigureAwait(false);
                    (job, finalJson) = await client.GetJobStatusAsync(job.JobId).ConfigureAwait(false);

                    if (job.StatusText != lastStatus)
                    {
                        log?.Report($"Rule job {job.JobId}: {job.StatusText}");
                        lastStatus = job.StatusText;
                    }
                }
                if (finalJson != null)
                    log?.Report("  Final status: " + finalJson);

                result.StatusText = job.StatusText;
                result.Details    = job.Details;
                result.Response   = ExtractResponse(job.Details);

                log?.Report("");
                log?.Report("──────── Rule output ────────");
                if (result.Response.Length == 0)
                    log?.Report("» (the rule returned no messages)");
                else
                    foreach (string line in result.Response.Split('\n'))
                        log?.Report("» " + line);
                log?.Report("─────────────────────────────");

                if (job.StatusCode != 0)
                    throw new RuleFailedException(
                        $"The rule finished with status '{job.StatusText}'." +
                        (result.Response.Length > 0 ? " See the response below." : ""),
                        result);

                return result;
            }
        }

        /// <summary>
        /// The rule's own output from the job's details text: line endings normalised to
        /// "\n", EPM's leading "Log messages :" label removed, trailing blank lines trimmed.
        /// </summary>
        public static string ExtractResponse(string details)
        {
            string s = (details ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
            Match m = LogMessagesLabel.Match(s);
            if (m.Success) s = s.Substring(m.Length);
            return s.TrimEnd('\n', ' ', '\t');
        }
    }
}
