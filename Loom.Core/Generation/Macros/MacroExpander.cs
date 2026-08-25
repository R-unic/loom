using System.Diagnostics.CodeAnalysis;
using Loom.Core.Diagnostics;
using Loom.Core.Generation.Macros.Providers;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Core.TypeChecking;
using Loom.Luau.AST;
using ElementAccess = Loom.Core.Parsing.AST.ElementAccess;
using Identifier = Loom.Core.Parsing.AST.Identifier;
using PropertyAccess = Loom.Core.Parsing.AST.PropertyAccess;
using Type = Loom.Core.TypeChecking.Types.Type;
using FunctionType = Loom.Core.TypeChecking.Types.FunctionType;
using OptionalType = Loom.Core.TypeChecking.Types.OptionalType;
using Return = Loom.Luau.AST.Return;
using Parameter = Loom.Luau.AST.Parameter;
using Loom.Core.TypeChecking.Solving;

namespace Loom.Core.Generation.Macros;

internal sealed class MacroExpander(SemanticModel semanticModel, LuauState state, DiagnosticBag diagnostics)
{
    internal static readonly IReadOnlyCollection<IMacroProvider> Providers =
    [
        new NumberMacroProvider(),
        new RangeMacroProvider(),
        new ArrayMacroProvider(),
        new StringMacroProvider(),
        new InstanceMacroProvider(),
        new ResultStaticMacroProvider(),
        new ResultMacroProvider(),
        new SetStaticMacroProvider(),
        new SetMacroProvider(),
        new FutureStaticMacroProvider(),
        new IntrinsicGlobalInvocationMacroProvider()
    ];

    private readonly MacroContext _context = new(semanticModel, state, diagnostics);

    public bool TryGetInvocationMacro(Invocation invocation, Call luauCall, [MaybeNullWhen(false)] out LuauExpression expression)
    {
        expression = null;
        _context.Node = invocation;
        if (!TryDecomposeInvocationTarget(invocation.Expression, luauCall.Callee, out var provider, out var member))
            return false;

        // A provider is written against the arity/shape its intrinsic declares, which the type checker
        // validates - but a call site that fails that check still reaches here, since diagnostics never
        // stop the pipeline. Falling back to the plain, unexpanded call is exactly what happens whenever
        // no provider matches at all, so it is a safe answer for "this one matched but could not expand" too.
        try
        {
            return provider.TryInvocation(_context, member.Trim(), invocation.TypeArguments, luauCall, out expression);
        }
        catch (Exception)
        {
            expression = null;
            return false;
        }
    }

    public bool TryGetInvocationMacroReference(
        Expression expression,
        LuauExpression callee,
        [MaybeNullWhen(false)] out LuauExpression referenceExpression)
    {
        _context.Node = expression;
        referenceExpression = null;
        if (!InvocationMacroReference.TryClassify(semanticModel, expression, out var provider, out var memberName))
            return false;

        // The callee of a call is being called, not referenced, and TryGetInvocationMacro handles it.
        // IsValidReferenceContext only asks whether some ancestor is an argument list, which is true of
        // 'xs.has(1)' inside 'print(...)' too - so the callee was wrapped in a reference lambda and then
        // expanded again as an invocation on top of it, emitting 'table.find(function(argument0) ... end, 1)'.
        if (InvocationMacroReference.IsDirectInvocationCallee(expression))
            return false;

        if (!InvocationMacroReference.IsValidReferenceContext(expression, semanticModel))
            return false;

        if (semanticModel.GetType(expression) is not FunctionType functionType)
            return false;

        var parameters = functionType.ParameterTypes.Select((_, index) => new Parameter($"argument{index}")).ToList();
        var arguments = parameters.ConvertAll(LuauExpression (parameter) => new Luau.AST.Identifier(parameter.Name));
        var call = new Call(callee, arguments);

        LuauExpression? body = null;
        bool matched;
        LuauScope scope;
        try
        {
            (matched, scope) = state.CaptureIsolatedScope(() => provider.TryInvocation(_context, memberName.Trim(), null, call, out body));
        }
        catch (Exception)
        {
            return false;
        }

        if (!matched || body == null)
            return false;

        referenceExpression = new AnonymousFunction(
            null,
            parameters,
            null,
            new Chunk([..scope.PrereqStatements, new Return(body), ..scope.PostreqStatements])
        );

        return true;
    }

