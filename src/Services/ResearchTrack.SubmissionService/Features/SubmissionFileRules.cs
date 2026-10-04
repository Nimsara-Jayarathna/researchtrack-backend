using ResearchTrack.SubmissionService.Domain;

namespace ResearchTrack.SubmissionService.Features;

internal static class SubmissionFileRules
{
    private static readonly IReadOnlyDictionary<string, string> MimeByExtension =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["pdf"] = "application/pdf",
            ["docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ["pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ["zip"] = "application/zip"
        };

    public static IReadOnlyList<string> ParseAllowedTypes(string raw) =>
        raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeExtension)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static string SerializeAllowedTypes(IEnumerable<string> values) =>
        string.Join(',', values.Select(NormalizeExtension).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase));

    public static string NormalizeExtension(string value) => value.Trim().TrimStart('.').ToLowerInvariant();

    public static string? ExtensionFromFileName(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return string.IsNullOrWhiteSpace(extension) ? null : NormalizeExtension(extension);
    }

    public static string? CanonicalContentType(string extension)
    {
        return MimeByExtension.TryGetValue(NormalizeExtension(extension), out var contentType)
            ? contentType
            : null;
    }

    public static bool ContentTypeMatchesExtension(string extension, string contentType)
    {
        var normalized = contentType.Trim().ToLowerInvariant();
        if (NormalizeExtension(extension) == "zip" && normalized == "application/x-zip-compressed") return true;
        return string.Equals(CanonicalContentType(extension), normalized, StringComparison.OrdinalIgnoreCase);
    }

    public static string SafeOriginalFileName(string value, int maxLength)
    {
        var name = Path.GetFileName(value.Trim());
        return name.Length <= maxLength ? name : name[..maxLength];
    }
}
