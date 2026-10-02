# Paymastech.SkLogin.AspNetCore

Sign in with Secret Keeper for ASP.NET Core (`net7.0`, `net8.0`). `AddSkLogin`
registers the service, `MapSkLogin` mounts the `init`, `login`, `status`,
`code` and `target` endpoints under a prefix (`/api/sk` by default), which the
browser widget and the Secret Keeper app talk to.

```csharp
builder.Services.AddSkLogin<User>(o =>
{
    o.Mnemonic = builder.Configuration["SK_SERVER_MNEMONIC"];
    o.Target = new SkLoginTarget { Id = "my-service", PublicUrl = "https://api.example.com" };
    o.Access = async address =>
    {
        var user = await users.FindByAddressAsync(address);
        return user is null ? AccessDecision<User>.Denied("unknown-address") : AccessDecision<User>.Granted(user);
    };
    o.OnAuthenticated = async (user, http) =>
    {
        await http.SignInAsync(principalFor(user));
        return new { userId = user.Id };
    };
});

app.MapSkLogin<User>("/api/sk");
```

Browser side: the `@paymastech/sk-login-widget` widget (a single JS file, no
framework) from [paymastech/sk-login](https://github.com/paymastech/sk-login).

Full description of options, HTTP API, error codes, target onboarding and
running on multiple replicas: [repository README](https://github.com/paymastech/sk-login-dotnet#readme).
