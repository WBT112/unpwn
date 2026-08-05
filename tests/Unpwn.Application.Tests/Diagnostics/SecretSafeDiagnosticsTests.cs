using Unpwn.Application.Diagnostics;
using Xunit;

namespace Unpwn.Application.Tests.Diagnostics;

public sealed class SecretSafeDiagnosticsTests
{
    [Fact]
    public void FailureDiagnosticsDoNotExposeExceptionContents()
    {
        string[] syntheticSecrets =
        [
            "UNPWN_TEST_SECRET_PASSWORD_Correct-Horse-Battery-Staple",
            "UNPWN_TEST_SECRET_RESET_TOKEN_7ca2f72d8a4b",
            "UNPWN_TEST_SECRET_COOKIE_session-id-4f8e",
            "UNPWN_TEST_SECRET_MFA_RECOVERY_CODE_1234-5678",
        ];

        foreach (var syntheticSecret in syntheticSecrets)
        {
            var sink = new CapturingDiagnosticSink();
            var diagnostics = new SecretSafeDiagnostics(sink);
            var exception = new InvalidOperationException($"Provider response contained {syntheticSecret}");

            var safeException = diagnostics.ReportFailureAndCreateSafeException(
                DiagnosticOperation.VaultUnlock,
                exception);

            var diagnosticEvent = Assert.Single(sink.Events);
            var capturedLog = Render(diagnosticEvent);

            Assert.DoesNotContain(syntheticSecret, capturedLog, StringComparison.Ordinal);
            Assert.DoesNotContain(exception.Message, capturedLog, StringComparison.Ordinal);
            Assert.DoesNotContain(syntheticSecret, safeException.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(exception.Message, safeException.ToString(), StringComparison.Ordinal);
            Assert.Null(safeException.InnerException);
            Assert.Equal("UNPWN1001", diagnosticEvent.EventId);
            Assert.Equal("Vault unlock failed.", diagnosticEvent.Message);
            Assert.Equal(nameof(InvalidOperationException), diagnosticEvent.ExceptionType);
        }
    }

    [Fact]
    public void NullExceptionIsRejectedWithoutEmittingADiagnostic()
    {
        var sink = new CapturingDiagnosticSink();
        var diagnostics = new SecretSafeDiagnostics(sink);

        Assert.Throws<ArgumentNullException>(
            () => diagnostics.ReportFailureAndCreateSafeException(
                DiagnosticOperation.RecoveryAction,
                null!));
        Assert.Empty(sink.Events);
    }

    [Theory]
    [InlineData(DiagnosticOperation.VaultUnlock, "UNPWN1001", "Vault unlock failed.")]
    [InlineData(DiagnosticOperation.RecoverySessionLoad, "UNPWN1002", "Recovery session loading failed.")]
    [InlineData(DiagnosticOperation.RecoveryAction, "UNPWN1003", "Recovery action failed.")]
    [InlineData(DiagnosticOperation.CredentialExport, "UNPWN1004", "Credential export failed.")]
    [InlineData(DiagnosticOperation.VaultLock, "UNPWN1005", "Vault lock failed.")]
    [InlineData(DiagnosticOperation.VaultPasswordChange, "UNPWN1006", "Vault password change failed.")]
    public void EverySupportedOperationEmitsAStableSafeEvent(
        DiagnosticOperation operation,
        string expectedEventId,
        string expectedMessage)
    {
        var sink = new CapturingDiagnosticSink();
        var diagnostics = new SecretSafeDiagnostics(sink);

        var safeException = diagnostics.ReportFailureAndCreateSafeException(
            operation,
            new InvalidOperationException("UNPWN_TEST_SECRET_must-not-be-retained"));

        var diagnosticEvent = Assert.Single(sink.Events);
        Assert.Equal(expectedEventId, diagnosticEvent.EventId);
        Assert.Equal(expectedMessage, diagnosticEvent.Message);
        Assert.Equal(expectedMessage, safeException.Message);
        Assert.DoesNotContain("UNPWN_TEST_SECRET_", Render(diagnosticEvent), StringComparison.Ordinal);
        Assert.DoesNotContain("UNPWN_TEST_SECRET_", safeException.ToString(), StringComparison.Ordinal);
    }

    private static string Render(DiagnosticEvent diagnosticEvent) => string.Join(
        '|',
        diagnosticEvent.Severity,
        diagnosticEvent.Operation,
        diagnosticEvent.EventId,
        diagnosticEvent.Message,
        diagnosticEvent.ExceptionType);

    private sealed class CapturingDiagnosticSink : ISecretSafeDiagnosticSink
    {
        public List<DiagnosticEvent> Events { get; } = [];

        public void Write(DiagnosticEvent diagnosticEvent)
        {
            Events.Add(diagnosticEvent);
        }
    }
}
