using System;
using System.Text.RegularExpressions;

namespace DalamudRepoBrowser;

internal static class RepoUrlHelper
{
    private static readonly Regex GitHubRegex = new("github", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RawRegex = new("/raw", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string GetRawUrl(string url)
    {
        return url.StartsWith("https://raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
            ? url
            : GitHubRegex.Replace(RawRegex.Replace(url, string.Empty, 1), "raw.githubusercontent", 1);
    }
}
