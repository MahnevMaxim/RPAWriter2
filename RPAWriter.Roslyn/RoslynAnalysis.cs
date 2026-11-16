using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace RPAWriter.Roslyn
{
    /// <summary>
    /// Pure Roslyn analysis over <see cref="Document"/>/<see cref="Solution"/> objects.
    /// No Visual Studio dependency: the VSIX feeds it the live <c>VisualStudioWorkspace</c>
    /// documents, and the tests feed it documents from an <c>AdhocWorkspace</c>.
    /// </summary>
    public static class RoslynAnalysis
    {
        private const int DefaultMaxResults = 100;

        private static readonly SymbolDisplayFormat SignatureFormat = new SymbolDisplayFormat(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            memberOptions:
                SymbolDisplayMemberOptions.IncludeType |
                SymbolDisplayMemberOptions.IncludeParameters |
                SymbolDisplayMemberOptions.IncludeExplicitInterface,
            kindOptions:
                SymbolDisplayKindOptions.IncludeTypeKeyword |
                SymbolDisplayKindOptions.IncludeNamespaceKeyword,
            parameterOptions:
                SymbolDisplayParameterOptions.IncludeType |
                SymbolDisplayParameterOptions.IncludeName |
                SymbolDisplayParameterOptions.IncludeParamsRefOut,
            propertyStyle: SymbolDisplayPropertyStyle.ShowReadWriteDescriptor,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

        // ------------------------------------------------------------------
        // Outline
        // ------------------------------------------------------------------

        public static async Task<CodeOutlineEntry> BuildOutlineAsync(Document document, CancellationToken cancellationToken)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false) as CompilationUnitSyntax;
            if (root == null)
                throw new InvalidOperationException("The document does not contain a C# syntax tree.");

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            var lineSpan = root.SyntaxTree.GetLineSpan(root.Span);

            var fileEntry = new CodeOutlineEntry
            {
                Kind = "file",
                Name = document.Name,
                Signature = document.Name,
                StartLine = 1,
                EndLine = lineSpan.EndLinePosition.Line + 1
            };

            foreach (var member in root.Members)
                AddOutlineMember(member, fileEntry, semanticModel);

            return fileEntry;
        }

        private static void AddOutlineMember(SyntaxNode node, CodeOutlineEntry parent, SemanticModel semanticModel)
        {
            if (node is BaseNamespaceDeclarationSyntax ns)
            {
                var entry = CreateEntry("namespace", ns.Name.ToString(), "namespace " + ns.Name, node);
                parent.Children.Add(entry);
                foreach (var member in ns.Members)
                    AddOutlineMember(member, entry, semanticModel);
                return;
            }

            if (node is TypeDeclarationSyntax typeDecl)
            {
                var entry = CreateEntry(TypeKindLabel(typeDecl), typeDecl.Identifier.Text, node, semanticModel);
                parent.Children.Add(entry);
                foreach (var member in typeDecl.Members)
                    AddOutlineMember(member, entry, semanticModel);
                return;
            }

            if (node is EnumDeclarationSyntax enumDecl)
            {
                var entry = CreateEntry("enum", enumDecl.Identifier.Text, node, semanticModel);
                parent.Children.Add(entry);
                foreach (var member in enumDecl.Members)
                    AddOutlineMember(member, entry, semanticModel);
                return;
            }

            if (node is DelegateDeclarationSyntax del)
            {
                parent.Children.Add(CreateEntry("delegate", del.Identifier.Text, node, semanticModel));
                return;
            }

            if (node is MemberDeclarationSyntax memberDeclaration)
            {
                parent.Children.Add(CreateEntry(MemberKindLabel(memberDeclaration), GetMemberName(memberDeclaration), node, semanticModel));
            }
        }

        private static CodeOutlineEntry CreateEntry(string kind, string name, SyntaxNode node, SemanticModel semanticModel)
        {
            return CreateEntry(kind, name, GetSignature(node, semanticModel), node);
        }

        private static CodeOutlineEntry CreateEntry(string kind, string name, string signature, SyntaxNode node)
        {
            var lineSpan = node.SyntaxTree.GetLineSpan(node.Span);
            return new CodeOutlineEntry
            {
                Kind = kind,
                Name = name ?? string.Empty,
                Signature = signature,
                StartLine = lineSpan.StartLinePosition.Line + 1,
                EndLine = lineSpan.EndLinePosition.Line + 1
            };
        }

        private static string GetSignature(SyntaxNode node, SemanticModel semanticModel)
        {
            if (semanticModel != null)
            {
                try
                {
                    var symbol = semanticModel.GetDeclaredSymbol(node);
                    if (symbol != null)
                        return symbol.ToDisplayString(SignatureFormat);
                }
                catch
                {
                    // Fall back to syntax text on any display-format problem.
                }
            }

            return FirstLine(node.ToString());
        }

        private static string FirstLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            var first = normalized.Split('\n')[0].Trim();
            return first;
        }

        private static string TypeKindLabel(TypeDeclarationSyntax type)
        {
            if (type is ClassDeclarationSyntax) return "class";
            if (type is StructDeclarationSyntax) return "struct";
            if (type is InterfaceDeclarationSyntax) return "interface";
            // EnumDeclarationSyntax derives from BaseTypeDeclarationSyntax and is handled separately.
            if (type is RecordDeclarationSyntax) return "record";
            return "type";
        }

        private static string MemberKindLabel(MemberDeclarationSyntax member)
        {
            if (member is MethodDeclarationSyntax) return "method";
            if (member is ConstructorDeclarationSyntax) return "constructor";
            if (member is DestructorDeclarationSyntax) return "destructor";
            if (member is OperatorDeclarationSyntax || member is ConversionOperatorDeclarationSyntax) return "operator";
            if (member is PropertyDeclarationSyntax) return "property";
            if (member is IndexerDeclarationSyntax) return "indexer";
            if (member is FieldDeclarationSyntax) return "field";
            if (member is EventFieldDeclarationSyntax) return "event field";
            if (member is EventDeclarationSyntax) return "event";
            if (member is EnumMemberDeclarationSyntax) return "enum member";
            return "member";
        }

        private static string GetMemberName(MemberDeclarationSyntax member)
        {
            if (member is MethodDeclarationSyntax m) return m.Identifier.Text;
            if (member is ConstructorDeclarationSyntax c) return c.Identifier.Text;
            if (member is DestructorDeclarationSyntax d) return d.Identifier.Text;
            if (member is OperatorDeclarationSyntax o) return "operator " + o.OperatorToken.Text;
            if (member is ConversionOperatorDeclarationSyntax co) return "operator " + co.Type.ToString();
            if (member is PropertyDeclarationSyntax p) return p.Identifier.Text;
            if (member is IndexerDeclarationSyntax ix) return "this";
            if (member is FieldDeclarationSyntax f) return VariableNames(f.Declaration.Variables);
            if (member is EventFieldDeclarationSyntax ef) return VariableNames(ef.Declaration.Variables);
            if (member is EventDeclarationSyntax e) return e.Identifier.Text;
            if (member is EnumMemberDeclarationSyntax em) return em.Identifier.Text;
            return string.Empty;
        }

        private static string VariableNames(IEnumerable<VariableDeclaratorSyntax> variables)
        {
            return string.Join(", ", variables.Select(v => v.Identifier.Text));
        }

        // ------------------------------------------------------------------
        // Symbol usages
        // ------------------------------------------------------------------

        public static async Task<SymbolUsageResult> FindSymbolUsagesAsync(
            Solution solution,
            string symbolName,
            Document scopeDocument,
            int maxResults,
            CancellationToken cancellationToken)
        {
            if (solution == null) throw new ArgumentNullException(nameof(solution));
            if (string.IsNullOrWhiteSpace(symbolName)) throw new ArgumentException("Symbol name is required.", nameof(symbolName));
            if (maxResults <= 0) maxResults = DefaultMaxResults;

            var symbol = await ResolveSymbolAsync(solution, symbolName, scopeDocument, cancellationToken).ConfigureAwait(false);
            if (symbol == null)
                throw new InvalidOperationException("Symbol not found: " + symbolName);

            var references = await SymbolFinder.FindReferencesAsync(symbol, solution, cancellationToken: cancellationToken).ConfigureAwait(false);

            var usages = new List<SymbolUsage>();
            var truncated = false;
            foreach (var referenced in references)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var location in referenced.Locations)
                {
                    if (location.Location.SourceTree == null)
                        continue; // metadata reference, not a source file

                    if (usages.Count >= maxResults)
                    {
                        truncated = true;
                        break;
                    }

                    var lineSpan = location.Location.GetLineSpan();
                    var sourceText = location.Location.SourceTree.GetText(cancellationToken);
                    usages.Add(new SymbolUsage
                    {
                        FilePath = location.Location.SourceTree.FilePath,
                        Line = lineSpan.StartLinePosition.Line + 1,
                        Column = lineSpan.StartLinePosition.Character + 1,
                        CodeLine = GetLineAt(sourceText.ToString(), lineSpan.StartLinePosition.Line)
                    });
                }

                if (truncated)
                    break;
            }

            return new SymbolUsageResult
            {
                SymbolDisplay = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                Usages = usages,
                Truncated = truncated,
                MaxResults = maxResults
            };
        }

        private static async Task<ISymbol> ResolveSymbolAsync(Solution solution, string name, Document scopeDocument, CancellationToken cancellationToken)
        {
            if (scopeDocument != null)
            {
                var root = await scopeDocument.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                var semanticModel = await scopeDocument.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                var symbol = FindDeclarationSymbol(root, semanticModel, name);
                if (symbol != null)
                    return symbol;
            }

            foreach (var project in solution.Projects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var symbols = await SymbolFinder.FindDeclarationsAsync(project, name, false, cancellationToken).ConfigureAwait(false);
                var match = symbols.FirstOrDefault();
                if (match != null)
                    return match;
            }

            return null;
        }

        private static ISymbol FindDeclarationSymbol(SyntaxNode root, SemanticModel semanticModel, string name)
        {
            foreach (var node in root.DescendantNodes())
            {
                var nodeName = DeclarationName(node);
                if (nodeName == null || !string.Equals(nodeName, name, StringComparison.Ordinal))
                    continue;

                var symbol = semanticModel.GetDeclaredSymbol(node) ?? semanticModel.GetSymbolInfo(node).Symbol;
                if (symbol != null)
                    return symbol;
            }
            return null;
        }

        private static string DeclarationName(SyntaxNode node)
        {
            if (node is MethodDeclarationSyntax m) return m.Identifier.Text;
            if (node is ConstructorDeclarationSyntax c) return c.Identifier.Text;
            if (node is DestructorDeclarationSyntax d) return d.Identifier.Text;
            if (node is PropertyDeclarationSyntax p) return p.Identifier.Text;
            if (node is EventDeclarationSyntax e) return e.Identifier.Text;
            if (node is ClassDeclarationSyntax cls) return cls.Identifier.Text;
            if (node is StructDeclarationSyntax st) return st.Identifier.Text;
            if (node is InterfaceDeclarationSyntax it) return it.Identifier.Text;
            if (node is EnumDeclarationSyntax en) return en.Identifier.Text;
            if (node is RecordDeclarationSyntax rec) return rec.Identifier.Text;
            if (node is DelegateDeclarationSyntax del) return del.Identifier.Text;
            if (node is EnumMemberDeclarationSyntax em) return em.Identifier.Text;
            if (node is VariableDeclaratorSyntax v) return v.Identifier.Text;
            return null;
        }

        private static string GetLineAt(string text, int zeroBasedLine)
        {
            if (text == null) return string.Empty;
            var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            var lines = normalized.Split('\n');
            if (zeroBasedLine >= 0 && zeroBasedLine < lines.Length)
                return lines[zeroBasedLine].Trim();
            return string.Empty;
        }

        // ------------------------------------------------------------------
        // Inheritance
        // ------------------------------------------------------------------

        public static async Task<InheritanceInfo> GetInheritanceAsync(
            Solution solution,
            string symbolName,
            Document scopeDocument,
            CancellationToken cancellationToken)
        {
            if (solution == null) throw new ArgumentNullException(nameof(solution));
            if (string.IsNullOrWhiteSpace(symbolName)) throw new ArgumentException("Type name is required.", nameof(symbolName));

            var namedType = await ResolveNamedTypeAsync(solution, symbolName, scopeDocument, cancellationToken).ConfigureAwait(false);
            if (namedType == null)
                throw new InvalidOperationException("Type not found: " + symbolName);

            var info = new InheritanceInfo
            {
                Symbol = namedType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                Kind = namedType.TypeKind.ToString()
            };

            var baseType = namedType.BaseType;
            while (baseType != null && baseType.SpecialType != SpecialType.System_Object)
            {
                info.BaseTypes.Add(baseType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
                baseType = baseType.BaseType;
            }

            foreach (var iface in namedType.AllInterfaces)
                info.Interfaces.Add(iface.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));

            if (namedType.TypeKind == TypeKind.Interface)
            {
                var implementations = await SymbolFinder.FindImplementationsAsync(namedType, solution, transitive: false, cancellationToken: cancellationToken).ConfigureAwait(false);
                foreach (var implementation in implementations)
                    info.Implementations.Add(FormatTypeSymbol(implementation));
            }
            else
            {
                var derived = await SymbolFinder.FindDerivedClassesAsync(namedType, solution, transitive: false, cancellationToken: cancellationToken).ConfigureAwait(false);
                foreach (var d in derived)
                    info.DerivedTypes.Add(FormatTypeSymbol(d));
            }

            return info;
        }

        private static async Task<INamedTypeSymbol> ResolveNamedTypeAsync(Solution solution, string name, Document scopeDocument, CancellationToken cancellationToken)
        {
            if (scopeDocument != null)
            {
                var root = await scopeDocument.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                var semanticModel = await scopeDocument.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                foreach (var node in root.DescendantNodes())
                {
                    if (node is BaseTypeDeclarationSyntax type && string.Equals(type.Identifier.Text, name, StringComparison.Ordinal))
                    {
                        var symbol = semanticModel.GetDeclaredSymbol(type) as INamedTypeSymbol;
                        if (symbol != null)
                            return symbol;
                    }
                    else if (node is DelegateDeclarationSyntax del && string.Equals(del.Identifier.Text, name, StringComparison.Ordinal))
                    {
                        var symbol = semanticModel.GetDeclaredSymbol(del) as INamedTypeSymbol;
                        if (symbol != null)
                            return symbol;
                    }
                }
            }

            foreach (var project in solution.Projects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var symbols = await SymbolFinder.FindDeclarationsAsync(project, name, false, cancellationToken).ConfigureAwait(false);
                var match = symbols.OfType<INamedTypeSymbol>().FirstOrDefault();
                if (match != null)
                    return match;
            }

            return null;
        }

        private static string FormatTypeSymbol(ISymbol symbol)
        {
            var display = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            var location = symbol.Locations.FirstOrDefault(l => l.SourceTree != null);
            if (location == null)
                return display;

            var lineSpan = location.GetLineSpan();
            var file = location.SourceTree.FilePath ?? string.Empty;
            var start = lineSpan.StartLinePosition.Line + 1;
            return display + " (" + file + ":" + start + ")";
        }

        // ------------------------------------------------------------------
        // Diagnostics
        // ------------------------------------------------------------------

        public static async Task<IReadOnlyList<CodeDiagnostic>> GetDiagnosticsAsync(Document document, CancellationToken cancellationToken)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            var result = new List<CodeDiagnostic>();
            foreach (var diagnostic in semanticModel.GetDiagnostics(null, cancellationToken))
            {
                if (diagnostic.Severity < DiagnosticSeverity.Warning)
                    continue;

                var lineSpan = diagnostic.Location.GetLineSpan();
                result.Add(new CodeDiagnostic
                {
                    Severity = diagnostic.Severity.ToString(),
                    Id = diagnostic.Id,
                    Message = diagnostic.GetMessage(),
                    FilePath = diagnostic.Location.SourceTree?.FilePath ?? document.FilePath,
                    Line = lineSpan.StartLinePosition.Line + 1,
                    Column = lineSpan.StartLinePosition.Character + 1
                });
            }
            return result;
        }
    }

    /// <summary>Result of a compiler-verified symbol usage search.</summary>
    public sealed class SymbolUsageResult
    {
        public string SymbolDisplay { get; set; }
        public IReadOnlyList<SymbolUsage> Usages { get; set; } = new List<SymbolUsage>();
        public bool Truncated { get; set; }
        public int MaxResults { get; set; }
    }
}
