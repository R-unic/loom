using System.Diagnostics.CodeAnalysis;
using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Core.Resolving.Symbols;
using Loom.Luau;
using Loom.Luau.AST;
using Attribute = Loom.Core.Parsing.AST.Attribute;
using BinaryOperator = Loom.Luau.AST.BinaryOperator;
using Identifier = Loom.Luau.AST.Identifier;
using InterfaceType = Loom.Core.TypeChecking.Types.InterfaceType;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Core.Generation.Macros.Providers;

internal sealed class IntrinsicGlobalInvocationMacroProvider : IMacroProvider
{
    public bool Supports(SemanticModel _, Type __) => false;

    public bool Supports(SemanticModel semanticModel, Expression expression) =>
        expression is Parsing.AST.Identifier && semanticModel.GetSymbol(expression) is { IsIntrinsic: true };

    public bool IsInvocationOnlyMember(string memberName) =>
        memberName is "string" or "number" or "new_instance" or "get_service" or "type_is" or "get_metadata" or "has_attribute"
            or "serialize_binary" or "deserialize_binary" or "serializer" or "serializer_of" or "diff_binary" or "apply_diff_binary";

    public bool TryInvocation(
        MacroContext context,
        string name,
        TypeArguments? typeArguments,
        Call call,
        [MaybeNullWhen(false)] out LuauExpression expression)
    {
        switch (name)
        {
            case "get_service":
                var serviceName = context.GetTextOfOnlyTypeArgument(typeArguments, name);
                expression = LuauFactory.LibraryCall("game", ["GetService"], [new StringLiteral(serviceName)], true);

                return true;
            case "new_instance":
                var instanceName = context.GetTextOfOnlyTypeArgument(typeArguments, name);
                expression = LuauFactory.LibraryCall("Instance", ["new"], [new StringLiteral(instanceName)]);

                return true;
            case "string":
                expression = TryFoldToString(call.Arguments[0], out var stringLiteral)
                    ? stringLiteral
                    : new Call(new Identifier("tostring"), call.Arguments);

                return true;
            case "number":
                expression = call.Arguments[0] is StringLiteral numberSource
                    ? LuauNumberFormat.TryParse(numberSource.Value, out var parsed) ? new NumberLiteral(parsed) : new NilLiteral()
                    : new Call(new Identifier("tonumber"), call.Arguments);

                return true;
            case "type_is":
                expression = new BinaryOperator(new Call(new Identifier("typeof"), [call.Arguments[0]]), "==", call.Arguments[1]);
                return true;
            case "get_metadata":
            {
                var matchedAttribute = FindMatchedAttribute(context, typeArguments, name, out var hadError);
                expression = hadError || matchedAttribute == null
                    ? new NilLiteral()
                    : new Table(
                        matchedAttribute.Arguments.ArgumentList
                            .ConvertAll(argument => new TableInitializer(ToLuauConstant(context.SemanticModel.GetConstantValue(argument))))
                    );

                return true;
            }
            case "has_attribute":
            {
                var matchedAttribute = FindMatchedAttribute(context, typeArguments, name, out var hadError);
                expression = new BooleanLiteral(!hadError && matchedAttribute != null);
                return true;
            }
            case "serializer_of":
            {
                // The whole table, not one codec: inside a generic wrapper the key is a runtime value,
                // so the choice cannot be made statically the way serializer::<T>() does.
                if (typeArguments?.ArgumentsList.FirstOrDefault() is not { } mapArgument
                    || context.SemanticModel.GetType(mapArgument) is not InterfaceType mapType)
                {
                    context.Diagnostics.Error(context.Node, InternalCodes.InvalidTypeArguments, "'serializer_of' requires a mapping interface as its first type argument.");
                    expression = new NilLiteral();
                    return true;
                }

                if (!context.SemanticModel.SerializerMaps.Contains(mapType))
                    context.SemanticModel.SerializerMaps.Add(mapType);

                expression = new Luau.AST.ElementAccess(new Identifier(SerializationEmitter.SerializerMapName(mapType.Name)), call.Arguments[0]);
                return true;
            }
            case "serialize_binary":
            case "deserialize_binary":
            case "serializer":
            {
                // serialize_binary infers its interface from the argument's static type; deserialize_binary
                // has no value to infer from, so it takes the interface as a type argument.
                var isSerialize = name == "serialize_binary";
                var invocation = (Invocation)context.Node;
                Node? subject = isSerialize
                    ? invocation.Arguments.ArgumentList.FirstOrDefault()
                    : typeArguments?.ArgumentsList.FirstOrDefault();

                if (subject == null || !TryGetSerializableName(context, subject, name, out var interfaceName, out var declaringFile))
                {
                    expression = new NilLiteral();
                    return true;
                }

                // Only the codec object crosses a module boundary, so an imported interface's calls go
                // through it rather than through function names that are local to the declaring file.
                var isImported = declaringFile != context.SemanticModel.Tree.File.AbsolutePath;

                // 'serializer' hands back the codec value itself rather than calling through it.
                if (name == "serializer")
                {
                    expression = new Identifier(SerializationEmitter.SerializerName(interfaceName));
                    return true;
                }

                LuauExpression callee = isImported
                    ? new Luau.AST.PropertyAccess(
                        new Identifier(SerializationEmitter.SerializerName(interfaceName)),
                        [isSerialize ? "serialize" : "deserialize"]
                    )
                    : new Identifier(isSerialize ? SerializationEmitter.SerializeName(interfaceName) : SerializationEmitter.DeserializeName(interfaceName));

                expression = new Call(callee, call.Arguments);
                return true;
            }
            case "diff_binary":
            case "apply_diff_binary":
            {
                // Both take the baseline as their first argument, so - unlike deserialize_binary - the
                // interface is always inferable from a value and neither needs a type argument.
                var isDiff = name == "diff_binary";
                var invocation = (Invocation)context.Node;
                var subject = invocation.Arguments.ArgumentList.FirstOrDefault();

                if (subject == null || !TryGetSerializableName(context, subject, name, out var interfaceName, out var declaringFile))
                {
                    expression = new NilLiteral();
                    return true;
                }

                var isImported = declaringFile != context.SemanticModel.Tree.File.AbsolutePath;
                LuauExpression callee = isImported
                    ? new Luau.AST.PropertyAccess(
                        new Identifier(SerializationEmitter.SerializerName(interfaceName)),
                        [isDiff ? "diff" : "apply_diff"]
                    )
                    : new Identifier(isDiff ? SerializationEmitter.DiffName(interfaceName) : SerializationEmitter.ApplyDiffName(interfaceName));

                expression = new Call(callee, call.Arguments);
                return true;
            }
        }

        expression = null;
        return false;
    }

