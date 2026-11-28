import { afterEach, describe, expect, expectTypeOf, it, vi } from 'vitest'
import type { UserInfo } from '@/types'

import { apiClient, ApiError, EMAIL_NOT_CONFIRMED } from './apiClient'

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
      churches: [{
        churchId: '019a0000-0000-7000-8000-000000000001',
        churchName: 'Community Church',
        role: 'Coordinator',
      }],
      currentChurch: {
        churchId: '019a0000-0000-7000-8000-000000000001',
        churchName: 'Community Church',
        role: 'Coordinator',
      },
    } satisfies UserInfo

    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify(user), { status: 200 }),
    )

    const { data, error } = await apiClient.GET('/api/auth/user')

    expect(error).toBeUndefined()
    expect(data).toEqual(user)
    expectTypeOf(data).toEqualTypeOf<UserInfo | undefined>()
    expectTypeOf(data?.currentChurch?.role).toEqualTypeOf<
      'Leader' | 'Coordinator' | 'Staff' | 'Admin' | undefined
    >()
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
          churches: [],
          currentChurch: null,
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

  it('POST_HouseholdOmittedLabel_SerializesMemberIdsOnly', async () => {
    const memberIds = ['019a0000-0000-7000-8000-000000000001']
    const version = '019a0000-0000-7000-8000-0000000000ff'

    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async () =>
      new Response(JSON.stringify({ id: 'household-1', label: null, members: [], version }), { status: 201 }),
    )

    await apiClient.POST('/api/church/households', { body: { memberIds } })

    await apiClient.PUT('/api/church/households/{id}', {
      params: { path: { id: 'household-1' }, header: { 'If-Match': `"${version}"` } },
      body: { memberIds },
    })

    expect(fetchMock).toHaveBeenCalledTimes(2)

    const requests = fetchMock.mock.calls.map(([request]) => new Request(request))

    for (const request of requests) {
      expect(await request.json()).toEqual({ memberIds })
    }

    expect(requests.map((request) => request.headers.get('If-Match'))).toEqual([null, `"${version}"`])
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
      body: {
        email: 'leader@example.com',
        password: 'password',
        confirmPassword: 'different',
        firstName: 'Lena',
        lastName: 'Leader',
      },
    })).rejects.toThrow('Passwords do not match.')
  })

  it('PUT_ValidationProblem_RejectsWithServerMessage', async () => {
    const problem = {
      title: 'One or more validation errors occurred.',
      status: 400,
      errors: { FirstName: ['Enter a first name.'] },
    }

    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify(problem), {
      status: 400,
      headers: { 'content-type': 'application/problem+json' },
    }))

    await expect(apiClient.PUT('/api/auth/profile', {
      body: { firstName: '', lastName: 'Leader' },
    })).rejects.toThrow('Enter a first name.')
  })

  it('POST_LoginUnverifiedEmail_RejectsWithProblemType', async () => {
    const problem = {
      type: EMAIL_NOT_CONFIRMED,
      title: 'Verify your email address.',
      status: 403,
      detail: 'Enter the code we emailed you to finish signing in.',
    }

    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify(problem), {
      status: 403,
      headers: { 'content-type': 'application/problem+json' },
    }))

    const failure = apiClient.POST('/api/auth/login', {
      body: { email: 'leader@example.com', password: 'correct horse battery staple' },
    })

    await expect(failure).rejects.toBeInstanceOf(ApiError)
    await expect(failure).rejects.toMatchObject({ status: 403, problemType: EMAIL_NOT_CONFIRMED })
  })

  it('POST_LogoutUnauthorizedWithoutBody_RejectsWithStatus', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(null, { status: 401 }))

    await expect(apiClient.POST('/api/auth/logout')).rejects.toThrow('HTTP error! status: 401')
  })
})
