using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Core.Resolving.Symbols;
using Loom.Core.Text;
using Loom.Core.TypeChecking.Types;
using Loom.Luau;
using Loom.Luau.AST;
using BinaryOperator = Loom.Core.Parsing.AST.BinaryOperator;
using ElementAccess = Loom.Core.Parsing.AST.ElementAccess;
using ExpressionStatement = Loom.Luau.AST.ExpressionStatement;
using Identifier = Loom.Core.Parsing.AST.Identifier;
using LiteralType = Loom.Core.TypeChecking.Types.LiteralType;
using Parameter = Loom.Luau.AST.Parameter;
using Parenthesized = Loom.Core.Parsing.AST.Parenthesized;
using PrimitiveType = Loom.Core.TypeChecking.Types.PrimitiveType;
using PropertyAccess = Loom.Core.Parsing.AST.PropertyAccess;
using UnaryOperator = Loom.Core.Parsing.AST.UnaryOperator;

namespace Loom.Core.Generation;

public sealed partial class LuauGenerator
{
    public override LuauNode VisitInvocation(Invocation invocation)
    {
        var callee = Visit(invocation.Expression);
        var arguments = invocation.Arguments.ArgumentList.ConvertAll(Visit);
        if (_semanticModel.GetType(invocation.Expression) is InstantiatedType { GenericType.UnderlyingType: InterfaceType { Name: "Event" } })
            return new Call(new Luau.AST.PropertyAccess(callee, ["Fire"]), arguments, true);

        var call = new Call(callee, arguments, IsMethodReference(invocation.Expression));
        return _macroExpander.TryGetInvocationMacro(invocation, call, out var replacement) ? replacement : call;
    }

    public override LuauNode VisitQualifiedName(QualifiedName qualifiedName)
    {
        var luauAccess = new Luau.AST.PropertyAccess(Visit(qualifiedName.Identifier), qualifiedName.Names.ConvertAll(dotName => dotName.Name.Text));
        if (_macroExpander.TryGetQualifiedNameMacro(qualifiedName, luauAccess, out var propertyReplacement))
            return propertyReplacement;

        return _macroExpander.TryGetInvocationMacroReference(qualifiedName, luauAccess, out var referenceReplacement)
            ? referenceReplacement
            : GenerateRenamedAccess(qualifiedName, luauAccess.Target, luauAccess.Names);
    }

    public override LuauNode VisitPropertyAccess(PropertyAccess propertyAccess)
    {
        var luauAccess = new Luau.AST.PropertyAccess(Visit(propertyAccess.Expression), propertyAccess.Names.ConvertAll(dotName => dotName.Name.Text));
        if (_macroExpander.TryGetPropertyAccessMacro(propertyAccess, luauAccess, out var propertyReplacement))
            return propertyReplacement;

        return _macroExpander.TryGetInvocationMacroReference(propertyAccess, luauAccess, out var referenceReplacement)
            ? referenceReplacement
            : GenerateRenamedAccess(propertyAccess, luauAccess.Target, luauAccess.Names);
    }

    public override LuauNode VisitElementAccess(ElementAccess elementAccess)
    {
        var luauAccess = new Luau.AST.ElementAccess(Visit(elementAccess.Expression), Visit(elementAccess.IndexExpression));
        return _macroExpander.TryGetElementAccessMacro(elementAccess, luauAccess, out var replacement)
            ? replacement
            : luauAccess.Index is StringLiteral literal
                ? GenerateRenamedAccess(elementAccess, luauAccess.Target, [literal.Value])
                : luauAccess;
    }

