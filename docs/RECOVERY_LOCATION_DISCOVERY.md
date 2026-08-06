# Recovery Location Discovery

## Purpose

Recovery-location discovery identifies a safe official destination for starting a credential change or recovery action. It does not perform the recovery action, submit forms, infer success, or transmit account credentials.

The application contract is `IRecoveryLocationDiscoveryService`. It returns a structured `RecoveryNavigationHandoff` that a later workflow screen can display before opening an external browser.

## Selection policies

A request chooses one explicit policy:

- `WellKnownFirst`: try `/.well-known/change-password`, then fall back to a reviewed provider location
- `ProviderDefinedFirst`: use the reviewed provider location when it is valid; otherwise try the standard endpoint
- `ProviderDefinedOnly`: do not make a network request and use only the reviewed provider location

This keeps provider-specific exceptions explicit. A workflow can prefer a repository-reviewed URL where the standard endpoint is unsuitable, while generic sites can use standards-based discovery first.

## `/.well-known/change-password`

The standard endpoint is treated only as location discovery.

unpwn constructs the request from the HTTPS origin of the supplied account URL:

```text
https://example.test/.well-known/change-password
```

Paths, queries, fragments, usernames, and other account-specific URL data are not copied into the discovery request. The request:

- uses `GET`
- has no request body
- sends no `Authorization`, `Cookie`, or `Referer` header
- disables automatic redirects and cookie storage in the default HTTP handler
- does not submit a password-reset request or trigger provider email

Opening or resolving this endpoint never marks a recovery action complete.

## URL and redirect validation

Every candidate destination must be an absolute HTTPS URL without embedded user information.

Redirects are followed manually and are limited to a small bounded chain. Each target must:

- remain HTTPS
- match the account origin or one of the exact repository-reviewed expected origins
- contain a valid redirect location
- remain within the configured redirect limit

An insecure redirect, an unexpected origin, a missing `Location` header, an unsupported response, a timeout, or a transport failure is represented by a stable language-neutral code. Source exception messages are not exposed through the result.

Exact origin matching is intentional. Subdomains are not trusted implicitly; a provider workflow must list every expected origin explicitly.

## Provider fallback

A repository-defined `RecoveryLocationDefinition` is accepted only when:

- its URL is absolute HTTPS
- all expected origins are valid HTTPS origins
- the destination origin appears in the expected-origin list

When standards-based discovery fails and a valid reviewed provider location exists, the result uses `ProviderFallback` and retains the structured fallback reason. If no safe fallback exists, the caller receives a failure and must show manual guidance rather than guessing a URL.

## Visible navigation handoff

A successful result contains:

- the normalized destination URL
- the actual destination origin
- the complete expected-origin allowlist
- the resolution source
- a mandatory visible-confirmation flag

The workflow UI must display the destination and expected origin before navigation. A browser open, browser return, redirect, or elapsed time is never evidence that the external action succeeded.

## Testing

Tests use injected synthetic `HttpMessageHandler` fixtures. They cover:

- provider-first and provider-only selection
- same-origin and explicitly allowed cross-origin redirects
- insecure and unexpected redirects
- missing redirect locations
- redirect limits
- unsupported responses and network failures
- provider fallback
- cancellation
- absence of credentials and account-path data in requests

No live provider dependency is used in the normal test suite.
