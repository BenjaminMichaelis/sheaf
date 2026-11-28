import { createContext, useContext, useEffect, useLayoutEffect, useMemo, useState, type ReactNode } from 'react'
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
        if (!cancelled && data) {
          setSettings({
            churchId,
            labels: { one: data.groupLabel, many: data.groupLabelPlural },
            timeZoneId: data.timeZoneId,
          })
        }
      })
      // Settings only affect wording and date display: keep the defaults if they cannot be read.
      .catch(() => {})

    return () => {
      cancelled = true
    }
  }, [churchId])

  const current = settings !== null && settings.churchId === churchId ? settings : null

  const value = useMemo<ChurchContextType>(
    () => ({
      church,
      churches: churches ?? [],
      labels: current?.labels ?? DEFAULT_GROUP_LABELS,
      timeZoneId: current?.timeZoneId ?? null,
      can: capability => can(church?.role, capability),
      switchChurch,
    }),
    [church, churches, current, switchChurch],
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
