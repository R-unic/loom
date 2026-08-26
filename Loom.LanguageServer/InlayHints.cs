using Loom.Core.Parsing.AST;
using Loom.Core.Pipeline;
using Loom.Core.Resolving;
using Loom.Core.Text;
using Loom.Core.TypeChecking.Types;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Attribute = Loom.Core.Parsing.AST.Attribute;
using FunctionType = Loom.Core.TypeChecking.Types.FunctionType;
using Location = Loom.Core.Text.Location;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.LanguageServer;

/// <summary>
///     The types and names the source leaves out but the compiler knows. Loom infers freely - a binding needs
///     no annotation and a call names no parameters - which reads well while writing and poorly while reading;
///     these put the inferred half back without putting it in the file.
/// </summary>
public static class InlayHints
{
    public static IReadOnlyList<InlayHint> Of(CompiledFile file, TextSpan range)
    {
        var hints = new List<InlayHint>();
        var semanticModel = file.SemanticModel;
        foreach (var node in file.Tree.EnumerateDescendants())
        {
            if (!Overlaps(node.Span, range)) continue;

            switch (node)
            {
                case VariableDeclaration { ColonTypeClause: null } variable:
                    AddTypeHint(hints, semanticModel, variable, variable.Name);
                    break;
                case FunctionDeclaration { ReturnType: null, Parameters: { } parameters } function:
                    AddReturnTypeHint(hints, semanticModel, function, parameters.RightParen);
                    break;
                case Invocation invocation:
                    AddParameterNameHints(hints, semanticModel, invocation);
                    break;
            }
        }

        return hints;
    }

    /// <summary>The type an unannotated binding was inferred to have, written where its annotation would go.</summary>
    private static void AddTypeHint(List<InlayHint> hints, SemanticModel semanticModel, Node declaration, Token name)
    {
        if (TypeOf(semanticModel, declaration) is not { } type)
            return;

        hints.Add(
            new InlayHint
            {
                Position = Conversion.ToPosition(new Location(declaration.File, name.Span.End)),
                Label = new StringOrInlayHintLabelParts($": {type}"),
                Kind = InlayHintKind.Type,
                // inserting exactly what is displayed turns the hint into the annotation the reader wanted
                TextEdits = new Container<TextEdit>(
                    new TextEdit { Range = EmptyRangeAt(declaration.File, name.Span.End), NewText = $": {type}" }
                ),
                PaddingLeft = false,
                PaddingRight = false
            }
        );
    }

    private static void AddReturnTypeHint(List<InlayHint> hints, SemanticModel semanticModel, FunctionDeclaration function, Token rightParen)
    {
        if (TypeOf(semanticModel, function) is not FunctionType signature)
            return;

        AddReturnHint(hints, function.File, rightParen.Span.End, signature.ReturnType);
    }

    private static void AddReturnHint(List<InlayHint> hints, SourceFile file, int position, Type returnType) =>
        hints.Add(
            new InlayHint
            {
                Position = Conversion.ToPosition(new Location(file, position)),
                Label = new StringOrInlayHintLabelParts($": {returnType}"),
                Kind = InlayHintKind.Type,
                TextEdits = new Container<TextEdit>(new TextEdit { Range = EmptyRangeAt(file, position), NewText = $": {returnType}" }),
                PaddingLeft = false,
                PaddingRight = false
            }
        );

    /// <summary>
    ///     The name of the parameter each argument is being passed as. Skipped when the argument is already
    ///     that name - <c>move(x, y)</c> gains nothing from being told it is passing <c>x</c> as <c>x</c>.
    /// </summary>
    private static void AddParameterNameHints(List<InlayHint> hints, SemanticModel semanticModel, Invocation invocation)
    {
        if (invocation is Attribute { IsInvoked: false })
            return;

        var callee = invocation.Expression;
        var symbols = CallSiteFinder.SymbolsOf(callee, semanticModel);
        var arguments = invocation.Arguments.ArgumentList;
        var signatures = DeclarationDisplay.CallSignatures(symbols, TypeOf(semanticModel, callee), CallSiteFinder.NameOf(callee, symbols.FirstOrDefault()));
        if (BestFit(signatures, arguments.Count) is not { } signature)
            return;

        for (var index = 0; index < arguments.Count && index < signature.Parameters.Count; index++)
        {
            // a rest parameter swallows every argument from its position on, and they are not all "..values"
            if (signature.HasRestParameter && index == signature.Parameters.Count - 1)
                break;

            // Already named at the call site, and not necessarily the parameter at this position anyway -
            // a named argument can reorder past where its own list position would otherwise place it.
            if (arguments[index] is NamedArgument)
                continue;

            var parameter = signature.Parameters[index];
            var argument = arguments[index];
            if (parameter.Name.Length == 0 || RepeatsTheName(argument, parameter.Name))
                continue;

            hints.Add(
                new InlayHint
                {
                    Position = Conversion.ToPosition(new Location(argument.File, argument.Span.Position)),
                    Label = new StringOrInlayHintLabelParts($"{parameter.Name}:"),
                    Kind = InlayHintKind.Parameter,
                    PaddingLeft = false,
                    PaddingRight = true
                }
            );
        }
    }

    /// <summary>The overload the call's argument count fits, or the only one when it fits none.</summary>
    private static SignatureDisplay? BestFit(IReadOnlyList<SignatureDisplay> signatures, int argumentCount) =>
        signatures.FirstOrDefault(signature => signature.Accepts(argumentCount)) ?? signatures.FirstOrDefault();

    /// <summary>Whether the argument is written as the parameter's own name, which a hint would only repeat.</summary>
    private static bool RepeatsTheName(Expression argument, string parameterName) =>
        argument switch
        {
            Identifier identifier => identifier.Name.Text == parameterName,
            QualifiedName { Names: [.., var last] } => last.Name.Text == parameterName,
            PropertyAccess { Names: [.., var last] } => last.Name.Text == parameterName,
            _ => false
        };

    private static LspRange EmptyRangeAt(SourceFile file, int position)
    {
        var at = Conversion.ToPosition(new Location(file, position));
        return new LspRange(at, at);
    }

    private static bool Overlaps(TextSpan span, TextSpan range) => span.Position <= range.End && span.End >= range.Position;

    private static Type? TypeOf(SemanticModel semanticModel, Node node)
    {
        try
        {
            var type = semanticModel.GetType(node);
            return type is TypeVariable ? null : type;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
