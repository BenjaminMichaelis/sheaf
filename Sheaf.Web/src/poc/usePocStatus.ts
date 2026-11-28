import { useEffect, useState } from 'react'
import { apiClient } from '@/services/apiClient'
import type { components } from '@/api/schema'

export type PocStatus = components['schemas']['PocStatusResponse']

/** Whether Claude is live and whether the Development-only demo tools exist. */
export function usePocStatus() {
  const [status, setStatus] = useState<PocStatus | null>(null)

  useEffect(() => {
    let cancelled = false
    apiClient
      .GET('/api/poc/status')
      .then(({ data }) => {
        if (!cancelled && data) setStatus(data)
      })
      .catch(() => {
        if (!cancelled) setStatus(null)
      })

    return () => {
      cancelled = true
    }
  }, [])

  return status
}
