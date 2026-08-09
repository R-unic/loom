using System.Text;
using Loom.TypeGenerator.ApiTypes;

namespace Loom.TypeGenerator.Generators;

internal sealed class ClassGenerator(
    string filePath,
    ReflectionMetadataReader metadata,
    HashSet<string> definedClassNames,
    string security,
    string? lowerSecurity = null
)
    : BaseGenerator(filePath, metadata)
{
    private readonly Dictionary<string, Class> _classRefs = [];
    private readonly Dictionary<string, HashSet<string>> _definedMemberNames = [];
    private string[] _instanceNameCandidates = [];

    public void Generate(Class[] rbxClasses)
    {
        foreach (var rbxClass in rbxClasses)
        {
            var className = rbxClass.Name;

            // rbxClass.Subclasses = [];
            _classRefs[className] = rbxClass;

            // var superclass = GetSuperclass(rbxClass);
            // superclass?.Subclasses.Add(className);
        }

        _instanceNameCandidates = _classRefs.Keys.Concat(["Character", "Input"]).Where(k => k != "Instance").ToArray();

        var classesToGenerate = rbxClasses
            .Where(rbxClass => !Constants.DirectClassBlacklist.Contains(rbxClass.Name) && ShouldGenerateClass(rbxClass))
            .ToArray();

        GenerateClasses(classesToGenerate);
        WriteContentsOfFile("roblox_ext.loom");
    }

    private void GenerateClasses(Class[] rbxClasses)
    {
        Write("## Generated Roblox instance classes");
        Write();
        foreach (var rbxClass in rbxClasses)
            GenerateClass(rbxClass);
    }

    private void GenerateClass(Class rbxClass)
    {
        definedClassNames.Add(rbxClass.Name);
        _definedMemberNames[rbxClass.Name] = [];

        var className = ClassUtility.AssertClassName(rbxClass.Name, _classRefs);
        var members = rbxClass.Members;
        var noSecurity = security == "None" || IsPluginOnlyClass(rbxClass);
        switch (noSecurity)
        {
            case true:
            {
                var description = (
                        !string.IsNullOrEmpty(rbxClass.Description)
                            ? rbxClass.Description
                            : Metadata.ReadClassDescription(rbxClass.Name)
                    )
                    .Split('\n');

                WriteDescription(description);
                break;
            }

            case false when members.Length == 0:
                return;
        }

        if (className == "Studio") return;

        var membersToGenerate = members.Where(member => ShouldGenerateMember(rbxClass, member)).ToArray();
        var isValidSuperclass = rbxClass.Superclass != Constants.RootClassName;
        var superclassText = isValidSuperclass ? $": {rbxClass.Superclass}" : "";
        var emitCreatable = ClassUtility.IsCreatable(rbxClass) && !ClassUtility.HasMatchingSuperclass(rbxClass, _classRefs, ClassUtility.IsCreatable);
        var emitService = ClassUtility.IsService(rbxClass) && !ClassUtility.HasMatchingSuperclass(rbxClass, _classRefs, ClassUtility.IsService);
        var useBraces = emitCreatable || emitService || membersToGenerate.Length > 0;
        Write($"declare interface {className}{superclassText}{(useBraces ? " {" : ";")}");
        if (useBraces)
            PushIndent();

        if (emitCreatable)
            Write("_is_creatable: true;");

        if (emitService)
            Write("_is_service: true;");

        foreach (var member in membersToGenerate)
        {
            _definedMemberNames[rbxClass.Name].Add(member.Name!.Trim());
            switch (member)
            {
                case Property property:
                    GenerateProperty(property, rbxClass);
                    continue;

                case Event @event:
                    GenerateEvent(@event, rbxClass);
                    break;
                case Function function:
                    GenerateFunction(function, rbxClass);
                    break;
                case Callback callback:
                    GenerateCallback(callback, rbxClass);
                    continue;

                default:
                    Log.Fatal($"received unsupported member type: {member.MemberType}");
                    break;
            }
        }

        if (!useBraces) return;
        PopIndent();
        Write("}");
        Write();
    }

    private void GenerateProperty(Property property, Class rbxClass)
    {
        var valueType = ClassUtility.SafeValueType(property.ValueType);
        var (name, description) = GetMemberNameAndDescription(property, rbxClass);
        var definitelyDefined = property.ValueType.Category != "Class";
        var extraPropertyData = CanWrite(rbxClass.Name, property) && !ClassUtility.HasTag(property, "ReadOnly") ? "mut " : "";
        var (snakeName, attributes) = GetMemberAttributes(name, DeprecationAttributes(property));

        WriteMetadata(description, attributes);
        Write($"{extraPropertyData}{snakeName}: {valueType}{(definitelyDefined || valueType.EndsWith('?') ? "" : "?")};");
    }

    private void GenerateEvent(Event @event, Class rbxClass)
    {
        var parameterList = GenerateParameterList(@event.Parameters);
        var (name, description) = GetMemberNameAndDescription(@event, rbxClass);
        var (snakeName, attributes) = GetMemberAttributes(name, DeprecationAttributes(@event));
        WriteMetadata(description, attributes);
        Write($"event {snakeName}{parameterList};");
    }

    internal void GenerateFunction(Function function, Class rbxClass)
    {
        var returnType = ClassUtility.SafeReturnType(function.ReturnType);
        var parameterList = GenerateParameterList(function.Parameters);
        var (name, description) = GetMemberNameAndDescription(function, rbxClass);
        var mustOverride = ClassUtility.HasMatchingSuperclass(rbxClass, _classRefs, c => c.Members.Any(m => m.Name == function.Name));
        var attributeList = new List<string>();
        if (mustOverride)
            attributeList.Add("override");

        attributeList.Add("luau_method");
        if (ClassUtility.HasTag(function, "Deprecated"))
            attributeList.Add("deprecated");

        var (snakeName, attributes) = GetMemberAttributes(name, attributeList);

        if (Constants.MemberSignatureOverrides.TryGetValue($"{rbxClass.Name}:{function.Name}", out var overloads))
        {
            for (var i = 0; i < overloads.Length; i++)
            {
                WriteMetadata(i == 0 ? description : [""], attributes);
                Write($"{snakeName}: {overloads[i]};");
            }

            return;
        }

        WriteMetadata(description, attributes);
        Write($"{snakeName}: fn{parameterList}: {returnType};");
    }

    internal void GenerateCallback(Callback callback, Class rbxClass)
    {
        var (name, description) = GetMemberNameAndDescription(callback, rbxClass);
        var parameterList = GenerateParameterList(callback.Parameters);
        var returnType = ClassUtility.SafeReturnType(callback.ReturnType);
        var (snakeName, attributes) = GetMemberAttributes(name);

        WriteMetadata(description, attributes);
        Write($"mut {snakeName}: fn{parameterList}: {returnType};");
    }

    private static List<string>? DeprecationAttributes(MemberBase member) =>
        ClassUtility.HasTag(member, "Deprecated") ? ["deprecated"] : null;

    private void WriteMetadata(string[] descriptionLines, string? attributes)
    {
        WriteDescription(descriptionLines);
        if (attributes != null)
            Write(attributes);
    }

    private void WriteDescription(string[] descriptionLines)
    {
        if (descriptionLines is [""]) return;
        if (descriptionLines.Length == 1)
        {
            Write($"#: {descriptionLines[0]} :#");
            return;
        }

        Write("#:");
        WriteList(descriptionLines);
        Write(":#");
    }

    private string GenerateParameterList(Parameter[] parameters)
    {
        var parameterNames = GetParameterNames(parameters);
        foreach (var parameter in parameters)
        {
            var index = parameters.IndexOf(parameter);
            var name = parameterNames.ElementAtOrDefault(index) ?? $"arg{index}";
            parameter.Name = ClassUtility.SafeRename(name);
        }

        return parameters.Length > 0
            ? '(' + string.Join(", ", parameters.Select(GenerateParameter)) + ')'
            : "";
    }

    private string GenerateParameter(Parameter parameter)
    {
        // The Roblox API dump has no dedicated variadic-parameter flag; a parameter typed "Tuple" is its
        // only signal for an untyped variadic, so it becomes a rest parameter (with no per-element type
        // info to draw on). Rest parameters must be last, which a "Tuple"-typed parameter always is here.
        if (parameter.Type.Name == "Tuple")
            return $"..{parameter.Name}: unknown[]";

        var type = ClassUtility.SafeValueType(parameter.Type);
        var isOptional = !string.IsNullOrEmpty(parameter.Default) || type == "any" || type.EndsWith('?');
        if (!string.IsNullOrEmpty(parameter.Name) && type == "Instance")
        {
            var findings = _instanceNameCandidates
                .Where(k => parameter.Name.Contains(k, StringComparison.CurrentCultureIgnoreCase))
                .ToList();

            if (findings.Count != 0)
            {
                var partPosition = findings.IndexOf("Part");
                var doSplice = !findings.Contains("Part")
                    && findings.Count != 0
                    && !parameter.Name.Contains("or", StringComparison.CurrentCultureIgnoreCase);

                if (doSplice && partPosition != -1)
                    findings.RemoveAt(partPosition);

                var instanceName = findings.FirstOrDefault(found => string.Equals(found, parameter.Name, StringComparison.CurrentCultureIgnoreCase));
                type = instanceName == null ? "Instance" : ClassUtility.SafeRenamedInstance(instanceName);
            }
        }

        type = string.IsNullOrEmpty(type) ? "unknown" : $"{type}{(isOptional && !type.EndsWith('?') ? '?' : "")}";
        return $"{parameter.Name}: {type}";
    }

    internal static string[] GetParameterNames(Parameter[] parameters)
    {
        var parameterNames = parameters.Select(param => param.Name).ToArray();
        for (var i = 0; i < parameterNames.Length; i++)
        {
            if (parameterNames.IndexOf(parameterNames[i]) == i) continue;

            var n = 0;
            for (var j = i; j < parameters.Length; j++)
            {
                parameterNames[j] = $"{parameterNames[i]}{n}";
                n++;
            }
        }

        return parameterNames;
    }

    private (string Name, string[] DescriptionLines) GetMemberNameAndDescription(MemberBase member, Class rbxClass)
    {
        var name = ClassUtility.SafeName(member.Name);
        Func<string, string, string>? readDescription = member switch
        {
            Property => Metadata.ReadPropertyDescription,
            Event => Metadata.ReadEventDescription,
            Function => Metadata.ReadFunctionDescription,
            Callback => Metadata.ReadCallbackDescription,
            _ => null
        };

        var description = readDescription == null || !string.IsNullOrWhiteSpace(member.Description)
            ? member.Description
            : readDescription(rbxClass.Name, name);

        return (name, description.Split('\n'));
    }

    private Class? GetSuperclass(Class rbxClass) =>
        rbxClass.Superclass != Constants.RootClassName
            ? _classRefs[rbxClass.Superclass]
            : null;

    private bool CanRead(string className, MemberBase member)
    {
        var readSecurity = ClassUtility.GetSecurity(className, member).Read;
        return readSecurity == security
            || Constants.PluginOnlyClasses.Contains(className) && readSecurity == lowerSecurity;
    }

    private bool CanWrite(string className, MemberBase member)
    {
        var security1 = ClassUtility.GetSecurity(className, member);
        if (security1 is { Read: "None", Write: "PluginSecurity" })
            return true; // dumb hack to fix PluginSecurity writable things being marked as readonly in None.loom

        return security1.Write == security || Constants.PluginOnlyClasses.Contains(className) && security1.Write == lowerSecurity;
    }

    private bool IsPluginOnlyClass(Class rbxClass) =>
        Constants.PluginOnlyClasses.Contains(rbxClass.Name)
        || GetSuperclass(rbxClass) is { } superClass && IsPluginOnlyClass(superClass);

    private bool ShouldGenerateClass(Class rbxClass)
    {
        var superClass = rbxClass.Superclass != Constants.RootClassName ? _classRefs[rbxClass.Superclass] : null;
        if (superClass != null && !ShouldGenerateClass(superClass)) return false;
        if (Constants.ClassBlacklist.Contains(rbxClass.Name)) return false;

        return security == "PluginSecurity" || !Constants.PluginOnlyClasses.Contains(rbxClass.Name);
    }

    private bool ShouldGenerateMember(Class rbxClass, MemberBase member)
    {
        if (member.Name == null)
            return false;

        var isBlacklisted = Constants.MemberBlacklist.TryGetValue(rbxClass.Name, out var value) && value.Contains(member.Name);
        if (isBlacklisted || !CanRead(rbxClass.Name, member))
            return false;

        if (ClassUtility.HasTag(member, "Deprecated"))
        {
            var firstCharacter = member.Name[0];
            if (char.IsLower(firstCharacter))
            {
                var pascalCaseName = char.ToUpper(firstCharacter) + member.Name[1..];
                var pascalCaseMember = rbxClass.Members.FirstOrDefault(v => v.Name == pascalCaseName);
                if (pascalCaseMember != null)
                    return false;
            }
        }

        if (ClassUtility.HasTag(member, "Hidden")) return false;
        if (ClassUtility.HasTag(member, "NotScriptable")) return false;
        return !_definedMemberNames[rbxClass.Name].Contains(member.Name.Trim());
    }

    private static (string NewName, string? AttributeList) GetMemberAttributes(string originalName, IReadOnlyList<string>? existing = null)
    {
        var result = new List<string>();
        var snake = ToSnakeCase(originalName.Replace(" ", ""));
        if (snake != originalName)
            result.Add($"luau_name(\"{originalName}\")");

        if (existing != null)
            result.AddRange(existing);

        return (ClassUtility.SafeRename(snake), result.Count > 0 ? $"[{string.Join(", ", result)}]" : null);
    }

    private static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        var builder = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var ch = name[i];
            if (char.IsUpper(ch))
            {
                var hasPrevious = i > 0;
                var hasNext = i + 1 < name.Length;
                if (hasPrevious && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]) || hasNext && char.IsLower(name[i + 1])))
                    builder.Append('_');

                builder.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }
}