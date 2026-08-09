# unpwn Roadmap

The roadmap prioritizes a trustworthy guided recovery flow over broad automation. Detailed tasks and acceptance criteria live in GitHub issues.

## Current foundation

The repository already contains the main technical foundations for the MVP:

- platform-neutral recovery domain and state machines;
- encrypted local Recovery Vault;
- trusted-device and vault-entry flow;
- recovery session and risk-first dashboard;
- account inventory, roles, dependencies, and deterministic planning;
- reviewed CSV import;
- generated-credential lifecycle and secure export core;
- safe recovery-location discovery;
- multilingual Avalonia presentation foundation;
- atomic workspace persistence for the critical recovery state;
- canonical per-account recovery execution state.

## Current focus

### Guided account recovery

The next major product step is [Issue #34](https://github.com/WBT112/unpwn/issues/34): turn the existing recovery state and provider workflows into the user-facing, action-by-action account recovery experience.

The UI must explain why an account is recommended, keep prerequisites and unresolved risks visible, show official recovery locations and expected origins, and require explicit confirmation of completion criteria. Opening a provider page never proves success.

## Before the MVP release

After guided account recovery, the remaining MVP work centers on:

- credential export and cleanup UX;
- completion review and final report;
- remaining resilience/error-handling work from Issue #37;
- accessibility and minimum-window verification;
- additional reviewed provider workflows and end-to-end integration coverage;
- release packaging and security review.

## Later

Possible later work includes:

- macOS and Linux packaging;
- more provider workflows and password-manager formats;
- more reviewed GUI languages and RTL support;
- improved dependency suggestions and recommendations;
- carefully bounded browser assistance.

Automation remains secondary to clear recovery guidance, dependency-aware ordering, and honest progress reporting.

For product scope see [Vision](VISION.md). For the detailed issue sequence use the repository's GitHub issues and the MVP UI epic.
