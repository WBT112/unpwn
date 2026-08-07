namespace Unpwn.Core;

public enum GeneratedCredentialStage
{
    Generated,
    Used,
    Confirmed,
    Exported,
    Deleted,
}

public enum GeneratedCredentialAuditEventType
{
    Generated,
    Used,
    Confirmed,
    Exported,
    Deleted,
}

public sealed record CredentialGenerationPolicy(
    int Length,
    bool IncludeLowercase,
    bool IncludeUppercase,
    bool IncludeDigits,
    bool IncludeSymbols)
{
    public static CredentialGenerationPolicy Default { get; } = new(
        Length: 24,
        IncludeLowercase: true,
        IncludeUppercase: true,
        IncludeDigits: true,
        IncludeSymbols: true);

    public void Validate()
    {
        if (Length is < 12 or > 128)
        {
            throw new ArgumentOutOfRangeException(nameof(Length), "Generated credentials must be between 12 and 128 characters.");
        }

        var selectedSets = Convert.ToInt32(IncludeLowercase) +
            Convert.ToInt32(IncludeUppercase) +
            Convert.ToInt32(IncludeDigits) +
            Convert.ToInt32(IncludeSymbols);
        if (selectedSets == 0 || Length < selectedSets)
        {
            throw new ArgumentException("The generation policy must select at least one character set and fit every selected set.");
        }
    }
}

public sealed record GeneratedCredentialReference(Guid CredentialId, Guid AccountId)
{
    public void Validate()
    {
        if (CredentialId == Guid.Empty || AccountId == Guid.Empty)
        {
            throw new InvalidOperationException("A generated credential reference requires opaque credential and account identifiers.");
        }
    }
}

public sealed record GeneratedCredentialAuditEvent(
    Guid OperationId,
    GeneratedCredentialAuditEventType EventType,
    DateTimeOffset OccurredAt)
{
    public void Validate()
    {
        if (OperationId == Guid.Empty)
        {
            throw new InvalidOperationException("A generated credential audit event requires an operation identifier.");
        }
    }
}

