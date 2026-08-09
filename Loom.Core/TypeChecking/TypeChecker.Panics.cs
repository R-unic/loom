using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Text;
using Loom.Core.TypeChecking.Types;

namespace Loom.Core.TypeChecking;

using Type = Types.Type;
using Attribute = Loom.Core.Parsing.AST.Attribute;

/// <summary>
///     Enforces that an operation which can raise a Luau error only appears inside a function that
///     declared it can, so a signature never hides the fact that calling it may end the thread.
/// </summary>
/// <remarks>
///     Because <c>[fallible]</c> is declared rather than inferred, this is a purely local check: at
///     each call site, ask whether the callee panics and whether the enclosing function carries the
///     attribute. No call graph and no inference are involved.
/// </remarks>
public sealed partial class TypeChecker
{
    private const string FallibleAttribute = "fallible";
    private static readonly HashSet<string> _panickingResultMembers = ["unwrap", "expect"];

    private void CheckPanicIsDeclared(Invocation invocation)
    {
        if (PanickingOperationName(invocation) is not { } operation)
            return;

        var enclosing = EnclosingFallibleCandidate(invocation);
        if (enclosing != null && HasAttribute(enclosing, FallibleAttribute))
            return;

        if (enclosing == null)
        {
            _diagnostics.Error(
                invocation,
                InternalCodes.PanicOutsideFallibleFunction,
                $"'{operation}' can panic, and this code cannot recover from it.",
                "handle the error instead - 'match', 'unwrap_or', or move this into a function returning 'Result<T, Error>'"
            );

            return;
        }

        var name = enclosing.Name.Text;
        _diagnostics.Error(
            invocation,
            InternalCodes.PanicOutsideFallibleFunction,
            $"'{operation}' can panic, but '{name}' is not marked '[fallible]'.",
            $"return a 'Result<T, Error>' and propagate with '?', or mark '{name}' with '[fallible]' if you really need to panic"
        );
    }

    private string? PanickingOperationName(Invocation invocation)
    {
        if (TryGetMemberCall(invocation, out var receiver, out var member)
            && _panickingResultMembers.Contains(member)
            && IsResultType(_semanticModel.GetType(receiver)))
            return member;

        if (_semanticModel.GetSymbol(invocation.Expression) is not { } symbol)
            return null;

        if (symbol is { Name: "error", IsIntrinsic: true })
            return "error";

        return HasAttribute(symbol.Declaration, FallibleAttribute) ? symbol.Name : null;
    }

    private static bool TryGetMemberCall(Invocation invocation, out Expression receiver, out string member)
    {
        switch (invocation.Expression)
        {
            case QualifiedName { Names.Count: > 0 } qualified:
                receiver = qualified.Names.Count == 1 ? qualified.Identifier : qualified;
                member = qualified.Names[^1].Name.Text;
                return true;
            case PropertyAccess { Names.Count: > 0 } access:
                receiver = access.Expression;
                member = access.Names[^1].Name.Text;
                return true;
            default:
                receiver = null!;
                member = "";
                return false;
        }
    }

    /// <returns>
    ///     The named function the panic would be attributed to, or <see langword="null" /> when there
    ///     is none to mark: module top level, or a function expression. An event handler is anonymous
    ///     and runs on a thread Roblox owns, so it has no caller to propagate to and is treated the
    ///     same way as top-level code.
    /// </returns>
    private static FunctionDeclaration? EnclosingFallibleCandidate(Node node)
    {
        for (var current = node.Parent; current != null; current = current.Parent)
            switch (current)
            {
                case FunctionExpression:
                    return null;
                case FunctionDeclaration declaration:
                    return declaration;
            }

        return null;
    }

    private static bool HasAttribute(Node? declaration, string name) =>
        declaration is IWithAttributes { Attributes: { } attributes }
        && attributes.AttributeList.Exists(attribute => AttributeName(attribute) == name);

    private static string? AttributeName(Attribute attribute) =>
        attribute.Expression.Tokens.LastOrDefault(token => token.Kind == SyntaxKind.Identifier)?.Text;

    private static bool IsResultType(Type type) => IsResultType(type, 0);

    private static bool IsResultType(Type type, int depth) =>
        depth < 4
        && type switch
        {
            InterfaceType { Name: "ResultOk" or "ResultError" } => true,
            InstantiatedType instantiated => IsResultType(instantiated.Expand(), depth + 1),
            Types.UnionType union => union.Types.Count > 0 && union.Types.TrueForAll(member => IsResultType(member, depth + 1)),
            _ => false
        };
}
