using System;
using System.Collections.Generic;
using System.Text;

namespace RPAWriter.Core.Services
{
    /// <summary>A fragment of a chat message: plain text or a fenced code block.</summary>
    public abstract class MessageBlock
    {
    }

    /// <summary>A plain-text fragment of a chat message.</summary>
    public sealed class TextMessageBlock : MessageBlock
    {
        public TextMessageBlock(string text)
        {
            Text = text ?? "";
        }

        /// <summary>The plain text content.</summary>
        public string Text { get; }
    }

    /// <summary>A fenced code block (```...```) extracted from a chat message.</summary>
    public sealed class CodeMessageBlock : MessageBlock
    {
        public CodeMessageBlock(string language, string code, bool highlight)
        {
            Language = language ?? "";
            Code = code ?? "";
            Tokens = highlight
                ? CSharpHighlighter.Highlight(Code)
                : new[] { new CodeToken(CodeTokenKind.Plain, Code) };
        }

        /// <summary>The language hint written after the opening fence (e.g. "csharp").</summary>
        public string Language { get; }

        /// <summary>The raw code without the fences.</summary>
        public string Code { get; }

        /// <summary>Highlighted tokens; for non-C# code this is a single plain token.</summary>
        public IReadOnlyList<CodeToken> Tokens { get; }
    }

    /// <summary>Splits a chat message into text and code blocks based on Markdown fenced code blocks.</summary>
    public static class ChatMarkdown
    {
        private static readonly HashSet<string> CSharpLanguages =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "csharp", "cs", "c#" };

        public static IReadOnlyList<MessageBlock> SplitBlocks(string text)
        {
            var blocks = new List<MessageBlock>();
            if (string.IsNullOrEmpty(text))
                return blocks;

            var textBuffer = new StringBuilder();
            int i = 0;
            int n = text.Length;

            while (i < n)
            {
                int fenceStart = FindOpeningFence(text, i);
                if (fenceStart < 0)
                {
                    textBuffer.Append(text, i, n - i);
                    break;
                }

                int fenceLineStart = LineStartIndex(text, fenceStart);
                if (fenceLineStart > i)
                    textBuffer.Append(text, i, fenceLineStart - i);

                int contentStart = SkipLine(text, fenceStart);
                string language = ReadLanguage(text, fenceStart);
                int closeStart = FindClosingFence(text, contentStart);
                int codeEnd = closeStart < 0 ? n : closeStart;
                string code = TrimTrailingNewlines(text.Substring(contentStart, codeEnd - contentStart));

                FlushText(textBuffer, blocks);
                blocks.Add(new CodeMessageBlock(language, code, CSharpLanguages.Contains(language)));

                i = closeStart < 0 ? n : SkipLine(text, closeStart);
            }

            FlushText(textBuffer, blocks);
            return blocks;
        }

        private static void FlushText(StringBuilder buffer, List<MessageBlock> blocks)
        {
            if (buffer.Length == 0)
                return;
            blocks.Add(new TextMessageBlock(buffer.ToString()));
            buffer.Clear();
        }
        /// <summary>Returns the index of the next opening ``` fence at a line start, or -1.</summary>
        private static int FindOpeningFence(string text, int from)
        {
            int i = from;
            while (i < text.Length)
            {
                int p = text.IndexOf('`', i);
                if (p < 0)
                    return -1;
                if (IsFenceAt(text, p) && IsLineStart(text, p))
                    return p;
                i = p + 1;
            }
            return -1;
        }

        /// <summary>Returns the index of the closing ``` fence (nothing but whitespace after it), or -1.</summary>
        private static int FindClosingFence(string text, int from)
        {
            int i = from;
            while (i < text.Length)
            {
                int p = text.IndexOf('`', i);
                if (p < 0)
                    return -1;
                if (IsFenceAt(text, p) && IsLineStart(text, p)
                    && IsOnlyWhitespace(text, p + 3, EndOfLine(text, p + 3)))
                    return p;
                i = p + 1;
            }
            return -1;
        }

        private static bool IsFenceAt(string text, int index)
        {
            return index + 2 < text.Length
                && text[index] == '`'
                && text[index + 1] == '`'
                && text[index + 2] == '`';
        }

        /// <summary>True when only whitespace precedes the character (i.e. it is at the start of a line).</summary>
        private static bool IsLineStart(string text, int index)
        {
            int i = index - 1;
            while (i >= 0 && (text[i] == ' ' || text[i] == '\t'))
                i--;
            return i < 0 || text[i] == '\n' || text[i] == '\r';
        }

        private static bool IsOnlyWhitespace(string text, int from, int to)
        {
            for (int i = from; i < to; i++)
            {
                char c = text[i];
                if (c != ' ' && c != '\t' && c != '\r' && c != '\n')
                    return false;
            }
            return true;
        }

        private static int LineStartIndex(string text, int index)
        {
            int i = index - 1;
            while (i >= 0 && text[i] != '\n')
                i--;
            return i + 1;
        }

        private static int EndOfLine(string text, int index)
        {
            int i = index;
            while (i < text.Length && text[i] != '\n' && text[i] != '\r')
                i++;
            return i;
        }

        /// <summary>Returns the index just past the line containing <paramref name="index"/> (handles CR, LF and CRLF).</summary>
        private static int SkipLine(string text, int index)
        {
            int i = index;
            while (i < text.Length && text[i] != '\n' && text[i] != '\r')
                i++;
            if (i < text.Length && text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                return i + 2;
            if (i < text.Length && (text[i] == '\n' || text[i] == '\r'))
                return i + 1;
            return i;
        }

        /// <summary>Reads the language hint (first word) after the opening fence.</summary>
        private static string ReadLanguage(string text, int fenceStart)
        {
            int start = fenceStart + 3;
            int end = EndOfLine(text, start);
            string line = text.Substring(start, end - start).Trim();
            int space = line.IndexOfAny(new[] { ' ', '\t' });
            return (space < 0 ? line : line.Substring(0, space)).Trim();
        }

        private static string TrimTrailingNewlines(string value)
        {
            int end = value.Length;
            while (end > 0 && (value[end - 1] == '\n' || value[end - 1] == '\r'))
                end--;
            return value.Substring(0, end);
        }
    }
}
