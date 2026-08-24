using FilesCollector.Core.Signatures;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FilesCollector.Extractors;

public sealed class CSharpSignatureExtractor : ISignatureExtractor
{
    public bool CanHandle(string extension)
    {
        return string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase);
    }

    public SignatureExtractionResult Extract(string path, string source)
    {
        try
        {
            var tree = CSharpSyntaxTree.ParseText(source, path: path);
            var root = tree.GetCompilationUnitRoot();
            var lines = new List<string>();
            foreach (var usingDirective in root.Usings)
            {
                lines.Add(Normalize(usingDirective.ToString()).TrimEnd(';'));
            }

            if (root.Usings.Count > 0 && root.Members.Count > 0)
            {
                lines.Add(string.Empty);
            }

            RenderMembers(root.Members, source, lines, 0);
            var diagnostics = tree.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
            if (lines.Count == 0)
            {
                return new SignatureExtractionResult(false, null, "csharp-roslyn-v1", "signature_extraction_failed");
            }

            return new SignatureExtractionResult(true, string.Join(Environment.NewLine, lines).TrimEnd() + Environment.NewLine, "csharp-roslyn-v1", diagnostics.Length == 0 ? null : "signature_syntax_warning");
        }
        catch (Exception)
        {
            return new SignatureExtractionResult(false, null, "csharp-roslyn-v1", "signature_extraction_failed");
        }
    }

    private static void RenderMembers(SyntaxList<MemberDeclarationSyntax> members, string source, List<string> lines, int indent)
    {
        foreach (var member in members)
        {
            switch (member)
            {
                case NamespaceDeclarationSyntax namespaceDeclaration:
                    lines.Add(Indent(indent) + "namespace " + namespaceDeclaration.Name);
                    RenderMembers(namespaceDeclaration.Members, source, lines, indent + 1);
                    break;
                case FileScopedNamespaceDeclarationSyntax fileScopedNamespace:
                    lines.Add(Indent(indent) + "namespace " + fileScopedNamespace.Name);
                    RenderMembers(fileScopedNamespace.Members, source, lines, indent + 1);
                    break;
                case TypeDeclarationSyntax typeDeclaration when IsVisible(typeDeclaration.Modifiers):
                    lines.Add(Indent(indent) + HeaderBeforeBody(typeDeclaration, source));
                    RenderMembers(typeDeclaration.Members, source, lines, indent + 1);
                    break;
                case EnumDeclarationSyntax enumDeclaration when IsVisible(enumDeclaration.Modifiers):
                    lines.Add(Indent(indent) + HeaderBeforeBody(enumDeclaration, source));
                    foreach (var enumMember in enumDeclaration.Members)
                    {
                        lines.Add(Indent(indent + 1) + Normalize(enumMember.ToString()).TrimEnd(','));
                    }
                    break;
                case DelegateDeclarationSyntax delegateDeclaration when IsVisible(delegateDeclaration.Modifiers):
                    lines.Add(Indent(indent) + Normalize(delegateDeclaration.ToString()).TrimEnd(';'));
                    break;
                case MethodDeclarationSyntax methodDeclaration when IsVisible(methodDeclaration.Modifiers):
                    lines.Add(Indent(indent) + HeaderBeforeBody(methodDeclaration, source));
                    break;
                case ConstructorDeclarationSyntax constructorDeclaration when IsVisible(constructorDeclaration.Modifiers):
                    lines.Add(Indent(indent) + HeaderBeforeBody(constructorDeclaration, source));
                    break;
                case PropertyDeclarationSyntax propertyDeclaration when IsVisible(propertyDeclaration.Modifiers):
                    lines.Add(Indent(indent) + HeaderBeforeBody(propertyDeclaration, source));
                    break;
                case IndexerDeclarationSyntax indexerDeclaration when IsVisible(indexerDeclaration.Modifiers):
                    lines.Add(Indent(indent) + HeaderBeforeBody(indexerDeclaration, source));
                    break;
                case EventDeclarationSyntax eventDeclaration when IsVisible(eventDeclaration.Modifiers):
                    lines.Add(Indent(indent) + HeaderBeforeBody(eventDeclaration, source));
                    break;
                case EventFieldDeclarationSyntax eventFieldDeclaration when IsVisible(eventFieldDeclaration.Modifiers):
                    lines.Add(Indent(indent) + Normalize(eventFieldDeclaration.ToString()).TrimEnd(';'));
                    break;
                case FieldDeclarationSyntax fieldDeclaration when IsVisible(fieldDeclaration.Modifiers):
                    lines.Add(Indent(indent) + Normalize(fieldDeclaration.ToString()).TrimEnd(';'));
                    break;
                case OperatorDeclarationSyntax operatorDeclaration when IsVisible(operatorDeclaration.Modifiers):
                    lines.Add(Indent(indent) + HeaderBeforeBody(operatorDeclaration, source));
                    break;
                case ConversionOperatorDeclarationSyntax conversionOperator when IsVisible(conversionOperator.Modifiers):
                    lines.Add(Indent(indent) + HeaderBeforeBody(conversionOperator, source));
                    break;
            }
        }
    }

    private static bool IsVisible(SyntaxTokenList modifiers)
    {
        return modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PublicKeyword) || modifier.IsKind(SyntaxKind.ProtectedKeyword) || modifier.IsKind(SyntaxKind.InternalKeyword));
    }

    private static string HeaderBeforeBody(MemberDeclarationSyntax declaration, string source)
    {
        var value = source[declaration.SpanStart..declaration.Span.End];
        var bodyStart = value.IndexOf('{');
        var expressionBodyStart = value.IndexOf("=>", StringComparison.Ordinal);
        var end = bodyStart >= 0 && expressionBodyStart >= 0
            ? Math.Min(bodyStart, expressionBodyStart)
            : Math.Max(bodyStart, expressionBodyStart);
        var header = end >= 0 ? value[..end] : value.TrimEnd(';');
        return Normalize(header).TrimEnd(';');
    }

    private static string Normalize(string value)
    {
        return string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string Indent(int depth)
    {
        return new string(' ', depth * 4);
    }
}
