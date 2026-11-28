import { Route, Routes } from 'react-router-dom'
import { screen, waitFor, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import type { components } from '@/api/schema'
import Person from '@/pages/Person'
import { json, mockApi, renderApp, signedInUser } from '@/test/renderApp'

type PersonResponse = components['schemas']['PersonResponse']

type HouseholdResponse = components['schemas']['HouseholdResponse']

type HouseholdRequest = components['schemas']['HouseholdRequest']

type PersonMatch = components['schemas']['PersonLookupResponse']

const settings = { name: 'Church 1', timeZoneId: 'America/Chicago', groupLabel: 'Village', groupLabelPlural: 'Villages' }

const max = (changes: Partial<PersonResponse> = {}): PersonResponse => ({
  id: 'p-max',
  name: 'Max Member',
  firstName: 'Max',
  lastName: 'Member',
  email: null,
  phone: null,
  householdId: 'h1',
  householdLabel: 'The Member Family',
  householdMembers: [{ id: 'p-mia', firstName: 'Mia' }],
  isActive: true,
  canEdit: true,
  version: 'v1',
  mergedIntoId: null,
  ...changes,
})

const family = (changes: Partial<HouseholdResponse> = {}): HouseholdResponse => ({
  id: 'h1',
  label: 'The Member Family',
  members: [
    { id: 'p-max', firstName: 'Max' },
    { id: 'p-mia', firstName: 'Mia' },
  ],
  version: 'hv1',
  ...changes,
})

const problem = (status: number, title: string) =>
  new Response(JSON.stringify({ title, status }), { status, headers: { 'content-type': 'application/problem+json' } })

interface Saved {
  method: string
  request: HouseholdRequest
  ifMatch: string | null
}

interface ApiOptions {
  /** Returned in turn by each profile read. */
  people?: PersonResponse[]
  /** Returned in turn by each household read: a household, or a function answering with an error. */
  households?: (HouseholdResponse | (() => Response))[]
  lookup?: PersonMatch[]
  save?: (request: HouseholdRequest) => Response
}

/** Stubs Max's page and household; records every household save and whether the household was read. */
function mockHouseholdApi({ people = [max()], households = [family()], lookup = [], save = request => json(family({ members: request.memberIds.map(id => ({ id, firstName: id })), version: 'hv2' })) }: ApiOptions = {}) {
  const saved: Saved[] = []
  let personReads = 0
  let householdReads = 0

  const record = async (request: Request) => {
    const body: HouseholdRequest = await request.json()
    saved.push({ method: request.method, request: body, ifMatch: request.headers.get('If-Match') })

    return save(body)
  }

  mockApi({
    'GET /api/auth/user': () => json(signedInUser()),
    'GET /api/church/settings': () => json(settings),
    'GET /api/church/people/p-max': () => json(people[Math.min(personReads++, people.length - 1)]),
    'GET /api/church/people/p-max/groups': () => json([]),
    'GET /api/church/people/p-max/attendance': () => json({ items: [], nextOffset: null }),
    'GET /api/church/households/h1': () => {
      const next = households[Math.min(householdReads++, households.length - 1)] ?? family()

      return next instanceof Function ? next() : json(next)
    },
    'GET /api/church/people/lookup': () => json(lookup),
    'PUT /api/church/households/h1': record,
    'POST /api/church/households': record,
  })

  return { saved, householdReads: () => householdReads }
}

function renderPerson() {
  return renderApp(
    <Routes>
      <Route path="/people/:personId" element={<Person />} />
    </Routes>,
    '/people/p-max',
  )
}

const householdList = () => screen.findByRole('list', { name: 'Household' })

describe('HouseholdSection', () => {
  it('HouseholdSection_CanManage_ListsMembersByFirstNameLinkingToThem', async () => {
    mockHouseholdApi()
    renderPerson()

    const members = await householdList()
    expect(screen.getByText('The Member Family')).toBeInTheDocument()
    expect(within(members).getByRole('link', { name: 'Max (this person)' })).toHaveAttribute('href', '/people/p-max')
    expect(within(members).getByRole('link', { name: 'Mia' })).toHaveAttribute('href', '/people/p-mia')
  })

  it('HouseholdSection_CannotManage_ShowsTheProfilesHouseholdWithoutLoadingOrChangingIt', async () => {
    const api = mockHouseholdApi({ people: [max({ canEdit: false })] })
    renderPerson()

    expect(within(await householdList()).getByText('Mia')).toBeInTheDocument()
    expect(screen.getByText('The Member Family')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Remove Mia' })).not.toBeInTheDocument()
    expect(screen.queryByRole('combobox', { name: 'Add someone to this household' })).not.toBeInTheDocument()
    expect(api.householdReads()).toBe(0)
  })

  it('HouseholdSection_HouseholdRefusedToTheUser_ShowsTheProfilesHouseholdReadOnly', async () => {
    mockHouseholdApi({ households: [() => problem(404, 'Resource not found.')] })
    renderPerson()

    expect(within(await householdList()).getByText('Mia')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Remove Mia' })).not.toBeInTheDocument()
  })

  it('HouseholdSection_NoHousehold_StartsOneWithThisPerson', async () => {
    const { saved } = mockHouseholdApi({
      people: [max({ householdId: null, householdLabel: null, householdMembers: [] }), max({ householdLabel: 'Max and friends' })],
      save: () => json(family({ label: 'Max and friends' })),
    })

    const { user } = renderPerson()

    expect(await screen.findByText('Not in a household.')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Start a household' }))
    expect(screen.getByText('Left empty, it is called "The Member Family".')).toBeInTheDocument()
    await user.type(screen.getByLabelText('Household name (optional)'), 'Max and friends')
    await user.click(screen.getByRole('button', { name: 'Start household' }))

    expect(await screen.findByText('Household started.')).toBeInTheDocument()
    expect(saved).toEqual([{ method: 'POST', request: { memberIds: ['p-max'], label: 'Max and friends' }, ifMatch: null }])
  })

  it('HouseholdSection_AddSomeoneWithoutAHousehold_SavesThemForTheVersionShown', async () => {
    const { saved } = mockHouseholdApi({ lookup: [{ id: 'p-zoe', name: 'Zoe Visitor', householdLabel: null, groups: null }] })
    const { user } = renderPerson()

    await user.type(await screen.findByRole('combobox', { name: 'Add someone to this household' }), 'Zo')
    await user.click(await screen.findByRole('option', { name: /^Zoe Visitor/ }))

    expect(await screen.findByText('Zoe Visitor added.')).toBeInTheDocument()
    expect(saved).toEqual([{ method: 'PUT', request: { memberIds: ['p-max', 'p-mia', 'p-zoe'], label: null }, ifMatch: '"hv1"' }])
  })

  it('HouseholdSection_AddSomeoneFromAnotherHousehold_MovesThemOnlyWhenConfirmed', async () => {
    const { saved } = mockHouseholdApi({ lookup: [{ id: 'p-zoe', name: 'Zoe Visitor', householdLabel: 'The Visitor Family', groups: null }] })
    const { user } = renderPerson()

    await user.type(await screen.findByRole('combobox', { name: 'Add someone to this household' }), 'Zo')
    await user.click(await screen.findByRole('option', { name: /^Zoe Visitor/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Move to this household?' })
    expect(within(dialog).getByText(/Zoe Visitor is in The Visitor Family/)).toBeInTheDocument()
    expect(saved).toEqual([])
    await user.click(within(dialog).getByRole('button', { name: 'Move here' }))

    expect(await screen.findByText('Zoe Visitor moved here.')).toBeInTheDocument()
    expect(saved[0]?.request.memberIds).toEqual(['p-max', 'p-mia', 'p-zoe'])
  })

  it('HouseholdSection_AddSomeoneAlreadyInIt_SaysSoWithoutSaving', async () => {
    const { saved } = mockHouseholdApi({ lookup: [{ id: 'p-mia', name: 'Mia Member', householdLabel: 'The Member Family', groups: null }] })
    const { user } = renderPerson()

    await user.type(await screen.findByRole('combobox', { name: 'Add someone to this household' }), 'Mi')
    await user.click(await screen.findByRole('option', { name: /^Mia Member/ }))

    expect(await screen.findByText('Mia Member is already in this household.')).toBeInTheDocument()
    expect(saved).toEqual([])
  })

  it('HouseholdSection_SearchToAdd_DoesNotOfferToCreateSomeone', async () => {
    mockHouseholdApi()
    const { user } = renderPerson()

    await user.type(await screen.findByRole('combobox', { name: 'Add someone to this household' }), 'Nobody')

    expect(await screen.findByText('No one found')).toBeInTheDocument()
    expect(screen.queryByRole('option', { name: /Create new person/ })).not.toBeInTheDocument()
  })

  it('HouseholdSection_RemoveMember_SavesTheOthers', async () => {
    const { saved } = mockHouseholdApi()
    const { user } = renderPerson()

    await user.click(await screen.findByRole('button', { name: 'Remove Mia' }))

    expect(await screen.findByText('Mia removed.')).toBeInTheDocument()
    expect(saved[0]).toEqual({ method: 'PUT', request: { memberIds: ['p-max'], label: null }, ifMatch: '"hv1"' })
  })

  it('HouseholdSection_Rename_SavesTheNewNameWithTheSameMembers', async () => {
    const { saved } = mockHouseholdApi()
    const { user } = renderPerson()

    const name = await screen.findByLabelText('Household name')
    expect(screen.getByRole('button', { name: 'Rename' })).toBeDisabled()
    await user.clear(name)
    await user.type(name, 'The Members')
    await user.click(screen.getByRole('button', { name: 'Rename' }))

    expect(await screen.findByText('Household renamed.')).toBeInTheDocument()
    expect(saved[0]?.request).toEqual({ memberIds: ['p-max', 'p-mia'], label: 'The Members' })
  })

  it('HouseholdSection_ChangedBySomeoneElseFirst_SaysSoAndReloadsTheirVersion', async () => {
    mockHouseholdApi({
      households: [family(), family({ label: 'The Members', version: 'hv2' })],
      save: () => problem(412, 'The resource changed since it was read.'),
    })
    const { user } = renderPerson()

    await user.click(await screen.findByRole('button', { name: 'Remove Mia' }))
    expect(await screen.findByText(/Someone changed this household since you opened it/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Rename' })).toBeDisabled()
    await user.click(screen.getByRole('button', { name: 'Reload' }))

    expect(await screen.findByDisplayValue('The Members')).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByText(/Someone changed this household/)).not.toBeInTheDocument())
  })

  it('HouseholdSection_SaveRefused_ShowsTheServersReason', async () => {
    mockHouseholdApi({
      lookup: [{ id: 'p-zoe', name: 'Zoe Visitor', householdLabel: null, groups: null }],
      save: () => problem(403, 'Access denied.'),
    })
    const { user } = renderPerson()

    await user.type(await screen.findByRole('combobox', { name: 'Add someone to this household' }), 'Zo')
    await user.click(await screen.findByRole('option', { name: /^Zoe Visitor/ }))

    expect(await screen.findByText('Access denied.')).toBeInTheDocument()
  })
})
