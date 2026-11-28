import { Route, Routes } from 'react-router-dom'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { FIRST_CHURCH, SECOND_CHURCH, json, mockApi, renderApp, signedInUser } from '@/test/renderApp'
import Layout from './Layout'

const villageSettings = {
  name: 'Church 1',
  timeZoneId: 'America/Chicago',
  groupLabel: 'Village',
  groupLabelPlural: 'Villages',
}

function renderLayout() {
  return renderApp(
    <Routes>
      <Route path="/" element={<Layout />}>
        <Route index element={<p>Home page</p>} />
      </Route>
    </Routes>,
  )
}

describe('Layout', () => {
  it('Layout_SignedInToOneChurch_ShowsChurchNameAndChurchLabelsInNavigation', async () => {
    mockApi({
      'GET /api/auth/user': () => json(signedInUser()),
      'GET /api/church/settings': () => json(villageSettings),
    })

    renderLayout()

    expect(await screen.findByText('Church 1')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Open navigation' }))
    const nav = screen.getByRole('navigation', { name: 'Main' })
    expect(await within(nav).findByText('Villages')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Switch church/ })).not.toBeInTheDocument()
  })

  it('Layout_SignedInToSeveralChurches_SwitchesToChosenChurch', async () => {
    const user = signedInUser('Leader', [FIRST_CHURCH, SECOND_CHURCH])
    const switched = { ...user, currentChurch: user.churches?.[1] ?? null }

    const fetchMock = mockApi({
      'GET /api/auth/user': () => json(user),
      'GET /api/church/settings': () => json(villageSettings),
      [`POST /api/auth/church/${SECOND_CHURCH}`]: () => json(switched),
    })

    renderLayout()
    await userEvent.click(await screen.findByRole('button', { name: 'Current church: Church 1. Switch church' }))
    await userEvent.click(screen.getByRole('menuitem', { name: 'Church 2' }))

    expect(await screen.findByRole('button', { name: 'Current church: Church 2. Switch church' })).toBeInTheDocument()

    const switchRequest = fetchMock.mock.calls
      .map(([request]) => new Request(request))
      .find(request => request.method === 'POST')

    expect(new URL(switchRequest?.url ?? 'http://x').pathname).toBe(`/api/auth/church/${SECOND_CHURCH}`)
  })

  it('Layout_SignedInWithoutChurch_HidesNavigation', async () => {
    mockApi({ 'GET /api/auth/user': () => json({ ...signedInUser(), churches: [], currentChurch: null }) })

    renderLayout()

    await waitFor(() => expect(screen.getByRole('button', { name: 'Logout' })).toBeInTheDocument())
    expect(screen.queryByRole('button', { name: 'Open navigation' })).not.toBeInTheDocument()
    expect(screen.queryByRole('navigation', { name: 'Main' })).not.toBeInTheDocument()
  })
})
