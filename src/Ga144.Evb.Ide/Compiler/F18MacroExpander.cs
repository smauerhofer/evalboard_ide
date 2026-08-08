using System.Text;

namespace Ga144.Evb.Ide.Compiler;

internal sealed class F18MacroExpansionResult
{
    public required string Source { get; init; }
}

internal static class F18MacroExpander
{
    private const int MaximumNestingDepth = 64;
    private const int MaximumExpandedLength = 1_000_000;

    public static F18MacroExpansionResult Expand(
        string source,
        F18CompilerOptions options,
        List<F18Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var stack = new List<string>();
        string expanded = ExpandSource(
            source ?? string.Empty,
            options.MacroLookupScope,
            "node source",
            options,
            diagnostics,
            stack);

        if (expanded.Length > MaximumExpandedLength)
        {
            diagnostics.Add(new F18Diagnostic(
                F18DiagnosticSeverity.Error,
                "F18P008",
                $"Expanded source exceeds the {MaximumExpandedLength:N0}-character safety limit.",
                new F18SourceLocation(1, 1)));
            expanded = expanded[..MaximumExpandedLength];
        }

        return new F18MacroExpansionResult { Source = expanded };
    }

    private static string ExpandSource(
        string source,
        F18MacroLookupScope scope,
        string sourceName,
        F18CompilerOptions options,
        List<F18Diagnostic> diagnostics,
        List<string> stack)
    {
        if (stack.Count >= MaximumNestingDepth)
        {
            diagnostics.Add(new F18Diagnostic(
                F18DiagnosticSeverity.Error,
                "F18P001",
                $"Macro nesting exceeds {MaximumNestingDepth} levels while expanding {sourceName}.",
                new F18SourceLocation(1, 1)));
            return string.Empty;
        }

        // The final compiler tokenization reports lexical diagnostics. This scan is
        // only for locating textual macro-import spans, so its diagnostics are not
        // added a second time.
        var tokenDiagnostics = new List<F18Diagnostic>();
        IReadOnlyList<F18Token> tokens = F18Tokenizer.Tokenize(source, tokenDiagnostics);

        if (tokens.Count < 2)
        {
            return source;
        }

        var output = new StringBuilder(source.Length);
        int cursor = 0;

        for (int index = 0; index < tokens.Count - 1; index++)
        {
            F18Token macroToken = tokens[index];
            F18Token importToken = tokens[index + 1];
            if (!importToken.Text.Equals("import", StringComparison.OrdinalIgnoreCase) ||
                IsDecimalToken(macroToken.Text))
            {
                continue;
            }

            output.Append(source, cursor, macroToken.StartIndex - cursor);

            if (options.MacroResolver is null)
            {
                AddError(
                    diagnostics,
                    "F18P002",
                    $"Macro '{macroToken.Text}' cannot be imported because no macro resolver is available.",
                    macroToken.Location,
                    sourceName);
                cursor = importToken.StartIndex + importToken.Length;
                index++;
                continue;
            }

            F18MacroResolution resolution;
            try
            {
                resolution = options.MacroResolver(macroToken.Text, scope);
            }
            catch (Exception exception)
            {
                AddError(
                    diagnostics,
                    "F18P003",
                    $"Macro '{macroToken.Text}' import failed: {exception.Message}",
                    macroToken.Location,
                    sourceName);
                cursor = importToken.StartIndex + importToken.Length;
                index++;
                continue;
            }

            if (!resolution.Success || resolution.SourceCode is null)
            {
                AddError(
                    diagnostics,
                    "F18P004",
                    $"Macro '{macroToken.Text}' import failed: {resolution.ErrorMessage ?? "unknown macro"}",
                    macroToken.Location,
                    sourceName);
                cursor = importToken.StartIndex + importToken.Length;
                index++;
                continue;
            }

            string resolvedName = string.IsNullOrWhiteSpace(resolution.Name)
                ? macroToken.Text
                : resolution.Name.Trim();
            string identity = $"{resolution.Kind}:{resolvedName}";
            int cycleStart = stack.FindIndex(item => string.Equals(item, identity, StringComparison.OrdinalIgnoreCase));
            if (cycleStart >= 0)
            {
                string cycle = string.Join(
                    " -> ",
                    stack.Skip(cycleStart).Append(identity).Select(FormatIdentity));
                AddError(
                    diagnostics,
                    "F18P005",
                    $"Cyclic macro import detected: {cycle}.",
                    macroToken.Location,
                    sourceName);
                cursor = importToken.StartIndex + importToken.Length;
                index++;
                continue;
            }

            F18MacroLookupScope nestedScope = resolution.Kind == F18MacroKind.System
                ? F18MacroLookupScope.SystemOnly
                : F18MacroLookupScope.UserAndSystem;

            stack.Add(identity);
            string nested;
            try
            {
                nested = ExpandSource(
                    resolution.SourceCode,
                    nestedScope,
                    $"{resolution.Kind.ToString().ToLowerInvariant()} macro '{resolvedName}'",
                    options,
                    diagnostics,
                    stack);
            }
            finally
            {
                stack.RemoveAt(stack.Count - 1);
            }

            output.AppendLine();
            output.Append("\\ begin ");
            output.Append(resolution.Kind.ToString().ToLowerInvariant());
            output.Append(" macro ");
            output.AppendLine(resolvedName);
            output.AppendLine(nested);
            output.Append("\\ end macro ");
            output.AppendLine(resolvedName);

            cursor = importToken.StartIndex + importToken.Length;
            index++;

            if (output.Length > MaximumExpandedLength)
            {
                AddError(
                    diagnostics,
                    "F18P006",
                    $"Macro expansion exceeded the {MaximumExpandedLength:N0}-character safety limit.",
                    macroToken.Location,
                    sourceName);
                break;
            }
        }

        if (cursor == 0)
        {
            return source;
        }

        if (cursor < source.Length)
        {
            output.Append(source, cursor, source.Length - cursor);
        }

        return output.ToString();
    }

    private static void AddError(
        List<F18Diagnostic> diagnostics,
        string code,
        string message,
        F18SourceLocation location,
        string sourceName)
    {
        diagnostics.Add(new F18Diagnostic(
            F18DiagnosticSeverity.Error,
            code,
            sourceName == "node source" ? message : $"In {sourceName}: {message}",
            location));
    }

    private static bool IsDecimalToken(string text) =>
        !string.IsNullOrWhiteSpace(text) && text.All(char.IsDigit);

    private static string FormatIdentity(string identity)
    {
        int separator = identity.IndexOf(':');
        return separator < 0 ? identity : identity[(separator + 1)..];
    }
}
