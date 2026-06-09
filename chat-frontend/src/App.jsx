import { useState, useRef, useEffect, useCallback } from 'react'
import * as signalR from '@microsoft/signalr'
import './App.css'

function formatTime(date) {
  return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
}

// ── Auth screen ───────────────────────────────────────────────────────────────

function AuthScreen({ onAuth }) {
  const [tab, setTab] = useState('login')
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState(null)
  const [loading, setLoading] = useState(false)

  async function handleSubmit(e) {
    e.preventDefault()
    setError(null)
    setLoading(true)
    try {
      const res = await fetch(`/auth/${tab}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({ username: username.trim(), password }),
      })
      const data = await res.json()
      if (!res.ok) { setError(data.error ?? 'Something went wrong.'); return }
      onAuth(data.username)
    } catch {
      setError('Could not reach the server.')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="app-shell auth-only">
      <header className="app-header">
        <span className="app-logo">💬</span>
        <h1 className="app-title">ChatApp</h1>
      </header>
      <div className="auth-screen">
        <div className="auth-tabs">
          <button type="button" className={`auth-tab ${tab === 'login' ? 'active' : ''}`} onClick={() => { setTab('login'); setError(null) }}>Login</button>
          <button type="button" className={`auth-tab ${tab === 'register' ? 'active' : ''}`} onClick={() => { setTab('register'); setError(null) }}>Register</button>
        </div>
        <form className="auth-form" onSubmit={handleSubmit}>
          <input type="text" placeholder="Username" value={username} onChange={e => setUsername(e.target.value)} maxLength={30} autoFocus required />
          <input type="password" placeholder="Password" value={password} onChange={e => setPassword(e.target.value)} required />
          {error && <p className="auth-error">{error}</p>}
          <button type="submit" disabled={loading || !username.trim() || !password}>
            {loading ? '…' : tab === 'login' ? 'Login' : 'Create account'}
          </button>
        </form>
      </div>
    </div>
  )
}

// ── Sidebar ───────────────────────────────────────────────────────────────────

function Sidebar({ username, rooms, convos, active, unread, onSelect, onLogout, onCreateRoom, onNewDm }) {
  return (
    <aside className="sidebar">
      <div className="sidebar-user">
        <span className="sidebar-username">{username}</span>
        <button className="logout-btn" onClick={onLogout} title="Logout">↩</button>
      </div>

      <section className="sidebar-section">
        <div className="sidebar-section-header">
          <span>Rooms</span>
          <button className="sidebar-add-btn" onClick={onCreateRoom} title="New room">+</button>
        </div>
        {rooms.map(room => (
          <button
            key={room.id}
            className={`sidebar-item ${active?.type === 'room' && active.id === room.id ? 'active' : ''}`}
            onClick={() => onSelect({ id: room.id, type: 'room', name: room.name, isAdmin: room.isAdmin })}
          >
            <span className="sidebar-item-name"># {room.name}</span>
            {unread.has(`room:${room.id}`) && <span className="unread-dot" />}
          </button>
        ))}
        {rooms.length === 0 && <p className="sidebar-empty">No rooms yet</p>}
      </section>

      <section className="sidebar-section">
        <div className="sidebar-section-header">
          <span>Direct messages</span>
          <button className="sidebar-add-btn" onClick={onNewDm} title="New message">+</button>
        </div>
        {convos.map(c => (
          <button
            key={c.id}
            className={`sidebar-item ${active?.type === 'conversation' && active.id === c.id ? 'active' : ''}`}
            onClick={() => onSelect({ id: c.id, type: 'conversation', name: c.otherUsername, isAdmin: false })}
          >
            <span className="sidebar-item-name">@ {c.otherUsername}</span>
            {unread.has(`dm:${c.id}`) && <span className="unread-dot" />}
          </button>
        ))}
        {convos.length === 0 && <p className="sidebar-empty">No messages yet</p>}
      </section>
    </aside>
  )
}

// ── Message ───────────────────────────────────────────────────────────────────

function Message({ msg, currentUser }) {
  const isMine = msg.user === currentUser
  return (
    <div className={`message ${isMine ? 'sent' : 'received'}`}>
      {!isMine && <span className="msg-user">{msg.user}</span>}
      <div className="bubble">{msg.text}</div>
      <span className="timestamp">{formatTime(msg.time)}</span>
    </div>
  )
}

// ── Member panel ──────────────────────────────────────────────────────────────

function MemberPanel({ roomId, onClose }) {
  const [members, setMembers] = useState([])
  const [search, setSearch] = useState('')
  const [searchResults, setSearchResults] = useState([])
  const [loading, setLoading] = useState(false)

  useEffect(() => {
    fetch(`/rooms/${roomId}/members`, { credentials: 'include' })
      .then(r => r.json()).then(setMembers).catch(() => {})
  }, [roomId])

  useEffect(() => {
    if (!search.trim()) { setSearchResults([]); return }
    const t = setTimeout(async () => {
      const res = await fetch(`/users?search=${encodeURIComponent(search.trim())}`, { credentials: 'include' })
      if (res.ok) setSearchResults(await res.json())
    }, 250)
    return () => clearTimeout(t)
  }, [search])

  async function addMember(username) {
    setLoading(true)
    try {
      const res = await fetch(`/rooms/${roomId}/members`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({ username }),
      })
      if (res.ok) {
        setSearch('')
        setSearchResults([])
        const updated = await fetch(`/rooms/${roomId}/members`, { credentials: 'include' })
        if (updated.ok) setMembers(await updated.json())
      }
    } finally {
      setLoading(false)
    }
  }

  async function removeMember(userId) {
    await fetch(`/rooms/${roomId}/members/${userId}`, { method: 'DELETE', credentials: 'include' })
    setMembers(prev => prev.filter(m => m.userId !== userId))
  }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-panel" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <h3>Manage members</h3>
          <button className="modal-close" onClick={onClose}>✕</button>
        </div>
        <div className="member-list">
          {members.map(m => (
            <div key={m.userId} className="member-row">
              <span className="member-name">{m.username}</span>
              <span className={`member-role ${m.role}`}>{m.role}</span>
              {m.role !== 'admin' && (
                <button className="member-remove" onClick={() => removeMember(m.userId)}>Remove</button>
              )}
            </div>
          ))}
        </div>
        <div className="member-search-section">
          <input
            type="text"
            placeholder="Add by username…"
            value={search}
            onChange={e => setSearch(e.target.value)}
            className="member-search-input"
          />
          {searchResults.length > 0 && (
            <div className="search-results">
              {searchResults
                .filter(u => !members.some(m => m.userId === u.id))
                .map(u => (
                  <button key={u.id} className="search-result-item" onClick={() => addMember(u.username)} disabled={loading}>
                    {u.username}
                  </button>
                ))}
            </div>
          )}
        </div>
      </div>
    </div>
  )
}

// ── Create room dialog ────────────────────────────────────────────────────────

function CreateRoomDialog({ onClose, onCreated }) {
  const [name, setName] = useState('')
  const [error, setError] = useState(null)
  const [loading, setLoading] = useState(false)

  async function handleSubmit(e) {
    e.preventDefault()
    setError(null)
    setLoading(true)
    try {
      const res = await fetch('/rooms', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({ name: name.trim() }),
      })
      const data = await res.json()
      if (!res.ok) { setError(data.error ?? 'Failed to create room.'); return }
      onCreated({ id: data.id, name: data.name, isAdmin: true })
    } catch {
      setError('Could not reach the server.')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-panel modal-small" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <h3>Create room</h3>
          <button className="modal-close" onClick={onClose}>✕</button>
        </div>
        <form onSubmit={handleSubmit} className="modal-form">
          <input type="text" placeholder="Room name" value={name} onChange={e => setName(e.target.value)} maxLength={50} autoFocus required />
          {error && <p className="auth-error">{error}</p>}
          <button type="submit" disabled={loading || !name.trim()}>{loading ? '…' : 'Create'}</button>
        </form>
      </div>
    </div>
  )
}

// ── New DM dialog ─────────────────────────────────────────────────────────────

function NewDmDialog({ onClose, onStarted }) {
  const [search, setSearch] = useState('')
  const [results, setResults] = useState([])
  const [error, setError] = useState(null)

  useEffect(() => {
    if (!search.trim()) { setResults([]); return }
    const t = setTimeout(async () => {
      const res = await fetch(`/users?search=${encodeURIComponent(search.trim())}`, { credentials: 'include' })
      if (res.ok) setResults(await res.json())
    }, 250)
    return () => clearTimeout(t)
  }, [search])

  async function startDm(username) {
    try {
      const res = await fetch('/conversations', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({ username }),
      })
      const data = await res.json()
      if (!res.ok) { setError(data.error ?? 'Failed to start DM.'); return }
      onStarted({ id: data.id, otherUsername: data.otherUsername })
    } catch {
      setError('Could not reach the server.')
    }
  }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-panel modal-small" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <h3>New message</h3>
          <button className="modal-close" onClick={onClose}>✕</button>
        </div>
        <div className="modal-form">
          <input type="text" placeholder="Search by username…" value={search} onChange={e => setSearch(e.target.value)} autoFocus />
          {error && <p className="auth-error">{error}</p>}
          {results.length > 0 && (
            <div className="search-results">
              {results.map(u => (
                <button key={u.id} className="search-result-item" onClick={() => startDm(u.username)}>
                  {u.username}
                </button>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  )
}

// ── Main App ──────────────────────────────────────────────────────────────────

const STATUS_LABEL = {
  connecting: 'Connecting…',
  connected: 'Connected',
  reconnecting: 'Reconnecting…',
  disconnected: 'Disconnected',
}

export default function App() {
  const [initializing, setInitializing] = useState(true)
  const [username, setUsername] = useState(null)
  const [rooms, setRooms] = useState([])
  const [convos, setConvos] = useState([])
  const [active, setActive] = useState(null)   // { id, type, name, isAdmin }
  const [messages, setMessages] = useState([])
  const [input, setInput] = useState('')
  const [unread, setUnread] = useState(new Set())
  const [connStatus, setConnStatus] = useState('disconnected')
  const [showMemberPanel, setShowMemberPanel] = useState(false)
  const [showCreateRoom, setShowCreateRoom] = useState(false)
  const [showNewDm, setShowNewDm] = useState(false)

  const connectionRef = useRef(null)
  const activeRef = useRef(null)
  const bottomRef = useRef(null)

  const startConnection = useCallback(async () => {
    const conn = new signalR.HubConnectionBuilder()
      .withUrl('/chatHub')
      .withAutomaticReconnect()
      .build()

    conn.on('RoomList', roomList => setRooms(roomList))

    conn.on('ConversationList', convoList => setConvos(convoList))

    conn.on('RoomAdded', room => {
      setRooms(prev => [...prev, room])
      conn.invoke('JoinRoom', room.id).catch(() => {})
    })

    conn.on('RoomRemoved', ({ id }) => {
      setRooms(prev => prev.filter(r => r.id !== id))
      if (activeRef.current?.type === 'room' && activeRef.current?.id === id) {
        activeRef.current = null
        setActive(null)
        setMessages([])
      }
      setUnread(prev => { const n = new Set(prev); n.delete(`room:${id}`); return n })
      conn.invoke('LeaveRoom', id).catch(() => {})
    })

    conn.on('ConversationStarted', data => {
      setConvos(prev => {
        if (prev.some(c => c.id === data.id)) return prev
        return [...prev, { id: data.id, otherUsername: data.fromUsername }]
      })
      conn.invoke('JoinConversation', data.id).catch(() => {})
    })

    conn.on('MessageHistory', data => {
      const cur = activeRef.current
      if (cur?.id === data.targetId && cur?.type === data.targetType) {
        setMessages(data.messages.map(m => ({
          id: m.id,
          user: m.user,
          text: m.text,
          time: new Date(m.sentAt),
        })))
      }
    })

    conn.on('ReceiveMessage', msg => {
      const cur = activeRef.current
      if (cur?.id === msg.targetId && cur?.type === msg.targetType) {
        setMessages(prev => [...prev, {
          id: Date.now() + Math.random(),
          user: msg.user,
          text: msg.text,
          time: new Date(msg.sentAt),
        }])
      } else {
        const key = msg.targetType === 'room' ? `room:${msg.targetId}` : `dm:${msg.targetId}`
        setUnread(prev => new Set([...prev, key]))
      }
    })

    conn.onreconnecting(() => setConnStatus('reconnecting'))
    conn.onreconnected(() => setConnStatus('connected'))
    conn.onclose(() => setConnStatus('disconnected'))

    connectionRef.current = conn
    setConnStatus('connecting')
    await conn.start()
    setConnStatus('connected')
  }, [])

  useEffect(() => {
    let cancelled = false

    fetch('/auth/me', { credentials: 'include' })
      .then(res => res.ok ? res.json() : null)
      .then(async data => {
        if (cancelled) return
        if (data?.username) {
          setUsername(data.username)
          await startConnection()
        }
      })
      .catch(() => {})
      .finally(() => { if (!cancelled) setInitializing(false) })

    return () => {
      cancelled = true
      connectionRef.current?.stop()
    }
  }, [startConnection])

  useEffect(() => { activeRef.current = active }, [active])

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages])

  async function handleAuth(name) {
    setUsername(name)
    await startConnection()
  }

  async function handleLogout() {
    await fetch('/auth/logout', { method: 'POST', credentials: 'include' })
    await connectionRef.current?.stop()
    setUsername(null)
    setRooms([])
    setConvos([])
    activeRef.current = null
    setActive(null)
    setMessages([])
    setUnread(new Set())
    setConnStatus('disconnected')
  }

  useEffect(() => {
    return () => { connectionRef.current?.stop() }
  }, [])

  async function handleSelectChat(item) {
    if (activeRef.current?.id === item.id && activeRef.current?.type === item.type) return
    activeRef.current = item
    setActive(item)
    setMessages([])
    setUnread(prev => {
      const key = item.type === 'room' ? `room:${item.id}` : `dm:${item.id}`
      const n = new Set(prev)
      n.delete(key)
      return n
    })
    if (connectionRef.current?.state === signalR.HubConnectionState.Connected) {
      await connectionRef.current.invoke('GetHistory', item.id, item.type)
    }
  }

  async function sendMessage(e) {
    e.preventDefault()
    const text = input.trim()
    if (!text || !active || connectionRef.current?.state !== signalR.HubConnectionState.Connected) return
    setInput('')
    await connectionRef.current.invoke('SendMessage', active.id, active.type, text)
  }

  function handleRoomCreated(room) {
    setShowCreateRoom(false)
    setRooms(prev => [...prev, room])
    connectionRef.current?.invoke('JoinRoom', room.id).catch(() => {})
    handleSelectChat({ id: room.id, type: 'room', name: room.name, isAdmin: true })
  }

  function handleDmStarted(convo) {
    setShowNewDm(false)
    setConvos(prev => prev.some(c => c.id === convo.id) ? prev : [...prev, convo])
    connectionRef.current?.invoke('JoinConversation', convo.id).catch(() => {})
    handleSelectChat({ id: convo.id, type: 'conversation', name: convo.otherUsername, isAdmin: false })
  }

  if (initializing) return null
  if (!username) return <AuthScreen onAuth={handleAuth} />

  return (
    <div className="app-shell">
      <Sidebar
        username={username}
        rooms={rooms}
        convos={convos}
        active={active}
        unread={unread}
        onSelect={handleSelectChat}
        onLogout={handleLogout}
        onCreateRoom={() => setShowCreateRoom(true)}
        onNewDm={() => setShowNewDm(true)}
      />

      <div className="chat-area">
        {active ? (
          <>
            <header className="chat-header">
              <div className="chat-header-info">
                <span className="chat-header-icon">{active.type === 'room' ? '#' : '@'}</span>
                <span className="chat-header-name">{active.name}</span>
                <span className={`conn-status status--${connStatus}`}>{STATUS_LABEL[connStatus]}</span>
              </div>
              {active.isAdmin && (
                <button className="settings-btn" onClick={() => setShowMemberPanel(true)} title="Manage members">⚙</button>
              )}
            </header>

            <div className="messages">
              {messages.map(msg => (
                <Message key={msg.id} msg={msg} currentUser={username} />
              ))}
              {(connStatus === 'connecting' || connStatus === 'reconnecting') && (
                <div className="message received">
                  <div className="bubble loading-dots"><span /><span /><span /></div>
                </div>
              )}
              <div ref={bottomRef} />
            </div>

            <form className="chat-input" onSubmit={sendMessage}>
              <input
                type="text"
                placeholder={`Message ${active.type === 'room' ? '#' : '@'}${active.name}…`}
                value={input}
                onChange={e => setInput(e.target.value)}
                disabled={connStatus !== 'connected'}
                autoFocus
              />
              <button
                type="submit"
                className="send-btn"
                disabled={!input.trim() || connStatus !== 'connected'}
                aria-label="Send"
              >
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
                  <line x1="22" y1="2" x2="11" y2="13" />
                  <polygon points="22 2 15 22 11 13 2 9 22 2" />
                </svg>
              </button>
            </form>
          </>
        ) : (
          <div className="no-chat">
            <p>Select a room or direct message to start chatting</p>
          </div>
        )}
      </div>

      {showMemberPanel && active?.type === 'room' && (
        <MemberPanel roomId={active.id} onClose={() => setShowMemberPanel(false)} />
      )}
      {showCreateRoom && (
        <CreateRoomDialog onClose={() => setShowCreateRoom(false)} onCreated={handleRoomCreated} />
      )}
      {showNewDm && (
        <NewDmDialog onClose={() => setShowNewDm(false)} onStarted={handleDmStarted} />
      )}
    </div>
  )
}
