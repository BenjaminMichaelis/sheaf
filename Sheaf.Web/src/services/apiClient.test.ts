import { afterEach, describe, expect, expectTypeOf, it, vi } from 'vitest'
import type { UserInfo } from '@/types'

import { apiClient } from './apiClient'

describe('apiClient', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('GET_User_IncludesCredentialsAndReturnsTypedResponse', async () => {
    const user = {
      userId: 'user-1',
      userName: 'leader@example.com',
      email: 'leader@example.com',
      isAuthenticated: true,
    }

    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify(user), { status: 200 }),
    )

    const { data, error } = await apiClient.GET('/api/auth/user')

    expect(error).toBeUndefined()
    expect(data).toEqual(user)
    expectTypeOf(data).toEqualTypeOf<UserInfo | undefined>()
    expect(fetchMock).toHaveBeenCalledWith(
      expect.objectContaining({
        url: `${__API_BASE_URL__}/api/auth/user`,
        method: 'GET',
        credentials: 'include',
      }),
    )
  })

  it('POST_Login_SerializesTypedRequestAndIncludesCredentials', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(
        JSON.stringify({
          userId: 'user-1',
          userName: 'leader@example.com',
          email: 'leader@example.com',
          isAuthenticated: true,
        }),
        { status: 200 },
      ),
    )

    await apiClient.POST('/api/auth/login', {
      body: {
        email: 'leader@example.com',
        password: 'correct horse battery staple',
        rememberMe: false,
      },
    })

    const call = fetchMock.mock.calls[0]

    if (!call) {
      throw new Error('Fetch was not called')
    }

    const [request] = call

    expect(request).toMatchObject({
      url: `${__API_BASE_URL__}/api/auth/login`,
      method: 'POST',
      credentials: 'include',
    })
    expect(await new Request(request).json()).toEqual({
      email: 'leader@example.com',
      password: 'correct horse battery staple',
      rememberMe: false,
    })
  })

  it('POST_LogoutNoContent_ReturnsUndefined', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(null, { status: 204 }))

    const { data } = await apiClient.POST('/api/auth/logout')

    expect(data).toBeUndefined()
  })

  it('GET_EmptySuccessResponse_ReturnsUndefined', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response('', { status: 200 }))

    const { data } = await apiClient.GET('/api/auth/user')

    expect(data).toBeUndefined()
  })

  it('GET_UnauthorizedResponse_RejectsWithStatus', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(null, { status: 401 }))

    await expect(apiClient.GET('/api/auth/user')).rejects.toThrow('HTTP error! status: 401')
  })

  it('POST_RegistrationError_RejectsWithServerMessage', async () => {
    const problem = {
      title: 'One or more validation errors occurred.',
      status: 400,
      errors: { ConfirmPassword: ['Passwords do not match.'] },
    }

    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify(problem), {
      status: 400,
      headers: { 'content-type': 'application/problem+json' },
    }))

    await expect(apiClient.POST('/api/auth/register', {
      body: { email: 'leader@example.com', password: 'password', confirmPassword: 'different' },
    })).rejects.toThrow('Passwords do not match.')
  })

  it('POST_LogoutUnauthorizedWithoutBody_RejectsWithStatus', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(null, { status: 401 }))

    await expect(apiClient.POST('/api/auth/logout')).rejects.toThrow('HTTP error! status: 401')
  })
})
