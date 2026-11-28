import type { ReactElement } from 'react'
import { MemoryRouter } from 'react-router-dom'
import { SnackbarProvider } from 'notistack'
import { render } from '@testing-library/react'
import { vi } from 'vitest'
import type { UserInfo } from '@/types'
import { AuthProvider } from '@/contexts/AuthContext'
import { ChurchProvider } from '@/contexts/ChurchContext'
import { ThemeProvider } from '@/contexts/ThemeContext'

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

/** Renders `ui` inside the app's providers at `path`. */
export function renderApp(ui: ReactElement, path = '/') {
  return render(
    <ThemeProvider>
      <SnackbarProvider>
        <MemoryRouter initialEntries={[path]}>
          <AuthProvider>
            <ChurchProvider>{ui}</ChurchProvider>
          </AuthProvider>
        </MemoryRouter>
      </SnackbarProvider>
    </ThemeProvider>,
  )
}