    public bool TryGetElementAccessMacro(ElementAccess access, Luau.AST.ElementAccess luauAccess, [MaybeNullWhen(false)] out LuauExpression expression)
    {
        _context.Node = access;
        if (TryGetEnumConstant(access, out expression))
            return true;

        var targetType = semanticModel.GetType(access.Expression);
        if (GetProvider(access.IndexExpression) is { } provider && TryElementAccess(provider, luauAccess, targetType, out expression))
            return true;

        return semanticModel.GetConstantValue(access.IndexExpression) is string name
            && TryGetNamedAccessMacro(access.Expression, name, luauAccess.Target, out expression);
    }

    public bool TryGetQualifiedNameMacro(QualifiedName name, Luau.AST.PropertyAccess luauAccess, [MaybeNullWhen(false)] out LuauExpression expression) =>
        TryRewriteNamedAccess(name, name.Identifier, name.Names, luauAccess, out expression);

    public bool TryGetPropertyAccessMacro(PropertyAccess access, Luau.AST.PropertyAccess luauAccess, [MaybeNullWhen(false)] out LuauExpression expression) =>
        TryRewriteNamedAccess(access, access.Expression, access.Names, luauAccess, out expression);

    public bool TryGetOptionalChainMemberMacro(
        Expression access,
        Expression rootExpression,
        List<DotName> names,
        LuauExpression receiverTarget,
        [MaybeNullWhen(false)] out LuauExpression expression)
    {
        expression = null;
        if (names.Count == 0)
            return false;

        _context.Node = access;
        var receiverType = semanticModel.GetType(rootExpression);
        for (var i = 0; i < names.Count - 1; i++)
        {
            receiverType = TypeSimplifier.GetMemberPropertyType(receiverType, names[i].Name.Text);
            if (receiverType is null)
                return false;
        }

        return GetProvider(receiverType) is { } provider && TryProperty(provider, names[^1].Name.Text, receiverTarget, out expression);
    }

    private bool TryDecomposeInvocationTarget(
        Expression expression,
        LuauExpression target,
        [MaybeNullWhen(false)] out IMacroProvider provider,
        [MaybeNullWhen(false)] out string memberName)
    {
        switch (expression)
        {
            case Identifier identifier:
            {
                provider = GetProvider(expression);
                memberName = identifier.Name.Text;
                return provider != null;
            }

            case QualifiedName qualified:
            {
                if (!TryResolveMacroReceiver(
                        qualified.Identifier,
                        qualified.Names,
                        target,
                        out provider,
                        out _,
                        out var macroIndex
                    ))
                {
                    memberName = null;
                    return false;
                }

                memberName = qualified.Names[macroIndex].Name.Text;
                return true;
            }

            case PropertyAccess property:
            {
                if (!TryResolveMacroReceiver(
                        property.Expression,
                        property.Names,
                        target,
                        out provider,
                        out _,
                        out var macroIndex
                    ))
                {
                    memberName = null;
                    return false;
                }

                memberName = property.Names[macroIndex].Name.Text;
                return true;
            }

            case ElementAccess element
                when semanticModel.GetConstantValue(element.IndexExpression) is string name:
            {
                provider = GetProvider(element.Expression);
                memberName = name;
                return provider != null;
            }
        }

        provider = null;
        memberName = null;
        return false;
    }

