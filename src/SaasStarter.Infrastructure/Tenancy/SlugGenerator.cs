using System.Text.RegularExpressions;

namespace SaasStarter.Infrastructure.Tenancy;

/// <summary>Turns a workspace name into a URL-safe, unique-friendly slug.</summary>
public static partial class SlugGenerator
{
    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlugChars();

    public static string FromName(string name)
    {
        var slug = NonSlugChars().Replace(name.Trim().ToLowerInvariant(), "-").Trim('-');
        return slug.Length == 0 ? "workspace" : slug;
    }
}
