import { useState } from 'react'
import { useAuth } from '../AuthContext'

export default function Login() {
  const { loginWithEmail, loginWithApiKey } = useAuth()
  const [tab, setTab] = useState<'email' | 'apikey'>('email')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [apiKey, setApiKey] = useState('')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  const handleEmailLogin = async (e: React.FormEvent) => {
    e.preventDefault()
    setLoading(true); setError('')
    try {
      await loginWithEmail(email, password)
    } catch {
      setError('Invalid email or password.')
    } finally { setLoading(false) }
  }

  const handleApiKeyLogin = (e: React.FormEvent) => {
    e.preventDefault()
    if (!apiKey.trim()) { setError('Enter an API key.'); return }
    loginWithApiKey(apiKey.trim())
  }

  return (
    <div className="login-page">
      <div className="login-card">
        <div className="login-logo">
          <div className="icon">⚡</div>
          <div>
            <div className="login-title">Atlas</div>
          </div>
        </div>
        <p className="login-subtitle">Distributed Job Queue Engine — Dashboard</p>

        <div className="tab-row">
          <button className={`tab-btn ${tab === 'email' ? 'active' : ''}`} onClick={() => setTab('email')}>Email</button>
          <button className={`tab-btn ${tab === 'apikey' ? 'active' : ''}`} onClick={() => setTab('apikey')}>API Key</button>
        </div>

        {error && <div style={{ background: 'rgba(239,68,68,0.1)', border: '1px solid rgba(239,68,68,0.3)', borderRadius: 8, padding: '10px 14px', color: '#ef4444', marginBottom: 16, fontSize: 13 }}>{error}</div>}

        {tab === 'email' ? (
          <form onSubmit={handleEmailLogin} style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
            <div className="form-group">
              <label className="form-label">Email</label>
              <input className="form-input" type="email" value={email} onChange={e => setEmail(e.target.value)} required placeholder="admin@example.com" />
            </div>
            <div className="form-group">
              <label className="form-label">Password</label>
              <input className="form-input" type="password" value={password} onChange={e => setPassword(e.target.value)} required placeholder="••••••••" />
            </div>
            <button className="btn btn-primary w-full" type="submit" disabled={loading} style={{ justifyContent: 'center', padding: '12px' }}>
              {loading ? <span className="spinner" /> : 'Sign In'}
            </button>
          </form>
        ) : (
          <form onSubmit={handleApiKeyLogin} style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
            <div className="form-group">
              <label className="form-label">API Key</label>
              <input className="form-input text-mono" type="text" value={apiKey} onChange={e => setApiKey(e.target.value)} required placeholder="Paste your X-Api-Key here..." />
            </div>
            <button className="btn btn-primary w-full" type="submit" style={{ justifyContent: 'center', padding: '12px' }}>
              Connect
            </button>
          </form>
        )}
      </div>
    </div>
  )
}
