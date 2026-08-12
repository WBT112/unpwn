# Contributing to unpwn

unpwn is security-sensitive software. Prefer small, reviewable changes with clear reasoning and tests.

Coding agents must also follow [AGENTS.md](AGENTS.md).

## Before starting

For non-trivial work, reference or open an issue that defines the problem and scope. Check the [documentation index](docs/README.md) and read the documents relevant to the area you are changing.

Never put real credentials, reset links, cookies, MFA secrets, tokens, personal account data, or sensitive screenshots into issues, commits, tests, logs, or pull requests.

## Pull requests

A pull request should:

- address one coherent problem;
- preserve the documented architecture and security boundaries;
- include regression or feature tests where behavior changes;
- update the canonical documentation when behavior or architecture changes;
- keep user-facing text localizable and canonical state language-neutral;
- describe what was actually tested.

Do not claim checks passed unless they were run.

## Local checks

Requires the .NET 10 SDK.

```shell
dotnet restore unpwn.slnx
dotnet build unpwn.slnx --no-restore
dotnet test unpwn.slnx --no-build
dotnet format unpwn.slnx --no-restore --verify-no-changes --severity info
```

The authoritative CI, synthetic-provider, Recovery Browser, artifact, and secret-scanning rules are in [Testing Strategy](docs/TESTING.md).

## Specialized changes

### Recovery workflows

Follow [Recovery Workflows](docs/RECOVERY_WORKFLOWS.md). Provider changes require reviewed official locations, canonical workflow data, completion criteria, validation, and tests. Do not add CAPTCHA, MFA, identity-verification, rate-limit, or ownership-check bypasses.

### Localization

Follow [Localization](docs/LOCALIZATION.md). User-facing text belongs in version-controlled presentation resources. Translated text must never control workflow execution, parsing, cryptography, authorization, or persisted state.

### Vault, credentials, and cryptography

Follow [Vault Security](docs/VAULT_SECURITY.md), [Threat Model](docs/THREAT_MODEL.md), and the relevant credential/persistence documents. Do not invent custom cryptographic primitives or expose secrets through diagnostics.

### Recovery Browser and UI

Follow [Recovery Browser Security Boundary](docs/RECOVERY_BROWSER.md) and [UI Foundation](docs/UI_FOUNDATION.md). Browser observations must never become recovery truth. Security meaning must not depend on color alone, longer localized text must remain usable, and recovery logic belongs outside code-behind.

## Security reports

Do not publish exploitable vulnerability details or sensitive reproduction data. Follow [SECURITY.md](SECURITY.md).

## License

Contributions are licensed under the repository's GNU Affero General Public License v3.0.
