using Loom.TypeGenerator.ApiTypes;

namespace Loom.TypeGenerator;

internal static class Constants
{
    public const string RootClassName = "<<<ROOT>>>";
    public static readonly List<string> BadNameCharacters = [" ", "/", "\""];

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

    public static readonly Dictionary<string, string> RenamableAutoTypes = new()
    {
        { "Part", "BasePart" }, { "Script", "LuaSourceContainer" }, { "Character", "Model" }, { "Input", "InputObject" }
    };

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