using Loom.TypeGenerator.ApiTypes;

namespace Loom.TypeGenerator;

internal static class Constants
{
    public const string RootClassName = "<<<ROOT>>>";
    public static readonly List<string> BadNameCharacters = [" ", "/", "\""];

    /// <summary>
    ///     Signatures the API dump cannot express, written by hand and keyed by
    ///     <c>Class:Member</c>. Each entry replaces the derived signature with one line per
    ///     overload; Loom merges same-named function properties into an overload set.
    /// </summary>
    /// <remarks>
    ///     The dump has no way to mark a parameter optional, and no way to give a member more than
    ///     one shape, so any method whose result depends on which arguments were passed comes out
    ///     wrong in both directions.
    /// </remarks>
    public static readonly Dictionary<string, string[]> MemberSignatureOverrides = new()
    {
        // WaitForChild's timeOut is optional, and passing it makes the result nullable - the docs
        // say it "will time out after the specified number of seconds and return nil". The dump
        // records neither fact, so the derived signature forced the timeout and then promised a
        // non-nil T that the method does not deliver.
        {
            "Instance:WaitForChild",
            [
                "fn<T: Instance>(childName: string): T",
                "fn<T: Instance>(childName: string, timeOut: number): T?"
            ]
        }
    };

    public static readonly Dictionary<string, HashSet<string>> MemberBlacklist = new()
    {
        { "Workspace", ["FilteringEnabled"] },
        { "BodyGyro", ["cframe"] },
        { "BodyAngularVelocity", ["FilteringEnabled"] },
        { "BodyPosition", ["FilteringEnabled"] },
        { "Debris", ["FilteringEnabled"] },
        { "LayerCollector", ["FilteringEnabled"] },
        { "GuiBase3d", ["FilteringEnabled"] },
        { "Model", ["FilteringEnabled"] },
        { "DataModel", ["FilteringEnabled", "Workspace", "lighting"] }
    };

    public static readonly HashSet<string> CreatableBlacklist =
    [
        "UserSettings",
        "DebugSettings",
        "Studio",
        "GameSettings",
        "ParabolaAdornment",
        "LuaSettings",
        "PhysicsSettings",
        "Player",
        "DebuggerWatch",
        "Tween",
        "UserGameSettings"
    ];

    public static readonly HashSet<string> PluginOnlyClasses =
    [
        "ABTestService",
        "ChangeHistoryService",
        "CoreGui",
        "DataModelSession",
        "DebuggerBreakpoint",
        "DebuggerManager",
        "DebuggerWatch",
        "DebugSettings",
        "File",
        "GameSettings",
        "GlobalSettings",
        "LuaSettings",
        "MemStorageConnection",
        "MultipleDocumentInterfaceInstance",
        "NetworkPeer",
        "NetworkReplicator",
        "NetworkSettings",
        "PackageService",
        "PhysicsSettings",
        "Plugin",
        "PluginAction",
        "PluginDebugService",
        "PluginDragEvent",
        "PluginGui",
        "PluginGuiService",
        "PluginMenu",
        "PluginMouse",
        "PluginToolbar",
        "PluginToolbarButton",
        "RenderingTest",
        "RenderSettings",
        "RobloxPluginGuiService",
        "ScriptDebugger",
        "Selection",
        "StatsItem",
        "Studio",
        "StudioData",
        "StudioService",
        "StudioTheme",
        "TaskScheduler",
        "TestService",
        "VersionControlService"
    ];

