import { createContext, useContext, useState, type ReactNode } from 'react'
import { login as apiLogin } from './api'

interface AuthCtx {
  token: string | null
  isAuth: boolean
  loginWithEmail: (email: string, password: string) => Promise<void>
  loginWithApiKey: (key: string) => void
  logout: () => void
}

const AuthContext = createContext<AuthCtx>({} as AuthCtx)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [token, setToken] = useState<string | null>(localStorage.getItem('atlas_token'))

  const loginWithEmail = async (email: string, password: string) => {
    const { data } = await apiLogin(email, password)
    localStorage.setItem('atlas_token', data.token)
    localStorage.removeItem('atlas_api_key')
    setToken(data.token)
  }

  const loginWithApiKey = (key: string) => {
    localStorage.setItem('atlas_api_key', key)
    localStorage.removeItem('atlas_token')
    setToken(key) // use key as token signal
  }

  const logout = () => {
    localStorage.removeItem('atlas_token')
    localStorage.removeItem('atlas_api_key')
    setToken(null)
  }

  return (
    <AuthContext.Provider value={{ token, isAuth: !!token, loginWithEmail, loginWithApiKey, logout }}>
      {children}
    </AuthContext.Provider>
  )
}

export const useAuth = () => useContext(AuthContext)
