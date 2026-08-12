using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Unpwn.App.Services;

public sealed record MainWindowPresentationPreferences(
    double NormalWidth,
    double NormalHeight,
    bool IsMaximized)
{
    public const double DefaultWidth = 1080;
    public const double DefaultHeight = 720;

    public static MainWindowPresentationPreferences Default { get; } =
        new(DefaultWidth, DefaultHeight, IsMaximized: true);
}

public sealed record ApplicationPreferencesSnapshot(
    MainWindowPresentationPreferences MainWindow)
{
    public static ApplicationPreferencesSnapshot Default { get; } =
        new(MainWindowPresentationPreferences.Default);
}

public interface IApplicationPreferences
{
    ApplicationPreferencesSnapshot Load();

    bool TrySave(ApplicationPreferencesSnapshot preferences);
}

public static class MainWindowPresentationPolicy
{
    public const double MinimumWidth = 760;
    public const double MinimumHeight = 560;

    public static MainWindowPresentationPreferences Normalize(
        MainWindowPresentationPreferences? preferences,
        double availableWidth,
        double availableHeight)
    {
        var source = preferences ?? MainWindowPresentationPreferences.Default;
        var usableWidth = IsUsableDimension(availableWidth)
            ? Math.Max(MinimumWidth, availableWidth)
            : double.PositiveInfinity;
        var usableHeight = IsUsableDimension(availableHeight)
            ? Math.Max(MinimumHeight, availableHeight)
            : double.PositiveInfinity;

        var width = IsUsableDimension(source.NormalWidth)
            ? source.NormalWidth
            : MainWindowPresentationPreferences.DefaultWidth;
        var height = IsUsableDimension(source.NormalHeight)
            ? source.NormalHeight
            : MainWindowPresentationPreferences.DefaultHeight;

        width = Math.Clamp(width, MinimumWidth, usableWidth);
        height = Math.Clamp(height, MinimumHeight, usableHeight);
        return new MainWindowPresentationPreferences(width, height, source.IsMaximized);
    }

    private static bool IsUsableDimension(double value) =>
        double.IsFinite(value) && value > 0;
}

public sealed class FileApplicationPreferences : IApplicationPreferences
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;

    public FileApplicationPreferences(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public static FileApplicationPreferences CreateDefault() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "unpwn",
        "preferences.json"));

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Presentation preferences are non-sensitive convenience state and must never make startup unavailable.")]
    public ApplicationPreferencesSnapshot Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return ApplicationPreferencesSnapshot.Default;
            }

            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<ApplicationPreferencesSnapshot>(json, SerializerOptions)
                ?? ApplicationPreferencesSnapshot.Default;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or
                NotSupportedException or ArgumentException)
        {
            return ApplicationPreferencesSnapshot.Default;
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Failure to persist non-sensitive presentation preferences must not prevent application shutdown.")]
    public bool TrySave(ApplicationPreferencesSnapshot preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(
                    temporaryPath,
                    JsonSerializer.Serialize(preferences, SerializerOptions));
                File.Move(temporaryPath, _path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException or
                ArgumentException)
        {
            return false;
        }
    }
}
