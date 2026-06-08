# Persistence Proposal

## What needs to be persisted

Currently the app holds zero state between connections. Two problems result:

1. **Message history is lost** — a user who refreshes or reconnects sees an empty screen, with no way to read what was said before they joined.
2. **Server restart wipes everything** — even users currently connected lose all context.

The only data that needs storing right now is messages. The app has no user accounts, no rooms, and no authentication, so those are deliberately out of scope.

---

## Options considered

### Option A — SQLite + Entity Framework Core ✅ Recommended

SQLite is an embedded relational database stored as a single file on disk. EF Core has a first-class SQLite provider and ships as a NuGet package; no separate server process is needed.

**Pros**
- Zero infrastructure — just a `.db` file next to the binary
- EF Core migrations manage schema changes
- Simple flat schema maps cleanly to the message structure
- Swapping to PostgreSQL or SQL Server later requires changing one line (the provider registration) and re-running migrations — nothing else changes
- Well understood, well documented, production-proven for low-to-moderate traffic

**Cons**
- Not suitable if the app ever needs to scale to multiple backend instances (SQLite does not support concurrent writers across processes)

---

### Option B — PostgreSQL + Entity Framework Core

A full client-server relational database. The natural upgrade path from SQLite.

**Pros**
- Handles high write concurrency
- Supports horizontal scaling with connection pooling
- Same EF Core API as SQLite — migration is mostly mechanical

**Cons**
- Requires a running PostgreSQL server (Docker, cloud-hosted, or local install)
- Operational overhead that is not justified at this stage

---

### Option C — Redis

An in-memory key-value store. Chat messages could be stored as a Redis List per channel (`LPUSH` / `LRANGE`).

**Pros**
- Extremely fast reads and writes
- Pub/Sub built in — relevant if SignalR ever needs to scale across multiple backend instances (Redis backplane)

**Cons**
- Persistence is optional and configured separately (RDB snapshots / AOF); it is primarily a cache, not a database of record
- Querying message history beyond simple range reads requires extra data structures
- Adds a second infrastructure dependency for something SQLite handles without any extra process

---

### Option D — MongoDB

A document database where each message is a JSON document.

**Pros**
- Flexible schema
- Natural JSON fit

**Cons**
- The message structure is simple and flat — there is no schema flexibility needed here
- Adds a server dependency and a different query model for no gain over SQLite at this scale

---

## Recommendation: SQLite + EF Core

SQLite is the right choice for this stage of the app:

- The message schema is simple (`Id`, `User`, `Text`, `SentAt`) — a flat table is ideal
- No external process or infrastructure to manage during development
- EF Core makes the swap to PostgreSQL trivial when the app outgrows SQLite — the hub, repositories, and migrations all stay the same
- Matches the project's bare-minimum philosophy: add complexity only when it is earned

---

## Proposed data model

```csharp
public class ChatMessage
{
    public int Id { get; set; }
    public string User { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
}
```

One table, four columns. No foreign keys required until rooms or user accounts are introduced.

---

## How it plugs into the existing app

### On send (`ChatHub.SendMessage`)
1. Persist the message to the database before (or after) broadcasting
2. Broadcast proceeds exactly as today via `Clients.All.SendAsync`

### On connect (`ChatHub.OnConnectedAsync`)
1. Load the last N messages from the database (e.g. 50)
2. Send them only to the caller via `Clients.Caller.SendAsync("MessageHistory", messages)`

### Frontend
- Register a `"MessageHistory"` handler alongside the existing `"ReceiveMessage"` handler
- Prepend the history batch to the messages state on connect, before any live messages arrive

---

## File changes required (implementation, not done yet)

| File | Action |
|---|---|
| `chat-backend/ChatApp.csproj` | Add `Microsoft.EntityFrameworkCore.Sqlite` NuGet package |
| `chat-backend/Data/AppDbContext.cs` | Create — EF Core DbContext with `DbSet<ChatMessage>` |
| `chat-backend/Models/ChatMessage.cs` | Create — the entity class |
| `chat-backend/Hubs/ChatHub.cs` | Inject `AppDbContext`, save on send, load history on connect |
| `chat-backend/Program.cs` | Register `AddDbContext<AppDbContext>`, apply migrations on startup |
| `chat-frontend/src/App.jsx` | Handle `"MessageHistory"` event, prepend messages on connect |
