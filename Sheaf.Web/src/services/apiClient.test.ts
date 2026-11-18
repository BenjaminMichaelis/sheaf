import { afterEach, describe, expect, expectTypeOf, it, vi } from 'vitest'

import { apiClient } from './apiClient'

describe('ApiClient', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('Post_FalseBody_SendsSerializedFalse', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response('{"ok":true}', { status: 200 }),
    )

    await apiClient.post('/test', false)

    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining('/test'),
      expect.objectContaining({ body: 'false' }),
    )
  })

  it.each(['get', 'post', 'put', 'delete'] as const)(
    '%s_NoContentResponse_ReturnsUndefined',
    async method => {
      vi.spyOn(globalThis, 'fetch').mockResolvedValue(
        new Response(null, { status: 204 }),
      )

      const result = apiClient[method]<{ ok: boolean }>('/test')

      expectTypeOf(result).toEqualTypeOf<Promise<{ ok: boolean } | undefined>>()

      await expect(result).resolves.toBeUndefined()
    },
  )

  it.each(['get', 'post'] as const)(
    '%s_EmptySuccessResponse_ReturnsUndefined',
    async method => {
      vi.spyOn(globalThis, 'fetch').mockResolvedValue(
        new Response('', { status: 200 }),
      )

      await expect(apiClient[method]<{ ok: boolean }>('/test')).resolves.toBeUndefined()
    },
  )

  it.each(['get', 'post', 'put', 'delete'] as const)(
    '%s_JsonResponse_ReturnsParsedBody',
    async method => {
      vi.spyOn(globalThis, 'fetch').mockResolvedValue(
        new Response('{"ok":true}', { status: 200 }),
      )

      await expect(apiClient[method]<{ ok: boolean }>('/test')).resolves.toEqual({ ok: true })
    },
  )
})
