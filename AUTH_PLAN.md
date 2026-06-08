# Authentication Plan

## Overview

Authentication adds verified identity to the app. Today the username is an unvalidated
string — anyone can claim any name and messages are stored against it with no integrity
guarantee. This plan replaces that with a register/login flow backed by JWT tokens.
Once a user is authenticated, their identity comes from the token, not from what the
client claims to be.

---

## Token storage options

Where the frontend stores the JWT determines the security and UX tradeoffs of the entire
auth system. There are four options.

### Option 1 — In-memory (React state / ref)
The token is held in a JavaScript variable. Nothing is written to any browser storage API.

- Survives refresh: **No** — the variable is gone when the page reloads; the user must log in again
- XSS resistant: **Yes** — injected scripts have no API to reach a variable in another scope
- CSRF risk: **No** — no cookie involved
- Complexity: **Low**

### Option 2 — `sessionStorage`
The token is written to `sessionStorage`, which persists through a page refresh but is
cleared when the tab is closed.

- Survives refresh: **Yes** (same tab only)
- XSS resistant: **No** — `sessionStorage` is readable by any JS on the page
- CSRF risk: **No**
- Complexity: **Low**

### Option 3 — `localStorage`
The token is written to `localStorage`, which persists indefinitely across sessions.

- Survives refresh: **Yes**
- XSS resistant: **No** — same exposure as `sessionStorage`, and the token never expires
  from storage even if its JWT expiry passes
- CSRF risk: **No**
- Complexity: **Low**

`localStorage` is widely used but considered the worst option for auth tokens. An XSS
bug anywhere on the page exposes every stored token with no time limit.

### Option 4 — HttpOnly cookie ✅ Recommended
The server sets the JWT in a `Set-Cookie` response header with `HttpOnly` and
`SameSite=Strict` flags. The browser manages the cookie entirely.

- Survives refresh: **Yes** — cookies are browser-managed and persist across page loads
- XSS resistant: **Yes** — `HttpOnly` means JavaScript has no API to read the cookie at
  all (`document.cookie` does not include it)
- CSRF risk: **Yes, mitigated** — the browser auto-attaches cookies to every matching
  request, which a malicious site could exploit. `SameSite=Strict` instructs the browser
  to only send the cookie when the request originates from the same site that set it,
  which blocks cross-site request forgery entirely for same-origin apps
- Complexity: **Medium** — the server must set and clear the cookie; the frontend no
  longer manages a token value at all

For SignalR, the browser includes cookies in the WebSocket upgrade request automatically,
so no special token delivery mechanism is needed on the frontend.

### Option 5 — Refresh token pattern
A hybrid approach: a short-lived access token (15 min) held in memory, paired with a
long-lived refresh token stored in an HttpOnly cookie. On page load, a silent
`POST /auth/refresh` call exchanges the refresh cookie for a new access token without
user interaction.

- Survives refresh: **Yes**
- XSS resistant: **Yes**
- CSRF risk: **Yes, mitigated** (same `SameSite=Strict` applies to the refresh cookie)
- Complexity: **High** — requires token rotation, revocation storage, and silent refresh
  logic on the frontend

Best for production apps that need fine-grained token control, but beyond bare-minimum
scope.

### Comparison

| Option | Survives refresh | XSS resistant | CSRF risk | Complexity |
|---|---|---|---|---|
| In-memory | No | Yes | No | Low |
| `sessionStorage` | Yes (tab only) | No | No | Low |
| `localStorage` | Yes | No | No | Low |
| **HttpOnly cookie** | **Yes** | **Yes** | **Mitigated** | **Medium** |
| Refresh token pattern | Yes | Yes | Mitigated | High |

---

## Key decisions

### Password hashing — BCrypt
Use `BCrypt.Net-Next` (NuGet). BCrypt is purpose-built for passwords: it is slow by
design, has a built-in salt, and is the standard choice for .NET apps that are not using
ASP.NET Core Identity. ASP.NET Core Identity is not used here — it brings an entire user
management framework (roles, claims, external providers, EF schema) that is far more than
needed for a bare-minimum app.

### Tokens — JWT (JSON Web Token)
A signed JWT is issued on login and set as an HttpOnly cookie. The server validates the
signature on each request without hitting the database.

### Token storage — HttpOnly cookie with `SameSite=Strict`
The JWT is placed in a `Set-Cookie` header by the server on login and register. The
frontend never sees the token value — it only receives the username from the response
body for display purposes. The browser attaches the cookie to every subsequent request
automatically, including the SignalR WebSocket upgrade.

