using System.Globalization;

namespace Unpwn.App.Localization;

public sealed record LocalizationLanguage(
    string Code,
    string DisplayNameKey,
    CultureInfo FormattingCulture,
    bool IsPseudoLocalization = false);

public interface ILocalizationService
{
    event EventHandler? CultureChanged;

    string CurrentLanguageCode { get; }

    CultureInfo CurrentCulture { get; }

    IReadOnlyList<LocalizationLanguage> SupportedLanguages { get; }

    string GetString(string key);

    string Format(string key, params object?[] arguments);

    string FormatPlural(string keyPrefix, int count, params object?[] arguments);

    IReadOnlyCollection<string> GetResourceKeys(string languageCode);

    void SetCulture(CultureInfo culture);

    void SetLanguage(string languageCode);
}
