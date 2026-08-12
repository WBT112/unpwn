using Unpwn.App.Services;
using Xunit;

namespace Unpwn.App.Tests.Services;

public sealed class ApplicationPreferencesTests
{
    [Fact]
    public void FirstRunDefaultsToMaximizedDesktopWindow()
    {
        var normalized = MainWindowPresentationPolicy.Normalize(
            ApplicationPreferencesSnapshot.Default.MainWindow,
            1920,
            1080);

        Assert.True(normalized.IsMaximized);
        Assert.Equal(1080, normalized.NormalWidth);
        Assert.Equal(720, normalized.NormalHeight);
    }

    [Fact]
    public void ValidNormalWindowStateIsPreserved()
    {
        var normalized = MainWindowPresentationPolicy.Normalize(
            new MainWindowPresentationPreferences(1440, 900, IsMaximized: false),
            2560,
            1440);

        Assert.False(normalized.IsMaximized);
        Assert.Equal(1440, normalized.NormalWidth);
        Assert.Equal(900, normalized.NormalHeight);
    }

    [Fact]
    public void OversizedOrInvalidStateIsClampedWithoutPersistingPosition()
    {
        var normalized = MainWindowPresentationPolicy.Normalize(
            new MainWindowPresentationPreferences(double.NaN, 5000, IsMaximized: false),
            1366,
            768);

        Assert.False(normalized.IsMaximized);
        Assert.Equal(1080, normalized.NormalWidth);
        Assert.Equal(768, normalized.NormalHeight);
        Assert.DoesNotContain(
            "Position",
            typeof(MainWindowPresentationPreferences).GetProperties().Select(property => property.Name));
    }

    [Fact]
    public void PreferencesRoundTripOnlyNonSensitivePresentationState()
    {
        var root = Path.Combine(Path.GetTempPath(), $"unpwn-preferences-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "preferences.json");
        try
        {
            var store = new FileApplicationPreferences(path);
            var expected = new ApplicationPreferencesSnapshot(
                new MainWindowPresentationPreferences(1280, 800, IsMaximized: false));

            Assert.True(store.TrySave(expected));
            Assert.Equal(expected, store.Load());

            var json = File.ReadAllText(path);
            Assert.DoesNotContain("vault", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("account", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("credential", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("session", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void MalformedPreferencesFailSoftToFirstRunDefaults()
    {
        var root = Path.Combine(Path.GetTempPath(), $"unpwn-preferences-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "preferences.json");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(path, "{not-json");
            var store = new FileApplicationPreferences(path);

            Assert.Equal(ApplicationPreferencesSnapshot.Default, store.Load());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
