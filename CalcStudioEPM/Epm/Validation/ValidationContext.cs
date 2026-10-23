namespace CalcStudioEPM.Epm.Validation
{
    /// <summary>What the validators may know about the script beyond its text.</summary>
    public sealed class ValidationContext
    {
        /// <summary>The connection's local folder: pulled templates, metadata.</summary>
        public string LocalFolder { get; set; }

        /// <summary>The cube the rule belongs to (from its path), when known.</summary>
        public string Cube { get; set; }

        /// <summary>Substitution / user variables and, when downloaded, dimensions and members.</summary>
        public EpmMetadata Metadata { get; set; }

        public static ValidationContext For(string localFolder, string cube) => new ValidationContext
        {
            LocalFolder = localFolder,
            Cube        = cube,
            Metadata    = string.IsNullOrWhiteSpace(localFolder) ? new EpmMetadata() : EpmMetadata.Load(localFolder),
        };
    }
}