Cookie attributes:
- `HttpOnly` — JS cannot read it, eliminating XSS token theft
- `SameSite=Strict` — browser only sends it on same-site requests, eliminating CSRF
- `Secure` — set to `false` in development (HTTP), `true` in production (HTTPS)
- No `Domain` attribute — scopes the cookie to whatever origin the browser received it
  from, which through the Vite dev proxy is `localhost:5173`

### SignalR token delivery — cookie (automatic)
Because the browser attaches the auth cookie to the WebSocket upgrade request, the server
can read it from `HttpContext.Request.Cookies` in the `OnMessageReceived` JWT event. No
`accessTokenFactory` is needed on the frontend.

---

## Backend changes

### 1. NuGet packages

```
Microsoft.AspNetCore.Authentication.JwtBearer
BCrypt.Net-Next
```

### 2. `Models/User.cs` — new
```csharp
public class User
{
    public int Id { get; set; }
    public string Username { get; set; }
    public string PasswordHash { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

### 3. `Data/AppDbContext.cs` — add Users table
Add `DbSet<User> Users` and a unique index on `Username` via Fluent API so the database
enforces uniqueness, not just application code.

### 4. New migration
`dotnet ef migrations add AddUsers` — creates the `Users` table with the unique index.

### 5. `Repositories/IUserRepository.cs` and `UserRepository.cs` — new
Follows the same pattern as `IChatMessageRepository`.

```
Task<User?> FindByUsernameAsync(string username);
Task<bool> ExistsAsync(string username);
Task<User> CreateAsync(User user);
```

### 6. `Services/IAuthService.cs` and `AuthService.cs` — new
Encapsulates the two operations that need both the repository and JWT logic.

```
Task<AuthResult> RegisterAsync(string username, string password);
Task<AuthResult> LoginAsync(string username, string password);
```

`AuthResult` is a simple record:
```csharp
record AuthResult(bool Success, string? Username, string? Error);
```

Note: `AuthResult` carries back the `Username` (for the response body), not the token.
The token is written directly onto the HTTP response as a cookie inside the auth
endpoints — it never appears in the response body.

`AuthService` responsibilities:
- **Register**: check username not taken → hash password with BCrypt → persist `User` →
  generate JWT (returns raw string internally)
- **Login**: load user by username → verify BCrypt hash → generate JWT on match, return
  error on mismatch

JWT generation lives in `AuthService` as a private helper. It reads the secret key,
issuer, and audience from configuration and sets a 24-hour expiry.

### 7. Auth endpoints in `Program.cs`

```
POST /auth/register   { username, password }  →  sets cookie, returns { username }  or 400/409
POST /auth/login      { username, password }  →  sets cookie, returns { username }  or 401
POST /auth/logout     (no body)               →  clears cookie, returns 200
```

The endpoints call `AuthService`, then write the cookie onto the response:

```csharp
app.MapPost("/auth/login", async (LoginRequest req, IAuthService auth, HttpResponse res) =>
{
    var result = await auth.LoginAsync(req.Username, req.Password);
    if (!result.Success) return Results.Unauthorized();

    res.Cookies.Append("auth_token", token, new CookieOptions
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Secure = false, // true in production
        Expires = DateTimeOffset.UtcNow.AddHours(24)
    });

    return Results.Ok(new { username = result.Username });
});
```

The logout endpoint clears the cookie by overwriting it with an immediately-expired one:

```csharp
app.MapPost("/auth/logout", (HttpResponse res) =>
{
    res.Cookies.Delete("auth_token");
    return Results.Ok();
});
```

### 8. `appsettings.json` — JWT configuration

```json
"Jwt": {
  "Key": "CHANGE_THIS_IN_USER_SECRETS",
  "Issuer": "ChatApp",
  "Audience": "ChatApp",
  "ExpiryHours": 24
}
```

The `Key` value in `appsettings.json` is a placeholder only. The real secret is stored in
**dotnet user secrets** for development and in environment variables for any deployed
environment. It must never be committed to source control.

#### What the key is used for

When the server issues a JWT it **signs** the token using this key — attaching a
tamper-proof seal to the payload. On every subsequent request the server **verifies** the
signature using the same key. If anyone alters the token contents (e.g. changing the
username claim to impersonate another user) the signature no longer matches and the
request is rejected.

The algorithm is **HMAC-SHA256**, which is symmetric: the same key both signs and
verifies. This means the key must remain secret on the server — anyone who obtains it can
forge valid tokens for any username.

#### Requirements for the key value

- **Minimum 32 characters (256 bits)** — the JWT library throws at startup if the key is
  shorter
- **Random** — not a dictionary word, app name, or any guessable string
- **Unique per environment** — dev, staging, and production must each have their own key

#### Generating a suitable value

```bash
# macOS / Linux
openssl rand -base64 32
# outputs something like: K7gNU3sdo+OL0wNhqoVWhr3g6s1xYv72ol/pe/Unols=
```

#### Setting it via user secrets (development)

```bash
cd chat-backend
dotnet user-secrets init
dotnet user-secrets set "Jwt:Key" "K7gNU3sdo+OL0wNhqoVWhr3g6s1xYv72ol/pe/Unols="
```

User secrets are stored outside the project directory (in `~/.microsoft/usersecrets/`)
and are never included in source control. In production, set the equivalent environment
variable `Jwt__Key` (double underscore for nested config in .NET).

### 9. `Program.cs` — JWT middleware

The `OnMessageReceived` event reads the JWT from the cookie for all requests, replacing
the old query-string approach:

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // standard parameter validation ...

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                if (ctx.Request.Cookies.TryGetValue("auth_token", out var token))
                {
                    ctx.Token = token;
                }
                return Task.CompletedTask;
            }
        };
    });

app.UseAuthentication();
app.UseAuthorization();
```