    private bool TryRewriteNamedAccess(
        Expression access,
        Expression receiver,
        List<DotName> names,
        Luau.AST.PropertyAccess luauAccess,
        [MaybeNullWhen(false)] out LuauExpression expression)
    {
        _context.Node = access;
        if (TryGetEnumConstant(access, out expression))
            return true;

        if (!TryResolveMacroReceiver(
                receiver,
                names,
                luauAccess.Target,
                out var provider,
                out var target,
                out var macroIndex
            ))
        {
            expression = null;
            return false;
        }

        if (!TryProperty(provider, names[macroIndex].Name.Text, target, out expression))
            return false;

        if (macroIndex + 1 < names.Count)
            expression = new Luau.AST.PropertyAccess(expression, luauAccess.Names.Skip(macroIndex + 1).ToList());

        return true;
    }

    private bool TryResolveMacroReceiver(
        Expression rootExpression,
        List<DotName> names,
        LuauExpression rootTarget,
        [MaybeNullWhen(false)] out IMacroProvider provider,
        [MaybeNullWhen(false)] out LuauExpression target,
        out int macroIndex)
    {
        provider = null;
        target = null;
        macroIndex = -1;

        var currentType = semanticModel.GetType(rootExpression);
        var currentTarget = rootTarget;
        for (var i = 0; i < names.Count; i++)
        {
            if (GetProvider(currentType) is { } p)
            {
                provider = p;
                target = currentTarget;
                macroIndex = i;
            }

            currentType = TypeSimplifier.GetMemberPropertyType(currentType, names[i].Name.Text);
            if (currentType == null)
                break;

            currentTarget = new Luau.AST.PropertyAccess(currentTarget, [names[i].Name.Text]);
        }

        return provider != null;
    }

    private bool TryGetNamedAccessMacro(Expression objectExpression, string name, LuauExpression target, [MaybeNullWhen(false)] out LuauExpression expression)
    {
        if (GetProvider(objectExpression) is { } provider)
            return TryProperty(provider, name, target, out expression);

        expression = null;
        return false;
    }

    /// <summary>
    ///     A provider is written against the shape its intrinsic declares, which the type checker validates -
    ///     but a call site that fails that check still reaches codegen, since diagnostics never stop the
    ///     pipeline. Every caller already falls back to leaving the access unexpanded when no provider
    ///     matches at all, so treating a provider that throws the same way is a safe answer too.
    /// </summary>
    private bool TryProperty(IMacroProvider provider, string name, LuauExpression target, [MaybeNullWhen(false)] out LuauExpression expression)
    {
        try
        {
            return provider.TryProperty(_context, name, target, out expression);
        }
        catch (Exception)
        {
            expression = null;
            return false;
        }
    }

    private bool TryElementAccess(IMacroProvider provider, Luau.AST.ElementAccess luauAccess, Type targetType, [MaybeNullWhen(false)] out LuauExpression expression)
    {
        try
        {
            return provider.TryElementAccess(_context, luauAccess, targetType, out expression);
        }
        catch (Exception)
        {
            expression = null;
            return false;
        }
    }

    private bool TryGetEnumConstant(Expression expression, [MaybeNullWhen(false)] out LuauExpression constantValue)
    {
        constantValue = null;
        var value = semanticModel.GetConstantValue(expression);
        if (value is not (long or int or double or string))
            return false;

        constantValue = value is string s ? new StringLiteral(s) : new NumberLiteral(Convert.ToDouble(value));
        return true;
    }

    private IMacroProvider? GetProvider(Expression receiver) =>
        GetProvider(semanticModel.GetType(receiver)) ?? Providers.FirstOrDefault(provider => provider.Supports(semanticModel, receiver));

    private IMacroProvider? GetProvider(Type type)
    {
        if (type is OptionalType optionalType)
            type = optionalType.NonNullableType;

        return Providers.FirstOrDefault(provider => provider.Supports(semanticModel, type));
    }
}
