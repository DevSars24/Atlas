import { useEffect, useState, useCallback } from 'react'
import { useNavigate } from 'react-router-dom'
import { getJobs, retryJob, deleteJob } from '../api'

const STATUS_OPTIONS = ['', 'Pending', 'Processing', 'Succeeded', 'Failed', 'DeadLettered']
const QUEUE_OPTIONS  = ['', 'default', 'emails', 'reports']

interface Job {
  id: string; queue: string; jobType: string; status: string
  priority: string; attempts: number; maxAttempts: number
  createdAt: string; scheduledAt: string; lastError?: string
}

const statusBadge = (s: string) => {
  const cls: Record<string, string> = {
    Pending: 'badge-pending', Processing: 'badge-processing',
    Succeeded: 'badge-succeeded', Failed: 'badge-failed', DeadLettered: 'badge-deadlettered'
  }
  const dots: Record<string, string> = { Pending: '⏳', Processing: '⚙️', Succeeded: '✅', Failed: '❌', DeadLettered: '☠️' }
  return <span className={`badge ${cls[s] || 'badge-pending'}`}>{dots[s] || '?'} {s}</span>
}

export default function Jobs() {
  const nav = useNavigate()
  const [jobs, setJobs] = useState<Job[]>([])
  const [loading, setLoading] = useState(true)
  const [page, setPage]     = useState(1)
  const [queue, setQueue]   = useState('')
  const [status, setStatus] = useState('')

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const params: Record<string, string | number> = { page, pageSize: 20 }
      if (queue) params.queue = queue
      if (status) params.status = status
      const { data } = await getJobs(params)
      setJobs(data)
    } catch { /* ignore */ }
    setLoading(false)
  }, [page, queue, status])

  useEffect(() => { load() }, [load])
  useEffect(() => { const t = setInterval(load, 8000); return () => clearInterval(t) }, [load])

  const handleRetry = async (id: string, e: React.MouseEvent) => {
    e.stopPropagation()
    try { await retryJob(id); load() } catch { /* ignore */ }
  }

  const handleDelete = async (id: string, e: React.MouseEvent) => {
    e.stopPropagation()
    if (!confirm('Delete this job?')) return
    try { await deleteJob(id); load() } catch { /* ignore */ }
  }

  return (
    <div>
      <div className="page-header">
        <div>
          <div className="page-title">Jobs</div>
          <div className="page-subtitle">Browse and manage job queue entries</div>
        </div>
        <button className="btn btn-primary" onClick={load}>↻ Refresh</button>
      </div>

      <div className="toolbar">
        <select className="form-select" value={queue} onChange={e => { setQueue(e.target.value); setPage(1) }} style={{ width: 'auto' }}>
          {QUEUE_OPTIONS.map(q => <option key={q} value={q}>{q || '— All Queues —'}</option>)}
        </select>
        <select className="form-select" value={status} onChange={e => { setStatus(e.target.value); setPage(1) }} style={{ width: 'auto' }}>
          {STATUS_OPTIONS.map(s => <option key={s} value={s}>{s || '— All Statuses —'}</option>)}
        </select>
      </div>

      <div className="table-wrapper">
        <table>
          <thead>
            <tr>
              <th>Job ID</th><th>Type</th><th>Queue</th><th>Status</th>
              <th>Attempts</th><th>Priority</th><th>Created</th><th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr><td colSpan={8} style={{ textAlign: 'center', padding: 40 }}><div className="spinner" style={{ margin: 'auto' }} /></td></tr>
            ) : jobs.length === 0 ? (
              <tr><td colSpan={8}><div className="empty-state">No jobs found</div></td></tr>
            ) : jobs.map(job => (
              <tr key={job.id} style={{ cursor: 'pointer' }} onClick={() => nav(`/jobs/${job.id}`)}>
                <td className="td-mono">{job.id.slice(0, 8)}…</td>
                <td style={{ fontWeight: 500 }}>{job.jobType}</td>
                <td><span className="badge badge-pending" style={{ background: 'rgba(99,102,241,0.1)', color: '#818cf8' }}>{job.queue}</span></td>
                <td>{statusBadge(job.status)}</td>
                <td style={{ color: job.attempts >= job.maxAttempts ? '#ef4444' : '#94a3b8' }}>{job.attempts}/{job.maxAttempts}</td>
                <td style={{ color: '#94a3b8', fontSize: 12 }}>{job.priority}</td>
                <td className="td-mono">{new Date(job.createdAt).toLocaleString()}</td>
                <td>
                  <div className="flex gap-2" onClick={e => e.stopPropagation()}>
                    {(job.status === 'Failed' || job.status === 'DeadLettered') && (
                      <button className="btn btn-ghost btn-sm" onClick={e => handleRetry(job.id, e)} title="Retry">↺</button>
                    )}
                    <button className="btn btn-ghost btn-sm" style={{ color: '#ef4444' }} onClick={e => handleDelete(job.id, e)} title="Delete">🗑</button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="pagination">
        <button className="page-btn" disabled={page === 1} onClick={() => setPage(p => p - 1)}>← Prev</button>
        <span className="page-btn active">{page}</span>
        <button className="page-btn" disabled={jobs.length < 20} onClick={() => setPage(p => p + 1)}>Next →</button>
      </div>
    </div>
  )
}
