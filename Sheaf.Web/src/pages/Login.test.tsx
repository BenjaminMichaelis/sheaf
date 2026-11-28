import { screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { mockApi, renderApp } from '@/test/renderApp'
import Login from './Login'

describe('Login', () => {
  it('Login_SubmitInFlight_DisablesLoginButton', async () => {
    const fetchMock = mockApi({
      'GET /api/auth/user': () => new Response(null, { status: 401 }),
      'POST /api/auth/login': () => new Promise<Response>(() => {}),
    })

    const { user } = renderApp(<Login />)

    await user.type(screen.getByLabelText(/Email/), 'leader@example.com')
    await user.type(screen.getByLabelText(/Password/), 'password')
    await user.click(screen.getByRole('button', { name: 'Login' }))

    // A disabled button can't be pressed again, so the sign-in is sent once.
    const logins = fetchMock.mock.calls.filter(([request]) => new Request(request).method === 'POST')
    expect(screen.getByRole('button', { name: 'Login' })).toBeDisabled()
    expect(logins).toHaveLength(1)
  })
})
