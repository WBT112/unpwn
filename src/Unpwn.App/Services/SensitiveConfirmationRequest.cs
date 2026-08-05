namespace Unpwn.App.Services;

public sealed class SensitiveConfirmationRequest
{
    public SensitiveConfirmationRequest(
        string action,
        string affectedItem,
        string consequence,
        string confirmLabel,
        bool isDestructive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(affectedItem);
        ArgumentException.ThrowIfNullOrWhiteSpace(consequence);
        ArgumentException.ThrowIfNullOrWhiteSpace(confirmLabel);

        Action = action;
        AffectedItem = affectedItem;
        Consequence = consequence;
        ConfirmLabel = confirmLabel;
        IsDestructive = isDestructive;
    }

    public string Action { get; }

    public string AffectedItem { get; }

    public string Consequence { get; }

    public string ConfirmLabel { get; }

    public bool IsDestructive { get; }

    public string RiskLabel => IsDestructive ? "DESTRUCTIVE ACTION" : "SENSITIVE ACTION";
}
