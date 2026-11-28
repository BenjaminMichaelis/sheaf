import { onlineManager } from '@tanstack/react-query'
import { describe, expect, it, vi } from 'vitest'
import { ApiError } from './apiClient'
import { createQueryClient } from './queryClient'

/** How many times a query runs when every attempt fails with `error`. */
async function attemptsFailingWith(error: Error) {
  const queryFn = vi.fn(() => Promise.reject(error))

  await createQueryClient()
    .fetchQuery({ queryKey: ['test'], queryFn, retryDelay: 0 })
    .catch(() => {})

  return queryFn.mock.calls.length
}

describe('createQueryClient', () => {
  it('fetchQuery_ClientError_DoesNotRetry', async () => {
    expect(await attemptsFailingWith(new ApiError('Not found.', 404, null))).toBe(1)
  })

  it('fetchQuery_ServerError_RetriesThreeTimes', async () => {
    expect(await attemptsFailingWith(new ApiError('Unavailable.', 503, null))).toBe(4)
  })

  it('fetchQuery_NetworkError_RetriesThreeTimes', async () => {
    expect(await attemptsFailingWith(new TypeError('Failed to fetch'))).toBe(4)
  })

  it('fetchQuery_NetworkErrorWhileOffline_DoesNotRetry', async () => {
    onlineManager.setOnline(false)

    try {
      expect(await attemptsFailingWith(new TypeError('Failed to fetch'))).toBe(1)
    } finally {
      onlineManager.setOnline(true)
    }
  })
})
