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

export default function App() {
  const [username, setUsername] = useState('')
  const [joined, setJoined] = useState(false)
  const [usernameInput, setUsernameInput] = useState('')

  const [messages, setMessages] = useState([])
  const [input, setInput] = useState('')
  const [connStatus, setConnStatus] = useState('disconnected')

  const connectionRef = useRef(null)
  const bottomRef = useRef(null)

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages])

  const startConnection = useCallback(async (user) => {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/chatHub')
      .withAutomaticReconnect()
      .build()

    connection.on('MessageHistory', (history) => {
      const mapped = history.map(m => ({
        id: m.id,
        user: m.user,
        text: m.text,
        time: new Date(m.sentAt),
      }))
      setMessages(mapped)
    })

    connection.on('ReceiveMessage', (user, message) => {
      setMessages(prev => [...prev, { id: Date.now() + Math.random(), user, text: message, time: new Date() }])
    })

    connection.onreconnecting(() => setConnStatus('reconnecting'))
    connection.onreconnected(() => setConnStatus('connected'))
    connection.onclose(() => setConnStatus('disconnected'))

    connectionRef.current = connection

    setConnStatus('connecting')
    await connection.start()
    setConnStatus('connected')
  }, [])

  async function handleJoin(e) {
    e.preventDefault()
    const name = usernameInput.trim()
    if (!name) return
    setUsername(name)
    setJoined(true)
    await startConnection(name)
  }

  useEffect(() => {
    return () => {
      connectionRef.current?.stop()
    }
  }, [])

  async function sendMessage(e) {
    e.preventDefault()
    const text = input.trim()
    if (!text || connStatus !== 'connected') return
    setInput('')
    await connectionRef.current.invoke('SendMessage', username, text)
  }

  if (!joined) {
    return (
      <div className="chat-container">
        <header className="chat-header">
          <div className="avatar">💬</div>
          <div className="header-info">
            <h2>ChatApp</h2>
          </div>
        </header>
        <form className="join-screen" onSubmit={handleJoin}>
          <p>Choose a username to start chatting</p>
          <input
            type="text"
            placeholder="Your name…"
            value={usernameInput}
            onChange={e => setUsernameInput(e.target.value)}
            maxLength={30}
            autoFocus
          />
          <button type="submit" disabled={!usernameInput.trim()}>Join</button>
        </form>
      </div>
    )
  }

  return (
    <div className="chat-container">
      <header className="chat-header">
        <div className="avatar">💬</div>
        <div className="header-info">
          <h2>ChatApp</h2>
          <span className={`status status--${connStatus}`}>{STATUS_LABEL[connStatus]}</span>
        </div>
      </header>

      <div className="messages">
        {messages.map(msg => (
          <Message key={msg.id} msg={msg} currentUser={username} />
        ))}
        {connStatus === 'connecting' || connStatus === 'reconnecting'
          ? <TypingIndicator />
          : null}
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
