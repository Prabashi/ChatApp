# Direct & Group Chat Plan

## Overview

This plan adds two features on top of the existing authenticated single-room chat:

1. **Admin-managed group rooms** — a user creates a room, becomes its admin, and is the
   only one who can add or remove members. There is no self-join.
2. **Private 1-to-1 chat** — any two authenticated users can start a direct message
   thread. No admin concept; both participants are equal.

The existing global chat and its `ChatMessage` table are dropped entirely. Room messages
and DM messages are stored in two separate dedicated tables (`RoomMessage` and
`DirectMessage`), each with a required non-nullable FK to their parent. This avoids
nullable FKs, check constraints, and shared query paths.

---

## Prerequisite — add `UserId` to JWT claims

Currently `AuthService.GenerateToken` puts only `ClaimTypes.Name` (username) in the
token. Both features need the user's database `Id` for membership lookups and SignalR
user-addressing without extra database round-trips.

`GenerateToken` must be updated to include `ClaimTypes.NameIdentifier`:

```csharp
claims:
[
    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
    new Claim(ClaimTypes.Name, user.Username),
]
```

`GenerateToken(string username)` becomes `GenerateToken(User user)`. `IAuthService`,
`AuthService`, and the auth endpoints in `Program.cs` are updated accordingly.

SignalR's `Clients.User()` relies on `ClaimTypes.NameIdentifier` by default — this
change makes targeted user notifications work without extra configuration.

---

## Data model

### New: `Room`
```
Id          int       PK
Name        string    required
CreatedAt   DateTime
```

### New: `RoomMembership`
```
Id          int           PK
RoomId      int           FK → Room
UserId      int           FK → User
Role        MemberRole    enum: Admin | Member
JoinedAt    DateTime
```
Composite unique index on `(RoomId, UserId)`.

### New: `MemberRole` enum
```csharp
public enum MemberRole { Member, Admin }
```

### New: `Conversation`
Represents a DM thread between exactly two users.
```
Id          int       PK
CreatedAt   DateTime
```

### New: `ConversationParticipant`
```
Id                int     PK
ConversationId    int     FK → Conversation
UserId            int     FK → User
```
Composite unique index on `(ConversationId, UserId)`.

### New: `RoomMessage`
Stores messages that belong to a room. `RoomId` is required and non-nullable.
```
Id          int       PK
RoomId      int       FK → Room (required)
UserId      int       FK → User (required)
Text        string
SentAt      DateTime
```

### New: `DirectMessage`
Stores messages that belong to a DM conversation. `ConversationId` is required and
non-nullable.
```
Id                  int       PK
ConversationId      int       FK → Conversation (required)
UserId              int       FK → User (required)
Text                string
SentAt              DateTime
```

### Deleted: `ChatMessage`
The existing `ChatMessage` entity and its `Messages` table are dropped. The old global
chat data does not need to be migrated. Both sender identity columns change from a stored
username string to a `UserId` FK — identity now comes from the verified token, not a
stored string.

---

## AppDbContext changes

- Remove `DbSet<ChatMessage> Messages`
- Add `DbSet<Room>`, `DbSet<RoomMembership>`, `DbSet<Conversation>`,
  `DbSet<ConversationParticipant>`, `DbSet<RoomMessage>`, `DbSet<DirectMessage>`
- Composite unique indexes on `RoomMembership(RoomId, UserId)` and
  `ConversationParticipant(ConversationId, UserId)` via Fluent API
- Cascade deletes: deleting a `Room` removes its `RoomMessage` rows; deleting a
  `Conversation` removes its `DirectMessage` rows

---

## New migration

`dotnet ef migrations add AddRoomsAndConversations`

This migration drops the `Messages` table and creates `Rooms`, `RoomMemberships`,
`Conversations`, `ConversationParticipants`, `RoomMessages`, and `DirectMessages`.

---

## Repository layer

### New: `IRoomRepository` / `RoomRepository`

```csharp
Task<Room> CreateAsync(string name, int creatorUserId);
Task<IReadOnlyList<Room>> GetRoomsForUserAsync(int userId);
Task<RoomMembership?> GetMembershipAsync(int roomId, int userId);
Task<bool> IsMemberAsync(int roomId, int userId);
Task AddMemberAsync(int roomId, int userId);
Task RemoveMemberAsync(int roomId, int userId);
Task<IReadOnlyList<RoomMembership>> GetMembersAsync(int roomId);
```

`CreateAsync` wraps room creation and the admin membership insertion in one operation.

### New: `IConversationRepository` / `ConversationRepository`

```csharp
Task<Conversation> GetOrCreateAsync(int userIdA, int userIdB);
Task<IReadOnlyList<Conversation>> GetConversationsForUserAsync(int userId);
Task<bool> IsParticipantAsync(int conversationId, int userId);
```

