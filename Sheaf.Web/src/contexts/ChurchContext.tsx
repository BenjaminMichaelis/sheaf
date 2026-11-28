import { createContext, useCallback, useContext, useEffect, useLayoutEffect, useMemo, useState, type ReactNode } from 'react'
import type { components } from '@/api/schema'
import { can, type Capability } from '@/auth/permissions'
import { apiClient, setActiveChurch } from '@/services/apiClient'
import { useAuth } from './AuthContext'

type ChurchInfo = components['schemas']['ChurchInfo']

/** The church's own words for a group, e.g. "Village" and "Villages". */
export interface GroupLabels {
  one: string
  many: string
}

const DEFAULT_GROUP_LABELS: GroupLabels = { one: 'Group', many: 'Groups' }

interface ChurchSettings {
  churchId: string
  labels: GroupLabels
  timeZoneId: string
}

interface ChurchContextType {
  /** The church this page acts for, or null when the account has none selected. */
  church: ChurchInfo | null
  churches: readonly ChurchInfo[]
  labels: GroupLabels
  /** The IANA time zone the church's dates are kept in, or null until its settings load. */
  timeZoneId: string | null
  /** The message from the last failed settings request, or null. Church dates cannot be shown until it is resolved. */
  settingsError: string | null
  reloadSettings: () => void
  can: (capability: Capability) => boolean
  switchChurch: (churchId: string) => Promise<void>
}

const ChurchContext = createContext<ChurchContextType | undefined>(undefined)

export function ChurchProvider({ children }: { children: ReactNode }) {
  const { user, switchChurch } = useAuth()
  const church = user?.isAuthenticated ? (user.currentChurch ?? null) : null
  const churches = user?.isAuthenticated ? user.churches : undefined
  const churchId = church?.churchId ?? null
  const [settings, setSettings] = useState<ChurchSettings | null>(null)
  const [failure, setFailure] = useState<{ churchId: string; message: string } | null>(null)
  const [attempt, setAttempt] = useState(0)

  // Layout effects (this one included) all run before any child's useEffect, so the first requests children make
  // already carry the church.
  useLayoutEffect(() => {
    setActiveChurch(churchId)
  }, [churchId])

  useEffect(() => {
    if (churchId === null) return
    let cancelled = false

    apiClient
      .GET('/api/church/settings')
      .then(({ data }) => {
        if (cancelled) return

        if (!data) throw new Error('Church settings response is empty.')
        setSettings({
          churchId,
          labels: { one: data.groupLabel, many: data.groupLabelPlural },
          timeZoneId: data.timeZoneId,
        })
        setFailure(null)
      })
      // Labels fall back to the defaults, but dates need the church's zone, so the failure is reported for retry.
      .catch(caught => {
        if (!cancelled) setFailure({ churchId, message: caught instanceof Error ? caught.message : 'Could not load church settings.' })
      })

    return () => {
      cancelled = true
    }
  }, [churchId, attempt])

  const current = settings !== null && settings.churchId === churchId ? settings : null
  const settingsError = failure !== null && failure.churchId === churchId && current === null ? failure.message : null

  // Clears the old error so it does not linger while the retry is in flight.
  const reloadSettings = useCallback(() => {
    setFailure(null)
    setAttempt(value => value + 1)
  }, [])

  const value = useMemo<ChurchContextType>(
    () => ({
      church,
      churches: churches ?? [],
      labels: current?.labels ?? DEFAULT_GROUP_LABELS,
      timeZoneId: current?.timeZoneId ?? null,
      settingsError,
      reloadSettings,
      can: capability => can(church?.role, capability),
      switchChurch,
    }),
    [church, churches, current, settingsError, reloadSettings, switchChurch],
  )

  return <ChurchContext.Provider value={value}>{children}</ChurchContext.Provider>
}

// eslint-disable-next-line react-refresh/only-export-components
export function useChurch() {
  const context = useContext(ChurchContext)

  if (context === undefined) {
    throw new Error('useChurch must be used within a ChurchProvider')
  }

  return context
}
