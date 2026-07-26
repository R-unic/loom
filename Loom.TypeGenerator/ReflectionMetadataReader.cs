using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Loom.TypeGenerator;

internal partial class ReflectionMetadataReader
{
    private readonly Dictionary<string, XElement> _classesByName;
    private readonly Dictionary<(string ClassName, string GroupKey), Dictionary<string, XElement>> _memberIndexCache = [];

    public ReflectionMetadataReader(string body)
    {
        var metadata = XElement.Parse(body);
        _classesByName = BuildClassIndex(metadata);
    }

    public string ReadFunctionDescription(string className, string name) =>
        ReadMemberDescription(className, name, ["ReflectionMetadataFunctions", "ReflectionMetadataYieldFunctions"]);

    public string ReadPropertyDescription(string className, string name) => ReadMemberDescription(className, name, ["ReflectionMetadataProperties"]);
    public string ReadCallbackDescription(string className, string name) => ReadMemberDescription(className, name, ["ReflectionMetadataCallbacks"]);
    public string ReadEventDescription(string className, string name) => ReadMemberDescription(className, name, ["ReflectionMetadataEvents"]);
    public string ReadClassDescription(string name) => !_classesByName.TryGetValue(name, out var classItem) ? string.Empty : GetSummary(classItem);

    private string ReadMemberDescription(string className, string name, string[] specifier)
    {
        var memberIndex = GetMemberIndex(className, specifier);
        return memberIndex.TryGetValue(name, out var memberItem) ? GetSummary(memberItem) : string.Empty;
    }

    private static string GetSummary(XElement item)
    {
        var summaryElement = ChildElementByAttribute(item.Element("Properties"), "summary");
        return summaryElement != null ? HyperlinkToMarkdown(summaryElement.Value) : string.Empty;
    }

    private Dictionary<string, XElement> GetMemberIndex(string className, string[] specifier)
    {
        var groupKey = string.Join(",", specifier);
        var cacheKey = (className, groupKey);
        if (_memberIndexCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var index = new Dictionary<string, XElement>();
        if (_classesByName.TryGetValue(className, out var classItem))
            foreach (var container in classItem.Elements("Item"))
            {
                var containerClass = (string?)container.Attribute("class");
                if (containerClass == null || Array.IndexOf(specifier, containerClass) < 0) continue;

                foreach (var memberItem in container.Elements("Item"))
                {
                    if ((string?)memberItem.Attribute("class") != "ReflectionMetadataMember")
                        continue;

                    var name = ChildElementByAttribute(memberItem.Element("Properties"), "Name")?.Value;
                    if (name == null) continue;

                    index.TryAdd(name, memberItem);
                }
            }

        _memberIndexCache[cacheKey] = index;
        return index;
    }

    private static Dictionary<string, XElement> BuildClassIndex(XElement metadata)
    {
        var result = new Dictionary<string, XElement>();
        foreach (var classItem in metadata.Descendants("Item"))
        {
            if ((string?)classItem.Attribute("class") != "ReflectionMetadataClass")
                continue;

            var name = ChildElementByAttribute(classItem.Element("Properties"), "Name")?.Value;
            if (name == null) continue;

            result.TryAdd(name, classItem);
        }

        return result;
    }

    private static XElement? ChildElementByAttribute(XElement? parent, string attributeValue) =>
        parent?.Elements("string").FirstOrDefault(e => (string?)e.Attribute("name") == attributeValue);

    private static string HyperlinkToMarkdown(string s) => HyperlinkFilterRegex().Replace(s, m => $"[{m.Groups[2].Value}]({m.Groups[1].Value})");

    [GeneratedRegex("<a href=\"([^\"]+)\"[^>]+>([^<]+)</a>")]
    private static partial Regex HyperlinkFilterRegex();
}