`GetOrCreateAsync` is idempotent — if a DM thread between two users already exists it
returns it rather than creating a duplicate.

### Deleted: `IChatMessageRepository` / `ChatMessageRepository`

The shared message repository is removed entirely. It is replaced by two purpose-built
repositories with no shared interface — room and DM messages never share a query path.

### New: `IRoomMessageRepository` / `RoomMessageRepository`

```csharp
Task SaveAsync(int roomId, int userId, string text);
Task<IReadOnlyList<RoomMessage>> GetRecentAsync(int roomId, int count = 50);
```

### New: `IDirectMessageRepository` / `DirectMessageRepository`

```csharp
Task SaveAsync(int conversationId, int userId, string text);
Task<IReadOnlyList<DirectMessage>> GetRecentAsync(int conversationId, int count = 50);
```

---

## Authorization filters

### `RoomMemberFilter`
Reads `{id}` from the route, resolves `UserId` from `ClaimTypes.NameIdentifier`, calls
`IRoomRepository.IsMemberAsync`. Returns `403` if not a member.

### `RoomAdminFilter`
Same lookup but asserts `Role == MemberRole.Admin`. Returns `403` if not the admin.

### `ConversationParticipantFilter`
Reads `{id}` from the route, calls `IConversationRepository.IsParticipantAsync`. Returns
`403` if not a participant.

---

## API endpoints

All endpoints require authentication.

### Group room endpoints

```
POST   /rooms                        — create room; creator inserted as Admin
GET    /rooms                        — list rooms the current user belongs to
GET    /rooms/{id}/members           — list members with roles       [RoomMemberFilter]
POST   /rooms/{id}/members           — add user by username          [RoomAdminFilter]
DELETE /rooms/{id}/members/{userId}  — remove a member               [RoomAdminFilter]
```

**`POST /rooms`** — body: `{ name }`. Returns `{ id, name }`.

**`POST /rooms/{id}/members`** — body: `{ username }`. Looks up the target user,
checks they are not already a member, inserts a `Member` record. After persisting,
sends a `RoomAdded` SignalR event to the target user via `Clients.User(targetUserId)`
so their sidebar updates if they are online.

**`DELETE /rooms/{id}/members/{userId}`** — admin cannot remove themselves. After
persisting, sends a `RoomRemoved` SignalR event to the removed user.

### Direct message endpoints

```
POST   /conversations                    — start or retrieve a DM thread (body: { username })
GET    /conversations                    — list all DM threads for the current user
```

**`POST /conversations`** — looks up the target user, calls `GetOrCreateAsync`, returns
`{ id, otherUsername }`. Idempotent — safe to call multiple times for the same pair.

### Shared

```
GET    /users?search={query}    — search users by username prefix, max 10 results
```

Used by the group admin's add-member UI and by the new DM flow to find a recipient.

---

## SignalR changes

### Updated hub methods

**`SendMessage(int targetId, string targetType, string message)`**
- `targetType` is `"room"` or `"conversation"`
- Verifies membership/participation via the relevant repository
- Calls `IRoomMessageRepository.SaveAsync` or `IDirectMessageRepository.SaveAsync`
  depending on `targetType` — no shared path
- Broadcasts to `Clients.Group(groupKey)` where `groupKey` is `"room:{id}"` or
  `"dm:{id}"` to avoid key collisions between room IDs and conversation IDs

**`GetHistory(int targetId, string targetType)`**
- New invokable method called when the user opens a room or DM thread
- Verifies access, calls the correct repository's `GetRecentAsync`, sends to
  `Clients.Caller`

**`OnConnectedAsync`**
- Adds the connection to SignalR groups for all the user's rooms and conversations
- Sends `RoomList` and `ConversationList` events to the caller
- Removes the old global `MessageHistory` send — history is loaded on-demand

**`OnDisconnectedAsync`**
- SignalR removes connections from all groups automatically; no manual cleanup needed

### New SignalR client events

| Event | Sent to | Payload | Trigger |
|---|---|---|---|
| `RoomList` | Caller | `[{ id, name }]` | On connect |
| `ConversationList` | Caller | `[{ id, otherUsername }]` | On connect |
| `RoomAdded` | Target user | `{ id, name }` | Admin adds user to a room |
| `RoomRemoved` | Target user | `{ id }` | Admin removes user from a room |
| `ConversationStarted` | Target user | `{ id, fromUsername }` | Someone starts a DM with them |
| `ReceiveMessage` | Group | `{ user, text, sentAt }` | Message sent to room or DM |

---

## Frontend changes

### Layout — two-panel

