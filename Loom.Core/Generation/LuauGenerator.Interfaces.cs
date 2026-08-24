using Loom.Core.Diagnostics;
using Loom.Core.Generation.Serialization;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving.Symbols;
using Loom.Core.TypeChecking.Serialization;
using Loom.Core.TypeChecking.Types;
using Loom.Luau;
using Loom.Luau.AST;
using BinaryOperator = Loom.Luau.AST.BinaryOperator;
using ExpressionStatement = Loom.Luau.AST.ExpressionStatement;
using FunctionType = Loom.Luau.AST.FunctionType;
using Identifier = Loom.Luau.AST.Identifier;
using IntersectionType = Loom.Luau.AST.IntersectionType;
using Parameter = Loom.Luau.AST.Parameter;
using PropertyAccess = Loom.Luau.AST.PropertyAccess;
using TypeAlias = Loom.Luau.AST.TypeAlias;
using TypeName = Loom.Luau.AST.TypeName;
using TypeParameters = Loom.Luau.AST.TypeParameters;

namespace Loom.Core.Generation;

public sealed partial class LuauGenerator
{
    public override LuauNode VisitImplement(Implement implement)
    {
        _generatedImplements.Add(implement);
        var interfaceName = Visit(implement.InterfaceName);
        var metaName = GetImplementationMetaName(implement);
        var identifier = new Identifier(metaName);
        var variable = new LocalVariable(metaName, null, Table.Empty);
        _state.Postreq(new ExpressionStatement(new BinaryOperator(new PropertyAccess(identifier, ["__index"]), "=", identifier)));
        _state.Postreq(new ExpressionStatement(new BinaryOperator(identifier, "=", new TypeCast(identifier, interfaceName))));

        var overridden = new HashSet<string>();
        foreach (var declaration in implement.Body.Implementations)
        {
            overridden.Add(declaration.Name.Text);
            var typeParameters = MaybeVisit<TypeParameters>(declaration.TypeParameters);
            var parameters = declaration.Parameters?.ParameterList.ConvertAll(Visit<Parameter>) ?? [];
            parameters.Insert(0, new Parameter("self", interfaceName));

            var returnType = MaybeVisit<LuauType>(declaration.ReturnType);
            var body = GenerateFunctionBody(declaration);
            _state.Postreq(
                new Function(
                    $"{metaName}.{declaration.Name.Text}",
                    typeParameters,
                    parameters,
                    returnType,
                    body,
                    false
                )
            );
        }

        if (_semanticModel.GetSymbol(implement.TraitName, SymbolKind.Trait) is TraitSymbol traitSymbol)
        {
            foreach (var (name, defaultDeclaration) in traitSymbol.Defaults)
            {
                if (overridden.Contains(name)) continue;
                var defaultFunction = GetOrCreateTraitDefault(traitSymbol, defaultDeclaration);
                _state.Postreq(new ExpressionStatement(new BinaryOperator(new PropertyAccess(identifier, [name]), "=", defaultFunction)));
            }

            foreach (var (metamethodName, methodName) in traitSymbol.Metamethods)
                _state.Postreq(
                    new ExpressionStatement(new BinaryOperator(new PropertyAccess(identifier, [metamethodName]), "=", new PropertyAccess(identifier, [methodName])))
                );
        }

        EmitMergedMetatableOnceComplete(implement);
        return variable;
    }

    /// <summary>
    ///     The runtime table backing an interface's static members, bound directly to the plain interface
    ///     name - unlike <see cref="VisitImplement" />'s synthesized meta-name, since a static access
    ///     (<c>Vector2::create</c>) erases to an ordinary <c>Vector2.create</c> in Luau and has to find a
    ///     table actually named that. No <c>self</c> parameter is inserted into any method here: a static
    ///     member is not an instance method, so nothing is a receiver.
    /// </summary>
    public override LuauNode VisitStaticBlock(StaticBlock staticBlock)
    {
        var name = staticBlock.InterfaceName.Name.Text;
        var identifier = new Identifier(name);
        var variable = new LocalVariable(name, null, Table.Empty);

        foreach (var field in staticBlock.Body.Fields)
        {
            var value = Visit(field.EqualsValueClause.Value);
            _state.Postreq(new ExpressionStatement(new BinaryOperator(new PropertyAccess(identifier, [field.Name.Text]), "=", value)));
        }

        foreach (var method in staticBlock.Body.Methods)
        {
            var typeParameters = MaybeVisit<TypeParameters>(method.TypeParameters);
            var parameters = method.Parameters?.ParameterList.ConvertAll(Visit<Parameter>) ?? [];
            var returnType = MaybeVisit<LuauType>(method.ReturnType);
            var body = GenerateFunctionBody(method);
            _state.Postreq(new Function($"{name}.{method.Name.Text}", typeParameters, parameters, returnType, body, false));
        }

        return variable;
    }

