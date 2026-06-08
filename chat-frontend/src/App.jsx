import { useState, useRef, useEffect, useCallback } from 'react'
import * as signalR from '@microsoft/signalr'
import './App.css'

function formatTime(date) {
  return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
}

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

function TypingIndicator() {
  return (
    <div className="message received">
      <div className="bubble loading-dots">
        <span /><span /><span />
      </div>
    </div>
  )
}

const STATUS_LABEL = {
  connecting: 'Connecting…',
  connected: 'Connected',
  reconnecting: 'Reconnecting…',
  disconnected: 'Disconnected',
}

// ── Auth screen ────────────────────────────────────────────────────────────────

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
      if (!res.ok) {
        setError(data.error ?? 'Something went wrong.')
        return
      }
      onAuth(data.username)
    } catch {
      setError('Could not reach the server.')
    } finally {
      setLoading(false)
    }
  }

  function switchTab(next) {
    setTab(next)
    setError(null)
  }

  return (
    <div className="chat-container">
      <header className="chat-header">
        <div className="avatar">💬</div>
        <div className="header-info">
          <h2>ChatApp</h2>
        </div>
      </header>

      <div className="auth-screen">
        <div className="auth-tabs">
          <button
            type="button"
            className={`auth-tab ${tab === 'login' ? 'active' : ''}`}
            onClick={() => switchTab('login')}
          >Login</button>
          <button
            type="button"
            className={`auth-tab ${tab === 'register' ? 'active' : ''}`}
            onClick={() => switchTab('register')}
          >Register</button>
        </div>

        <form className="auth-form" onSubmit={handleSubmit}>
          <input
            type="text"
            placeholder="Username"
            value={username}
            onChange={e => setUsername(e.target.value)}
            maxLength={30}
            autoFocus
            required
          />
          <input
            type="password"
            placeholder="Password"
            value={password}
            onChange={e => setPassword(e.target.value)}
            required
          />
          {error && <p className="auth-error">{error}</p>}
          <button type="submit" disabled={loading || !username.trim() || !password}>
            {loading ? '…' : tab === 'login' ? 'Login' : 'Create account'}
          </button>
        </form>
      </div>
    </div>
  )
}

// ── Chat screen ────────────────────────────────────────────────────────────────

export default function App() {
  const [username, setUsername] = useState(null)
  const [messages, setMessages] = useState([])
  const [input, setInput] = useState('')
  const [connStatus, setConnStatus] = useState('disconnected')

  const connectionRef = useRef(null)
  const bottomRef = useRef(null)

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages])

  const startConnection = useCallback(async () => {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/chatHub')
      .withAutomaticReconnect()
      .build()

    connection.on('MessageHistory', (history) => {
      setMessages(history.map(m => ({
        id: m.id,
        user: m.user,
        text: m.text,
        time: new Date(m.sentAt),
      })))
    })

    connection.on('ReceiveMessage', (user, message) => {
      setMessages(prev => [...prev, {
        id: Date.now() + Math.random(),
        user,
        text: message,
        time: new Date(),
      }])
    })

    connection.onreconnecting(() => setConnStatus('reconnecting'))
    connection.onreconnected(() => setConnStatus('connected'))
    connection.onclose(() => setConnStatus('disconnected'))

    connectionRef.current = connection
    setConnStatus('connecting')
    await connection.start()
    setConnStatus('connected')
  }, [])

  async function handleAuth(name) {
    setUsername(name)
    await startConnection()
  }

  async function handleLogout() {
    await fetch('/auth/logout', { method: 'POST', credentials: 'include' })
    await connectionRef.current?.stop()
    setUsername(null)
    setMessages([])
    setConnStatus('disconnected')
  }

  useEffect(() => {
    return () => { connectionRef.current?.stop() }
  }, [])

  async function sendMessage(e) {
    e.preventDefault()
    const text = input.trim()
    if (!text || connStatus !== 'connected') return
    setInput('')
    await connectionRef.current.invoke('SendMessage', text)
  }

  if (!username) return <AuthScreen onAuth={handleAuth} />

  return (
    <div className="chat-container">
      <header className="chat-header">
        <div className="avatar">💬</div>
        <div className="header-info">
          <h2>ChatApp</h2>
          <span className={`status status--${connStatus}`}>{STATUS_LABEL[connStatus]}</span>
        </div>
        <div className="header-right">
          <span className="current-user">{username}</span>
          <button className="logout-btn" onClick={handleLogout} title="Logout">↩</button>
        </div>
      </header>

      <div className="messages">
        {messages.map(msg => (
          <Message key={msg.id} msg={msg} currentUser={username} />
        ))}
        {(connStatus === 'connecting' || connStatus === 'reconnecting') && <TypingIndicator />}
        <div ref={bottomRef} />
      </div>

      <form className="chat-input" onSubmit={sendMessage}>
        <input
          type="text"
          placeholder="Type a message…"
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
    </div>
  )
}
