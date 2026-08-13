# unpwn Architecture

## Goals

unpwn is a modular, local-first desktop application for account-recovery orchestration. The architecture keeps the recovery domain independent from UI, storage, operating-system APIs, localization, providers, and browser-host implementation details.

Windows is the first target platform. Core recovery logic must remain portable to macOS and Linux.

## Technology

- C# / .NET 10 (LTS)
- Avalonia UI
- SQLite
- Argon2id and AES-256-GCM in the Recovery Vault
- .NET resource files for presentation localization
- Avalonia `NativeWebView` for the embedded Recovery Browser host

Detailed cryptographic rules live in [Vault Security](VAULT_SECURITY.md); localization rules live in [Localization](LOCALIZATION.md).

## Projects

```text
Unpwn.App
  Desktop UI, MVVM presentation, navigation, localization, composition root

Unpwn.Application
  Application services and use cases

Unpwn.Core
  Recovery domain, state machines, account categories, execution state, progress

Unpwn.Infrastructure
  General infrastructure and OS integration boundary

Unpwn.Vault
  Encrypted Recovery Vault, keys, records, generated credentials

Unpwn.Automation
  Recovery-location discovery and read-only provider smoke checks

Unpwn.Import
  Platform-neutral import parsing and mapping

Unpwn.Export
  Credential export formats

Unpwn.Providers
  Repository-controlled provider workflow definitions
```

## Dependency direction

Dependencies point inward:

```text
Unpwn.App
 ├── Infrastructure ─┐
 ├── Vault ──────────┤
 ├── Automation ─────┤
 ├── Import ─────────┼──> Unpwn.Application ──> Unpwn.Core
 ├── Export ─────────┤
 └── Providers ──────┘
```

`Unpwn.Core` must not depend on Avalonia, SQLite, browser engines, operating-system APIs, localization resources, or provider-specific infrastructure. Architecture tests enforce the project-reference boundary.

`Unpwn.App` is the composition root. View models and UI-facing services use constructor injection; code-behind is limited to Avalonia-specific bridging such as native file pickers, dialog results, focus hooks, and the native Recovery Browser surface. See [UI Foundation](UI_FOUNDATION.md).

## Canonical boundaries

### Recovery domain

Canonical accounts, account categories, actions, statuses, progress, and audit semantics live in the platform-neutral domain. Presentation code must not create a parallel recovery state machine. The repository-controlled account-classification catalog is platform-neutral and offline-only.

See [Data Model](DATA_MODEL.md) and [Account Recovery Execution](ACCOUNT_RECOVERY_EXECUTION.md).

### Recovery workflows

Providers describe what must be done for a service. Generic orchestration owns execution state, category-based ordering, and progress. Account category determines when work is recommended; provider workflow determines how it is performed. Provider code does not own category classification, vault cryptography, localization, browser-session state, or generic browser automation.

See [Recovery Workflows](RECOVERY_WORKFLOWS.md).

### Persistence and vault

Sensitive recovery state is stored in the encrypted local Recovery Vault. Logically related workspace changes are persisted atomically where required. Storage failure must not be represented as saved work.

See [Vault Security](VAULT_SECURITY.md) and [Workspace Persistence](WORKSPACE_PERSISTENCE.md).

### Localization

Localization is a presentation concern. Workflow IDs, action IDs, error codes, URLs, record identifiers, serialized values, and security decisions remain language-neutral.

See [Localization](LOCALIZATION.md).

### Recovery Browser

The embedded Recovery Browser consumes the validated `RecoveryNavigationHandoff`; it does not rediscover or infer provider destinations. The platform-neutral origin/security contract lives in `Unpwn.Application`, while Avalonia and native WebView2/WPE WebKit details remain in `Unpwn.App` behind browser-host and platform-adapter boundaries.

Browser observations are transient presentation context and have no dependency path to canonical recovery transitions. Navigation, redirects, browser close, form state, or credential insertion cannot complete an action or confirm provider success.

`RecoveryBrowserSessionLifecycle` owns temporary account-isolated browser-profile state separately from the encrypted workspace. Account association exists only in memory; persistent markers are opaque and support cleanup retry/orphan detection only. Browser resources are cleared and released before profile-directory deletion. The browser lifecycle never writes recovery execution state.

Provider-reviewed browser assistance and future provider-specific action automation share a scoped `RecoveryBrowserActionAutomationContract`. The contract binds a stable adapter identifier to one provider/action pair, the canonical `AutomationSupport` level, browser content mode, exact expected origins, and whether the adapter is assist-only or may mutate provider state. The current credential-insertion path is registered through this boundary as `ASSISTED` and assist-only.

The contract is an extension and validation boundary, not authorization to execute arbitrary browser code. `NONE` and `NAVIGATION` actions cannot use it, an `ASSISTED` adapter cannot claim provider mutation, synthetic adapters remain HTTP(S)-loopback-only, and production adapter origins remain HTTPS-only. The workflow validator still rejects `AUTOMATED` workflow claims today. Enabling a real `AUTOMATED` action requires a separate provider/action implementation and security review that deliberately changes that policy; browser observations must still never become canonical proof of recovery success.

Manual Reveal/Copy remains the safe default, and arbitrary provider DOM is never used to infer a password field or automation target.

See [Recovery Location Discovery](RECOVERY_LOCATION_DISCOVERY.md), [Recovery Browser Security Boundary](RECOVERY_BROWSER.md), and [Generated Credentials](GENERATED_CREDENTIALS.md).

## Documentation ownership

Detailed rules should live in the specialized documents listed in the [documentation index](README.md). This architecture document defines module boundaries and dependency direction rather than repeating complete vault, workflow, browser, localization, or testing specifications.
