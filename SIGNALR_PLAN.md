# SignalR Migration Plan

## What changes and why

The current app is request-response: the user sends an HTTP POST, the server echoes back only to that user.
SignalR replaces this with a persistent WebSocket connection so every connected client receives every message in real-time — which is the core behaviour of a chat app.

---

## Backend (`chat-backend/`)

### 1. Add SignalR (no NuGet needed)
SignalR ships inside `Microsoft.AspNetCore` for .NET 6+. Nothing extra to install.

### 2. Create `Hubs/ChatHub.cs`
```
Hubs/
  ChatHub.cs
```
The hub exposes one method clients can invoke:
```csharp
public async Task SendMessage(string user, string message)
    => await Clients.All.SendAsync("ReceiveMessage", user, message);
```
`Clients.All.SendAsync` pushes the message to every connected client simultaneously.

### 3. Update `Program.cs`
- Add `builder.Services.AddSignalR()`
- Map the hub: `app.MapHub<ChatHub>("/chatHub")`
- Update the CORS policy to add `.AllowCredentials()` — SignalR's WebSocket negotiation requires it (credentials must be allowed alongside a specific origin, which is already set to `http://localhost:5173`)
- Remove `POST /chat` (replaced by the hub)

---

## Frontend (`chat-frontend/`)

### 4. Install the SignalR client
```
npm install @microsoft/signalr
```

### 5. Add a username step
The current UI has no concept of "who is talking". Add a simple username entry screen shown before the chat — just a text input and a "Join" button. Once joined, the username is held in state for the session.

### 6. Replace `fetch` with a `HubConnection` in `App.jsx`
| Current | After |
|---|---|
| `fetch POST /chat` on send | `connection.invoke("SendMessage", user, text)` |
| HTTP response sets bot reply | `connection.on("ReceiveMessage", ...)` appends message for all senders |
| `loading` state drives typing indicator | Connection status drives a status badge |

Lifecycle:
- **On mount / join**: build connection with `HubConnectionBuilder`, `.withUrl("/chatHub")`, `.withAutomaticReconnect()`, then `.start()`
- **On `ReceiveMessage`**: push `{ sender, text, time }` into messages state — works for both own and others' messages
- **On send**: call `connection.invoke(...)` only; do not optimistically add the message (the echo back from the server is the source of truth)
- **On unmount**: call `connection.stop()`

### 7. Show connection status
Replace the static `"Online"` badge in the header with live connection state: `Connecting → Connected → Reconnecting → Disconnected`.

---

## Vite proxy (`vite.config.js`)

SignalR starts with an HTTP negotiation request then upgrades to WebSocket. The proxy must handle both:

```js
'/chatHub': {
  target: 'http://localhost:5065',
  ws: true,          // proxy WebSocket upgrade
  changeOrigin: true,
}
```

The existing `/chat` proxy entry can be removed.

---

## File change summary

| File | Action |
|---|---|
| `chat-backend/Hubs/ChatHub.cs` | **Create** — hub class |
| `chat-backend/Program.cs` | **Edit** — register SignalR, map hub, update CORS, remove POST /chat |
| `chat-frontend/package.json` | **Edit** — add `@microsoft/signalr` dependency |
| `chat-frontend/src/App.jsx` | **Edit** — hub connection, username screen, ReceiveMessage handler |
| `chat-frontend/vite.config.js` | **Edit** — replace `/chat` proxy with `/chatHub` + `ws: true` |

No new frontend files are strictly required; the username screen can live inside `App.jsx` as a conditional render.

---

## What is explicitly out of scope (bare-minimum)

- Message persistence (no database — history is lost on refresh/reconnect)
- Chat rooms / channels
- Authentication / user accounts
- Typing indicators broadcast to others
- Read receipts
- Message editing or deletion
