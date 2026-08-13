using System.Text;

namespace Unpwn.App.Services;

public static class RecoverySessionNameSuggestion
{
    private const string FallbackName = "Recovery";
    private const string Suffix = "-Recovery";
    private const int MaximumSessionNameLength = 120;

    public static string CreateForCurrentUser()
    {
        try
        {
            return Create(Environment.UserName);
        }
        catch (InvalidOperationException)
        {
            return FallbackName;
        }
        catch (PlatformNotSupportedException)
        {
            return FallbackName;
        }
    }

    public static string Create(string? localUserName)
    {
        if (string.IsNullOrWhiteSpace(localUserName))
        {
            return FallbackName;
        }

        var maximumUserNameLength = MaximumSessionNameLength - Suffix.Length;
        var builder = new StringBuilder(maximumUserNameLength);
        var previousWasSeparator = false;
        foreach (var character in localUserName.Trim())
        {
            if (builder.Length >= maximumUserNameLength)
            {
                break;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator)
            {
                builder.Append('-');
                previousWasSeparator = true;
            }
        }

        var sanitized = builder.ToString().Trim('-');
        return sanitized.Length == 0 ? FallbackName : sanitized + Suffix;
    }
}