    public override LuauNode VisitBinaryOperator(BinaryOperator binaryOperator)
    {
        if (SyntaxFacts.IsBitwiseOperator(binaryOperator.Operator.Kind))
            return GenerateBitwiseOperator(binaryOperator);

        if (binaryOperator.Operator.Kind == SyntaxKind.InKeyword)
        {
            var index = Visit(binaryOperator.Left);
            var target = Visit(binaryOperator.Right);
            var access = new Luau.AST.ElementAccess(target, index);
            return new Luau.AST.BinaryOperator(access, "~=", new NilLiteral());
        }

        var op = binaryOperator.Operator.Text;
        var leftType = _semanticModel.GetType(binaryOperator.Left);
        var rightType = _semanticModel.GetType(binaryOperator.Right);
        var @string = PrimitiveType.String;
        var isConcatenation = op.StartsWith('+') && leftType.IsAssignableTo(@string) && rightType.IsAssignableTo(@string);
        var left = Visit(binaryOperator.Left);
        var right = Visit(binaryOperator.Right);
        var mappedOperator = isConcatenation ? op.Replace("+", "..") : LuauOperatorMap.BinaryOperator(op);
        return new Luau.AST.BinaryOperator(left, mappedOperator, right);
    }

    public override LuauNode VisitUnaryOperator(UnaryOperator unaryOperator)
    {
        var operand = Visit(unaryOperator.Operand);
        return SyntaxFacts.IsBitwiseOperator(unaryOperator.Operator.Kind)
            ? LuauFactory.Bit32Call("bnot", [operand])
            : new Luau.AST.UnaryOperator(LuauOperatorMap.UnaryOperator(unaryOperator.Operator.Text), operand);
    }

    public override LuauNode VisitTernaryOperator(TernaryOperator ternaryOperator) =>
        new IfExpression(Visit(ternaryOperator.Condition), Visit(ternaryOperator.ThenBranch), [], Visit(ternaryOperator.ElseBranch));

    public override LuauNode VisitParenthesized(Parenthesized parenthesized) => new Luau.AST.Parenthesized(Visit(parenthesized.Expression));

    public override LuauNode VisitRangeLiteral(RangeLiteral rangeLiteral) =>
        new Table([new PropertyTableInitializer("minimum", Visit(rangeLiteral.Minimum)), new PropertyTableInitializer("maximum", Visit(rangeLiteral.Maximum))]);

    public override LuauNode VisitArrayLiteral(ArrayLiteral arrayLiteral) => new Table(arrayLiteral.Expressions.ConvertAll(e => new TableInitializer(Visit(e))));

    public override LuauNode VisitLiteral(Literal literal) =>
        literal.Value switch
        {
            long l => new NumberLiteral(l),
            int i => new NumberLiteral(i),
            double d => new NumberLiteral(d),
            string s => new StringLiteral(s),
            bool b => new BooleanLiteral(b),
            _ => new NilLiteral()
        };

    public override LuauNode VisitInterpolatedStringLiteral(InterpolatedStringLiteral interpolatedStringLiteral)
    {
        var segments = interpolatedStringLiteral.Parts.ConvertAll<InterpolatedStringSegment>(part => part switch
            {
                InterpolationTextPart text => new InterpolatedStringTextSegment(text.Text),
                InterpolationHolePart hole => new InterpolatedStringExpressionSegment(Visit(hole.Expression)),
                _ => throw new ArgumentException($"Unknown interpolation part type: {part.GetType()}")
            }
        );

        return new InterpolatedString(segments);
    }

    public override LuauNode VisitIdentifier(Identifier identifier)
    {
        var luauIdentifier = new Luau.AST.Identifier(identifier.Name.Text);
        return _macroExpander.TryGetInvocationMacroReference(identifier, luauIdentifier, out var referenceReplacement)
            ? referenceReplacement
            : _semanticModel.GetSymbol(identifier) is { Kind: SymbolKind.InjectedPropertyVariable }
                ? new Luau.AST.PropertyAccess(LuauFactory.Self, [luauIdentifier.Name])
                : luauIdentifier;
    }

