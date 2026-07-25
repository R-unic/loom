namespace Loom.Core.Diagnostics;

public static class InternalCodes
{
    public const string Unknown = "L???";
    public const string NotImplemented = "L000";
    public const string CompilerError = "L001";

    public const string UnexpectedCharacter = "L101";
    public const string MalformedNumber = "L102";
    public const string UnterminatedString = "L103";
    public const string UnterminatedComment = "L104";

    public const string UnexpectedToken = "L201";
    public const string UnexpectedEof = "L202";
    public const string InvalidAssignmentTarget = "L203";
    public const string MissingFunctionBody = "L204";
    public const string MissingDeclareFnReturnType = "L205";
    public const string UseOfDeclareFnParameterDefaults = "L206";
    public const string MissingDeclareFnParameterType = "L207";
    public const string ExpectedDeclarationSignature = "L208";
    public const string DeclarationOutsideOfBlock = "L209";
    public const string ExpectedInterfaceMemberType = "L210";

    public const string CannotFindName = "L301";
    public const string DuplicateName = "L302";
    public const string UseOfUninitialized = "L303";
    public const string MustHaveInitializer = "L304";
    public const string InfiniteType = "L305";
    public const string TypeMismatch = "L306";
    public const string CannotFindSymbol = "L307";
    public const string NotGeneric = "L308";
    public const string GenericArity = "L309";
    public const string InvalidUnaryOp = "L310";
    public const string InvalidBinaryOp = "L311";
    public const string AssignToImmutable = "L312";
    public const string MustHaveDefaultOrType = "L313";
    public const string InvalidInvocation = "L314";
    public const string InvocationArity = "L315";
    public const string RedundantCode = "L316";
    public const string InvalidMacroReference = "L317";
    public const string ConstraintViolation = "L318";
    public const string UseOfMaybeUninitialized = "L319";
    public const string InvalidAccess = "L320";
    public const string InvalidNameOf = "L321";
    public const string InvalidEnumBaseType = "L322";
    public const string StringEnumMemberMustHaveInitializer = "L323";
    public const string DynamicEnumMemberInitializer = "L324";
    public const string DynamicEnumAccess = "L325";
    public const string ReturnOutsideFunction = "L326";
    public const string UnreachableCode = "L327";
    public const string DuplicateIndexer = "L328";
    public const string NonInterfaceConstraint = "L329";
    public const string RuntimeInDeclarationFile = "L330";
    public const string IncompleteInterfaceInvocation = "L331";
    public const string BreakOutsideLoop = "L332";
    public const string ContinueOutsideLoop = "L333";
    public const string InheritFromSealed = "L334";
    public const string InvokeDeclaredInterface = "L335";
    public const string ReturnInAfter = "L336";
    public const string MissingDeclareVariableType = "L337";
    public const string InvalidKeyOf = "L338";
    public const string ConstraintPropertyOverride = "L339";
    public const string ConstraintIndexerOverride = "L340";
    public const string NonInterfaceImplementation = "L341";
    public const string InvalidImplementation = "L342";
    public const string MissingImplementation = "L343";
    public const string DuplicateImplementation = "L344";
    public const string IntrinsicImplementation = "L345";
    public const string InvalidTypeArguments = "L346";
    public const string AbstractTypeParameterInMacro = "L347";
    public const string NonFunctionAttribute = "L348";
    public const string InvalidLuauMethodAttribute = "L349";
    public const string InvalidLuauNameAttribute = "L350";
    public const string AnonymousEventDisconnect = "L351";
    public const string UnresolvedEventDisconnect = "L352";
    public const string RuntimeLibraryNotFound = "L353";
    public const string ReservedLuauKeyword = "L354";
    public const string NonExhaustiveMatch = "L355";
    public const string NoOverloadMatch = "L356";
    public const string RestParameterNotLast = "L357";
    public const string MissingRestParameterType = "L358";
    public const string InvalidRestParameterType = "L359";

    public const string SimplifiableCode = "L400";
}