    /// <summary>
    ///     Builds the one shared metatable an actually-constructed multi-trait interface's construction
    ///     sites all reuse, right after every trait table it merges is a declared local - which happens
    ///     exactly when this 'implement' block turns out to be the last of its interface's to generate.
    ///     Emitted as a postreq of THIS statement, alongside the trait tables it merges, so it is visible
    ///     everywhere they already are - which for an ordinary (module-scope) 'implement' block means every
    ///     construction site in the file, unlike a merge built lazily at the first construction site
    ///     reached, which could sit inside one function and be invisible to a sibling's.
    /// </summary>
    private void EmitMergedMetatableOnceComplete(Implement implement)
    {
        if (_semanticModel.GetSymbol(implement.InterfaceName, SymbolKind.Interface) is not InterfaceSymbol interfaceSymbol
            || !_multiTraitInterfacesConstructed.Contains(interfaceSymbol)
            || !interfaceSymbol.Implementations.TrueForAll(_generatedImplements.Contains))
        {
            return;
        }

        var metaNames = interfaceSymbol.Implementations.ConvertAll(LuauExpression (i) => new Identifier(GetImplementationMetaName(i)));
        _semanticModel.RuntimeReferences += 1;
        var mergedName = _state.Scope.AddIdentifier($"{implement.InterfaceName.Name.Text}_meta");
        _state.Postreq(new LocalVariable(mergedName, null, LuauFactory.RuntimeLibraryCall(["merge_meta"], metaNames)));
        _mergedMetatables[interfaceSymbol] = new Identifier(mergedName);
    }

    /// <summary>
    ///     A defaulted method the compiler itself implements: its intrinsic source body is a placeholder
    ///     only (typed correctly so the trait still checks, never actually compiled) because a real
    ///     structural implementation needs to walk a value generically, which nothing short of a runtime
    ///     helper can do without exposing that helper as a callable Loom name. Keyed by (trait, method)
    ///     rather than trusting the source, so a same-named user trait can never accidentally trigger it -
    ///     see <see cref="GetOrCreateTraitDefault" />.
    /// </summary>
    private static readonly Dictionary<(string Trait, string Method), string> _internalTraitDefaults = new()
    {
        [("Display", "to_string")] = "deep_display",
        [("Eq", "equals")] = "deep_equal",
        [("Hash", "hash")] = "deep_hash"
    };

    /// <summary>
    ///     A trait default is generated once - shared by value across every 'implement' block that doesn't
    ///     override it - rather than re-emitted per site: cheaper output the more types share it, and wiring
    ///     it in with a direct field assignment costs one write per instance method table instead of a
    ///     metatable-chained '__index' lookup on every call. 'self' erases to 'unknown' rather than any one
    ///     implementer's interface, matching '@''s own type inside the default's body (<see
    ///     cref="TypeChecking.TypeChecker.VisitSelfExpression" />) - the function is shared verbatim, so it
    ///     can assume nothing about which interface it is actually running against.
    /// </summary>
    private Identifier GetOrCreateTraitDefault(TraitSymbol traitSymbol, FunctionDeclaration defaultDeclaration)
    {
        if (_traitDefaultFunctions.TryGetValue(defaultDeclaration, out var existing))
            return existing;

        var name = _state.Scope.AddIdentifier($"{traitSymbol.Name}_{defaultDeclaration.Name.Text}_default");
        var typeParameters = MaybeVisit<TypeParameters>(defaultDeclaration.TypeParameters);
        var parameters = defaultDeclaration.Parameters?.ParameterList.ConvertAll(Visit<Parameter>) ?? [];
        parameters.Insert(0, new Parameter("self", Luau.AST.PrimitiveType.Unknown));

        var returnType = MaybeVisit<LuauType>(defaultDeclaration.ReturnType);
        var body = GenerateTraitDefaultBody(traitSymbol, defaultDeclaration, parameters);

        _state.Postreq(new Function(name, typeParameters, parameters, returnType, body));

        var identifier = new Identifier(name);
        _traitDefaultFunctions[defaultDeclaration] = identifier;
        return identifier;
    }

