import axios from 'axios'

const BASE = import.meta.env.VITE_API_URL || ''

const api = axios.create({ baseURL: BASE })

api.interceptors.request.use(config => {
  const token = localStorage.getItem('atlas_token')
  const apiKey = localStorage.getItem('atlas_api_key')
  if (token) config.headers.Authorization = `Bearer ${token}`
  else if (apiKey) config.headers['X-Api-Key'] = apiKey
  return config
})

// ── Auth ───────────────────────────────────────────────────────────────────
export const login = (email: string, password: string) =>
  api.post('/api/auth/login', { email, password })

// ── Stats ──────────────────────────────────────────────────────────────────
export const getStats = () => api.get('/api/stats')

// ── Jobs ───────────────────────────────────────────────────────────────────
export const getJobs = (params?: Record<string, string | number>) =>
  api.get('/api/jobs', { params })

export const getJob = (id: string) => api.get(`/api/jobs/${id}`)

export const enqueueJob = (data: {
  queue: string; jobType: string; payload: string
  priority?: string; idempotencyKey?: string; maxAttempts?: number
}) => api.post('/api/jobs', data)

export const retryJob = (id: string) => api.post(`/api/jobs/${id}/retry`)
export const deleteJob = (id: string) => api.delete(`/api/jobs/${id}`)

// ── Schedules ──────────────────────────────────────────────────────────────
export const getSchedules = () => api.get('/api/schedules')
export const getSchedule  = (id: string) => api.get(`/api/schedules/${id}`)
export const createSchedule = (data: object) => api.post('/api/schedules', data)
export const updateSchedule = (id: string, data: object) => api.put(`/api/schedules/${id}`, data)
export const deleteSchedule = (id: string) => api.delete(`/api/schedules/${id}`)
export const triggerSchedule = (id: string) => api.post(`/api/schedules/${id}/trigger`)

// ── Workers ────────────────────────────────────────────────────────────────
export const getWorkers = () => api.get('/api/workers')

// ── Seeds ──────────────────────────────────────────────────────────────────
export const seedDemo = () => api.post('/api/seed')

export default api
