# Security CI Gates

unpwn treats security regressions as blocking build failures. These checks complement ordinary unit/integration coverage; none of them changes the product security model or makes browser activity authoritative recovery evidence.

## Blocking gates

### .NET security analyzers

`.globalconfig` sets the built-in .NET analyzer `Security` category to `error`. The repository keeps the normal `Recommended` analysis mode for the broader analyzer set, but applicable Security-category diagnostics are not limited to that subset.

Do not globally disable a security analyzer ID to make CI green. A narrowly scoped suppression is allowed only when the finding is demonstrably not applicable and the pull request documents the rule ID, affected code, reasoning, and why the suppression is narrower than changing the repository-wide policy.

### NuGet vulnerability audit

`Directory.Build.props` enables NuGet audit for direct and transitive packages (`NuGetAuditMode=all`) and sets the blocking threshold to `moderate`. Warnings are treated as errors by the repository build policy. The Linux CI job repeats restore in a clearly named security gate and records the scope/threshold in the job summary.

Do not raise the repository-wide threshold to work around one advisory. If a fixed dependency cannot yet be used, a temporary advisory-specific exception requires:

- the advisory identifier and affected package/version;
- a linked issue explaining why the fixed version cannot be adopted immediately;
- impact analysis and compensating controls;
- an owner/review date or explicit removal condition;
- the narrowest supported NuGet advisory suppression mechanism.

There are no standing broad vulnerability-audit exemptions.

### Native and unsafe boundary

`eng/verify-native-interop.ps1` makes expansion of unmanaged memory boundaries an explicit review event.

Current allowlist:

- unsafe-enabled project: `src/Unpwn.App/Unpwn.App.csproj`;
- native interop source: `src/Unpwn.App/Services/RecoveryBrowserPlatformAdapter.cs`.

The gate rejects additional `AllowUnsafeBlocks` projects and unmanaged/import/raw-memory constructs outside the approved source boundary. Expanding the allowlist requires a security-focused issue/PR explaining why managed alternatives are insufficient, how lifetime/bounds/error handling are controlled, and what regression tests cover the new boundary.

### SecurityRegression test category

The Linux CI job runs a fast deterministic test pass filtered by `Category=SecurityRegression` before the complete coverage run. The sentinel suite covers representative invariants for:

- vault KDF/record resource limits;
- CSV parser/input complexity and secret-retention limits;
- public-network-only recovery targets;
- exact Recovery Browser origin/scheme enforcement;
- generated-credential lifecycle constraints;
- Unix plaintext export permissions;
- Linux Recovery Browser profile/marker permissions.

Property-style cases use fixed seeds and small bounds. Pull-request CI must not intentionally allocate huge payloads, contact real private networks, or rely on nondeterministic fuzzing. Larger fuzz campaigns, if added later, belong in a separate scheduled workflow.

### CodeQL

`.github/workflows/codeql.yml` is the single repository-maintained CodeQL advanced setup. It analyzes C# on pull requests, pushes to `main`, and a weekly schedule using the `security-extended` query suite. GitHub Actions are pinned to full commit SHAs.

Do not add a second CodeQL workflow or overlapping repository-maintained setup. Changes to the CodeQL setup must keep minimal workflow permissions and pinned actions.

### Secret-safe artifacts

The normal CI artifact scan remains mandatory. Deterministic test-secret markers (`UNPWN_TEST_SECRET_...`) must not appear in retained/uploaded test artifacts. Real credentials, reset links, cookies, MFA secrets, or account data are never valid CI fixtures.

## Local reproduction

Run the security-specific checks with:

```pwsh
./eng/verify-native-interop.ps1
dotnet restore unpwn.slnx --force-evaluate
dotnet build unpwn.slnx --configuration Release --no-restore
dotnet test unpwn.slnx --configuration Release --no-build --filter "Category=SecurityRegression"
```

The full CI/test suite is still required before merge; a green SecurityRegression subset is not a substitute for the complete tests.