    /// <summary>
    ///     The default is shared by ONE Luau function across every implementer, so a type parameter the
    ///     trait itself declares (as opposed to one the method redeclares on its own signature, which
    ///     <see cref="GetOrCreateTraitDefault" /> already forwards) has nowhere to come from at the shared
    ///     function's single declaration site - unlike an ordinary generic function, there is no per-call
    ///     instantiation to bind it against. Refused outright rather than emitted with the trait's type
    ///     parameter left dangling unbound in the signature, which Luau would reject.
    /// </summary>
    private Chunk GenerateTraitDefaultBody(TraitSymbol traitSymbol, FunctionDeclaration defaultDeclaration, List<Parameter> parameters)
    {
        if (traitSymbol.Declaration is TraitDeclaration { TypeParameters: not null })
        {
            _diagnostics.NotImplemented(
                defaultDeclaration,
                $"A default method on generic trait '{traitSymbol.Name}' is not yet supported.",
                "override it explicitly in every 'implement' block instead."
            );

            return new Chunk([new ExpressionStatement(new Call(new Identifier("error"), [new StringLiteral($"{traitSymbol.Name}.{defaultDeclaration.Name.Text} is not implemented")]))]);
        }

        return traitSymbol.IsIntrinsic && _internalTraitDefaults.TryGetValue((traitSymbol.Name, defaultDeclaration.Name.Text), out var runtimeFunction)
            ? GenerateInternalRuntimeDefaultBody(runtimeFunction, parameters)
            : GenerateFunctionBody(defaultDeclaration);
    }

    /// <summary>
    ///     A call straight into the Luau runtime library, never through a Loom-callable declaration - the
    ///     library keeps the actual structural walk (<c>deep_equal</c>/<c>deep_hash</c>/<c>deep_display</c>),
    ///     and this is the only place anything hands it a value to walk.
    /// </summary>
    private Chunk GenerateInternalRuntimeDefaultBody(string runtimeFunctionName, List<Parameter> parameters)
    {
        _semanticModel.RuntimeReferences += 1;
        var arguments = parameters.ConvertAll(LuauExpression (parameter) => new Identifier(parameter.Name));
        return new Chunk([new Loom.Luau.AST.Return(LuauFactory.RuntimeLibraryCall([runtimeFunctionName], arguments))]);
    }

    public override LuauNode VisitTraitDeclaration(TraitDeclaration traitDeclaration)
    {
        var signatures = traitDeclaration.Body.Members;
        var properties = signatures.ConvertAll(signature =>
            {
                var parameterTypes = signature.Parameters?.ParameterList.FindAll(p => p.ColonTypeClause != null).ConvertAll(p => Visit(p.ColonTypeClause!.Type))
                    ?? [];

                var typeArguments = traitDeclaration.TypeParameters?.ParameterList.ConvertAll(LuauType (p) => new TypeName(p.Name.Text));
                parameterTypes.Insert(0, new TypeName(traitDeclaration.Name.Text, typeArguments));

                return new TableTypeProperty(
                    null,
                    signature.Name.Text,
                    new FunctionType(
                        GenerateTypeParameters(signature.TypeParameters),
                        parameterTypes,
                        Visit(signature.ReturnType)
                    )
                );
            }
        );

        var tableType = new TableType(null, properties);
        var typeParameters = GenerateTypeParameters(traitDeclaration.TypeParameters);
        return new TypeAlias(traitDeclaration.Name.Text, typeParameters, tableType);
    }

