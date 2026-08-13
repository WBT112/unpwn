using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Resources;
using System.Text;

namespace Unpwn.App.Localization;

public sealed class ResourceLocalizationService : ILocalizationService
{
    public const string DefaultLanguageCode = "en";
    public const string PseudoLanguageCode = "qps-ploc";

    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en");
    private static readonly CultureInfo GermanCulture = CultureInfo.GetCultureInfo("de");
    private static readonly ResourceManager[] ResourceManagers =
    [
        new(
            "Unpwn.App.Localization.Strings",
            typeof(ResourceLocalizationService).Assembly),
        new(
            "Unpwn.App.Localization.VaultStrings",
            typeof(ResourceLocalizationService).Assembly),
        new(
            "Unpwn.App.Localization.VaultEntryUxStrings",
            typeof(ResourceLocalizationService).Assembly),
        new(
            "Unpwn.App.Localization.DashboardStrings",
            typeof(ResourceLocalizationService).Assembly),
        new(
            "Unpwn.App.Localization.AccountStrings",
            typeof(ResourceLocalizationService).Assembly),
        new(
            "Unpwn.App.Localization.WorkflowStrings",
            typeof(ResourceLocalizationService).Assembly),
        new(
            "Unpwn.App.Localization.RecoveryExecutionStrings",
            typeof(ResourceLocalizationService).Assembly),
        new(
            "Unpwn.App.Localization.CredentialStrings",
            typeof(ResourceLocalizationService).Assembly),
        new(
            "Unpwn.App.Localization.SettingsStrings",
            typeof(ResourceLocalizationService).Assembly),
        new(
            "Unpwn.App.Localization.ImportLimitStrings",
            typeof(ResourceLocalizationService).Assembly),
    ];
    private static readonly IReadOnlyList<LocalizationLanguage> Languages =
        new ReadOnlyCollection<LocalizationLanguage>(
        [
            new(DefaultLanguageCode, "Language.English", EnglishCulture),
            new("de", "Language.German", GermanCulture),
            new(PseudoLanguageCode, "Language.Pseudo", EnglishCulture, IsPseudoLocalization: true),
        ]);

    private string _currentLanguageCode;
    private CultureInfo _currentCulture;

    public ResourceLocalizationService(CultureInfo? systemCulture = null)
    {
        var requestedCulture = systemCulture ?? CultureInfo.CurrentUICulture;
        (_currentLanguageCode, _currentCulture) = ResolveCulture(requestedCulture);
    }

    public event EventHandler? CultureChanged;

    public string CurrentLanguageCode => _currentLanguageCode;

    public CultureInfo CurrentCulture => _currentCulture;

    public IReadOnlyList<LocalizationLanguage> SupportedLanguages => Languages;

    public string GetString(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var source = GetResourceValue(CultureInfo.InvariantCulture, key);
        if (_currentLanguageCode == PseudoLanguageCode)
        {
            return source is null ? MissingKey(key) : PseudoLocalize(source);
        }

        if (_currentLanguageCode != DefaultLanguageCode)
        {
            var localized = GetResourceValue(CultureInfo.GetCultureInfo(_currentLanguageCode), key);
            if (localized is not null)
            {
                return localized;
            }
        }

        return source ?? MissingKey(key);
    }

    public string Format(string key, params object?[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return string.Format(CurrentCulture, GetString(key), arguments);
    }

    public string FormatPlural(string keyPrefix, int count, params object?[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPrefix);
        ArgumentNullException.ThrowIfNull(arguments);

        var variant = count switch
        {
            0 => "Zero",
            1 => "One",
            _ => "Other",
        };
        return Format($"{keyPrefix}.{variant}", arguments);
    }

    public IReadOnlyCollection<string> GetResourceKeys(string languageCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(languageCode);

        var culture = languageCode switch
        {
            DefaultLanguageCode or PseudoLanguageCode => CultureInfo.InvariantCulture,
            "de" => GermanCulture,
            _ => throw new ArgumentOutOfRangeException(nameof(languageCode)),
        };

        return
        [
            .. ResourceManagers
                .SelectMany(resourceManager => GetResourceKeys(resourceManager, culture))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
    }

    public void SetCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var (languageCode, formattingCulture) = ResolveCulture(culture);
        SetResolvedCulture(languageCode, formattingCulture);
    }

    public void SetLanguage(string languageCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(languageCode);
        var language = Languages.SingleOrDefault(candidate =>
            string.Equals(candidate.Code, languageCode, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentOutOfRangeException(nameof(languageCode));

        SetResolvedCulture(language.Code, language.FormattingCulture);
    }

    private static (string LanguageCode, CultureInfo FormattingCulture) ResolveCulture(CultureInfo culture)
    {
        if (string.Equals(culture.TwoLetterISOLanguageName, "de", StringComparison.OrdinalIgnoreCase))
        {
            return ("de", culture);
        }

        if (string.Equals(culture.TwoLetterISOLanguageName, "en", StringComparison.OrdinalIgnoreCase))
        {
            return (DefaultLanguageCode, culture);
        }

        return (DefaultLanguageCode, EnglishCulture);
    }

    private void SetResolvedCulture(string languageCode, CultureInfo formattingCulture)
    {
        if (string.Equals(_currentLanguageCode, languageCode, StringComparison.Ordinal) &&
            string.Equals(_currentCulture.Name, formattingCulture.Name, StringComparison.Ordinal))
        {
            return;
        }

        _currentLanguageCode = languageCode;
        _currentCulture = formattingCulture;
        CultureChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string? GetResourceValue(CultureInfo culture, string key)
    {
        foreach (var resourceManager in ResourceManagers)
        {
            var value = resourceManager
                .GetResourceSet(culture, createIfNotExists: true, tryParents: false)
                ?.GetString(key, ignoreCase: false);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetResourceKeys(
        ResourceManager resourceManager,
        CultureInfo culture)
    {
        var resourceSet = resourceManager.GetResourceSet(
            culture,
            createIfNotExists: true,
            tryParents: false);
        return resourceSet is null
            ? []
            : resourceSet
                .Cast<DictionaryEntry>()
                .Select(entry => (string)entry.Key);
    }

    private static string MissingKey(string key) => $"⟦{key}⟧";

    private static string PseudoLocalize(string source)
    {
        var builder = new StringBuilder(source.Length + 12);
        builder.Append('⟦');
        var insidePlaceholder = false;
        foreach (var character in source)
        {
            if (character == '{')
            {
                insidePlaceholder = true;
            }

            builder.Append(insidePlaceholder ? character : ExpandCharacter(character));

            if (character == '}')
            {
                insidePlaceholder = false;
            }
        }

        builder.Append(" ···⟧");
        return builder.ToString();
    }

    private static char ExpandCharacter(char character) => character switch
    {
        'a' => 'á',
        'A' => 'Á',
        'e' => 'ë',
        'E' => 'Ë',
        'i' => 'ï',
        'I' => 'Ï',
        'o' => 'ö',
        'O' => 'Ö',
        'u' => 'ü',
        'U' => 'Ü',
        'y' => 'ÿ',
        'Y' => 'Ÿ',
        _ => character,
    };
}
