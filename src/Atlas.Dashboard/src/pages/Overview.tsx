import { useEffect, useState } from 'react'
import { getStats, seedDemo } from '../api'
import {
  AreaChart, Area, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer,
  PieChart, Pie, Cell
} from 'recharts'

interface Stats {
  total: number; pending: number; processing: number
  succeeded: number; failed: number; deadLettered: number
  workers: number; schedules: number
}

const COLORS = ['#f59e0b', '#6366f1', '#22c55e', '#ef4444', '#6b7280']

const StatCard = ({ label, value, color, icon }: { label: string; value: number; color: string; icon: string }) => (
  <div className="stat-card">
    <div className="stat-icon" style={{ background: color + '22' }}>{icon}</div>
    <div className="stat-value" style={{ color }}>{value.toLocaleString()}</div>
    <div className="stat-label">{label}</div>
  </div>
)

export default function Overview() {
  const [stats, setStats] = useState<Stats | null>(null)
  const [loading, setLoading] = useState(true)
  const [seeding, setSeeding] = useState(false)
  const [history, setHistory] = useState<{ time: string; pending: number; succeeded: number }[]>([])

  const load = async () => {
    try {
      const { data } = await getStats()
      setStats(data)
      setHistory(prev => {
        const entry = { time: new Date().toLocaleTimeString(), pending: data.pending, succeeded: data.succeeded }
        return [...prev.slice(-19), entry]
      })
    } catch { /* ignore */ }
    setLoading(false)
  }

  useEffect(() => {
    load()
    const interval = setInterval(load, 5000)
    return () => clearInterval(interval)
  }, [])

  const handleSeed = async () => {
    setSeeding(true)
    try { await seedDemo() } catch { /* ignore */ }
    setSeeding(false)
    load()
  }

  if (loading) return <div className="empty-state"><div className="spinner" /></div>
  if (!stats) return <div className="empty-state"><div className="empty-icon">⚠️</div><p>Failed to load stats</p></div>

  const pieData = [
    { name: 'Pending',   value: stats.pending },
    { name: 'Processing',value: stats.processing },
    { name: 'Succeeded', value: stats.succeeded },
    { name: 'Failed',    value: stats.failed + stats.deadLettered },
  ].filter(d => d.value > 0)

  return (
    <div>
      <div className="page-header">
        <div>
          <div className="page-title">Overview</div>
          <div className="page-subtitle">Real-time queue statistics</div>
        </div>
        <button className="btn btn-secondary" onClick={handleSeed} disabled={seeding}>
          {seeding ? <span className="spinner" /> : '🌱'} Seed Demo Data
        </button>
      </div>

      <div className="card-grid">
        <StatCard label="Total Jobs"  value={stats.total}       color="#94a3b8" icon="📋" />
        <StatCard label="Pending"     value={stats.pending}     color="#f59e0b" icon="⏳" />
        <StatCard label="Processing"  value={stats.processing}  color="#6366f1" icon="⚙️" />
        <StatCard label="Succeeded"   value={stats.succeeded}   color="#22c55e" icon="✅" />
        <StatCard label="Failed"      value={stats.failed}      color="#ef4444" icon="❌" />
        <StatCard label="Dead Letter" value={stats.deadLettered} color="#6b7280" icon="☠️" />
        <StatCard label="Workers"     value={stats.workers}     color="#38bdf8" icon="🖥️" />
        <StatCard label="Schedules"   value={stats.schedules}   color="#a78bfa" icon="📅" />
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: '1fr 340px', gap: 20, marginBottom: 24 }}>
        <div className="card">
          <h3 style={{ marginBottom: 16, fontWeight: 600, fontSize: 15 }}>Activity (live)</h3>
          <div className="chart-container">
            <ResponsiveContainer width="100%" height="100%">
              <AreaChart data={history}>
                <defs>
                  <linearGradient id="gPending" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="5%" stopColor="#f59e0b" stopOpacity={0.3}/>
                    <stop offset="95%" stopColor="#f59e0b" stopOpacity={0}/>
                  </linearGradient>
                  <linearGradient id="gSucceeded" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="5%" stopColor="#22c55e" stopOpacity={0.3}/>
                    <stop offset="95%" stopColor="#22c55e" stopOpacity={0}/>
                  </linearGradient>
                </defs>
                <CartesianGrid strokeDasharray="3 3" stroke="#1f2f47" />
                <XAxis dataKey="time" tick={{ fill: '#4a5568', fontSize: 11 }} tickLine={false} />
                <YAxis tick={{ fill: '#4a5568', fontSize: 11 }} tickLine={false} axisLine={false} />
                <Tooltip contentStyle={{ background: '#1a2235', border: '1px solid #1f2f47', borderRadius: 8 }} />
                <Area type="monotone" dataKey="pending" stroke="#f59e0b" fill="url(#gPending)" strokeWidth={2} name="Pending" />
                <Area type="monotone" dataKey="succeeded" stroke="#22c55e" fill="url(#gSucceeded)" strokeWidth={2} name="Succeeded" />
              </AreaChart>
            </ResponsiveContainer>
          </div>
        </div>

        <div className="card">
          <h3 style={{ marginBottom: 16, fontWeight: 600, fontSize: 15 }}>Status Distribution</h3>
          {pieData.length === 0 ? (
            <div className="empty-state" style={{ padding: '40px 0' }}>No jobs yet</div>
          ) : (
            <div style={{ height: 180 }}>
              <ResponsiveContainer>
                <PieChart>
                  <Pie data={pieData} cx="50%" cy="50%" innerRadius={50} outerRadius={80} paddingAngle={3} dataKey="value">
                    {pieData.map((_, i) => <Cell key={i} fill={COLORS[i % COLORS.length]} />)}
                  </Pie>
                  <Tooltip contentStyle={{ background: '#1a2235', border: '1px solid #1f2f47', borderRadius: 8 }} />
                </PieChart>
              </ResponsiveContainer>
            </div>
          )}
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8, marginTop: 8 }}>
            {pieData.map((d, i) => (
              <span key={d.name} style={{ fontSize: 11, color: '#94a3b8', display: 'flex', alignItems: 'center', gap: 4 }}>
                <span style={{ width: 8, height: 8, borderRadius: '50%', background: COLORS[i], display: 'inline-block' }} />
                {d.name}: {d.value}
              </span>
            ))}
          </div>
        </div>
      </div>
    </div>
  )
}
