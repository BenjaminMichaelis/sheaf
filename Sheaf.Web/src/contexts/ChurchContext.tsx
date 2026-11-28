import { createContext, useContext, useLayoutEffect, useMemo, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import type { components } from '@/api/schema'
import { can, type Capability } from '@/auth/permissions'
import { setActiveChurch } from '@/services/apiClient'
import { churchSettingsQuery } from '@/services/churchQueries'
import { useAuth } from './AuthContext'

type ChurchInfo = components['schemas']['ChurchInfo']

/** The church's own words for a group, e.g. "Village" and "Villages". */
export interface GroupLabels {
  one: string
  many: string
}

const DEFAULT_GROUP_LABELS: GroupLabels = { one: 'Group', many: 'Groups' }

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
  const settings = useQuery(churchSettingsQuery(churchId))

  // Not documented as an ordering guarantee: this relies on React committing layout effects (this one included)
  // before any passive useEffect, so children's first requests already carry the church. Requests made outside a
  // render cycle read the latest value, which is what the header check needs.
  useLayoutEffect(() => {
    setActiveChurch(churchId)
  }, [churchId])

  // Labels fall back to the defaults, but dates need the church's zone, so a failure is reported for retry. It is
  // hidden while the retry is in flight, and once settings have loaded a later failed refresh keeps them.
  const settingsError = settings.data === undefined && settings.isError && !settings.isFetching ? settings.error.message : null
  const { data, refetch } = settings

  const value = useMemo<ChurchContextType>(
    () => ({
      church,
      churches: churches ?? [],
      labels: data ? { one: data.groupLabel, many: data.groupLabelPlural } : DEFAULT_GROUP_LABELS,
      timeZoneId: data?.timeZoneId ?? null,
      settingsError,
      reloadSettings: () => void refetch(),
      can: capability => can(church?.role, capability),
      switchChurch,
    }),
    [church, churches, data, settingsError, refetch, switchChurch],
  )

  return <ChurchContext value={value}>{children}</ChurchContext>
}

// eslint-disable-next-line react-refresh/only-export-components
export function useChurch() {
  const context = useContext(ChurchContext)

  if (context === undefined) {
    throw new Error('useChurch must be used within a ChurchProvider')
  }

  return context
}
