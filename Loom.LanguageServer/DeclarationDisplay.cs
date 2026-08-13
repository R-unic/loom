using Loom.Core.Parsing.AST;
using Loom.Core.Resolving.Symbols;
using Loom.Core.Text;
using Loom.Core.TypeChecking.Types;
using Attribute = Loom.Core.Parsing.AST.Attribute;
using AstFunctionType = Loom.Core.Parsing.AST.FunctionType;
using FunctionType = Loom.Core.TypeChecking.Types.FunctionType;
using IntersectionType = Loom.Core.TypeChecking.Types.IntersectionType;
using LoomSymbolKind = Loom.Core.Resolving.Symbols.SymbolKind;
using OptionalType = Loom.Core.TypeChecking.Types.OptionalType;
using Parameter = Loom.Core.Parsing.AST.Parameter;
using Type = Loom.Core.TypeChecking.Types.Type;
using TypeParameters = Loom.Core.Parsing.AST.TypeParameters;

namespace Loom.LanguageServer;

/// <summary>One parameter of a rendered signature, and where its text sits inside the signature's label.</summary>
public sealed record ParameterDisplay(string Name, int Start, int End)
{
    /// <summary>The doc comment written directly above the parameter, for a declaration spelling its parameters over several lines.</summary>
    public string? Documentation { get; init; }
}

/// <summary>A callable's signature as one line, with each parameter's slice of it located.</summary>
public sealed record SignatureDisplay(string Label, IReadOnlyList<ParameterDisplay> Parameters, bool HasRestParameter)
{
    /// <summary>Whether a call passing <paramref name="argumentCount" /> arguments could be this signature.</summary>
    public bool Accepts(int argumentCount) => HasRestParameter ? argumentCount >= Parameters.Count - 1 : argumentCount <= Parameters.Count;
}

/// <summary>
///     Renders a symbol the way its declaration reads, rather than as the bare type the type checker settled
///     on. <c>fn add(x: number, y: number): number</c> answers questions that <c>fn(number, number): number</c>
///     does not - what the parameters are called, whether the binding is mutable, which keyword declared it.
/// </summary>
public static class DeclarationDisplay
{
    /// <summary>The header line shown above a symbol's documentation on hover.</summary>
    public static string Of(Symbol symbol, Type? type)
    {
        if (IsCallable(symbol.Declaration) && CallSignatures([symbol], type, symbol.Name) is [var signature, ..])
            return signature.Label;

        return symbol.Declaration switch
        {
            EventDeclaration eventDeclaration => $"event {symbol.Name}{Render(eventDeclaration.TypeParameters)}({RenderEventParameters(eventDeclaration, type)})",
            InterfaceDeclaration @interface =>
                $"{(@interface.SealedKeyword == null ? "" : "sealed ")}interface {symbol.Name}{Render(@interface.TypeParameters)}",
            TraitDeclaration trait => $"trait {symbol.Name}{Render(trait.TypeParameters)}",
            EnumDeclaration => $"enum {symbol.Name}",
            // an alias reads as what was written on its right-hand side; the type it resolves to has already
            // substituted the parameters away, which turns 'T[]' into the alias's own instantiation
            TypeAlias alias => $"type {symbol.Name}{Render(alias.TypeParameters)} = {alias.EqualsTypeClause.Type}",
            Parameter parameter => $"{(parameter.DotDot == null ? "" : "..")}{symbol.Name}: {TypeText(type)}",
            // A binder reads as the pattern position it was written in, not as what it resolved to: inside
            // the arm it stands for whatever the subject had there, and that differs per instantiation.
            InferType binder => $"let {symbol.Name}{(binder.ColonTypeClause == null ? "" : $": {binder.ColonTypeClause.Type}")}",
            MappedTypeDeclaration mapped => $"{symbol.Name} from {mapped.SourceType}",
            DeclareVariableSignature variable => $"{variable.Keyword.Text} {symbol.Name}: {TypeText(type)}",
            PropertyDeclaration property => $"{(property.MutKeyword == null ? "" : "mut ")}{symbol.Name}: {TypeText(type)}",
            _ => FallbackSignature(symbol, type)
        };
    }