    public override LuauNode VisitAs(As @as) => new TypeCast(Visit(@as.Expression), Visit(@as.Type));
    public override LuauNode VisitNameOf(NameOf nameOf) => new StringLiteral(((LiteralType)_semanticModel.GetType(nameOf)).Value!.ToString()!);

    public override LuauNode VisitInterfaceInvocation(InterfaceInvocation interfaceInvocation)
    {
        var symbol = _semanticModel.GetSymbol(interfaceInvocation.Name, SymbolKind.Interface);

        // the resolver already reported why this is not an interface; an invocation sits in expression
        // position, so a statement placeholder here would fail to cast and surface as a compiler crash
        if (symbol is not InterfaceSymbol interfaceSymbol)
            return new NilLiteral();

        var table = GenerateInterfaceInvocationBody(interfaceInvocation.Body, interfaceSymbol);
        if (interfaceSymbol.Implements.Count == 0)
            return table;

        _semanticModel.RuntimeReferences += 1; // merge_meta;
        var metaNames = interfaceSymbol.Implementations.ConvertAll(LuauExpression (i) => new Luau.AST.Identifier(GetImplementationMetaName(i)));
        var call = LuauFactory.SetMetatableCall(table, LuauFactory.RuntimeLibraryCall(["merge_meta"], metaNames));
        return new TypeCast(call, new Luau.AST.TypeName(interfaceInvocation.Name.Token.Text));
    }

    private Table GenerateInterfaceInvocationBody(InterfaceInvocationBody interfaceInvocationBody, InterfaceSymbol interfaceSymbol)
    {
        var unhandledInitializer = interfaceInvocationBody.Initializers
            .Find(i => i is not (IndexInitializer
                              or PropertyInitializer
                              or ShorthandPropertyInitializer)
            );

        if (unhandledInitializer != null)
            _diagnostics.CompilerError(unhandledInitializer, "Unhandled interface invocation initializer type");

        return new Table(
            interfaceInvocationBody.Initializers.ConvertAll(TableInitializer (i) => i switch
                {
                    IndexInitializer indexInitializer => GenerateInterfaceInvocationIndexInitializer(indexInitializer),
                    PropertyInitializer propertyInitializer => GenerateInterfaceInvocationPropertyInitializer(
                        propertyInitializer,
                        interfaceSymbol
                    ),
                    ShorthandPropertyInitializer shorthandPropertyInitializer => GenerateInterfaceInvocationShorthandPropertyInitializer(
                        shorthandPropertyInitializer,
                        interfaceSymbol
                    ),
                    _ => null!
                }
            )
        );
    }

    private ComputedPropertyTableInitializer GenerateInterfaceInvocationIndexInitializer(IndexInitializer indexInitializer) =>
        new(Visit(indexInitializer.IndexExpression), Visit(indexInitializer.Expression));

    private PropertyTableInitializer GenerateInterfaceInvocationPropertyInitializer(
        PropertyInitializer propertyInitializer,
        InterfaceSymbol interfaceSymbol)
    {
        var name = GetRenamedPropertyName(interfaceSymbol, propertyInitializer.Name.Text);
        var initializedValue = Visit(propertyInitializer.Expression);
        return new PropertyTableInitializer(name, initializedValue);
    }

    private string GetRenamedPropertyName(InterfaceSymbol interfaceSymbol, string name)
    {
        var propertySymbol = interfaceSymbol.GetPropertyAtPath([name]);
        return propertySymbol == null
            || !propertySymbol.TryGetIntrinsicAttribute("luau_name", out var luauNameAttribute)
            || !ValidateLuauNameAttribute(luauNameAttribute, out var nameLiteral)
                ? name
                : nameLiteral.Value;
    }

    private PropertyTableInitializer GenerateInterfaceInvocationShorthandPropertyInitializer(
        ShorthandPropertyInitializer shorthandPropertyInitializer,
        InterfaceSymbol interfaceSymbol) =>
        new(GetRenamedPropertyName(interfaceSymbol, shorthandPropertyInitializer.Identifier.Name.Text), Visit(shorthandPropertyInitializer.Identifier));

