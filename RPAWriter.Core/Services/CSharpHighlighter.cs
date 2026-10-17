using System;
using System.Collections.Generic;

namespace RPAWriter.Core.Services
{
    /// <summary>Token classification used for minimal C# syntax highlighting.</summary>
    public enum CodeTokenKind
    {
        Plain,
        Keyword,
        String,
        Comment,
        Number
    }

    /// <summary>A single highlighted code fragment: its text plus a classification.</summary>
    public sealed class CodeToken
    {
        public CodeToken(CodeTokenKind kind, string text)
        {
            Kind = kind;
            Text = text ?? "";
        }

        public CodeTokenKind Kind { get; }

        public string Text { get; }
    }

    /// <summary>Minimal, regex-free C# tokenizer for syntax highlighting in the chat UI.</summary>
    public static class CSharpHighlighter
    {
        private static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
        {
            // Reserved keywords.
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char",
            "checked", "class", "const", "continue", "decimal", "default", "delegate", "do",
            "double", "else", "enum", "event", "explicit", "extern", "false", "finally",
            "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int",
            "interface", "internal", "is", "lock", "long", "namespace", "new", "null",
            "object", "operator", "out", "override", "params", "private", "protected",
            "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof",
            "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
            "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
            "virtual", "void", "volatile", "while",
            // Common contextual keywords.
            "add", "alias", "async", "await", "dynamic", "from", "get", "global", "group",
            "into", "join", "let", "nameof", "notnull", "on", "orderby", "partial",
            "remove", "select", "set", "unmanaged", "value", "var", "when", "where",
            "yield", "record", "init", "required", "file", "scoped"
        };
        /// <summary>Splits C# source into a sequence of classified tokens preserving the original text.</summary>
        public static IReadOnlyList<CodeToken> Highlight(string code)
        {
            var tokens = new List<CodeToken>();
            if (string.IsNullOrEmpty(code))
                return tokens;

            int i = 0;
            int n = code.Length;
            while (i < n)
            {
                char c = code[i];

                // Block comment.
                if (c == '/' && i + 1 < n && code[i + 1] == '*')
                {
                    int end = code.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    int stop = end < 0 ? n : end + 2;
                    tokens.Add(new CodeToken(CodeTokenKind.Comment, code.Substring(i, stop - i)));
                    i = stop;
                    continue;
                }

                // Line comment (newline itself is handled below as a separate token).
                if (c == '/' && i + 1 < n && code[i + 1] == '/')
                {
                    int stop = i + 2;
                    while (stop < n && code[stop] != '\n' && code[stop] != '\r')
                        stop++;
                    tokens.Add(new CodeToken(CodeTokenKind.Comment, code.Substring(i, stop - i)));
                    i = stop;
                    continue;
                }

                // String literal (regular, verbatim, interpolated, or a mix).
                if (TryGetStringPrefix(code, i, out int quoteIndex, out bool verbatim))
                {
                    int stop = ConsumeString(code, quoteIndex, verbatim);
                    tokens.Add(new CodeToken(CodeTokenKind.String, code.Substring(i, stop - i)));
                    i = stop;
                    continue;
                }

                // Number literal.
                if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(code[i + 1])))
                {
                    int stop = ConsumeNumber(code, i);
                    tokens.Add(new CodeToken(CodeTokenKind.Number, code.Substring(i, stop - i)));
                    i = stop;
                    continue;
                }

                // Newline (CRLF is kept as one token so the UI emits a single line break).
                if (c == '\r' || c == '\n')
                {
                    if (c == '\r' && i + 1 < n && code[i + 1] == '\n')
                    {
                        tokens.Add(new CodeToken(CodeTokenKind.Plain, "\r\n"));
                        i += 2;
                    }
                    else
                    {
                        tokens.Add(new CodeToken(CodeTokenKind.Plain, c.ToString()));
                        i++;
                    }
                    continue;
                }

                // Identifier or keyword.
                if (char.IsLetter(c) || c == '_')
                {
                    int stop = i + 1;
                    while (stop < n && (char.IsLetterOrDigit(code[stop]) || code[stop] == '_'))
                        stop++;
                    string word = code.Substring(i, stop - i);
                    tokens.Add(new CodeToken(Keywords.Contains(word) ? CodeTokenKind.Keyword : CodeTokenKind.Plain, word));
                    i = stop;
                    continue;
                }

                // Any other single character (punctuation, whitespace, ...).
                tokens.Add(new CodeToken(CodeTokenKind.Plain, c.ToString()));
                i++;
            }

