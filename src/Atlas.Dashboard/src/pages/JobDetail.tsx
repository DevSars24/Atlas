import { useEffect, useState, useRef } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { getJob, retryJob, deleteJob } from '../api'
import * as signalR from '@microsoft/signalr'

interface Job {
  id: string; queue: string; jobType: string; status: string
  priority: string; attempts: number; maxAttempts: number
  createdAt: string; updatedAt: string; scheduledAt: string
  lastError?: string; payload: string; lockedBy?: string
  statusHistory: Array<{ fromStatus: string; toStatus: string; timestamp: string; notes?: string }>
  logs: Array<{ id: string; level: string; message: string; timestamp: string }>
}

interface LogEntry { level: string; message: string; timestamp: string }

const statusBadge = (s: string) => {
  const cls: Record<string, string> = { Pending: 'badge-pending', Processing: 'badge-processing', Succeeded: 'badge-succeeded', Failed: 'badge-failed', DeadLettered: 'badge-deadlettered' }
  return <span className={`badge ${cls[s] || 'badge-pending'}`}>{s}</span>
}

const levelClass = (l: string) => {
  const m: Record<string, string> = { Info: 'log-level-INFO', Debug: 'log-level-DEBUG', Warning: 'log-level-WARNING', Error: 'log-level-ERROR' }
  return m[l] || 'log-level-INFO'
}

