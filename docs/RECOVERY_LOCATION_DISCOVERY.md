# Recovery Location Discovery

## Purpose

Recovery-location discovery identifies a safe official destination for starting a credential change or recovery action. It does not perform the recovery action, submit forms, infer success, or transmit account credentials.

The application contract is `IRecoveryLocationDiscoveryService`. Its HTTP implementation belongs to `Unpwn.Automation`, because location discovery is an automation-assistance adapter rather than general infrastructure. The result is a structured `RecoveryNavigationHandoff` consumed by the managed Recovery Browser security boundary. The operating-system browser uses the same validated handoff only as an explicit fallback.

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
- disables the system proxy for this narrow discovery request so the public-target connection policy remains bound to the destination actually opened by unpwn
- does not submit a password-reset request or trigger provider email

A direct discovery result is accepted only for `200 OK`. Other successful status codes, including `204 No Content`, are not treated as a usable browser destination.

Opening or resolving this endpoint never marks a recovery action complete.

## URL, network-target, and redirect validation

Every candidate destination must be an absolute HTTPS URL without embedded user information.

The production discovery path also applies a public-network-only egress policy before every HTTP request. It rejects:

- localhost and local-name destinations;
- loopback and unspecified addresses;
- RFC1918/private and IPv6 unique-local addresses;
- link-local and site-local addresses;
- multicast addresses;
- shared, documentation, benchmark, reserved, and other explicitly non-public IPv4 ranges covered by the repository policy.

Literal IP addresses are checked directly. Host names are resolved through the production DNS resolver and the request is allowed only when the resolution is non-empty and every returned address is publicly routable under the policy. A mixed public/private DNS answer therefore fails closed.

The same target check runs again for every accepted HTTP redirect before the next request is sent. Redirects are followed manually and are limited to a small bounded chain. Each target must:

- remain HTTPS;
- match the account origin or one of the exact repository-reviewed expected origins;
- pass the public-network target policy;
- contain exactly one syntactically valid redirect location;
- remain within the configured redirect limit.

The default `SocketsHttpHandler` also resolves the connection target again inside its `ConnectCallback` and connects directly to one of those freshly checked IP addresses. It does not hand the hostname back to a second implicit DNS lookup for the socket connection. If resolution changes from public during preflight to a disallowed address at connection time, the connection fails instead of following the changed result. TLS certificate and hostname validation remain owned by the HTTP stack for the original HTTPS host.

An insecure redirect, an unexpected origin, a disallowed network destination, a missing or malformed `Location` header, an unsupported response, a timeout, or a transport failure is represented as a controlled discovery failure. Source exception messages are not exposed through the result. A valid reviewed provider location remains the normal fallback; otherwise the caller must show manual guidance rather than guess another URL.

Exact origin matching is intentional. Subdomains are not trusted implicitly; a provider workflow must list every expected origin explicitly.

Synthetic tests may inject their own HTTP transport, DNS resolver, and network-target policy. That injection is the only supported way to exercise reserved/local synthetic destinations without weakening the production policy, and the normal regression suite never contacts real private networks.

## Provider fallback

A repository-defined `RecoveryLocationDefinition` is accepted only when:

- its URL is absolute HTTPS
- all expected origins are valid HTTPS origins
- the destination origin appears in the expected-origin list

When standards-based discovery fails and a valid reviewed provider location exists, the result uses `ProviderFallback` and retains the structured fallback reason. If no safe fallback exists, the caller receives a failure and must show manual guidance rather than guessing a URL.

The generic unsupported-provider workflow deliberately has no provider fallback and no trusted-origin metadata. For its authenticated password-change action, discovery may derive only the standard endpoint from the validated HTTPS origin of the account URL. A successful handoff remains visible as part of the guided start transaction. Password-reset and manual-recovery destinations are never inferred from an unknown provider ID, name, or arbitrary path.

## Visible navigation handoff

A successful result contains:

- the normalized destination URL
- the actual destination origin
- the complete expected-origin allowlist
- the resolution source
- a mandatory visible-confirmation flag

The workflow UI must display the destination and expected origin before navigation. Opening either the embedded or fallback browser, returning, redirecting, or waiting is never evidence that the provider action succeeded.

The final destination is transient navigation data. It must not be copied into audit events or general diagnostics. Redirect diagnostics contain only normalized origins and a hop count; paths, queries, fragments, reset tokens, and other URL values are not retained.

The discovery egress policy protects only the narrow HTTP request used to resolve `/.well-known/change-password`. It is not a general host firewall, malware defense, or browser sandbox, and it does not make arbitrary provider page content trusted.

## Known standard limitation

The current resolver handles HTTP redirects and direct `200 OK` responses. It does not parse HTML meta-refresh instructions. A provider that relies on meta refresh must have a repository-reviewed provider location or fall back to manual guidance. This limitation must not be hidden by treating arbitrary HTML as a successful action.

## Testing

Tests live in `Unpwn.Automation.Tests` and use injected synthetic HTTP/DNS boundaries. They cover:

- provider-first and provider-only selection;
- same-origin and explicitly allowed cross-origin redirects;
- insecure, malformed, and unexpected redirects;
- literal local/private/link-local/multicast target rejection;
- DNS results containing private addresses and mixed public/private answers;
- public DNS results through deterministic fake resolution;
- redirect-target revalidation before the next request;
- missing redirect locations;
- redirect limits;
- unsupported responses, including `204 No Content`;
- network failures and cancellation;
- provider fallback;
- sanitized redirect-origin diagnostics;
- absence of credentials and account-path data in requests.

No live provider or real private-network dependency is used in the normal test suite.
