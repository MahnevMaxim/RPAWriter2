namespace RPAWriter2
{
    /// <summary>
    /// A single file shown in the Spec tab's docs expander.
    /// </summary>
    public sealed class SpecDocsFile
    {
        /// <summary>Absolute path to the file on disk.</summary>
        public string FullPath { get; set; } = string.Empty;

        /// <summary>Path relative to the solution's docs folder, used as the display label.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Whether the file is a markdown document and therefore clickable.</summary>
        public bool IsMarkdown { get; set; }
    }
}