            return tokens;
        }
        /// <summary>
        /// Detects a C# string-literal prefix at <paramref name="index"/>. When a string starts there,
        /// returns true and sets the index of the opening quote and whether the string is verbatim.
        /// Supported forms: "..." @"..." $"..." $@"..." @$"...".
        /// </summary>
        private static bool TryGetStringPrefix(string code, int index, out int quoteIndex, out bool verbatim)
        {
            quoteIndex = -1;
            verbatim = false;
            int n = code.Length;

            char c = code[index];

            if (c == '"')
            {
                quoteIndex = index;
                return true;
            }

            if (c == '$' && index + 1 < n)
            {
                if (code[index + 1] == '"')
                {
                    quoteIndex = index + 1;
                    return true;
                }
                if (code[index + 1] == '@' && index + 2 < n && code[index + 2] == '"')
                {
                    quoteIndex = index + 2;
                    verbatim = true;
                    return true;
                }
            }

            if (c == '@' && index + 1 < n)
            {
                if (code[index + 1] == '"')
                {
                    quoteIndex = index + 1;
                    verbatim = true;
                    return true;
                }
                if (code[index + 1] == '$' && index + 2 < n && code[index + 2] == '"')
                {
                    quoteIndex = index + 2;
                    verbatim = true;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Consumes a string literal starting at its opening quote; returns the index just past the closing quote.</summary>
        private static int ConsumeString(string code, int quoteIndex, bool verbatim)
        {
            int n = code.Length;
            int i = quoteIndex + 1;

            if (verbatim)
            {
                while (i < n)
                {
                    if (code[i] == '"')
                    {
                        if (i + 1 < n && code[i + 1] == '"')
                        {
                            i += 2; // Doubled quote inside a verbatim string.
                            continue;
                        }
                        return i + 1;
                    }
                    i++;
                }
                return n;
            }

            bool escaped = false;
            while (i < n)
            {
                char ch = code[i];
                if (escaped)
                {
                    escaped = false;
                    i++;
                    continue;
                }
                if (ch == '\\')
                {
                    escaped = true;
                    i++;
                    continue;
                }
                if (ch == '"')
                    return i + 1;
                i++;
            }
            return n;
        }
        /// <summary>Consumes a numeric literal (decimal, float, exponent, hex, binary, suffixes); returns the index past it.</summary>
        private static int ConsumeNumber(string code, int index)
        {
            int n = code.Length;
            int i = index;

            // Hex literal: 0x / 0X.
            if (i + 1 < n && code[i] == '0' && (code[i + 1] == 'x' || code[i + 1] == 'X'))
            {
                i += 2;
                while (i < n && IsHexDigit(code[i]))
                    i++;
                return ConsumeNumericSuffix(code, i);
            }

            // Binary literal: 0b / 0B.
            if (i + 1 < n && code[i] == '0' && (code[i + 1] == 'b' || code[i + 1] == 'B'))
            {
                i += 2;
                while (i < n && (code[i] == '0' || code[i] == '1'))
                    i++;
                return ConsumeNumericSuffix(code, i);
            }

            // Decimal / floating-point mantissa.
            bool seenDot = false;
            if (code[i] == '.')
            {
                seenDot = true;
                i++;
            }
            while (i < n)
            {
                char ch = code[i];
                if (char.IsDigit(ch))
                {
                    i++;
                    continue;
                }
                if (ch == '.' && !seenDot)
                {
                    seenDot = true;
                    i++;
                    continue;
                }
                break;
            }

            // Exponent part (only when it actually has digits).
            if (i < n && (code[i] == 'e' || code[i] == 'E'))
            {
                int j = i + 1;
                if (j < n && (code[j] == '+' || code[j] == '-'))
                    j++;
                if (j < n && char.IsDigit(code[j]))
                {
                    i = j;
                    while (i < n && char.IsDigit(code[i]))
                        i++;
                }
            }

            return ConsumeNumericSuffix(code, i);
        }

        private static int ConsumeNumericSuffix(string code, int index)
        {
            int n = code.Length;
            int i = index;
            while (i < n && (code[i] == 'f' || code[i] == 'F' || code[i] == 'd' || code[i] == 'D'
                || code[i] == 'm' || code[i] == 'M' || code[i] == 'u' || code[i] == 'U'
                || code[i] == 'l' || code[i] == 'L'))
                i++;
            return i;
        }

        private static bool IsHexDigit(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }
    }
}
