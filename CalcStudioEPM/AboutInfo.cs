using System.Collections.Generic;

namespace CalcStudioEPM
{
    /// <summary>
    /// Everything the About box (Plugins › CalcStudio for Oracle EPM › About) shows.
    /// Edit the values here; a link whose URL is empty is not shown. The version and build
    /// date come from the build (see StampBuildVersion in CalcStudioEPM.csproj).
    /// </summary>
    internal static class AboutInfo
    {
        public const string ProductName = Main.PluginName;

        public static string Version   => BuildInfo.Version;
        public static string BuildDate => BuildInfo.BuildDate;

        public const string Description =
            "Brings Oracle EPM Cloud Calculation Manager rules into Notepad++: pull them, edit Calc Script " +
            "and Groovy with syntax highlighting, autocomplete and local validation, then deploy and run " +
            "them without leaving the editor.";

        // ---- Author ----
        public const string Author     = "Rafael Lorenzoni";
        public const string AuthorNote = "Working as an Oracle EPM consultant since it was Hyperion 🙂";
        public const string Copyright  = "Copyright © 2026 Rafael Lorenzoni";

        public const string LinkedInUrl = "https://www.linkedin.com/in/rafaellorenzoni";
        public const string GitHubUrl   = "https://github.com/rlorenzoni";

        // ---- Project ----
        /// <summary>
        /// The public GitHub repository, e.g. "https://github.com/rlorenzoni/CalcStudio".
        /// Leave empty until it exists; the Issues and Releases links are derived from it.
        /// </summary>
        public const string RepositoryUrl = "https://github.com/rlorenzoni/CalcStudioEPM";

        /// <summary>Donation page (GitHub Sponsors, Buy Me a Coffee, PayPal.me …); empty hides it.</summary>
        public const string DonationUrl = "";

        // ---- Legal ----
        public const string Disclaimer =
            "CalcStudio is an independent community project. It is not an Oracle product and is not " +
            "affiliated with, endorsed, sponsored or supported by Oracle Corporation.\r\n\r\n" +
            "It only uses the REST APIs that Oracle EPM Cloud publicly exposes, with your own credentials " +
            "and the permissions of your EPM account. Your data goes only to the EPM services you " +
            "configure; the only other request is the optional check for a newer release on GitHub, " +
            "which sends nothing about you or your work.\r\n\r\n" +
            "Oracle, Hyperion and Essbase are registered trademarks of Oracle and/or its affiliates. " +
            "Other names may be trademarks of their respective owners.";

        public const string LicenseName = "GNU General Public License v3.0";
        public const string LicenseUrl  = "https://www.gnu.org/licenses/gpl-3.0.html";
        public const string LicenseNotice =
            "This program is free software: you can redistribute it and/or modify it under the terms of " +
            "the GNU GPL v3. It comes with ABSOLUTELY NO WARRANTY.";

        public const string ThirdParty =
            "Includes Newtonsoft.Json (MIT, James Newton-King) and DllExport (MIT, Denis Kuzmin).";

        /// <summary>The links row, in order; entries with an empty URL are skipped.</summary>
        public static IEnumerable<(string Text, string Url)> Links
        {
            get
            {
                yield return ("LinkedIn", LinkedInUrl);
                yield return ("GitHub",   GitHubUrl);
                yield return ("Project page",   RepositoryUrl);
                yield return ("Report an issue", RepositoryUrl.Length == 0 ? "" : RepositoryUrl + "/issues");
                yield return ("Releases",        RepositoryUrl.Length == 0 ? "" : RepositoryUrl + "/releases");
                yield return ("Support the project", DonationUrl);
            }
        }
    }
}
