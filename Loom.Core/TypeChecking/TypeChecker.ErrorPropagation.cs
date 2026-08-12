using System.Diagnostics.CodeAnalysis;
using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.TypeChecking.Types;

namespace Loom.Core.TypeChecking;

using Type = Types.Type;

public sealed partial class TypeChecker
{
    public override Type VisitErrorPropagation(ErrorPropagation errorPropagation)
    {
        var operandType = Visit(errorPropagation.Expression);
        if (!TryGetResultTypeArguments(operandType, out var valueType, out var errorType))
        {
            // '?' written straight onto an un-awaited call is the common shape of this mistake, and the
            // Result it is reaching for is one 'await' away
            if (IsFutureType(errorPropagation, operandType))
                _diagnostics.Error(
                    errorPropagation,
                    InternalCodes.UnawaitedFutureAccess,
                    $"The '?' operator cannot be used on '{operandType}' - the Result is inside the future, not on it.",
                    "await the call first: 'await expression?' already means '(await expression)?'"
                );
            else
                _diagnostics.Error(
                    errorPropagation,
                    InternalCodes.ErrorPropagationRequiresResultType,
                    $"The '?' operator can only be used on a value of type 'Result<T, E>', but got '{operandType}'."
                );

            return BindType(errorPropagation, Types.PrimitiveType.Never);
        }

        var enclosingReturnType = GetEnclosingDeclaredReturnType(errorPropagation);
        if (enclosingReturnType == null || !TryGetResultTypeArguments(enclosingReturnType, out _, out var enclosingErrorType))
        {
            _diagnostics.Error(
                errorPropagation,
                InternalCodes.ErrorPropagationOutsideResultFunction,
                "'?' can only be used inside of a function with a declared 'Result<T, E>' return type."
            );

            return BindType(errorPropagation, Types.PrimitiveType.Never);
        }

        if (!errorType.IsAssignableTo(enclosingErrorType))
        {
            _diagnostics.Error(
                errorPropagation,
                InternalCodes.ErrorPropagationErrorTypeMismatch,
                $"Cannot propagate error of type '{errorType}' through '?': the enclosing function's error type is '{enclosingErrorType}'."
            );

            return BindType(errorPropagation, Types.PrimitiveType.Never);
        }

        return BindType(errorPropagation, valueType);
    }

    private static bool TryGetResultTypeArguments(Type type, [MaybeNullWhen(false)] out Type value, [MaybeNullWhen(false)] out Type error)
    {
        if (type is InstantiatedType { GenericType.Declaration: TypeAlias { Name.Text: "Result" }, Arguments.Count: 2 } instantiated)
        {
            value = instantiated.Arguments[0];
            error = instantiated.Arguments[1];
            return true;
        }

        value = null;
        error = null;
        return false;
    }
}
