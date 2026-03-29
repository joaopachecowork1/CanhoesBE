using System.Text.Json;

namespace Canhoes.Api.Media;

/// <summary>
/// Normalizes media paths emitted by the API so the frontend always receives a
/// stable URL shape, regardless of whether the source was a stored relative
/// path, an uploaded media row or an absolute backend URL.
/// </summary>
internal static class MediaUrlFormatter
{
    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var normalizedPath = path.Trim().Replace("\\", "/");
        if (normalizedPath.StartsWith("/api/uploads/", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedPath["/api".Length..];
        }

        var uploadsIndex = normalizedPath.IndexOf("/uploads/", StringComparison.OrdinalIgnoreCase);
        if (uploadsIndex >= 0)
        {
            return normalizedPath[uploadsIndex..];
        }

        if (normalizedPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedPath;
        }

        if (normalizedPath.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase))
        {
            return $"/{normalizedPath}";
        }

        return normalizedPath.StartsWith("/")
            ? normalizedPath
            : $"/{normalizedPath}";
    }

    public static List<string> Collect(
        string? mediaUrl,
        string? mediaUrlsJson,
        IEnumerable<string>? extraUrls = null)
    {
        var uniqueUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var urls = new List<string>();

        void Add(string? candidate)
        {
            var normalized = Normalize(candidate);
            if (string.IsNullOrWhiteSpace(normalized)) return;
            if (uniqueUrls.Add(normalized))
            {
                urls.Add(normalized);
            }
        }

        if (!string.IsNullOrWhiteSpace(mediaUrlsJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<string>>(mediaUrlsJson);
                if (parsed is not null)
                {
                    foreach (var candidate in parsed)
                    {
                        Add(candidate);
                    }
                }
            }
            catch
            {
                // Older rows may contain malformed JSON. Ignore and fall back to
                // the single-url column plus uploaded media records.
            }
        }

        if (extraUrls is not null)
        {
            foreach (var candidate in extraUrls)
            {
                Add(candidate);
            }
        }

        Add(mediaUrl);
        return urls;
    }
}