    public override LuauNode VisitInterfaceDeclaration(InterfaceDeclaration interfaceDeclaration)
    {
        var symbol = _semanticModel.GetDeclarationSymbol(interfaceDeclaration, SymbolKind.Interface);
        if (symbol is not InterfaceSymbol interfaceSymbol)
            return new NoOpStatement();

        if (interfaceDeclaration.Body?.Members.OfType<MappedTypeDeclaration>().FirstOrDefault() is { } mapped)
            return GenerateMappedTypeAlias(interfaceDeclaration, mapped);

        var indexer = interfaceDeclaration.Body?.Members.OfType<IndexerDeclaration>().FirstOrDefault();
        var propertyDeclarations = interfaceDeclaration.Body?.Members.OfType<PropertyDeclaration>() ?? [];
        var eventDeclarations = interfaceDeclaration.Body?.Members.OfType<EventDeclaration>() ?? [];
        var tableIndexer = indexer != null
            ? new TableTypeIndexer(indexer.MutKeyword == null ? LuauVisibility.Read : null, Visit(indexer.IndexType), Visit(indexer.ColonTypeClause))
            : null;

        var typeParameters = GenerateTypeParameters(interfaceDeclaration.TypeParameters);
        var properties = MergeOverloadedPropertyTypes(
                propertyDeclarations.Select(property => GenerateInterfacePropertyType(interfaceDeclaration, property, typeParameters)).ToList()
            )
            .Concat(eventDeclarations.Select(GenerateInterfaceEventType))
            .ToList();

        TrackSerializerEmit(interfaceSymbol);

        var tableType = new TableType(tableIndexer, properties);
        return new TypeAlias(
            interfaceDeclaration.Name.Text,
            typeParameters,
            interfaceDeclaration.ColonTypeListClause != null || interfaceSymbol.Implementations.Count > 0
                ? new IntersectionType(
                    [
                        ..interfaceDeclaration.ColonTypeListClause?.Types.ConvertAll(Visit) ?? [],
                        tableType,
                        ..interfaceSymbol.Implementations.ConvertAll(i => Visit(i.TraitName))
                    ]
                )
                : tableType
        );
    }

    /// <summary>
    ///     A mapped type becomes a Luau indexer keyed on the whole key set: <c>{ [keyof&lt;T&gt;]: index&lt;T,
    ///     keyof&lt;T&gt;&gt; }</c>. That is less than Loom knows - which key has which type is lost, since
    ///     Luau has no way to say it - but it is the closest shape there is, and every concrete use resolves
    ///     to real properties before it reaches here anyway (see <see cref="LuauTypeRenderer" />).
    /// </summary>
    /// <remarks>
    ///     The binder needs no substituting here: Luau's indexer has no name to bind, so a use of it inside
    ///     the member type emits the key set it ranges over - see <c>VisitTypeName</c>.
    /// </remarks>
    private LuauNode GenerateMappedTypeAlias(InterfaceDeclaration interfaceDeclaration, MappedTypeDeclaration mapped)
    {
        var indexer = new TableTypeIndexer(mapped.MutKeyword == null ? LuauVisibility.Read : null, Visit(mapped.SourceType), Visit(mapped.ColonTypeClause));

        return new TypeAlias(interfaceDeclaration.Name.Text, GenerateTypeParameters(interfaceDeclaration.TypeParameters), new TableType(indexer, []));
    }

    /// <summary>
    ///     Emits a <c>[serializable]</c> interface's serializer pair after its type alias, the same way an
    ///     <c>implement</c> block trails its metatable. They live in the declaring file so a consumer in
    ///     another module reaches them through the ordinary export path. Only the pieces this file's calls
    ///     (or an export, conservatively) actually need are emitted - see <see cref="ResolveSerializationUsage" />.
    /// </summary>
    private void TrackSerializerEmit(InterfaceSymbol interfaceSymbol)
    {
        if (!_semanticModel.SerializationSchemas.TryGetValue(interfaceSymbol, out var schema)) return;
        if (interfaceSymbol.File.AbsolutePath != _semanticModel.Tree.File.AbsolutePath) return;
        if (!CheckSchemaIsEmittable(interfaceSymbol, schema)) return;

        var usage = ResolveSerializationUsage(interfaceSymbol);
        if (usage == SerializationUsage.None) return;
        
        _semanticModel.RuntimeReferences += 1;

        var emitter = new SerializationEmitter(schema, _bufferMembers);
        if (usage.HasFlag(SerializationUsage.Serialize))
            _serializerStatements.Add(emitter.EmitSerializer());

        if (usage.HasFlag(SerializationUsage.Deserialize))
            _serializerStatements.Add(emitter.EmitDeserializer());

        if (usage.HasFlag(SerializationUsage.Delta))
        {
            var deltaEmitter = new SerializationEmitter(schema, _bufferMembers);
            _serializerStatements.Add(deltaEmitter.EmitDeltaWriteHelper());
            _serializerStatements.Add(deltaEmitter.EmitDeltaAttempt());
            _serializerStatements.Add(deltaEmitter.EmitDeltaSerializer());
            _serializerStatements.Add(deltaEmitter.EmitDeltaReadHelper());
            _serializerStatements.Add(deltaEmitter.EmitDeltaDeserializer());
        }

        if (usage.HasFlag(SerializationUsage.SerializerObject))
            _serializerStatements.Add(emitter.EmitSerializerObject());
    }