public sealed record GeneratedCredentialMetadata(
    Guid CredentialId,
    Guid AccountId,
    DateTimeOffset GeneratedAt,
    DateTimeOffset? UsedAt,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? ExportedAt,
    int ExportCount,
    DateTimeOffset? DeletedAt,
    long Revision,
    GeneratedCredentialAuditEvent[] AuditEvents)
{
    public GeneratedCredentialReference Reference => new(CredentialId, AccountId);

    public GeneratedCredentialStage Stage => DeletedAt is not null
        ? GeneratedCredentialStage.Deleted
        : ExportedAt is not null
            ? GeneratedCredentialStage.Exported
            : ConfirmedAt is not null
                ? GeneratedCredentialStage.Confirmed
                : UsedAt is not null
                    ? GeneratedCredentialStage.Used
                    : GeneratedCredentialStage.Generated;

    public bool IsDeleted => DeletedAt is not null;

    public static GeneratedCredentialMetadata Create(
        Guid credentialId,
        Guid accountId,
        Guid operationId,
        DateTimeOffset occurredAt)
    {
        var metadata = new GeneratedCredentialMetadata(
            credentialId,
            accountId,
            occurredAt,
            UsedAt: null,
            ConfirmedAt: null,
            ExportedAt: null,
            ExportCount: 0,
            DeletedAt: null,
            Revision: 0,
            AuditEvents:
            [
                new GeneratedCredentialAuditEvent(
                    operationId,
                    GeneratedCredentialAuditEventType.Generated,
                    occurredAt),
            ]);
        metadata.Validate();
        return metadata;
    }

    public GeneratedCredentialMetadata MarkUsed(Guid operationId, DateTimeOffset occurredAt) =>
        ApplyIdempotent(
            operationId,
            GeneratedCredentialAuditEventType.Used,
            occurredAt,
            current => current with { UsedAt = occurredAt });

    public GeneratedCredentialMetadata Confirm(Guid operationId, DateTimeOffset occurredAt)
    {
        EnsureActive();
        if (UsedAt is null)
        {
            throw new InvalidOperationException("A generated credential must be recorded as used before it can be confirmed.");
        }

        return ApplyIdempotent(
            operationId,
            GeneratedCredentialAuditEventType.Confirmed,
            occurredAt,
            current => current with { ConfirmedAt = occurredAt });
    }

    public GeneratedCredentialMetadata MarkExported(Guid operationId, DateTimeOffset occurredAt) =>
        ApplyIdempotent(
            operationId,
            GeneratedCredentialAuditEventType.Exported,
            occurredAt,
            current => current with
            {
                ExportedAt = occurredAt,
                ExportCount = current.ExportCount + 1,
            });

    public GeneratedCredentialMetadata Delete(Guid operationId, DateTimeOffset occurredAt)
    {
        if (HasOperation(operationId, GeneratedCredentialAuditEventType.Deleted))
        {
            return this;
        }

        EnsureTimestamp(occurredAt);
        var updated = this with
        {
            DeletedAt = occurredAt,
            Revision = Revision + 1,
            AuditEvents = AppendAudit(
                operationId,
                GeneratedCredentialAuditEventType.Deleted,
                occurredAt),
        };
        updated.Validate();
        return updated;
    }

    public bool HasOperation(Guid operationId, GeneratedCredentialAuditEventType eventType) =>
        operationId != Guid.Empty && AuditEvents.Any(auditEvent =>
            auditEvent.OperationId == operationId && auditEvent.EventType == eventType);

    public void Validate()
    {
        Reference.Validate();
        ArgumentNullException.ThrowIfNull(AuditEvents);
        if (Revision < 0 || ExportCount < 0)
        {
            throw new InvalidOperationException("Generated credential revisions and export counts cannot be negative.");
        }

        foreach (var auditEvent in AuditEvents)
        {
            auditEvent.Validate();
            if (auditEvent.OccurredAt < GeneratedAt)
            {
                throw new InvalidOperationException("A generated credential audit event predates generation.");
            }
        }

        if (AuditEvents
            .Select(auditEvent => (auditEvent.OperationId, auditEvent.EventType))
            .Distinct()
            .Count() != AuditEvents.Length)
        {
            throw new InvalidOperationException("Generated credential audit operations must be unique per event type.");
        }

        if (!AuditEvents.Any(auditEvent =>
                auditEvent.EventType == GeneratedCredentialAuditEventType.Generated &&
                auditEvent.OccurredAt == GeneratedAt))
        {
            throw new InvalidOperationException("Generated credential metadata requires its generation audit event.");
        }

        ValidateOptionalTimestamp(UsedAt);
        ValidateOptionalTimestamp(ConfirmedAt);
        ValidateOptionalTimestamp(ExportedAt);
        ValidateOptionalTimestamp(DeletedAt);
        if (ConfirmedAt is not null && UsedAt is null)
        {
            throw new InvalidOperationException("Confirmed generated credentials require a recorded use time.");
        }

        if ((ExportCount == 0) != (ExportedAt is null))
        {
            throw new InvalidOperationException("Generated credential export count and timestamp are inconsistent.");
        }

        if (ExportCount != AuditEvents.Count(auditEvent =>
                auditEvent.EventType == GeneratedCredentialAuditEventType.Exported))
        {
            throw new InvalidOperationException("Generated credential export count must match structured audit events.");
        }

        if ((DeletedAt is null) == AuditEvents.Any(auditEvent =>
                auditEvent.EventType == GeneratedCredentialAuditEventType.Deleted))
        {
            throw new InvalidOperationException("Generated credential deletion state and audit event are inconsistent.");
        }
    }

    private GeneratedCredentialMetadata ApplyIdempotent(
        Guid operationId,
        GeneratedCredentialAuditEventType eventType,
        DateTimeOffset occurredAt,
        Func<GeneratedCredentialMetadata, GeneratedCredentialMetadata> mutation)
    {
        if (HasOperation(operationId, eventType))
        {
            return this;
        }

        EnsureActive();
        EnsureTimestamp(occurredAt);
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException("A credential lifecycle mutation requires an operation identifier.", nameof(operationId));
        }

        var updated = mutation(this) with
        {
            Revision = Revision + 1,
            AuditEvents = AppendAudit(operationId, eventType, occurredAt),
        };
        updated.Validate();
        return updated;
    }

    private GeneratedCredentialAuditEvent[] AppendAudit(
        Guid operationId,
        GeneratedCredentialAuditEventType eventType,
        DateTimeOffset occurredAt)
    {
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException("A credential lifecycle mutation requires an operation identifier.", nameof(operationId));
        }

        return
        [
            .. AuditEvents,
            new GeneratedCredentialAuditEvent(operationId, eventType, occurredAt),
        ];
    }

    private void EnsureActive()
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("A deleted generated credential cannot be changed or revealed.");
        }
    }

    private void EnsureTimestamp(DateTimeOffset occurredAt)
    {
        if (occurredAt < GeneratedAt || AuditEvents.Any(auditEvent => auditEvent.OccurredAt > occurredAt))
        {
            throw new ArgumentOutOfRangeException(nameof(occurredAt), "Credential lifecycle timestamps must be monotonic.");
        }
    }

    private void ValidateOptionalTimestamp(DateTimeOffset? value)
    {
        if (value is { } timestamp && timestamp < GeneratedAt)
        {
            throw new InvalidOperationException("A generated credential lifecycle timestamp predates generation.");
        }
    }
}
