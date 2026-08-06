namespace Unpwn.App.Services;

public sealed class SensitiveConfirmationRequest
{
    public SensitiveConfirmationRequest(
        string action,
        string affectedItem,
        string consequence,
        string confirmLabel,
        string riskLabel,
        bool isDestructive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(affectedItem);
        ArgumentException.ThrowIfNullOrWhiteSpace(consequence);
        ArgumentException.ThrowIfNullOrWhiteSpace(confirmLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(riskLabel);

        Action = action;
        AffectedItem = affectedItem;
        Consequence = consequence;
        ConfirmLabel = confirmLabel;
        RiskLabel = riskLabel;
        IsDestructive = isDestructive;
    }

    public string Action { get; }

    public string AffectedItem { get; }

    public string Consequence { get; }

    public string ConfirmLabel { get; }

    public string RiskLabel { get; }

    public bool IsDestructive { get; }
}