    /// <summary>
    ///     Resolves the interface a serialization call targets and confirms the type checker built a
    ///     schema for it. Nothing in the signature constrains T to a serializable type, so an unmarked
    ///     interface has to be caught here rather than silently emitting a call to a function that was
    ///     never generated.
    /// </summary>
    private static bool TryGetSerializableName(MacroContext context, Node subject, string name, out string interfaceName, out string declaringFile)
    {
        interfaceName = "";
        declaringFile = "";
        var semanticModel = context.SemanticModel;
        var subjectType = semanticModel.GetType(subject);
        if (subjectType is not InterfaceType interfaceType)
        {
            context.Diagnostics.Error(
                context.Node,
                InternalCodes.NotSerializable,
                $"'{name}' requires a serializable interface, but was given '{subjectType}'."
            );

            return false;
        }

        var schemaEntry = semanticModel.SerializationSchemas.Keys.FirstOrDefault(s => s.Name == interfaceType.Name);
        if (schemaEntry == null)
        {
            context.Diagnostics.Error(
                context.Node,
                InternalCodes.NotSerializable,
                $"Interface '{interfaceType.Name}' is not serializable.",
                $"add the 'serializable' attribute to '{interfaceType.Name}'."
            );

            return false;
        }

        interfaceName = interfaceType.Name;
        declaringFile = schemaEntry.File.AbsolutePath;
        return true;
    }

    private static Attribute? FindMatchedAttribute(MacroContext context, TypeArguments? typeArguments, string name, out bool hadError)
    {
        hadError = false;
        var invocation = (Invocation)context.Node;
        var semanticModel = context.SemanticModel;

        var typeArgument = typeArguments?.ArgumentsList.FirstOrDefault();
        if (typeArgument == null || semanticModel.GetSymbol(typeArgument, SymbolKind.Interface) is not InterfaceSymbol interfaceSymbol)
        {
            context.Diagnostics.Error(invocation, InternalCodes.InvalidTypeArguments, $"'{name}' requires an interface type argument.");
            hadError = true;
            return null;
        }

        if (invocation.Arguments.ArgumentList is not [var memberNameExpression, var attributeExpression])
        {
            context.Diagnostics.CompilerError(invocation, $"'{name}' expects exactly 2 arguments.");
            hadError = true;
            return null;
        }

        IWithAttributes? target;
        if (memberNameExpression is Literal { Value: null })
        {
            target = interfaceSymbol.Declaration as InterfaceDeclaration;
        }
        else if (memberNameExpression is Literal { Value: string memberName })
        {
            var propertySymbol = interfaceSymbol.GetPropertyAtPath([memberName]);
            if (propertySymbol == null)
            {
                context.Diagnostics.Error(
                    memberNameExpression,
                    InternalCodes.UnknownMetadataMember,
                    $"Interface '{interfaceSymbol.Name}' has no member '{memberName}'."
                );

                hadError = true;
                return null;
            }

            target = propertySymbol.Declaration as IWithAttributes;
        }
        else
        {
            context.Diagnostics.Error(memberNameExpression, InternalCodes.UnknownMetadataMember, "'member_name' must be a string literal or 'none'.");
            hadError = true;
            return null;
        }

        var attributeSymbol = semanticModel.GetSymbol(attributeExpression);
        if (attributeSymbol == null)
        {
            context.Diagnostics.Error(
                attributeExpression,
                InternalCodes.InvalidMetadataAttributeReference,
                "'attribute' must be a direct reference to a decorator function."
            );

            hadError = true;
            return null;
        }

        return target?.Attributes?.AttributeList.Find(a => semanticModel.GetSymbol(a.Expression) == attributeSymbol);
    }

    private static LuauExpression ToLuauConstant(object? value) =>
        value switch
        {
            long l => new NumberLiteral(l),
            int i => new NumberLiteral(i),
            double d => new NumberLiteral(d),
            string s => new StringLiteral(s),
            bool b => new BooleanLiteral(b),
            _ => new NilLiteral()
        };

    private static bool TryFoldToString(LuauExpression argument, [MaybeNullWhen(false)] out StringLiteral folded)
    {
        switch (argument)
        {
            case StringLiteral stringLiteral:
                folded = stringLiteral;
                return true;
            case BooleanLiteral booleanLiteral:
                folded = new StringLiteral(booleanLiteral.Value ? "true" : "false");
                return true;
            case NilLiteral:
                folded = new StringLiteral("nil");
                return true;
            default:
                if (MacroContext.TryComputeConstantArithmetic(argument, out var number))
                {
                    folded = new StringLiteral(LuauNumberFormat.ToLuauString(number));
                    return true;
                }

                folded = null;
                return false;
        }
    }
}