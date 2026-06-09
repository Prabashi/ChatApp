# ChatApp

A real-time chat application with group rooms and direct messaging, built with ASP.NET Core 10 and React 19.

## Features

- **Authentication** — register, login, and logout with BCrypt-hashed passwords and JWT stored as an HttpOnly cookie
- **Group rooms** — create named rooms; admins can add and remove members via a member management panel
- **Direct messages** — start a 1-to-1 conversation with any user by searching their username
- **Real-time messaging** — instant delivery via SignalR; messages persist to SQLite and are loaded as history on chat open
- **Unread indicators** — unread dot appears on rooms and DMs that received messages while you were elsewhere
- **Connection status** — live indicator shows connecting / connected / reconnecting / disconnected state

## Tech Stack

| Layer | Technology |
|---|---|
| Backend runtime | ASP.NET Core 10 (Minimal API) |
| Real-time | ASP.NET Core SignalR |
| ORM / DB | EF Core 10 + SQLite (code-first, auto-migrate on start) |
| Auth | JWT Bearer + BCrypt.Net-Next; token in HttpOnly `SameSite=Strict` cookie |
| Frontend | React 19 + Vite 8 |
| SignalR client | @microsoft/signalr 10 |

## Project Structure

```
ChatApp/
├── chat-backend/
│   ├── Data/           AppDbContext
│   ├── Filters/        RoomAdminFilter, RoomMemberFilter, ConversationParticipantFilter
│   ├── Hubs/           ChatHub (SignalR)
│   ├── Migrations/     EF Core migrations
│   ├── Models/         Room, RoomMembership, RoomMessage, Conversation,
│   │                   ConversationParticipant, DirectMessage, User, MemberRole
│   ├── Repositories/   IRoomRepository, IConversationRepository,
│   │                   IRoomMessageRepository, IDirectMessageRepository, IUserRepository
│   ├── Services/       IAuthService / AuthService
│   └── Program.cs      Minimal API routes + DI setup
└── chat-frontend/
    └── src/
        ├── App.jsx     Single-file UI (auth, sidebar, chat, dialogs)
        └── App.css
```

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 18+](https://nodejs.org/)

### Backend

```bash
cd chat-backend
dotnet run
```

The API starts on `http://localhost:5065`. EF Core migrations run automatically on startup, creating `chat.db` in the working directory.

Add your JWT settings to `appsettings.Development.json`:

```json
{
  "Jwt": {
    "Key": "your-secret-key-at-least-32-chars",
    "Issuer": "ChatApp",
    "Audience": "ChatApp"
  }
}
```

### Frontend

```bash
cd chat-frontend
npm install
npm run dev
```

The dev server runs on `http://localhost:5173` and proxies `/auth`, `/rooms`, `/conversations`, `/users`, and `/chatHub` to the backend, so cookies work same-origin.

## API Reference

### Auth

| Method | Route | Description |
|---|---|---|
| `POST` | `/auth/register` | Register `{ username, password }` |
| `POST` | `/auth/login` | Login `{ username, password }` |
| `POST` | `/auth/logout` | Clear auth cookie |
| `GET` | `/auth/me` | Returns `{ username }` for the current session |

### Rooms

| Method | Route | Description |
|---|---|---|
| `POST` | `/rooms` | Create a room `{ name }` |
| `GET` | `/rooms` | List rooms the current user belongs to |
| `GET` | `/rooms/{id}/members` | List members (member access required) |
| `POST` | `/rooms/{id}/members` | Add a member `{ username }` (admin only) |
| `DELETE` | `/rooms/{id}/members/{userId}` | Remove a member (admin only) |

### Conversations (Direct Messages)

| Method | Route | Description |
|---|---|---|
| `POST` | `/conversations` | Start or get a DM with `{ username }` |
| `GET` | `/conversations` | List the current user's conversations |

### Users

| Method | Route | Description |
|---|---|---|
| `GET` | `/users?search=` | Search users by username prefix |

## SignalR Hub (`/chatHub`)

### Client → Server

| Method | Parameters | Description |
|---|---|---|
| `SendMessage` | `targetId, targetType, message` | Send to a room (`"room"`) or conversation (`"conversation"`) |
| `GetHistory` | `targetId, targetType` | Load recent message history |
| `JoinRoom` | `roomId` | Join a SignalR group for a room |
| `JoinConversation` | `conversationId` | Join a SignalR group for a DM |
| `LeaveRoom` | `roomId` | Leave a SignalR room group |

### Server → Client

| Event | Payload | Description |
|---|---|---|
| `RoomList` | `[{ id, name, isAdmin }]` | Sent on connect with all the user's rooms |
| `ConversationList` | `[{ id, otherUsername }]` | Sent on connect with all the user's DMs |
| `RoomAdded` | `{ id, name, isAdmin }` | User was added to a new room |
| `RoomRemoved` | `{ id }` | User was removed from a room |
| `ConversationStarted` | `{ id, fromUsername }` | Another user started a DM |
| `MessageHistory` | `{ targetId, targetType, messages[] }` | Historical messages for a chat |
| `ReceiveMessage` | `{ targetId, targetType, user, text, sentAt }` | New real-time message |

## Architecture Notes

- **Two message tables** — `RoomMessage` (FK to Room) and `DirectMessage` (FK to Conversation) keep the schema clean with no nullable foreign keys.
- **Endpoint filters** — `RoomAdminFilter` and `RoomMemberFilter` implement `IEndpointFilter` and are composed directly on routes rather than using attributes.
- **JWT via cookie** — SignalR's `OnMessageReceived` event reads the `auth_token` cookie so the hub connection is authenticated without custom headers.
- **Group naming** — SignalR groups follow the pattern `room:{id}` and `dm:{id}`; users join all their groups on connect.
