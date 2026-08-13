# unpwn Roadmap

The roadmap prioritizes a trustworthy guided recovery flow over broad automation. Detailed tasks and acceptance criteria belong in GitHub issues; this document describes the current product direction rather than repeating a closed issue sequence.

## Current foundation

The repository now contains the main end-to-end foundations for the desktop MVP:

- trusted-device gate and encrypted local Recovery Vault;
- incident intake, recovery session, risk-first dashboard, pause/lock/resume, and final completion review;
- account inventory, CSV import, simple account categories, and a deterministic recovery queue;
- reviewed provider workflows plus a clearly distinguished generic manual fallback;
- canonical per-account/action recovery execution with explicit completion criteria and unresolved-risk handling;
- generated-credential lifecycle, password-manager handoff, secure export, and cleanup tracking;
- assistant-first guided recovery UX;
- an integrated managed Recovery Browser with isolated temporary profiles, safe navigation boundaries, cleanup/orphan handling, and explicit external-browser fallback;
- bounded in-context credential assistance that remains manual by default and enables insertion only for explicitly reviewed provider/action contracts;
- multilingual Avalonia presentation, accessibility baseline, atomic workspace persistence, resilience tests, and cross-platform CI.

## Current focus: stabilization and release readiness

The next work should be driven by review and validation rather than adding another broad feature layer. Priorities are:

1. exercise representative full recovery journeys from a user's perspective and turn concrete defects or friction into focused issues;
2. complete security review of the current trust boundaries, especially vault, Recovery Browser, credential handoff, exports, and interrupted-work behavior;
3. keep provider workflows current and add provider-specific browser assistance only when the user value justifies the maintenance/security cost;
4. perform the documented Windows/NVDA and Ubuntu/Orca accessibility acceptance checks and minimum-window validation;
5. define supported installation, packaging, update, and release-signing behavior before calling the desktop build production-ready;
6. keep documentation, tests, dependencies, and obsolete experimental code lean as the implementation evolves.

The absence of an open issue is not evidence that a release is ready. New work should be created from observed product, security, accessibility, provider, or release-readiness gaps.

## Later

Possible later work includes:

- macOS packaging and broader Linux packaging validation;
- more reviewed provider workflows and password-manager formats;
- more reviewed GUI languages and RTL support;
- improved local category suggestions and recommendation explanations;
- carefully reviewed provider-specific credential insertion where it is stable enough to maintain;
- a separately researched bootable recovery environment for cases where the normal host cannot reasonably be trusted.

Automation remains secondary to clear guidance, category-aware ordering, explicit human confirmation, and honest reporting of unresolved risk.

For product scope see [Vision](VISION.md), for security boundaries see [Threat Model](THREAT_MODEL.md), and for the current browser architecture see [Recovery Browser Security Boundary](RECOVERY_BROWSER.md).
