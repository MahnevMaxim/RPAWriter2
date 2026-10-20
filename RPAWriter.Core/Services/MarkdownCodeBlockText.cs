using Markdig.Syntax;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Extracts the raw source text of a Markdown code block (fenced or indented).
    /// </summary>
    public static class MarkdownCodeBlockText
    {
        /// <summary>
        /// Returns the code content without the fences. In Markdig 1.x the content lives in
        /// <c>code.Lines</c> (a StringLineGroup); <see cref="CodeBlock.CodeBlockLines"/> only
        /// carries roundtrip trivia and must not be used for the code text.
        /// </summary>
        public static string GetText(CodeBlock code)
        {
            if (code == null)
                return string.Empty;

            return code.Lines.ToString();
        }
    }
}