    /// <summary>
    ///     What this interface's codec actually needs to cover. An exported interface assumes everything:
    ///     a cross-module consumer always calls through the codec object rather than a bare function name,
    ///     and files compile in dependency order, so a declaring file's own codegen runs before anything
    ///     that later imports it is even analyzed - there is no "wait and see" available. A non-exported
    ///     interface is scoped to <see cref="Resolving.SemanticModel.SerializationUsages" />, collected by
    ///     <see cref="CollectSerializationUsage" /> before generation starts; requesting the codec object
    ///     itself still widens to everything, since the object bundles all four callbacks by name.
    /// </summary>
    private SerializationUsage ResolveSerializationUsage(InterfaceSymbol interfaceSymbol)
    {
        if (_semanticModel.Exports.Any(export => export.Symbol == interfaceSymbol))
            return SerializationUsage.All;

        var usage = _semanticModel.SerializationUsages.GetValueOrDefault(interfaceSymbol);
        if (usage.HasFlag(SerializationUsage.SerializerObject))
            return SerializationUsage.All;

        if (usage.HasFlag(SerializationUsage.Delta))
            usage |= SerializationUsage.Serialize;

        return usage;
    }

    /// <summary>
    ///     Scans every 'new X { ... }'/'x with { ... }' in the file before the main tree walk starts, so
    ///     <see cref="VisitImplement" /> knows - before it ever reaches one - which multi-trait interfaces
    ///     need a shared merged metatable built. See <see cref="_multiTraitInterfacesConstructed" /> for why
    ///     this cannot be decided lazily at the first construction site instead.
    /// </summary>
    private void CollectMultiTraitConstructions()
    {
        foreach (var node in _semanticModel.Tree.EnumerateDescendants())
        {
            InterfaceSymbol? interfaceSymbol = node switch
            {
                InterfaceInvocation invocation => _semanticModel.GetSymbol(invocation.Name, SymbolKind.Interface) as InterfaceSymbol,
                WithOperator withOperator when _semanticModel.GetType(withOperator.Expression) is InterfaceType interfaceType =>
                    _semanticModel.FindTypeDeclaration(interfaceType.Name) as InterfaceSymbol,
                _ => null
            };

            if (interfaceSymbol is { Implementations.Count: > 1 })
                _multiTraitInterfacesConstructed.Add(interfaceSymbol);
        }
    }

    /// <summary>
    ///     Scans every call this file makes to <c>serialize_binary</c>/<c>deserialize_binary</c>/
    ///     <c>serializer</c>/<c>diff_binary</c>/<c>apply_diff_binary</c>/<c>serializer_of</c> before the
    ///     main tree walk, so <see cref="TrackSerializerEmit" /> already knows what each interface needs
    ///     regardless of whether the declaration or the call comes first in the file. Best-effort: a
    ///     malformed call is left for the macro expander (which runs during the real walk) to diagnose
    ///     properly, so anything that does not resolve cleanly here is simply skipped rather than reported.
    /// </summary>
    private void CollectSerializationUsage()
    {
        foreach (var invocation in _semanticModel.Tree.EnumerateDescendants<Invocation>())
        {
            if (invocation.Expression is not Parsing.AST.Identifier identifier
                || _semanticModel.GetSymbol(identifier) is not { IsIntrinsic: true } symbol)
            {
                continue;
            }

            if (symbol.Name == "serializer_of")
            {
                MarkSerializerMapTargets(invocation);
                continue;
            }

            var usage = symbol.Name switch
            {
                "serialize_binary" => SerializationUsage.Serialize,
                "deserialize_binary" => SerializationUsage.Deserialize,
                "serializer" => SerializationUsage.SerializerObject,
                "diff_binary" or "apply_diff_binary" => SerializationUsage.Delta,
                _ => SerializationUsage.None
            };

            if (usage == SerializationUsage.None) continue;
            
            Node? subject = symbol.Name is "deserialize_binary" or "serializer"
                ? invocation.TypeArguments?.ArgumentsList.FirstOrDefault()
                : invocation.Arguments.ArgumentList.FirstOrDefault();

            if (subject != null)
                MarkUsage(subject, usage);
        }
    }