    private Luau.AST.PropertyAccess GenerateRenamedAccess(Expression access, LuauExpression target, List<string> names) =>
        _semanticModel.TryGetIntrinsicAttribute(access, "luau_name", out var attr) && ValidateLuauNameAttribute(attr, out var nameLiteral)
            ? new Luau.AST.PropertyAccess(target, [..names.SkipLast(1), nameLiteral.Value])
            : new Luau.AST.PropertyAccess(target, names);

    /// <summary>
    ///     Determines whether the given expression refers to a trait method that must be called
    ///     with Luau's ':' method-call syntax, as opposed to a plain function value.
    /// </summary>
    private bool IsMethodReference(Expression expression) =>
        _semanticModel.TryGetIntrinsicAttribute(expression, "luau_method", out _)
        || expression is { Children: [{ } child, ..], Tokens: [.., { Kind: SyntaxKind.Identifier } name] }
        && _semanticModel.GetType(child) is InterfaceType interfaceType
        && interfaceType.TraitMethodNames.Contains(name.Text);

    private LuauExpression WrapAnonymousFunction(Expression expression, LuauExpression luauExpression, LuauType? returnType = null)
    {
        if (luauExpression is Luau.AST.Identifier || !IsMethodReference(expression))
            return luauExpression;

        return new AnonymousFunction(
            null,
            [Parameter.Vararg()],
            returnType,
            new Chunk([new ExpressionStatement(new Call(luauExpression, [new Luau.AST.Identifier("...")], true))])
        );
    }

    private List<LuauExpression> UnwrapFunctionArgument(Statement body, LuauType? returnType = null)
    {
        var chunk = GenerateChunk(body);
        return chunk is { Statements: [ExpressionStatement { Expression: Call { IsMethod: false } call }] }
            ? [call.Callee, ..call.Arguments]
            : [new AnonymousFunction(null, [], returnType ?? new UnitType(), chunk)];
    }

    private LuauNode GenerateBitwiseOperator(BinaryOperator binaryOperator)
    {
        var left = Visit(binaryOperator.Left);
        var right = Visit(binaryOperator.Right);
        var op = binaryOperator.Operator.Text;
        if (op.EndsWith('='))
            return GenerateBitwiseAssignmentOperator(op, left, right);

        var name = LuauOperatorMap.BitwiseOperator(op);
        var arguments = new List<LuauExpression>();
        var leftUpdated = AddBit32Arguments(left, name, arguments);
        var rightUpdated = AddBit32Arguments(right, name, arguments);
        if (!leftUpdated)
            arguments.Add(left);

        if (!rightUpdated)
            arguments.Add(right);

        return LuauFactory.Bit32Call(name, arguments);
    }

    private static Luau.AST.BinaryOperator GenerateBitwiseAssignmentOperator(string op, LuauExpression left, LuauExpression right)
    {
        var name = LuauOperatorMap.BitwiseOperator(op);
        var arguments = new List<LuauExpression> { left };
        var rightUpdated = AddBit32Arguments(right, name, arguments);
        if (!rightUpdated)
            arguments.Add(right);

        return new Luau.AST.BinaryOperator(left, "=", LuauFactory.Bit32Call(name, arguments));
    }

    private static bool AddBit32Arguments(LuauExpression expression, string name, List<LuauExpression> arguments)
    {
        if (expression is not Call
            {
                Callee: Luau.AST.PropertyAccess { Target: Luau.AST.Identifier { Name: "bit32" }, Names: [{ } fnName] }, Arguments: { } fnArguments
            }
            || fnName != name)
            return false;

        // shift methods dont accept varargs
        if (fnName.EndsWith("shift"))
            return false;

        foreach (var argument in fnArguments)
        {
            arguments.Add(argument);
            AddBit32Arguments(argument, name, arguments);
        }

        return true;
    }
}