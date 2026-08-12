# unpwn

**unpwn** is an open-source, local-first recovery assistant for people who suspect that their digital identity has been compromised.

It helps users organize affected accounts, prioritize critical identities and dependencies, work through provider recovery actions step by step, track unresolved risks, and move newly generated credentials into an established password manager.

The desktop recovery flow includes a managed Recovery Browser so the provider website, current recovery guidance, explicit completion checklist, and credential handoff can remain in one workspace. Browser activity is never treated as proof that a recovery action succeeded.

unpwn is **not** an antivirus, malware scanner, password manager, general-purpose web browser, or autonomous account-recovery bot.

> **Status:** active development. There is no supported production release yet.

## Documentation

- [User Guide](docs/USER_GUIDE.md) — short recovery walkthrough
- [Documentation Index](docs/README.md) — product, architecture, security, workflow, vault, and engineering docs
- [Recovery Browser](docs/RECOVERY_BROWSER.md) — embedded browser security and session boundaries
- [Security Policy](SECURITY.md) — limitations and vulnerability reporting
- [Roadmap](docs/ROADMAP.md) — current development direction
- [Contributing](CONTRIBUTING.md) — development and pull-request workflow

## Build and run

Requires the .NET 10 SDK.

```shell
dotnet restore unpwn.slnx
dotnet test unpwn.slnx
dotnet run --project src/Unpwn.App/Unpwn.App.csproj
```

See [Contributing](CONTRIBUTING.md) for the full development checks.

## License

unpwn is licensed under the **GNU Affero General Public License v3.0 (AGPL-3.0)**. See [LICENSE](LICENSE).
