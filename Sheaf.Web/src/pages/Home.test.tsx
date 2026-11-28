import { Route, Routes } from 'react-router-dom'
import { screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import ProtectedRoute from '@/components/ProtectedRoute'
import { FIRST_CHURCH, SECOND_CHURCH, json, mockApi, renderApp, signedInUser } from '@/test/renderApp'
import Home from './Home'

const settings = { name: 'Church 2', timeZoneId: 'America/Chicago', groupLabel: 'Group', groupLabelPlural: 'Groups' }

function renderRoutes(path = '/') {
  return renderApp(
    <Routes>
      <Route path="/" element={<Home />} />
      <Route
        path="/groups"
        element={
          <ProtectedRoute requireChurch>
            <p>Groups page</p>
          </ProtectedRoute>
        }
      />
    </Routes>,
    path,
  )
}

describe('Home', () => {
  it('Home_MemberOfChurchesWithNoneSelected_LetsUserChooseOne', async () => {
    const account = { ...signedInUser('Leader', [FIRST_CHURCH, SECOND_CHURCH]), currentChurch: null }
    mockApi({
      'GET /api/auth/user': () => json(account),
      'GET /api/church/settings': () => json(settings),
      [`POST /api/auth/church/${SECOND_CHURCH}`]: () => json({ ...account, currentChurch: account.churches?.[1] ?? null }),
    })

    const { user } = renderRoutes()
    await user.click(await screen.findByRole('button', { name: /Church 2/ }))

    expect(await screen.findByRole('heading', { name: 'Church 2' })).toBeInTheDocument()
    expect(screen.queryByText("You're not part of a church yet")).not.toBeInTheDocument()
  })

  it('Home_NoChurches_ExplainsHowToGetAccess', async () => {
    mockApi({ 'GET /api/auth/user': () => json({ ...signedInUser(), churches: [], currentChurch: null }) })

    renderRoutes()

    expect(await screen.findByRole('heading', { name: "You're not part of a church yet" })).toBeInTheDocument()
  })

  it('ProtectedRoute_ChurchPageWithoutChurch_SendsUserHome', async () => {
    mockApi({ 'GET /api/auth/user': () => json({ ...signedInUser(), churches: [], currentChurch: null }) })

    renderRoutes('/groups')

    expect(await screen.findByRole('heading', { name: "You're not part of a church yet" })).toBeInTheDocument()
    expect(screen.queryByText('Groups page')).not.toBeInTheDocument()
  })
})