    /// <summary>
    ///     'serializer_of::&lt;MessageData, K&gt;(key)' never names the value interfaces it might return
    ///     directly - only the mapping interface. Every property and indexer value type the mapping
    ///     interface carries is a codec the emitted dispatch table can hand back, so each one needs its
    ///     object emitted the same as a direct 'serializer::&lt;T&gt;()' call would.
    /// </summary>
    private void MarkSerializerMapTargets(Invocation invocation)
    {
        if (invocation.TypeArguments?.ArgumentsList.FirstOrDefault() is not { } mapArgument
            || _semanticModel.GetType(mapArgument) is not InterfaceType mapType)
            return;

        foreach (var property in mapType.Properties)
            MarkSerializerObjectUsage(property.ValueType);

        foreach (var indexer in mapType.Indexers)
            MarkSerializerObjectUsage(indexer.ValueType);
    }

    private void MarkSerializerObjectUsage(TypeChecking.Types.Type valueType)
    {
        if (valueType is InterfaceType interfaceType)
            MarkUsage(interfaceType, SerializationUsage.SerializerObject);
    }

    private void MarkUsage(Node subject, SerializationUsage usage)
    {
        if (_semanticModel.GetType(subject) is InterfaceType interfaceType)
            MarkUsage(interfaceType, usage);
    }

    private void MarkUsage(InterfaceType interfaceType, SerializationUsage usage)
    {
        if (_semanticModel.SerializationSchemas.Keys.FirstOrDefault(s => s.Name == interfaceType.Name) is not { } interfaceSymbol) return;
        _semanticModel.SerializationUsages[interfaceSymbol] = _semanticModel.SerializationUsages.GetValueOrDefault(interfaceSymbol) | usage;
    }

    /// <summary>
    ///     Refuses to emit a schema the generator cannot fully cover. Every field kind the schema builder
    ///     produces is emittable today, so this reports nothing - it stays as a backstop for the next one
    ///     added, since a field the emitter skips goes missing from the payload silently rather than
    ///     loudly, which is how several bugs reached the snapshot suite unnoticed.
    /// </summary>
    private bool CheckSchemaIsEmittable(InterfaceSymbol interfaceSymbol, SerializationSchema schema)
    {
        var declaration = interfaceSymbol.Declaration;
        if (FindUnsupportedField(schema.Fields) is not { } unsupported)
            return true;

        _diagnostics.NotImplemented(
            declaration,
            $"Serializing '{unsupported.Path}' of type '{unsupported.ValueType}' is not yet generated.",
            "variable-length and union fields are described by the schema but not yet emitted."
        );

        return false;
    }

    /// <summary>Name of the codec emitted for <paramref name="interfaceType" />, or null when it has none.</summary>
    private string? ResolveSerializerName(InterfaceType interfaceType)
    {
        var symbol = _semanticModel.SerializationSchemas.Keys.FirstOrDefault(s => s.Name == interfaceType.Name);
        return symbol == null ? null : SerializationEmitter.SerializerName(symbol.Name);
    }

    private static SerializationField? FindUnsupportedField(IEnumerable<SerializationField> fields)
    {
        foreach (var serializationField in fields)
        {
            if (!IsMeasurable(serializationField))
                return serializationField;

            if (FindUnsupportedField(serializationField.Children) is { } nested)
                return nested;
        }

        return null;
    }

