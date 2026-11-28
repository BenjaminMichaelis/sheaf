import type { ReactElement } from 'react'
import { MemoryRouter } from 'react-router-dom'
import { SnackbarProvider } from 'notistack'
import { QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { vi } from 'vitest'
import type { UserInfo } from '@/types'
import AppThemeProvider from '@/components/AppThemeProvider'
import { AuthProvider } from '@/contexts/AuthContext'
import { ChurchProvider } from '@/contexts/ChurchContext'
import { createQueryClient } from '@/services/queryClient'

export type ChurchRole = NonNullable<UserInfo['currentChurch']>['role']

export const FIRST_CHURCH = '019a0000-0000-7000-8000-000000000001'
export const SECOND_CHURCH = '019a0000-0000-7000-8000-000000000002'

/** A signed-in account in `churches`, acting in the first one. */
export function signedInUser(role: ChurchRole = 'Leader', churches = [FIRST_CHURCH]): UserInfo {
  const infos = churches.map((churchId, index) => ({ churchId, churchName: `Church ${index + 1}`, role }))

  return {
    userId: 'user-1',
    userName: 'leader@example.com',
    email: 'leader@example.com',
    isAuthenticated: true,
    churches: infos,
    currentChurch: infos[0] ?? null,
  }
}

/** A stub may return a never-settling promise to model a request still in flight. */
type Handler = (request: Request) => Response | Promise<Response>

export const json = <T,>(body: T, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })

/**
 * Answers fetches by "METHOD /path" (path without the query string). Unknown requests fail the test with 404 so
 * missing stubs are obvious. Returns the spy so tests can inspect the requests sent.
 */
export function mockApi(routes: Record<string, Handler>) {
  return vi.spyOn(globalThis, 'fetch').mockImplementation(async input => {
    const request = input instanceof Request ? input : new Request(input)
    const key = `${request.method} ${new URL(request.url).pathname}`
    const handler = routes[key]

    return handler ? handler(request) : new Response(`No stub for ${key}`, { status: 404 })
  })
}

/**
 * Renders `ui` inside the app's providers at `path`, with a user-event session set up first as Testing Library recommends.
 * Each render gets its own app query cache with retries off, as TanStack Query's testing guide recommends, so tests
 * share no cached data and a failure shows at once.
 */
export function renderApp(ui: ReactElement, path = '/') {
  const user = userEvent.setup()
  const queryClient = createQueryClient()
  queryClient.setDefaultOptions({ queries: { ...queryClient.getDefaultOptions().queries, retry: false } })

  const result = render(
    <QueryClientProvider client={queryClient}>
      <AppThemeProvider>
        <SnackbarProvider>
          <MemoryRouter initialEntries={[path]}>
            <AuthProvider>
              <ChurchProvider>{ui}</ChurchProvider>
            </AuthProvider>
          </MemoryRouter>
        </SnackbarProvider>
      </AppThemeProvider>
    </QueryClientProvider>,
  )

  // The query cache is returned so a test can act as a background refresh would.
  return { user, queryClient, ...result }
}
