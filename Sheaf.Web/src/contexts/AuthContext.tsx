import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { UserInfo, LoginRequest, RegisterRequest } from '@/types'
import { apiClient } from '@/services/apiClient'

interface AuthContextType {
  user: UserInfo | null
  loading: boolean
  login: (credentials: LoginRequest) => Promise<void>
  register: (data: RegisterRequest) => Promise<void>
  logout: () => Promise<void>
  refreshUser: () => Promise<void>
  /** Selects another of the account's churches; the API re-issues the session for it. */
  switchChurch: (churchId: string) => Promise<void>
}

const AuthContext = createContext<AuthContextType | undefined>(undefined)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserInfo | null>(null)
  const [loading, setLoading] = useState(true)

  const refreshUser = useCallback(async () => {
    try {
      const { data } = await apiClient.GET('/api/auth/user')

      if (data === undefined) {
        throw new Error('User response is empty')
      }

      setUser(data)
    } catch {
      setUser(null)
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    let cancelled = false
    apiClient
      .GET('/api/auth/user')
      .then(({ data }) => {
        if (data === undefined) {
          throw new Error('User response is empty')
        }

        if (!cancelled) setUser(data)
      })
      .catch(() => {
        if (!cancelled) setUser(null)
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })

    return () => {
      cancelled = true
    }
  }, [])

  const login = useCallback(async (credentials: LoginRequest) => {
    const { data } = await apiClient.POST('/api/auth/login', { body: credentials })

    if (data === undefined) {
      throw new Error('Login response is empty')
    }

    setUser(data)
  }, [])

  const register = useCallback(async (data: RegisterRequest) => {
    const result = await apiClient.POST('/api/auth/register', { body: data })

    if (result.data === undefined) {
      throw new Error('Registration response is empty')
    }

    setUser(result.data)
  }, [])

  const logout = useCallback(async () => {
    await apiClient.POST('/api/auth/logout')

    setUser(null)
  }, [])

  const switchChurch = useCallback(async (churchId: string) => {
    const { data } = await apiClient.POST('/api/auth/church/{churchId}', { params: { path: { churchId } } })

    if (data === undefined) {
      throw new Error('Church switch response is empty')
    }

    setUser(data)
  }, [])

  // Stable functions and a memoized value, so consumers re-render only when the user or loading state changes
  // (react.dev/reference/react/useContext#optimizing-re-renders-when-passing-objects-and-functions).
  const value = useMemo(
    () => ({ user, loading, login, register, logout, refreshUser, switchChurch }),
    [user, loading, login, register, logout, refreshUser, switchChurch],
  )

  return <AuthContext value={value}>{children}</AuthContext>
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth() {
  const context = useContext(AuthContext)

  if (context === undefined) {
    throw new Error('useAuth must be used within an AuthProvider')
  }

  return context
}