export default function JobDetail() {
  const { id } = useParams<{ id: string }>()
  const nav = useNavigate()
  const [job, setJob] = useState<Job | null>(null)
  const [liveLogs, setLiveLogs] = useState<LogEntry[]>([])
  const [connected, setConnected] = useState(false)
  const logRef = useRef<HTMLDivElement>(null)
  const hubRef = useRef<signalR.HubConnection | null>(null)

  useEffect(() => {
    if (!id) return
    const load = async () => {
      try { const { data } = await getJob(id); setJob(data) } catch { /* ignore */ }
    }
    load()

    // SignalR connection
    const token = localStorage.getItem('atlas_token')
    const qs = token ? `?access_token=${token}` : ''
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`/hubs/joblogs${qs}`)
      .withAutomaticReconnect()
      .build()

    connection.on('ReceiveLog', (entry: LogEntry) => {
      setLiveLogs(prev => [...prev, entry])
      setTimeout(() => logRef.current?.scrollTo({ top: logRef.current.scrollHeight, behavior: 'smooth' }), 50)
    })

    connection.on('JobStatusChanged', () => load())

    connection.start()
      .then(() => { connection.invoke('SubscribeToJob', id); setConnected(true) })
      .catch(() => { /* ignore — API might not require auth for read */ })

    hubRef.current = connection

    const interval = setInterval(load, 5000)
    return () => {
      clearInterval(interval)
      connection.stop()
    }
  }, [id])

  if (!job) return <div className="empty-state"><div className="spinner" /></div>

  const allLogs = [
    ...job.logs.map(l => ({ ...l, source: 'history' as const })),
    ...liveLogs.map(l => ({ ...l, id: Math.random().toString(), source: 'live' as const }))
  ].sort((a, b) => new Date(a.timestamp).getTime() - new Date(b.timestamp).getTime())

  return (
    <div>
      <div className="page-header">
        <div>
          <button className="btn btn-ghost btn-sm" onClick={() => nav(-1)} style={{ marginBottom: 8 }}>← Back</button>
          <div className="page-title" style={{ fontSize: 18 }}>{job.jobType}</div>
          <div className="text-muted text-sm text-mono">{job.id}</div>
        </div>
        <div className="flex gap-2">
          {(job.status === 'Failed' || job.status === 'DeadLettered') && (
            <button className="btn btn-primary" onClick={async () => { await retryJob(job.id); const { data } = await getJob(job.id); setJob(data) }}>
              ↺ Retry
            </button>
          )}
          <button className="btn btn-danger" onClick={async () => { if (!confirm('Delete?')) return; await deleteJob(job.id); nav('/jobs') }}>
            🗑 Delete
          </button>
        </div>
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 20, marginBottom: 20 }}>
        <div className="card">
          <h3 style={{ marginBottom: 12, fontWeight: 600 }}>Details</h3>
          <table style={{ fontSize: 13 }}>
            <tbody>
              {[
                ['Status', statusBadge(job.status)],
                ['Queue', <span className="badge badge-processing">{job.queue}</span>],
                ['Priority', job.priority],
                ['Attempts', `${job.attempts} / ${job.maxAttempts}`],
                ['Worker', job.lockedBy || '—'],
                ['Created', new Date(job.createdAt).toLocaleString()],
                ['Updated', new Date(job.updatedAt).toLocaleString()],
              ].map(([k, v]) => (
                <tr key={k as string}><td style={{ color: '#94a3b8', paddingRight: 16, paddingBottom: 8, fontWeight: 500 }}>{k}</td><td>{v}</td></tr>
              ))}
            </tbody>
          </table>
          {job.lastError && (
            <div style={{ marginTop: 12, background: 'rgba(239,68,68,0.08)', border: '1px solid rgba(239,68,68,0.2)', borderRadius: 8, padding: '10px 12px' }}>
              <div style={{ color: '#ef4444', fontSize: 12, fontWeight: 600, marginBottom: 4 }}>Last Error</div>
              <pre style={{ fontSize: 11, color: '#fca5a5', whiteSpace: 'pre-wrap', wordBreak: 'break-all', fontFamily: 'JetBrains Mono, monospace' }}>{job.lastError}</pre>
            </div>
          )}
        </div>

        <div className="card">
          <h3 style={{ marginBottom: 12, fontWeight: 600 }}>Payload</h3>
          <pre style={{ background: '#070d1a', border: '1px solid #1f2f47', borderRadius: 8, padding: 12, fontSize: 12, color: '#e2e8f0', overflowX: 'auto', fontFamily: 'JetBrains Mono, monospace' }}>
            {(() => { try { return JSON.stringify(JSON.parse(job.payload), null, 2) } catch { return job.payload } })()}
          </pre>
        </div>
      </div>

      <div className="card" style={{ marginBottom: 20 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 14 }}>
          <h3 style={{ fontWeight: 600 }}>Live Logs</h3>
          {connected && <span className="badge badge-succeeded animate-pulse" style={{ fontSize: 10 }}>● LIVE</span>}
        </div>
        <div className="log-viewer" ref={logRef}>
          {allLogs.length === 0 ? (
            <div style={{ color: '#4a5568', padding: '20px 0', textAlign: 'center' }}>No logs yet…</div>
          ) : allLogs.map((l, i) => (
            <div key={i} className="log-line">
              <span className="log-ts">{new Date(l.timestamp).toLocaleTimeString()}</span>
              <span className={levelClass(l.level)}>[{l.level?.toUpperCase()}]</span>
              <span className="log-msg">{l.message}</span>
            </div>
          ))}
        </div>
      </div>

      <div className="card">
        <h3 style={{ fontWeight: 600, marginBottom: 14 }}>Status History</h3>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
          {job.statusHistory.map((h, i) => (
            <div key={i} style={{ display: 'flex', alignItems: 'center', gap: 12, fontSize: 13 }}>
              <span className="td-mono" style={{ fontSize: 11, minWidth: 160 }}>{new Date(h.timestamp).toLocaleString()}</span>
              <span style={{ color: '#94a3b8' }}>{h.fromStatus}</span>
              <span style={{ color: '#4a5568' }}>→</span>
              <span style={{ color: '#6366f1' }}>{h.toStatus}</span>
              {h.notes && <span style={{ color: '#4a5568' }}>— {h.notes}</span>}
            </div>
          ))}
        </div>
      </div>
    </div>
  )
}
