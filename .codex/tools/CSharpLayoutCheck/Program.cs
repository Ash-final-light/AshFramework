using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class Program
{
    private static int Main(string[] arguments)
    {
        if (arguments.Length == 0)
        {
            Console.Error.WriteLine("Provide explicit C# files to check.");
            return 2;
        }

        int errors = 0;
        foreach (string argument in arguments)
        {
            string path = Path.GetFullPath(argument);
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"Missing source: {path}");
                return 2;
            }

            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path);
            var root = tree.GetRoot();
            foreach (var diagnostic in tree.GetDiagnostics())
            {
                if (diagnostic.Severity == DiagnosticSeverity.Error)
                {
                    Console.Error.WriteLine(diagnostic);
                    errors++;
                }
            }

            foreach (var node in root.DescendantNodes())
            {
                switch (node)
                {
                    case BlockSyntax block:
                        errors += CheckBraces(block.OpenBraceToken, block.CloseBraceToken);
                        break;
                    case BaseTypeDeclarationSyntax type:
                        errors += CheckBraces(type.OpenBraceToken, type.CloseBraceToken);
                        break;
                    case NamespaceDeclarationSyntax declaration:
                        errors += CheckBraces(declaration.OpenBraceToken, declaration.CloseBraceToken);
                        break;
                    case SwitchStatementSyntax statement:
                        errors += CheckBraces(statement.OpenBraceToken, statement.CloseBraceToken);
                        break;
                }

                StatementSyntax? body = node switch
                {
                    IfStatementSyntax statement => statement.Statement,
                    ElseClauseSyntax clause when clause.Statement is not IfStatementSyntax => clause.Statement,
                    ForStatementSyntax statement => statement.Statement,
                    CommonForEachStatementSyntax statement => statement.Statement,
                    WhileStatementSyntax statement => statement.Statement,
                    DoStatementSyntax statement => statement.Statement,
                    UsingStatementSyntax statement => statement.Statement,
                    LockStatementSyntax statement => statement.Statement,
                    _ => null
                };

                if (body != null && body is not BlockSyntax)
                {
                    Report(body.GetFirstToken(), "Control-flow statements must use an expanded brace block.");
                    errors++;
                }
            }

            var leaves = root.DescendantNodes().OfType<StatementSyntax>().Where(IsExecutableStatement);
            foreach (var group in leaves.GroupBy(statement => Line(statement.GetFirstToken())))
            {
                if (group.Count() > 1)
                {
                    Report(group.First().GetFirstToken(), "Use one executable statement per line.");
                    errors++;
                }
            }
        }

        Console.WriteLine($"Checked {arguments.Length} C# file(s); {errors} layout error(s).");
        return errors == 0 ? 0 : 1;
    }

    private static int CheckBraces(SyntaxToken open, SyntaxToken close)
    {
        if (open.RawKind == 0 || close.RawKind == 0 || open.IsMissing || close.IsMissing)
        {
            return 0;
        }

        int errors = 0;
        var previous = open.GetPreviousToken();
        var next = open.GetNextToken();
        if (EndLine(previous) == Line(open) || Line(next) == Line(open))
        {
            Report(open, "Opening block braces must occupy their own line (Allman); no single-line blocks.");
            errors++;
        }

        previous = close.GetPreviousToken();
        next = close.GetNextToken();
        bool allowedSuffix = next.IsKind(SyntaxKind.SemicolonToken) ||
            next.IsKind(SyntaxKind.CommaToken) || next.IsKind(SyntaxKind.CloseParenToken) ||
            next.IsKind(SyntaxKind.CloseBracketToken) || next.IsKind(SyntaxKind.EndOfFileToken);
        if (EndLine(previous) == Line(close) || (Line(next) == Line(close) && !allowedSuffix))
        {
            Report(close, "Closing block braces must be on a separate line from executable statements.");
            errors++;
        }

        return errors;
    }

    private static bool IsExecutableStatement(StatementSyntax statement)
    {
        return statement is ExpressionStatementSyntax or LocalDeclarationStatementSyntax or
            ReturnStatementSyntax or ThrowStatementSyntax or BreakStatementSyntax or
            ContinueStatementSyntax or YieldStatementSyntax;
    }

    private static int Line(SyntaxToken token)
    {
        return token.GetLocation().GetLineSpan().StartLinePosition.Line;
    }

    private static int EndLine(SyntaxToken token)
    {
        return token.GetLocation().GetLineSpan().EndLinePosition.Line;
    }

    private static void Report(SyntaxToken token, string message)
    {
        var location = token.GetLocation().GetLineSpan();
        Console.Error.WriteLine($"{location.Path}({location.StartLinePosition.Line + 1}): LAYOUT001: {message}");
    }
}
