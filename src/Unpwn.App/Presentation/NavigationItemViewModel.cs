namespace Unpwn.App.Presentation;

public sealed record NavigationItemViewModel(
    AppRoute Route,
    string Label,
    string Description,
    string Symbol,
    bool IsEnabled = true);
