# Sign in with Secret Keeper for .NET (SK login)

Passwordless sign-in for a website: the user scans a QR code with the
Secret Keeper app (or opens it with a button on the phone), confirms in
the app, and the server receives their `sk1…` address and decides who
gets in. The server and the app exchange encrypted envelopes; the server
sees only the user's address.

This is the .NET implementation of the same protocol as
[paymastech/sk-login](https://github.com/paymastech/sk-login) (Node:
core, NestJS module and browser widget). The servers are interchangeable:
the widget from that repository works with this server unchanged, and
the cryptography is verified against the app with the same test vectors.

| Package | What it is |
| --- | --- |
| [`Paymastech.SkLogin`](src/Paymastech.SkLogin) | The protocol without any web framework dependency: envelope cryptography (X25519, XChaCha20-Poly1305, BIP-39, bech32), requests, challenge code, verification, request store. `net7.0`, `net8.0`. |
| [`Paymastech.SkLogin.AspNetCore`](src/Paymastech.SkLogin.AspNetCore) | `AddSkLogin` + `MapSkLogin`: five endpoints for the widget and the app, QR as SVG. Minimal APIs, also suitable for apps built on MVC controllers. |
| [`examples/AspNetCoreDemo`](examples/AspNetCoreDemo) | A working application: module + widget + cookie session, plus a phone emulation for development. |

## Quick start

```bash
dotnet add package Paymastech.SkLogin.AspNetCore
```

