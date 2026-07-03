import { useEffect, useState } from 'react'
import { getWorkers } from '../api'

interface Worker {
  id: string; status: string; concurrencyLimit: number
  activeJobs: number; lastHeartbeat: string
}

const statusBadge = (s: string) => (
  <span className={`badge ${s === 'Active' ? 'badge-active' : 'badge-inactive'}`}>
    <span className="status-dot" /> {s}
  </span>
)

export default function Workers() {
  const [workers, setWorkers] = useState<Worker[]>([])
  const [loading, setLoading] = useState(true)

  const load = async () => {
    setLoading(true)
    try { const { data } = await getWorkers(); setWorkers(data) } catch { /* ignore */ }
    setLoading(false)
  }

  useEffect(() => { load(); const t = setInterval(load, 10000); return () => clearInterval(t) }, [])

  const sinceHeartbeat = (ts: string) => {
    const diff = Date.now() - new Date(ts).getTime()
    if (diff < 60000) return `${Math.floor(diff / 1000)}s ago`
    return `${Math.floor(diff / 60000)}m ago`
  }

  return (
    <div>
      <div className="page-header">
        <div>
          <div className="page-title">Workers</div>
          <div className="page-subtitle">Active worker node health</div>
        </div>
        <button className="btn btn-secondary" onClick={load}>↻ Refresh</button>
      </div>

      {loading ? (
        <div className="empty-state"><div className="spinner" /></div>
      ) : workers.length === 0 ? (
        <div className="empty-state">
          <div className="empty-icon">🖥️</div>
          <p>No workers registered</p>
          <p className="text-sm text-muted">Start the Atlas.Worker service to see workers here</p>
        </div>
      ) : (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          {workers.map(w => {
            const load = w.activeJobs / Math.max(w.concurrencyLimit, 1)
            return (
              <div key={w.id} className="card">
                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 16 }}>
                    <div style={{ width: 44, height: 44, background: 'rgba(99,102,241,0.1)', borderRadius: 10, display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 22 }}>🖥️</div>
                    <div>
                      <div style={{ fontWeight: 600, fontFamily: 'JetBrains Mono, monospace', fontSize: 13 }}>{w.id}</div>
                      <div style={{ fontSize: 12, color: '#94a3b8', marginTop: 2 }}>
                        Heartbeat: {sinceHeartbeat(w.lastHeartbeat)}
                      </div>
                    </div>
                  </div>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 24 }}>
                    <div style={{ textAlign: 'right' }}>
                      <div style={{ fontSize: 22, fontWeight: 700 }}>{w.activeJobs}</div>
                      <div style={{ fontSize: 11, color: '#94a3b8' }}>/ {w.concurrencyLimit} slots</div>
                    </div>
                    {statusBadge(w.status)}
                  </div>
                </div>
                <div style={{ marginTop: 14 }}>
                  <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 11, color: '#94a3b8', marginBottom: 6 }}>
                    <span>Concurrency Load</span>
                    <span>{Math.round(load * 100)}%</span>
                  </div>
                  <div style={{ height: 6, background: '#1f2f47', borderRadius: 100, overflow: 'hidden' }}>
                    <div style={{ height: '100%', width: `${load * 100}%`, background: load > 0.8 ? '#ef4444' : load > 0.5 ? '#f59e0b' : '#22c55e', borderRadius: 100, transition: 'width 0.5s' }} />
                  </div>
                </div>
              </div>
            )
          })}
        </div>
      )}
    </div>
  )
}
