import { useEffect, useState } from 'react'
import { getSchedules, createSchedule, updateSchedule, deleteSchedule, triggerSchedule } from '../api'

interface Schedule {
  id: string; name: string; description?: string; cronExpression: string
  jobType: string; queue: string; payload: string; priority: string
  maxAttempts: number; isEnabled: boolean; misfirePolicy: string
  lastRunAt?: string; nextRunAt?: string
}

interface FormState { name: string; description: string; cronExpression: string; jobType: string; queue: string; payload: string; priority: string; maxAttempts: number; isEnabled: boolean; misfirePolicy: string }

const EMPTY: FormState = { name: '', description: '', cronExpression: '', jobType: '', queue: 'default', payload: '{}', priority: 'Normal', maxAttempts: 3, isEnabled: true, misfirePolicy: 'Skip' }

export default function Schedules() {
  const [schedules, setSchedules] = useState<Schedule[]>([])
  const [loading, setLoading] = useState(true)
  const [showModal, setShowModal] = useState(false)
  const [editing, setEditing] = useState<string | null>(null)
  const [form, setForm] = useState<FormState>(EMPTY)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  const load = async () => {
    setLoading(true)
    try { const { data } = await getSchedules(); setSchedules(data) } catch { /* ignore */ }
    setLoading(false)
  }

  useEffect(() => { load() }, [])

  const openCreate = () => { setForm(EMPTY); setEditing(null); setError(''); setShowModal(true) }
  const openEdit = (s: Schedule) => {
    setForm({ name: s.name, description: s.description || '', cronExpression: s.cronExpression, jobType: s.jobType, queue: s.queue, payload: s.payload, priority: s.priority, maxAttempts: s.maxAttempts, isEnabled: s.isEnabled, misfirePolicy: s.misfirePolicy })
    setEditing(s.id); setError(''); setShowModal(true)
  }

  const handleSave = async () => {
    setSaving(true); setError('')
    try {
      if (editing) await updateSchedule(editing, form)
      else await createSchedule(form)
      setShowModal(false); load()
    } catch (e: any) {
      setError(e?.response?.data?.error || 'Failed to save schedule.')
    }
    setSaving(false)
  }

  const handleDelete = async (id: string) => {
    if (!confirm('Delete this schedule?')) return
    await deleteSchedule(id); load()
  }

  const handleTrigger = async (id: string) => {
    await triggerSchedule(id)
    alert('Job triggered!')
  }

  return (
    <div>
      <div className="page-header">
        <div>
          <div className="page-title">Schedules</div>
          <div className="page-subtitle">Cron-based job schedules</div>
        </div>
        <button className="btn btn-primary" onClick={openCreate}>+ New Schedule</button>
      </div>

      <div className="table-wrapper">
        <table>
          <thead>
            <tr><th>Name</th><th>Cron</th><th>Job Type</th><th>Queue</th><th>Enabled</th><th>Last Run</th><th>Next Run</th><th>Actions</th></tr>
          </thead>
          <tbody>
            {loading ? (
              <tr><td colSpan={8} style={{ textAlign: 'center', padding: 40 }}><div className="spinner" style={{ margin: 'auto' }} /></td></tr>
            ) : schedules.length === 0 ? (
              <tr><td colSpan={8}><div className="empty-state">No schedules yet — create one!</div></td></tr>
            ) : schedules.map(s => (
              <tr key={s.id}>
                <td>
                  <div style={{ fontWeight: 500 }}>{s.name}</div>
                  {s.description && <div style={{ fontSize: 11, color: '#4a5568' }}>{s.description}</div>}
                </td>
                <td className="td-mono">{s.cronExpression}</td>
                <td>{s.jobType}</td>
                <td><span className="badge badge-processing">{s.queue}</span></td>
                <td>
                  <span className={`badge ${s.isEnabled ? 'badge-succeeded' : 'badge-inactive'}`}>
                    {s.isEnabled ? '● Active' : '○ Paused'}
                  </span>
                </td>
                <td className="td-mono">{s.lastRunAt ? new Date(s.lastRunAt).toLocaleString() : '—'}</td>
                <td className="td-mono">{s.nextRunAt ? new Date(s.nextRunAt).toLocaleString() : '—'}</td>
                <td>
                  <div className="flex gap-2">
                    <button className="btn btn-ghost btn-sm" onClick={() => handleTrigger(s.id)} title="Run now">▶</button>
                    <button className="btn btn-ghost btn-sm" onClick={() => openEdit(s)} title="Edit">✏️</button>
                    <button className="btn btn-ghost btn-sm" style={{ color: '#ef4444' }} onClick={() => handleDelete(s.id)} title="Delete">🗑</button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {showModal && (
        <div className="modal-overlay" onClick={() => setShowModal(false)}>
          <div className="modal" onClick={e => e.stopPropagation()}>
            <div className="modal-header">
              <span className="modal-title">{editing ? 'Edit Schedule' : 'New Schedule'}</span>
              <button className="btn btn-ghost btn-icon" onClick={() => setShowModal(false)}>✕</button>
            </div>
            {error && <div style={{ background: 'rgba(239,68,68,0.1)', border: '1px solid rgba(239,68,68,0.3)', borderRadius: 8, padding: '10px 14px', color: '#ef4444', marginBottom: 16, fontSize: 13 }}>{error}</div>}
            <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
              <div className="grid-2">
                <div className="form-group"><label className="form-label">Name</label><input className="form-input" value={form.name} onChange={e => setForm(f => ({ ...f, name: e.target.value }))} /></div>
                <div className="form-group"><label className="form-label">Cron Expression</label><input className="form-input text-mono" value={form.cronExpression} onChange={e => setForm(f => ({ ...f, cronExpression: e.target.value }))} placeholder="0 8 * * *" /></div>
              </div>
              <div className="grid-2">
                <div className="form-group"><label className="form-label">Job Type</label><input className="form-input" value={form.jobType} onChange={e => setForm(f => ({ ...f, jobType: e.target.value }))} /></div>
                <div className="form-group"><label className="form-label">Queue</label><input className="form-input" value={form.queue} onChange={e => setForm(f => ({ ...f, queue: e.target.value }))} /></div>
              </div>
              <div className="form-group"><label className="form-label">Payload (JSON)</label><textarea className="form-textarea" value={form.payload} onChange={e => setForm(f => ({ ...f, payload: e.target.value }))} /></div>
              <div className="grid-2">
                <div className="form-group"><label className="form-label">Priority</label>
                  <select className="form-select" value={form.priority} onChange={e => setForm(f => ({ ...f, priority: e.target.value }))}>
                    {['Low','Normal','High','Critical'].map(p => <option key={p}>{p}</option>)}
                  </select>
                </div>
                <div className="form-group"><label className="form-label">Misfire Policy</label>
                  <select className="form-select" value={form.misfirePolicy} onChange={e => setForm(f => ({ ...f, misfirePolicy: e.target.value }))}>
                    <option>Skip</option><option>RunOnce</option>
                  </select>
                </div>
              </div>
              <div className="form-group">
                <label style={{ display: 'flex', alignItems: 'center', gap: 8, cursor: 'pointer' }}>
                  <input type="checkbox" checked={form.isEnabled} onChange={e => setForm(f => ({ ...f, isEnabled: e.target.checked }))} />
                  <span style={{ fontSize: 13, fontWeight: 500 }}>Enabled</span>
                </label>
              </div>
            </div>
            <div className="modal-footer">
              <button className="btn btn-secondary" onClick={() => setShowModal(false)}>Cancel</button>
              <button className="btn btn-primary" onClick={handleSave} disabled={saving}>
                {saving ? <span className="spinner" /> : (editing ? 'Update' : 'Create')}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
