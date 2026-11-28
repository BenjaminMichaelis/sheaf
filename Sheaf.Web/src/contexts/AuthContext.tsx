import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import {
  UserInfo,
  LoginRequest,
  RegisterRequest,
  ConfirmEmailRequest,
  ConfirmEmailChangeRequest,
  PendingVerification,
  UpdateProfileRequest,
} from '@/types'
import { apiClient, ApiError, EMAIL_NOT_CONFIRMED } from '@/services/apiClient'

interface AuthContextType {
  user: UserInfo | null
  loading: boolean
  /** Set after sign-up, or after a sign-in the server refused until the email is verified. */
  pendingVerification: PendingVerification | null
  /** Signs in. Throws an {@link ApiError}; when it is {@link EMAIL_NOT_CONFIRMED}, `pendingVerification` is set first. */
  login: (credentials: LoginRequest) => Promise<void>
  /** Creates the account and emails a code. Nobody is signed in until the code is confirmed. */
  register: (data: RegisterRequest) => Promise<void>
  confirmEmail: (request: ConfirmEmailRequest) => Promise<void>
  /** Forgets the held address and password, e.g. to verify a different account. */
  clearPendingVerification: () => void
  /** Restarts the hold on the address and password, after a new code (with a new 10 minutes) was requested. */
  renewPendingVerification: () => void
  /** Finishes moving the sign-in email to a new address with the code sent there, and updates the signed-in user. */
  confirmEmailChange: (request: ConfirmEmailChangeRequest) => Promise<void>
  /** Changes the signed-in account's name. */
  updateProfile: (request: UpdateProfileRequest) => Promise<void>
  logout: () => Promise<void>
  refreshUser: () => Promise<void>
  /** Selects another of the account's churches; the API re-issues the session for it. */
  switchChurch: (churchId: string) => Promise<void>
}

const PENDING_VERIFICATION_LIFETIME_MS = 10 * 60 * 1000

const AuthContext = createContext<AuthContextType | undefined>(undefined)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserInfo | null>(null)
  const [loading, setLoading] = useState(true)
  const [pendingVerification, setPendingVerification] = useState<PendingVerification | null>(null)

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

  // Codes expire after about 10 minutes, so there is no reason to hold the password longer than that.
  useEffect(() => {
    if (pendingVerification === null) return

    const timer = setTimeout(() => setPendingVerification(null), PENDING_VERIFICATION_LIFETIME_MS)

    return () => clearTimeout(timer)
  }, [pendingVerification])

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
    try {
      const { data } = await apiClient.POST('/api/auth/login', { body: credentials })

      if (data === undefined) {
        throw new Error('Login response is empty')
      }

      setPendingVerification(null)
      setUser(data)
    } catch (error) {
      if (error instanceof ApiError && error.problemType === EMAIL_NOT_CONFIRMED) {
        setPendingVerification({ email: credentials.email, password: credentials.password })
      }

      throw error
    }
  }, [])

  const register = useCallback(async (data: RegisterRequest) => {
    await apiClient.POST('/api/auth/register', { body: data })

    setPendingVerification({ email: data.email, password: data.password })
  }, [])

  const confirmEmail = useCallback(async (request: ConfirmEmailRequest) => {
    const { data } = await apiClient.POST('/api/auth/confirm-email', { body: request })

    if (data === undefined) {
      throw new Error('Verification response is empty')
    }

    setPendingVerification(null)
    setUser(data)
  }, [])

  const confirmEmailChange = useCallback(async (request: ConfirmEmailChangeRequest) => {
    const { data } = await apiClient.POST('/api/auth/email/confirm', { body: request })

    if (data === undefined) {
      throw new Error('Email change response is empty')
    }

    setUser(data)
  }, [])

  const updateProfile = useCallback(async (request: UpdateProfileRequest) => {
    const { data } = await apiClient.PUT('/api/auth/profile', { body: request })

    if (data === undefined) {
      throw new Error('Profile response is empty')
    }

    setUser(data)
  }, [])

  const logout = useCallback(async () => {
    setPendingVerification(null)
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

  const clearPendingVerification = useCallback(() => setPendingVerification(null), [])

  // A new object restarts the expiry effect above.
  const renewPendingVerification = useCallback(
    () => setPendingVerification((pending) => (pending === null ? null : { ...pending })),
    [],
  )

  // Stable functions and a memoized value, so consumers re-render only when the user or loading state changes
  // (react.dev/reference/react/useContext#optimizing-re-renders-when-passing-objects-and-functions).
  const value = useMemo(
    () => ({
      user,
      loading,
      pendingVerification,
      login,
      register,
      confirmEmail,
      clearPendingVerification,
      renewPendingVerification,
      confirmEmailChange,
      updateProfile,
      logout,
      refreshUser,
      switchChurch,
    }),
    [
      user,
      loading,
      pendingVerification,
      login,
      register,
      confirmEmail,
      clearPendingVerification,
      renewPendingVerification,
      confirmEmailChange,
      updateProfile,
      logout,
      refreshUser,
      switchChurch,
    ],
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