    /// <summary>
    ///     Whether the generator can compute a field's width. Everything is allocated before a byte is
    ///     written, so a field whose width cannot be stated - inline or by walking the value - would leave
    ///     the buffer short and the writes running off the end.
    /// </summary>
    private static bool IsMeasurable(SerializationField serializationField) =>
        serializationField switch
        {
            _ when serializationField.BodyBytes != null => true,
            StringField => true,

            // Sentinelled: either the components in full or nothing, and which is known before allocating.
            DatatypeField or CFrameField => true,

            // Entries share a bit block reserved after the length prefix, and an entry whose own width
            // needs walking gets a nested loop, so the only requirement left is that it be measurable.
            ArrayField arrayField => IsMeasurable(arrayField.Element),
            OptionalField optionalField => IsMeasurable(optionalField.Inner),
            MapField mapField => IsMeasurable(mapField.Key) && IsMeasurable(mapField.Value),

            // A flattened nested struct, or a real tuple: measurable exactly when its parts are.
            TupleField tupleField => tupleField.Elements.All(IsMeasurable),
            UnionField unionField => unionField.Variants.All(v => v.Fields.All(IsMeasurable)),
            _ => false
        };

    // Mirrors TypeChecker.Interfaces.cs's MergeOverloadedProperties: same-named function-typed table properties become one field typed as their intersection.
    private static List<TableTypeProperty> MergeOverloadedPropertyTypes(List<TableTypeProperty> properties)
    {
        var merged = new List<TableTypeProperty>();
        var indexByName = new Dictionary<string, int>();
        foreach (var property in properties)
        {
            if (indexByName.TryGetValue(property.Name, out var index)
                && property.Type is FunctionType
                && merged[index].Type is FunctionType or IntersectionType)
            {
                var existingSignatures = merged[index].Type is IntersectionType existingIntersection ? existingIntersection.Types : [merged[index].Type];
                merged[index] = new TableTypeProperty(merged[index].Visibility, merged[index].Name, new IntersectionType([..existingSignatures, property.Type]));
                continue;
            }

            indexByName[property.Name] = merged.Count;
            merged.Add(property);
        }

        return merged;
    }

    private TableTypeProperty GenerateInterfacePropertyType(InterfaceDeclaration interfaceDeclaration, PropertyDeclaration property, TypeParameters typeParameters)
    {
        LuauVisibility? visibility = property.MutKeyword == null ? LuauVisibility.Read : null;
        var name = property.Name.Text;
        var type = Visit(property.ColonTypeClause);
        var tableProperty = new TableTypeProperty(visibility, name, type);
        if (property.TryGetIntrinsicAttribute(_semanticModel, "luau_name", out var luauNameAttribute)
            && ValidateLuauNameAttribute(luauNameAttribute, out var nameLiteral))
            tableProperty = new TableTypeProperty(visibility, nameLiteral.Value, type);

        if (!property.TryGetIntrinsicAttribute(_semanticModel, "luau_method", out var luauMethodAttribute))
            return tableProperty;

        if (type is not FunctionType functionType)
        {
            _diagnostics.Error(
                luauMethodAttribute.Attribute,
                InternalCodes.InvalidLuauMethodAttribute,
                "May only use function types directly when using 'luau_method' attribute"
            );

            return tableProperty;
        }

        var typeArguments = typeParameters.Parameters.Count > 0
            ? typeParameters.Parameters.ConvertAll(LuauType (param) => new TypeName(param.Name))
            : null;

        var selfType = new TypeName(interfaceDeclaration.Name.Text, typeArguments);
        functionType.ParameterTypes.Insert(0, selfType);

        return tableProperty;
    }

    private TableTypeProperty GenerateInterfaceEventType(EventDeclaration eventDeclaration)
    {
        _semanticModel.RuntimeReferences += 1;
        var parameterTypes = eventDeclaration.Parameters?.ParameterList.ConvertAll(p => Visit(p.ColonTypeClause!.Type)) ?? [];
        var eventType = LuauFactory.QualifyRuntimeType(new TypeName("Event", parameterTypes));
        var name = eventDeclaration.Name.Text;
        if (eventDeclaration.TryGetIntrinsicAttribute(_semanticModel, "luau_name", out var luauNameAttribute)
            && ValidateLuauNameAttribute(luauNameAttribute, out var nameLiteral))
            name = nameLiteral.Value;

        return new TableTypeProperty(LuauVisibility.Read, name, eventType);
    }

    private static string GetImplementationMetaName(Implement implement)
    {
        var typeArguments = implement.TraitName.TypeArguments;
        var typeArgumentText = typeArguments != null ? '_' + string.Join('_', typeArguments.ArgumentsList.Select(a => a.ToString())) : "";
        return $"{implement.TraitName.Name.Text}{typeArgumentText}_for_{implement.InterfaceName.Name.Text}";
    }
}