    /// <summary>
    ///     The signatures a call to <paramref name="type" /> may match, in declaration order. An overload set
    ///     is an intersection of function types (see <c>MergeOverloadedProperties</c>), so a callee with
    ///     several shapes arrives here as one type; <paramref name="symbols" /> is where its parameter names
    ///     survive, one symbol per shape, and is paired positionally when the two agree on how many there are.
    /// </summary>
    public static IReadOnlyList<SignatureDisplay> CallSignatures(IReadOnlyList<Symbol> symbols, Type? type, string name)
    {
        var functions = FunctionTypesOf(type);
        var paired = symbols.Count == functions.Count;
        return functions
            .Select((function, index) => Render(DeclaredParametersOf(symbols, paired ? index : 0), function, name))
            .ToArray();
    }

    private static DeclaredParameters? DeclaredParametersOf(IReadOnlyList<Symbol> symbols, int index) =>
        index < symbols.Count ? DeclaredParametersOf(symbols[index].Declaration) : null;

    /// <summary>
    ///     The signature shown beside a completion label. The label already is the name, and the client prints
    ///     the two run together, so everything up to and including the name is dropped: <c>add</c> reads
    ///     <c>(x: number): number</c> and <c>total</c> reads <c>: number</c>.
    /// </summary>
    public static string CompletionDetail(Symbol symbol, Type? type)
    {
        var signature = Of(symbol, type);
        var name = signature.IndexOf(symbol.Name, StringComparison.Ordinal);
        var detail = name < 0 ? $" {signature}" : signature[(name + symbol.Name.Length)..];
        return detail.Trim() is "" or "]" ? "" : detail;
    }

    /// <summary>The attribute list written above a declaration, as source text, or null when it has none.</summary>
    public static string? AttributesOf(Symbol symbol) =>
        (symbol.Declaration as IWithAttributes)?.Attributes is { AttributeList.Count: > 0 } attributes ? attributes.ToString() : null;

    /// <summary>
    ///     The <c>[deprecated]</c> notice for a symbol, message included when the attribute carries one, or
    ///     null when it is not deprecated. The type checker warns at the use site; hover says so before one
    ///     is written.
    /// </summary>
    public static string? DeprecationOf(Symbol symbol)
    {
        if ((symbol.Declaration as IWithAttributes)?.Attributes is not { } attributes)
            return null;

        var deprecation = attributes.AttributeList.Find(attribute => NameOf(attribute) == "deprecated");
        if (deprecation == null)
            return null;

        return deprecation.Arguments.ArgumentList is [Literal { Value: string message }] ? $"**Deprecated** — {message}" : "**Deprecated**";
    }

    /// <summary>The parameter list and type parameters a declaration was written with, wherever it keeps them.</summary>
    private sealed record DeclaredParameters(TypeParameters? TypeParameters, IReadOnlyList<Parameter> Parameters);

    /// <summary>
    ///     Whether a declaration reads better as <c>fn name(x: T): R</c> than as <c>name: fn(T): R</c>. A
    ///     function-typed interface member does - it is called, not read - but a variable holding a function
    ///     does not: what matters there is that it is a binding, and whether it can be reassigned.
    /// </summary>
    private static bool IsCallable(Node declaration) =>
        declaration is DeclareFunctionSignature or PropertyDeclaration { ColonTypeClause.Type: AstFunctionType };

    private static DeclaredParameters? DeclaredParametersOf(Node declaration) =>
        declaration switch
        {
            DeclareFunctionSignature function => new DeclaredParameters(function.TypeParameters, function.Parameters?.ParameterList ?? []),
            PropertyDeclaration { ColonTypeClause.Type: AstFunctionType annotation } => FromAnnotation(annotation),
            DeclareVariableSignature { ColonTypeClause.Type: AstFunctionType annotation } => FromAnnotation(annotation),
            Parameter { ColonTypeClause.Type: AstFunctionType annotation } => FromAnnotation(annotation),
            _ => null
        };