Until the packages are published to NuGet, the `.nupkg` files are attached to the
[release](https://github.com/paymastech/sk-login-dotnet/releases/latest):
download both into a folder and add it as a source
(`dotnet nuget add source ./packages -n sk-login-local`).

```csharp
using Paymastech.SkLogin;
using Paymastech.SkLogin.AspNetCore;

builder.Services.AddSkLogin<User>(o =>
{
    o.Mnemonic = builder.Configuration["SK_SERVER_MNEMONIC"];   // 12 BIP-39 words, see "Secrets"
    o.Target = new SkLoginTarget
    {
        Id = "my-service",                                  // destination in the hub catalog
        Hub = "auth_secretkeeper",                          // the hub's target id in the app
        Owner = builder.Configuration["SK_OWNER_ADDRESS"],  // your own sk1… address, see "Onboarding"
        PublicUrl = "https://api.example.com",
    };
    // Who gets in: called once after confirmation, receives the sk1… address
    o.Access = async address =>
    {
        var user = await users.FindByAddressAsync(address);
        return user is null ? AccessDecision<User>.Denied("unknown-address") : AccessDecision<User>.Granted(user);
    };
    // The browser learned about admission: issue a session. The return value is merged into the JSON response.
    o.OnAuthenticated = async (user, http) =>
    {
        await http.SignInAsync(principalFor(user));        // or your own cookie/JWT
        return new { userId = user.Id };
    };
});

var app = builder.Build();
app.MapSkLogin<User>("/api/sk");
```

For access to the container there is an overload `AddSkLogin<User>((sp, o) => …)`.
The `login` endpoint reads the `text/plain` body itself; body size limits
and MVC binding do not apply to it.

Sign-in page: the widget comes from
[`@paymastech/sk-login-widget`](https://github.com/paymastech/sk-login/tree/main/packages/widget)
(a single file `sk-login-widget.global.js`, no framework; a copy lives in
`examples/AspNetCoreDemo/wwwroot`):

```html
<!-- from the hub (see "Hub"), or a self-hosted copy of sk-login-widget.global.js -->
<script src="https://auth.secretkeeper.net/widget.js"></script>
<button id="login">Sign in</button>
<script>
  const login = SkLoginWidget.mountSkLogin({
    apiBase: '/api/sk',
    lang: 'en',
    onSuccess: (extra) => location.reload(),   // extra = what OnAuthenticated returned
  });
  document.getElementById('login').onclick = () => login.open();
</script>
```

## Onboarding a new service (checklist)

The Secret Keeper app only sends envelopes to targets it knows. There are
two ways to become one:

- **Through the hub** (recommended): the app has a single built-in entry
  for the hub (`auth.secretkeeper.net`), and your service is registered in
  the hub's registry under a short `destination` id. No app release is
  needed. The hub relays the app's envelopes to your `login` endpoint and
  does not read them: they are encrypted to your server address.
- **Direct**: your id, `login` URL and server address are built into the
  app, which requires an app release per service.

Steps for the hub mode:

1. Generate a server mnemonic (once, keep it as a secret):
   `string.Join(' ', Identity.GenerateMnemonic())` or from the demo:
   `dotnet run --project examples/AspNetCoreDemo -- --mnemonic`.
2. Run the service with `Target = new SkLoginTarget { Id = "<destination>", Hub = "auth_secretkeeper", Owner = "<sk1…>", PublicUrl = … }`.
   `Id` is the short Latin name you want in the registry (`[a-z0-9_-]{1,32}`),
   `Hub` is the hub's target id in the app (`auth_secretkeeper` in
   production; the Secret Keeper team may give you a staging one), `Owner`
   is the `sk1…` address of your own Secret Keeper app (Settings, address).
3. Open `GET <PublicUrl>/api/sk/target`: it returns `id`, `hub`, `url`
   (your `login` endpoint), `serverAddress` (`sk1…`), `checkDigits` for
   verification by voice and `ownerHash` (the hash of `Owner`; the address
   itself is not published).
4. Open the hub (`https://auth.secretkeeper.net/`), sign in with the phone
   whose address you put into `Owner`, and submit the URL of your service.
   The catalog fetches `GET target`, checks that `ownerHash` matches the
   signed-in address and that `hub` names this hub, and creates the
   registry entry. From that moment the sign-in works with the released
   app; the app shows your host name until the hub owner approves the
   display name you propose in the catalog.
5. The `login` endpoint must be reachable from the internet over HTTPS: it
   is called by the hub, not by the browser.
6. If `serverAddress` changes (a new mnemonic), press "Re-check" on your
   entry in the catalog: envelopes are encrypted to this address.

In the direct mode steps 2-4 differ: run without `Hub` and `Owner`, and
the team adds the target to the app and ships a release; until then,
sign-in is tested with the phone emulation (see the demo, `DEMO_FAKE_PHONE=1`).

## Hub

The hub is a relay operated by the Secret Keeper team. With `Hub` set the
QR becomes `https://secretkeeper.net/auth?v=1&sid=…&target=<hub>&destination=<id>`.
The app asks the hub for the display name and server address of
`destination`, encrypts the usual envelope to your server, wraps it into an
outer envelope addressed to the hub, and posts it there. The hub decrypts
only the outer envelope (which authenticates the sender), forwards the
inner one to your `login` URL and returns your reply unchanged. Your server
sees exactly the same envelopes as in the direct mode, so the library does
not change behavior; only the QR does.

The hub also serves the browser widget, so you do not have to host it:

```html
<script src="https://auth.secretkeeper.net/widget.js"></script>
```

The hub keeps a catalog of registered services. Service owners sign in to
the catalog with Secret Keeper and register their service by URL (see the
checklist above); the hub owner moderates display names and can block an
entry. The entry is public at `GET <hub>/targets/<destination>`.

## Secrets and environment

- `SK_SERVER_MNEMONIC`: 12 words. The server keys are derived from it. A
  leak means the ability to impersonate the service to the app. Keep it in
  a secret manager / user-secrets, never log it. Instead of a mnemonic you
  can pass a ready `Identity` (`Identity.DeriveIdentityKeys`).
- `SK_OWNER_ADDRESS` (suggested name): the `sk1…` address of your own
  Secret Keeper app, passed as `Target.Owner`. Not a secret, but only its
  hash is published; it is what lets you manage the service entry in the
  hub catalog.
- The module makes no network calls: everything happens between your
  server, the browser and the user's phone.

## HTTP API

The default prefix is `/api/sk`. Browser routes: `init`, `status`,
`code`. Phone route: `login`. Service route: `target`.

| Method and path | Caller | Request | Response |
| --- | --- | --- | --- |
| `POST init` | browser | empty | `{ sid, payloadUrl, schemeUrl, expiresAt, ttlMs, qrSvg? }` |
| `POST login` | app | `text/plain`, envelope | challenge envelope as `text/plain` (for `sk-login`), `{ sent: true }` (for `sk-login-code`), `204` (for `sk-login-cancel`) or `4xx { error, message }` |
| `GET status?sid=` | browser | | `{ state, reason?, ...extra }`, `state`: `new`, `challenged`, `authenticated`, `denied`, `cancelled`, `expired` |
| `POST code` | browser | `{ sid, code }` | `{ ok: true, ...extra }`, `403 { denied, reason, message }` or `4xx { error, message }` |
| `GET target` | people | | `{ id, hub?, v, url, serverAddress, checkDigits, ownerHash? }` |

| `error` code | Status | When |
| --- | --- | --- |
| `bad-envelope` | 400 / 413 | the body is not an envelope, failed to decrypt, is addressed to another server, or is too large |
| `bad-meta` | 400 | meta lacks `type`/`sid`, the target is foreign or the type is unknown |
| `in-progress` | 409 | a request from another address is already in progress for this sid, or a code/cancel arrived without a request |
| `sid-expired` | 404 | sid not found or expired (2 minutes by default) |
| `code-invalid` | 400 / 410 | the code did not match; after 5 attempts the request is closed (410) |
| `access-denied` | 403 | `Access` returned `Denied`; `reason` carries its reason |

`OnAuthenticated` is called exactly once per request: on the first
`status` with `authenticated` or on a successful `code`. Further `status`
calls for that sid answer `expired`.

## Options

| Option | Default | Meaning |
| --- | --- | --- |
| `Mnemonic` or `Identity` | one is required | server identity |
| `Target.Id` | required | service id: the hub `destination` (hub mode) or the entry in the app's target list (direct mode) |
| `Target.Hub` | | hub target id (e.g. `auth_secretkeeper`); switches the QR to `target=<hub>&destination=<id>` |
| `Target.Owner` | | `sk1…` address of the service owner; published as `ownerHash` in `GET target`, lets this address manage the entry in the hub catalog |
| `Target.PublicUrl` | from `Host` and `X-Forwarded-Proto` | origin for `GET target` |
| `Access(address)` | required | `AccessDecision<T>.Granted(user)` or `.Denied(reason, message?)` |
| `OnAuthenticated(user, HttpContext)` | | session, cookie, token; the return value goes into the JSON for the browser |
| `Store` | `MemoryPendingStore<T>` | request store, see "Multiple replicas" |
| `Ttl` | 2 minutes | sid lifetime |
| `TrustProxy` | `true` | take the IP from the last `X-Forwarded-For` element |
| `Geo(ip)` | | city and country for the context shown in the app |
| `Describe(ctx, lang)` | built-in | custom context lines |
| `Messages` | ru/en | override messages (for example `AccessDenied`) |
| `Qr` | `true` | include `qrSvg` in `init` (QRCoder) |
| `MaxBodyBytes` | 16 KiB | envelope body limit |

`SkLoginService<TUser>` is registered in DI: through it you can call
`Core.InitAsync / PollAsync / SubmitCodeAsync` from your own controllers
if the standard endpoints do not fit (for example, to finish the sign-in
inside your own SSO pipeline).

## Multiple replicas

A request lives for 2 minutes and spans three calls from two clients (browser
and phone). With `MemoryPendingStore` they must land on the same process.
With several instances you need sticky sessions by `sid` or your own
`IPendingStore<TUser>` on top of Redis or a database:

```csharp
public interface IPendingStore<TUser>
{
    Task<Pending<TUser>?> GetAsync(string sid, CancellationToken ct = default);
    Task SetAsync(Pending<TUser> entry, TimeSpan ttl, CancellationToken ct = default);
    Task DeleteAsync(string sid, CancellationToken ct = default);
}
```

`Pending<TUser>` is serialized by `System.Text.Json` as is; `SetAsync`
is `SET sid json PX ttl`.

## Development

```bash
dotnet test                                   # 33 tests: app vectors, protocol, HTTP
dotnet run --project examples/AspNetCoreDemo  # http://localhost:5000, random mnemonic
DEMO_FAKE_PHONE=1 dotnet run --project examples/AspNetCoreDemo   # plus POST /demo/phone?sid=…
dotnet pack -c Release -o ./artifacts
```

Test vectors: `tests/Paymastech.SkLogin.Tests/Fixtures/test_vectors.json`
(generator `secret_keeper/tools/test_vectors`) and `app_fixtures.json`
(envelopes recorded by the app itself). Compatibility with the Node core
has also been verified with a live run: a "phone" built on
`@paymastech/sk-login-core` completes a sign-in against this server.

## License

MIT.
