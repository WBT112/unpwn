namespace Unpwn.App.Presentation;

public sealed class LanguageOptionViewModel : ObservableObject
{
    private string _displayName;

    public LanguageOptionViewModel(string code, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        Code = code;
        _displayName = displayName;
    }

    public string Code { get; }

    public string DisplayName
    {
        get => _displayName;
        private set => SetProperty(ref _displayName, value);
    }

    public void UpdateDisplayName(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        DisplayName = displayName;
    }
}
