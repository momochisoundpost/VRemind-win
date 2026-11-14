namespace VRemindWin.Core.Services;

public static class LinkSafetyValidator
{
    public static bool TryValidateAllowedUri(string? rawUrl, out Uri? validatedUri)
    {
        validatedUri = null;
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return false;
        }

        var trimmed = rawUrl.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var candidate))
        {
            return false;
        }

        var scheme = candidate.Scheme.ToLowerInvariant();
        if (scheme == "http" || scheme == "https")
        {
            if (string.IsNullOrWhiteSpace(candidate.Host))
            {
                return false;
            }
            validatedUri = candidate;
            return true;
        }

        if (scheme == "mailto")
        {
            validatedUri = candidate;
            return true;
        }

        return false;
    }

    public static bool TryExtractTargetUrlFromToastArgs(string? toastArgs, out Uri? targetUri)
    {
        targetUri = null;
        if (string.IsNullOrWhiteSpace(toastArgs))
        {
            return false;
        }

        var query = toastArgs.TrimStart('?');
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=');
            if (idx <= 0)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(pair[..idx]);
            if (!string.Equals(key, "url", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var encodedValue = pair[(idx + 1)..];
            var decodedValue = Uri.UnescapeDataString(encodedValue);
            return TryValidateAllowedUri(decodedValue, out targetUri);
        }

        return false;
    }
}
