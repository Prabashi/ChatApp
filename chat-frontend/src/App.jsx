import { useState, useRef, useEffect } from 'react'
import './App.css'

const API_BASE = ''

function formatTime(date) {
  return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
}

function Message({ msg }) {
  return (
    <div className={`message ${msg.sender === 'user' ? 'sent' : 'received'}`}>
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

export default function App() {
  const [messages, setMessages] = useState([
    { id: 1, sender: 'bot', text: 'Hello! How can I help you today?', time: new Date() },
  ])
  const [input, setInput] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState(null)
  const bottomRef = useRef(null)

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages, loading])

  async function sendMessage(e) {
    e.preventDefault()
    const text = input.trim()
    if (!text || loading) return

    const userMsg = { id: Date.now(), sender: 'user', text, time: new Date() }
    setMessages(prev => [...prev, userMsg])
    setInput('')
    setError(null)
    setLoading(true)

    try {
      const res = await fetch(`${API_BASE}/chat`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ message: text }),
      })

      if (!res.ok) throw new Error(`Server error: ${res.status}`)

      const data = await res.json()
      const botMsg = {
        id: Date.now() + 1,
        sender: 'bot',
        text: data.reply ?? data.message ?? JSON.stringify(data),
        time: new Date(),
      }
      setMessages(prev => [...prev, botMsg])
    } catch (err) {
      setError(err.message)
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="chat-container">
      <header className="chat-header">
        <div className="avatar">💬</div>
        <div className="header-info">
          <h2>ChatApp</h2>
          <span className="status">Online</span>
        </div>
      </header>

      <div className="messages">
        {messages.map(msg => <Message key={msg.id} msg={msg} />)}
        {loading && <TypingIndicator />}
        <div ref={bottomRef} />
      </div>

      {error && <div className="error-banner">Error: {error}</div>}

      <form className="chat-input" onSubmit={sendMessage}>
        <input
          type="text"
          placeholder="Type a message..."
          value={input}
          onChange={e => setInput(e.target.value)}
          disabled={loading}
          autoFocus
        />
        <button type="submit" className="send-btn" disabled={!input.trim() || loading} aria-label="Send">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
            <line x1="22" y1="2" x2="11" y2="13" />
            <polygon points="22 2 15 22 11 13 2 9 22 2" />
          </svg>
        </button>
      </form>
    </div>
  )
}