    private static DeclaredParameters FromAnnotation(AstFunctionType annotation) =>
        new(annotation.TypeParameters, annotation.Parameters?.ParameterList ?? []);

    /// <summary>Every function shape a callee may have: one for a plain function, several for an overload set.</summary>
    private static List<FunctionType> FunctionTypesOf(Type? type) =>
        type switch
        {
            FunctionType function => [function],
            IntersectionType intersection => intersection.Types.OfType<FunctionType>().ToList(),
            InstantiatedType instantiated => FunctionTypesOf(instantiated.Expand()),
            OptionalType optional => FunctionTypesOf(optional.NonNullableType),
            _ => []
        };

    private static SignatureDisplay Render(DeclaredParameters? declared, FunctionType function, string name)
    {
        var declaredParameters = declared?.Parameters ?? [];

        // 'async' belongs in the header for the same reason the return type does: it is what a caller has to
        // know before writing the call, since what comes back is a Future rather than the return type
        var label = $"{(function.IsAsync ? "async " : "")}fn {name}{Render(declared?.TypeParameters, function)}(";
        var parameters = new List<ParameterDisplay>();

        for (var index = 0; index < function.ParameterTypes.Count; index++)
        {
            if (index > 0)
                label += ", ";

            var parameter = index < declaredParameters.Count ? declaredParameters[index] : null;
            var isRest = function.HasRestParameter && index == function.ParameterTypes.Count - 1;
            var parameterName = parameter?.Name.Text ?? $"arg{index + 1}";
            var text = $"{(isRest ? ".." : "")}{parameterName}: {TypeText(function.ParameterTypes[index])}";

            parameters.Add(new ParameterDisplay(parameterName, label.Length, label.Length + text.Length) { Documentation = parameter?.Documentation });
            label += text;
        }

        return new SignatureDisplay($"{label}): {TypeText(function.ReturnType)}", parameters, function.HasRestParameter);
    }

    /// <summary>An event's parameters, which have no return type and so are not rendered as a call signature.</summary>
    private static string RenderEventParameters(EventDeclaration declaration, Type? type)
    {
        var declared = declaration.Parameters?.ParameterList ?? [];
        var types = FunctionTypesOf(type) is [var function, ..] ? function.ParameterTypes : [];
        return string.Join(
            ", ",
            declared.Select((parameter, index) => $"{parameter.Name.Text}: {(index < types.Count ? TypeText(types[index]) : AnnotationText(parameter))}")
        );
    }

    /// <summary>The keyword-and-type form for a symbol whose declaration node says nothing more than its kind does.</summary>
    private static string FallbackSignature(Symbol symbol, Type? type) =>
        symbol.Kind switch
        {
            LoomSymbolKind.Function => $"fn {symbol.Name}: {TypeText(type)}",
            LoomSymbolKind.Attribute => $"[{symbol.Name}]",
            _ when symbol.IsTypeSymbol => $"type {symbol.Name} = {TypeText(type)}",
            _ => $"{(symbol.IsMutable ? "mut" : "let")} {symbol.Name}: {TypeText(type)}"
        };

    private static string Render(TypeParameters? typeParameters, FunctionType? function = null)
    {
        if (typeParameters is { ParameterList.Count: > 0 })
            return $"<{string.Join(", ", typeParameters.ParameterList.Select(parameter => parameter.Name.Text))}>";

        return function is { TypeParameters.Count: > 0 } ? $"<{string.Join(", ", function.TypeParameters)}>" : "";
    }

    private static string? NameOf(Attribute attribute) => attribute.Name;
    private static string AnnotationText(Parameter parameter) => parameter.ColonTypeClause?.Type.ToString() ?? "unknown";
    private static string TypeText(Type? type) => type?.ToString() ?? "unknown";
}