```
┌──────────────┬──────────────────────────────┐
│   Sidebar    │   Active chat panel           │
│              │                               │
│  Rooms       │  [Room or DM name]  [⚙ admin]│
│  ─────────   │  ───────────────────────────  │
│  Room A      │  messages...                  │
│  Room B  ●   │                               │
│              │  [input field]      [send]    │
│  Direct msgs │                               │
│  ─────────   │                               │
│  Alice    ●  │                               │
│  Bob         │                               │
│  + New msg   │                               │
└──────────────┴──────────────────────────────┘
```

The sidebar has two sections: **Rooms** (with a "+ New room" button) and **Direct
messages** (with a "+ New message" button). An unread dot appears when a message arrives
for an inactive thread.

### Create room
Button at the top of the Rooms section opens a name input. On submit, calls `POST /rooms`
and prepends the result to the sidebar.

### Start a DM
"+ New message" opens a user search input. Typing queries `GET /users?search=`. Selecting
a result calls `POST /conversations` and opens the thread.

### Room switching / thread switching
Clicking any item in the sidebar sets it as active and calls
`connection.invoke('GetHistory', id, type)` to populate the chat panel.

### Member management — admin only
The settings icon (⚙) on the room header is visible only to the admin. Opens a panel:
- Current member list from `GET /rooms/{id}/members`
- Search input querying `GET /users?search=` as the user types
- Add button next to search results; Remove button next to members (not the admin)

### Real-time sidebar updates
- `RoomAdded` / `ConversationStarted` → append to the relevant sidebar section
- `RoomRemoved` → remove from sidebar; clear chat panel if it was active

---

## File change summary

| File | Action |
|---|---|
| `chat-backend/Models/ChatMessage.cs` | **Delete** |
| `chat-backend/Models/Room.cs` | **Create** |
| `chat-backend/Models/RoomMembership.cs` | **Create** |
| `chat-backend/Models/MemberRole.cs` | **Create** — enum |
| `chat-backend/Models/RoomMessage.cs` | **Create** |
| `chat-backend/Models/Conversation.cs` | **Create** |
| `chat-backend/Models/ConversationParticipant.cs` | **Create** |
| `chat-backend/Models/DirectMessage.cs` | **Create** |
| `chat-backend/Data/AppDbContext.cs` | **Edit** — remove Messages, add new DbSets, indexes, FKs |
| `chat-backend/Repositories/IChatMessageRepository.cs` | **Delete** |
| `chat-backend/Repositories/ChatMessageRepository.cs` | **Delete** |
| `chat-backend/Repositories/IRoomRepository.cs` | **Create** |
| `chat-backend/Repositories/RoomRepository.cs` | **Create** |
| `chat-backend/Repositories/IConversationRepository.cs` | **Create** |
| `chat-backend/Repositories/ConversationRepository.cs` | **Create** |
| `chat-backend/Repositories/IRoomMessageRepository.cs` | **Create** |
| `chat-backend/Repositories/RoomMessageRepository.cs` | **Create** |
| `chat-backend/Repositories/IDirectMessageRepository.cs` | **Create** |
| `chat-backend/Repositories/DirectMessageRepository.cs` | **Create** |
| `chat-backend/Filters/RoomAdminFilter.cs` | **Create** |
| `chat-backend/Filters/RoomMemberFilter.cs` | **Create** |
| `chat-backend/Filters/ConversationParticipantFilter.cs` | **Create** |
| `chat-backend/Hubs/ChatHub.cs` | **Edit** — SendMessage, GetHistory, OnConnectedAsync |
| `chat-backend/Services/AuthService.cs` | **Edit** — add UserId to token claims |
| `chat-backend/Services/IAuthService.cs` | **Edit** — update GenerateToken signature |
| `chat-backend/Program.cs` | **Edit** — new endpoints, register repositories |
| `chat-backend/Migrations/` | **Generate** — `AddRoomsAndConversations` |
| `chat-frontend/src/App.jsx` | **Edit** — two-panel layout, rooms, DMs, member management |
| `chat-frontend/src/App.css` | **Edit** — sidebar, two-panel styles |

---

## Suggested implementation order

1. JWT prerequisite (`UserId` claim) — unblocks everything else
2. Models + migration — schema first
3. Repositories — data access before any endpoints
4. Filters — needed before admin endpoints can be registered
5. Group room endpoints + hub changes
6. Private chat endpoints + hub changes
7. Frontend — two-panel layout, then rooms, then DMs

---

## What is explicitly out of scope

- Room name editing or deletion
- Transferring admin ownership
- Multiple admins per room
- Group DMs (more than two participants without being a named room)
- Message editing or deletion
- Read receipts or unread counts beyond a dot indicator
- Message pagination beyond the initial 50
- Push notifications when the app is not open