This single handler covers both REST endpoints and the SignalR WebSocket upgrade because
the browser sends the cookie in both cases.

Register `IUserRepository`, `UserRepository`, `IAuthService`, `AuthService` as scoped
services.

### 10. `Hubs/ChatHub.cs` — secure the hub

- Add `[Authorize]` attribute — unauthenticated connections are rejected before
  `OnConnectedAsync` runs
- Remove the `string user` parameter from `SendMessage` — the username is read from the
  verified claims principal instead:
  ```csharp
  var username = Context.User!.Identity!.Name;
  ```
- This is the core security fix: the server no longer trusts what the client says its
  username is

---

## Frontend changes

### 11. Replace the username join screen with a login/register screen

The current single-field join screen is replaced by a two-tab form: **Login** and
**Register**. Both collect `username` and `password`.

On success, the server sets the HttpOnly cookie automatically — the frontend never sees
or stores the token. The response body returns `{ username }`, which is stored in React
state purely for display (chat header, own-message detection).

### 12. `App.jsx` — auth state and SignalR connection

No `accessTokenFactory` is needed. The browser sends the auth cookie on the WebSocket
upgrade request automatically:

```js
const connection = new HubConnectionBuilder()
  .withUrl('/chatHub')          // no accessTokenFactory
  .withAutomaticReconnect()
  .build()
```

On logout: call `POST /auth/logout` (which clears the cookie server-side), then call
`connection.stop()` and clear the username from state to return to the login screen.

### 13. Auth API calls

Two `fetch` calls against `/auth/register` and `/auth/login`. Both are `POST` with
`credentials: 'include'` so the browser accepts and stores the `Set-Cookie` response
header through the Vite proxy:

```js
const res = await fetch('/auth/login', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  credentials: 'include',
  body: JSON.stringify({ username, password }),
})
const { username: name } = await res.json()
```

On failure, show the error message from the response body.

### 14. `vite.config.js` — add `/auth` proxy

```js
'/auth': {
  target: 'http://localhost:5065',
  changeOrigin: true,
}
```

The proxy forwards `Set-Cookie` headers from the backend to the browser unchanged. Not
setting a `Domain` attribute on the cookie means the browser associates it with
`localhost:5173` (the origin it received it from through the proxy).

---

## File change summary

| File | Action |
|---|---|
| `chat-backend/Models/User.cs` | **Create** — User entity |
| `chat-backend/Data/AppDbContext.cs` | **Edit** — add `DbSet<User>`, unique index on Username |
| `chat-backend/Repositories/IUserRepository.cs` | **Create** — repository interface |
| `chat-backend/Repositories/UserRepository.cs` | **Create** — EF Core implementation |
| `chat-backend/Services/IAuthService.cs` | **Create** — service interface |
| `chat-backend/Services/AuthService.cs` | **Create** — register, login, JWT generation |
| `chat-backend/Hubs/ChatHub.cs` | **Edit** — add `[Authorize]`, read username from claims |
| `chat-backend/Program.cs` | **Edit** — JWT middleware (cookie-based), auth endpoints, register new services |
| `chat-backend/appsettings.json` | **Edit** — add `Jwt` config block |
| `chat-backend/Migrations/` | **Generate** — `AddUsers` migration |
| `chat-frontend/src/App.jsx` | **Edit** — login/register screen, username state, logout |
| `chat-frontend/vite.config.js` | **Edit** — add `/auth` proxy entry |

---

## What is explicitly out of scope

- Token refresh (expired tokens require re-login)
- Password change or account deletion
- Email verification
- OAuth / social login
- Role-based authorisation
- Rate limiting on auth endpoints
