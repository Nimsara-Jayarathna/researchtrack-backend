using System.Globalization;
using System.Text;

namespace ResearchTrack.SubmissionService.Infrastructure;

/// <summary>
/// Builds an ASCII-only Content-Disposition header for S3 response overrides.
/// The RFC 5987 filename* parameter preserves Unicode names without putting raw
/// Unicode in an HTTP header (which S3 rejects for characters outside Latin-1).
/// </summary>
internal static class DownloadContentDisposition
{
    public static string Create(string originalFileName, bool inline)
    {
        var fileName = SafeLeafName(originalFileName);
        var fallbackFileName = AsciiFallback(fileName);
        var disposition = inline ? "inline" : "attachment";

        // The AWS SDK URL-encodes this *entire* header when it signs the request.
        // EscapeDataString encodes filename* first, so S3 ultimately receives
        // ASCII percent sequences (e.g. %E2%80%94), never a raw em dash.
        return $"{disposition}; filename=\"{fallbackFileName}\"; filename*=UTF-8''{Uri.EscapeDataString(fileName)}";
    }

    private static string SafeLeafName(string originalFileName)
    {
        var path = originalFileName.Replace('\\', '/');
        var leaf = path[(path.LastIndexOf('/') + 1)..];
        var safe = new StringBuilder(leaf.Length);

        for (var i = 0; i < leaf.Length; i++)
        {
            var character = leaf[i];
            if (char.IsControl(character) || character is '\u2028' or '\u2029')
                continue;

            // Prevent an invalid unpaired UTF-16 surrogate from breaking URI encoding.
            if (char.IsHighSurrogate(character))
            {
                if (i + 1 < leaf.Length && char.IsLowSurrogate(leaf[i + 1]))
                {
                    safe.Append(character).Append(leaf[++i]);
                }

                continue;
            }

            if (!char.IsLowSurrogate(character))
                safe.Append(character);
        }

        var name = safe.ToString().Trim();
        return name is "" or "." or ".." ? "download" : name;
    }

    private static string AsciiFallback(string fileName)
    {
        var dotIndex = fileName.LastIndexOf('.');
        var hasExtension = dotIndex > 0 && dotIndex < fileName.Length - 1;
        var stem = hasExtension ? fileName[..dotIndex] : fileName;
        var extension = hasExtension ? fileName[dotIndex..] : string.Empty;

        var fallbackStem = ToAscii(stem).Trim(' ', '.', '-', '_');
        var fallbackExtension = ToAscii(extension);
        if (fallbackExtension.Length <= 1 || fallbackExtension[0] != '.')
            fallbackExtension = string.Empty;

        return (fallbackStem.Length == 0 ? "download" : fallbackStem) + fallbackExtension;
    }

    private static string ToAscii(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;

            var ascii = character
                is '\u2010'
                or '\u2011'
                or '\u2012'
                or '\u2013'
                or '\u2014'
                or '\u2015'
                or '\u2212'
                ? '-'
                : character;

            if (
                ascii is >= 'a' and <= 'z'
                or >= 'A' and <= 'Z'
                or >= '0' and <= '9'
                or ' ' or '.' or '_' or '-' or '(' or ')'
            )
            {
                result.Append(ascii);
            }
            else if (ascii is '\t' or '\n')
            {
                result.Append(' ');
            }
        }

        return result.ToString();
    }
}
