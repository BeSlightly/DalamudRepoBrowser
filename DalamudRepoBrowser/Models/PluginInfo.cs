using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace DalamudRepoBrowser;

public sealed class PluginInfo
{
    public string InternalName { get; }
    public string Author { get; }
    public string Name { get; }
    public string Description { get; }
    public string Punchline { get; }
    public string RepoUrl { get; }
    public byte ApiLevel { get; }
    public long LastUpdate { get; }
    public IReadOnlyList<string> Tags { get; }
    public IReadOnlyList<string> CategoryTags { get; }
    public bool IsClosedSource { get; }

    public PluginInfo(JToken json)
    {
        InternalName = (string?)json["InternalName"] ?? string.Empty;
        Author = (string?)json["Author"] ?? string.Empty;
        Name = (string?)json["Name"] ?? string.Empty;
        Description = (string?)json["Description"] ?? string.Empty;
        Punchline = (string?)json["Punchline"] ?? string.Empty;
        RepoUrl = (string?)json["RepoUrl"] ?? string.Empty;
        ApiLevel = (byte?)json["DalamudApiLevel"] ?? 0;
        LastUpdate = (long?)json["LastUpdate"] ?? 0;
        Tags = ParseStringList(json["Tags"]);
        CategoryTags = ParseStringList(json["CategoryTags"]);
        IsClosedSource = json.Value<bool?>("is_closed_source") ?? false;
    }

    private static IReadOnlyList<string> ParseStringList(JToken? token)
    {
        if (token is not JArray array)
        {
            return Array.Empty<string>();
        }

        return array.Values<string>()
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToList();
    }
}
