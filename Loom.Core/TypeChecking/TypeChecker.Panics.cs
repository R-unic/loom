using System.Diagnostics.CodeAnalysis;
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
    private const string DeprecatedAttribute = "deprecated";
    private const string WrapsErrorsAttribute = "wraps_errors";
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

    /// <summary>
    ///     A <c>[wraps_errors]</c> member only becomes a <c>Result</c> because the call site is
    ///     lowered to an xpcall, so a reference to one that is never called is a lie: its type says
    ///     it returns a Result while the underlying Luau function raises.
    /// </summary>
    private void CheckWrappedMemberIsCalled(Expression access)
    {
        if (access.Parent is Invocation invocation && invocation.Expression == access)
            return;

        if (_semanticModel.GetPropertySymbol(access) is not { } property || !property.TryGetIntrinsicAttribute(WrapsErrorsAttribute, out _))
            return;

        _diagnostics.Error(
            access,
            InternalCodes.UncalledWrappedMember,
            $"'{property.Name}' can only be called, not referenced.",
            $"its 'Result' comes from the call being wrapped, so a bare reference would raise instead - wrap it yourself with 'fn(..a) -> {access}(..a)'"
        );
    }

    /// <summary>
    ///     Warns rather than errors: a deprecated member still works, and turning its use into a
    ///     build failure would break projects on a Roblox release rather than on a change of theirs.
    /// </summary>
    private void CheckDeprecation(Invocation invocation)
    {
        var declaration = _semanticModel.GetSymbol(invocation.Expression)?.Declaration
            ?? _semanticModel.GetPropertySymbol(invocation.Expression)?.Declaration;

        if (declaration == null || !TryGetAttribute(declaration, DeprecatedAttribute, out var attribute))
            return;

        var name = _semanticModel.GetSymbol(invocation.Expression)?.Name
            ?? _semanticModel.GetPropertySymbol(invocation.Expression)?.Name
            ?? "member";

        _diagnostics.Warn(
            invocation,
            InternalCodes.DeprecatedMember,
            $"'{name}' is deprecated.",
            DeprecationMessage(attribute)
        );
    }

    private static string? DeprecationMessage(Attribute attribute) =>
        attribute.Arguments.ArgumentList is [Literal { Value: string message }] ? message : null;

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

    private static bool HasAttribute(Node? declaration, string name) => TryGetAttribute(declaration, name, out _);

    private static bool TryGetAttribute(Node? declaration, string name, [MaybeNullWhen(false)] out Attribute found)
    {
        found = declaration is IWithAttributes { Attributes: { } attributes }
            ? attributes.AttributeList.Find(attribute => AttributeName(attribute) == name)
            : null;

        return found != null;
    }

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
