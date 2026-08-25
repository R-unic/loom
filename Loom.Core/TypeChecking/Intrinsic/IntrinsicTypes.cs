using Loom.Core.TypeChecking.Types;
using PrimitiveType = Loom.Core.TypeChecking.Types.PrimitiveType;

namespace Loom.Core.TypeChecking.Intrinsic;

/// <summary>
///     The intrinsic types the compiler builds in C# rather than reading out of a <c>.loom</c> source, for
///     the cases where there is no declaration to read: the members of a primitive, and the shapes the type
///     checker has to name directly while deciding what an expression means.
/// </summary>
/// <remarks>
///     Distinct from <see cref="Intrinsics" />, which compiles the intrinsic <em>sources</em> - a whole
///     compiler run producing symbols. Nothing here is compiled from anything; these are constants, and
///     keeping them apart is what makes it obvious that reading one costs nothing.
/// </remarks>
public static class IntrinsicTypes
{
    public static readonly TupleMarkerType TupleMarker = new();

    public static readonly InterfaceType Range = new(
        "Range",
        [],
        new ObjectType(
            null,
            [
                new ObjectProperty(false, "minimum", PrimitiveType.Number),
                new ObjectProperty(false, "maximum", PrimitiveType.Number),
                new ObjectProperty(false, "length", PrimitiveType.Number),
                new ObjectProperty(false, "clamp", new FunctionType([], [PrimitiveType.Number], PrimitiveType.Number))
            ]
        )
    );

    public static readonly InterfaceType StringMembers = new(
        "string",
        [],
        new ObjectType(
            null,
            [
                new ObjectProperty(false, "length", PrimitiveType.Number),
                new ObjectProperty(false, "upper", new FunctionType([], [], PrimitiveType.String)),
                new ObjectProperty(false, "lower", new FunctionType([], [], PrimitiveType.String)),
                new ObjectProperty(false, "trim", new FunctionType([], [], PrimitiveType.String)),
                new ObjectProperty(false, "replace", new FunctionType([], [PrimitiveType.String, PrimitiveType.String], PrimitiveType.String)),
                new ObjectProperty(false, "reverse", new FunctionType([], [], PrimitiveType.String)),
                new ObjectProperty(false, "repeat", new FunctionType([], [PrimitiveType.Number], PrimitiveType.String)),
                new ObjectProperty(false, "split", new FunctionType([], [new OptionalType(PrimitiveType.String)], new ArrayType(PrimitiveType.String, true))),
                new ObjectProperty(false, "has", new FunctionType([], [PrimitiveType.String], PrimitiveType.Bool)),
                new ObjectProperty(false, "index_of", new FunctionType([], [PrimitiveType.String], new OptionalType(PrimitiveType.Number))),
                new ObjectProperty(false, "starts_with", new FunctionType([], [PrimitiveType.String], PrimitiveType.Bool)),
                new ObjectProperty(false, "ends_with", new FunctionType([], [PrimitiveType.String], PrimitiveType.Bool)),
                new ObjectProperty(false, "byte", new FunctionType([], [new OptionalType(PrimitiveType.Number)], new OptionalType(PrimitiveType.Number)))
            ]
        )
    );

    /// <summary>
    ///     The <c>Set&lt;T&gt;</c> definition from <c>loom.loom</c>, published by <see cref="Intrinsics" />
    ///     once the intrinsics have compiled so <see cref="ArrayType" /> can name it in <c>to_set</c>'s
    ///     return type.
    ///     <para>
    ///         An array's members are built in C# rather than declared, so unlike every other reference to an
    ///         intrinsic type there is no semantic model in reach to look this up through. It is null until
    ///         the intrinsics finish compiling - an array built during that bootstrap simply has no
    ///         <c>to_set</c>, which is fine because no intrinsic source calls it. First writer wins: every
    ///         project type includes <c>loom.loom</c>, so the definitions are interchangeable.
    ///     </para>
    /// </summary>
    internal static GenericType? SetDefinition;
}
