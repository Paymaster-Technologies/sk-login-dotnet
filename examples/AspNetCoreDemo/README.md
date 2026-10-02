# AspNetCoreDemo

Minimal ASP.NET Core application with `AddSkLogin`/`MapSkLogin` and the widget:
a page with a "Sign in" button, an in-memory cookie session, `GET /me`, `GET /logout`.

```bash
dotnet run --project examples/AspNetCoreDemo                 # random mnemonic, http://localhost:5000
dotnet run --project examples/AspNetCoreDemo -- --mnemonic   # print a new mnemonic
SK_SERVER_MNEMONIC="…" SK_TARGET=my-service dotnet run --project examples/AspNetCoreDemo
```

Phone emulation for development without the app:

```bash
DEMO_FAKE_PHONE=1 dotnet run --project examples/AspNetCoreDemo
# open the popup, take the sid from the "Sign in with the app" link, then
curl -X POST 'http://localhost:5000/demo/phone?sid=…'           # request + code: signed in immediately
curl -X POST 'http://localhost:5000/demo/phone?sid=…&show=1'    # request only, code in the response (for manual entry)
curl -X POST 'http://localhost:5000/demo/phone?sid=…&cancel=1'  # request + cancel
```

`wwwroot/sk-login-widget.js` is the built `@paymastech/sk-login-widget`
from [paymastech/sk-login](https://github.com/paymastech/sk-login).