    public static readonly HashSet<string> DirectClassBlacklist = ["Object", "Instance"]; // handwritten
    public static readonly HashSet<string> ClassBlacklist =
    [
        // Classes which Roblox leverages internally/in the CoreScripts but serve no purpose to developers
        "AnalyticsSettings",
        "BinaryStringValue",
        "BrowserService",
        "CacheableContentProvider",
        "ClusterPacketCache",
        "CookiesService",
        "CorePackages",
        "CoreScript",
        "CoreScriptSyncService",
        "DraftsService",
        "FlagStandService",
        "FlyweightService",
        "FriendService",
        "Geometry",
        "GoogleAnalyticsConfiguration",
        "GuidRegistryService",
        "HttpRbxApiService",
        "HttpRequest",
        "KeyboardService",
        "LocalStorageService",
        "LuaWebService",
        "MemStorageService",
        "MouseService",
        "PartOperationAsset",
        "PermissionsService",
        "PhysicsPacketCache",
        "PlayerEmulatorService",
        "ReflectionMetadataItem",
        "RobloxReplicatedStorage",
        "RuntimeScriptService",
        "SpawnerService",
        "StandalonePluginScripts",
        "StopWatchReporter",
        "ThirdPartyUserService",
        "TimerService",
        "TouchInputService",
        "VirtualInputManager",
        "Visit",

        // never implemented
        "AdvancedDragger",
        "LoginService",
        "NotificationService",
        "ScriptService",
        "Status",

        // super deprecated:
        "AdService",
        "FunctionalTest",
        "PluginManager",
        "VirtualUser",

        //"BevelMesh",
        "CustomEvent",
        "CustomEventReceiver",

        //"CylinderMesh",
        //"DoubleConstrainedValue",
        "Flag",
        "FlagStand",

        //"FloorWire",
        //"Glue",
        "GuiMain",

        //"Hat",
        "Hint",

        //"Hole",
        "Hopper",
        "HopperBin",

        //"IntConstrainedValue",
        //"JointsService",
        "Message",

        //"MotorFeature",
        "PointsService",

        //"SelectionPartLasso",
        //"SelectionPointLasso",
        //"SkateboardPlatform",
        "Skin",
        "ReflectionMetadata",
        "ReflectionMetadataCallbacks",
        "ReflectionMetadataClasses",
        "ReflectionMetadataEnums",
        "ReflectionMetadataEvents",
        "ReflectionMetadataFunctions",
        "ReflectionMetadataProperties",
        "ReflectionMetadataYieldFunctions",
        "Studio",

        // unused
        "UGCValidationService",
        "RbxAnalyticsService"
    ];

    public static readonly Dictionary<string, Dictionary<string, Security>?> SecurityOverrides = new()
    {
        ["StarterGui"] = new Dictionary<string, Security> { ["ShowDevelopmentGui"] = new() { Read = "PluginSecurity", Write = "PluginSecurity" } }
    };

    public static readonly Dictionary<string, List<string>> ExpectedExtraMembers = new()
    {
        { "Player", ["Name"] },
        { "ValueBase", ["Value", "Changed"] },
        { "DataStore", ["GetAsync", "IncrementAsync", "SetAsync", "UpdateAsync", "RemoveAsync"] },
        { "OrderedDataStore", ["GetAsync", "IncrementAsync", "SetAsync", "UpdateAsync", "RemoveAsync"] }
    };

    public static readonly Dictionary<string, string> RenamableAutoTypes = new()
    {
        { "Part", "BasePart" }, { "Script", "LuaSourceContainer" }, { "Character", "Model" }, { "Input", "InputObject" }
    };

    // public static readonly Dictionary<string, string> PropertyTypeMap = new();

    public static readonly Dictionary<string, string> ValueTypeMap = new()
    {
        { "Array", "unknown[]" },
        { "BinaryString", "string" },
        { "SharedString", "string" },
        { "String", "string" },
        { "Connection", "ScriptConnection" },
        { "ContentId", "string" },
        { "CoordinateFrame", "CFrame" },
        { "EventInstance", "ScriptSignal" },
        { "Function", "fn: void" },
        { "float", "number" },
        { "double", "number" },
        { "int", "number" },
        { "int64", "number" },
        { "Dictionary", "UnknownRecord" },
        { "Map", "UnknownRecord" },
        { "RBXScriptSignal", "ScriptSignal" },
        { "RBXScriptConnection", "ScriptConnection" },
        { "Objects", "Object[]" },
        { "Instances", "Instance[]" },
        { "Property", "string" },
        { "OptionalCoordinateFrame", "CFrame?" },
        { "ProtectedString", "string" },
        { "Rect2D", "Rect" },
        { "Tuple", "unknown" },
        { "Variant", "unknown" },
        { "Color3uint8", "Color3" },
        { "any", "unknown" },
        { "Array<any>", "unknown[]" }
    };

    public static readonly Dictionary<string, string> ReturnTypeMap = new() { { "null", "void" } };

    public static readonly Dictionary<string, string> RenameMap = new()
    {
        { "event", "_event" },
        { "type", "_type" },
        { "interface", "_interface" },
        { "old", "oldValue" },
        { "new", "newValue" },
        { "function", "callback" }
    };
}