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

const noHousehold: Partial<PersonResponse> = { householdId: null, householdLabel: null, householdMembers: [] }

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

const zoe = (changes: Partial<PersonMatch> = {}): PersonMatch => ({
  id: 'p-zoe',
  name: 'Zoe Visitor',
  householdLabel: null,
  groups: null,
  inHousehold: false,
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
  save?: (request: HouseholdRequest, attempt: number) => Response
}

/** Stubs Max's page and household; records every household save and how often each was read. */
function mockHouseholdApi({
  people = [max()],
  households = [family()],
  lookup = [],
  save = request => json(family({ members: request.memberIds.map(id => ({ id, firstName: id })), version: 'hv2' })),
}: ApiOptions = {}) {
  const saved: Saved[] = []
  const reads = { person: 0, household: 0 }

  const record = async (request: Request) => {
    const body: HouseholdRequest = await request.json()
    saved.push({ method: request.method, request: body, ifMatch: request.headers.get('If-Match') })

    return save(body, saved.length)
  }

  mockApi({
    'GET /api/auth/user': () => json(signedInUser()),
    'GET /api/church/settings': () => json(settings),
    'GET /api/church/people/p-max': () => json(people[Math.min(reads.person++, people.length - 1)]),
    'GET /api/church/people/p-max/groups': () => json([]),
    'GET /api/church/people/p-max/attendance': () => json({ items: [], nextOffset: null }),
    'GET /api/church/households/h1': () => {
      const next = households[Math.min(reads.household++, households.length - 1)] ?? family()

      return next instanceof Function ? next() : json(next)
    },
    'GET /api/church/people/lookup': () => json(lookup),
    'PUT /api/church/households/h1': record,
    'POST /api/church/households': record,
  })

  return { saved, reads }
}

function renderPerson() {
  return renderApp(
    <Routes>
      <Route path="/people/:personId" element={<Person />} />
    </Routes>,
    '/people/p-max',
  )
}

const memberList = () => screen.findByRole('list', { name: 'Household members' })
const addBox = () => screen.findByRole('combobox', { name: 'Add someone to this household' })

describe('HouseholdSection', () => {
  it('HouseholdSection_CanManage_ListsMembersByFirstNameLinkingToThem', async () => {
    mockHouseholdApi()
    renderPerson()

    const members = await memberList()
    expect(screen.getByText('The Member Family')).toBeInTheDocument()
    expect(within(members).getByRole('link', { name: 'Max (this person)' })).toHaveAttribute('href', '/people/p-max')
    expect(within(members).getByRole('link', { name: 'Mia' })).toHaveAttribute('href', '/people/p-mia')
  })

  it('HouseholdSection_CannotManage_ShowsTheProfilesHouseholdWithoutLoadingOrChangingIt', async () => {
    const { reads } = mockHouseholdApi({ people: [max({ canEdit: false })] })
    renderPerson()

    const members = await memberList()
    // The same members as when managing it: this person and the others by first name.
    expect(within(members).getByRole('link', { name: 'Max (this person)' })).toBeInTheDocument()
    expect(within(members).getByRole('link', { name: 'Mia' })).toBeInTheDocument()
    expect(screen.getByText('The Member Family')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Remove Mia' })).not.toBeInTheDocument()
    expect(screen.queryByRole('combobox', { name: 'Add someone to this household' })).not.toBeInTheDocument()
    expect(reads.household).toBe(0)
  })

  it('HouseholdSection_CannotManageAndNoHousehold_DoesNotOfferToStartOne', async () => {
    mockHouseholdApi({ people: [max({ ...noHousehold, canEdit: false })] })
    renderPerson()

    expect(await screen.findByText('Not in a household.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Start a household' })).not.toBeInTheDocument()
  })

  it('HouseholdSection_HouseholdRefusedToTheUser_ShowsTheProfilesHouseholdReadOnly', async () => {
    mockHouseholdApi({ households: [() => problem(404, 'Resource not found.')] })
    renderPerson()

    expect(within(await memberList()).getByText('Mia')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Remove Mia' })).not.toBeInTheDocument()
  })

  it('HouseholdSection_MergedRecord_ShowsNoHousehold', async () => {
    mockHouseholdApi({ people: [max({ mergedIntoId: 'p-kept', canEdit: false })] })
    renderPerson()

    expect(await screen.findByRole('link', { name: 'another person' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Household' })).not.toBeInTheDocument()
  })

  it('HouseholdSection_NoHousehold_StartsOneWithThisPersonAndShowsIt', async () => {
    const { saved } = mockHouseholdApi({
      people: [max(noHousehold), max({ householdLabel: 'Max and friends' })],
      households: [family({ label: 'Max and friends', members: [{ id: 'p-max', firstName: 'Max' }] })],
      save: () => json(family({ label: 'Max and friends' })),
    })

    const { user } = renderPerson()

    await user.click(await screen.findByRole('button', { name: 'Start a household' }))
    expect(screen.getByText('Left empty, it is called "The Member Family".')).toBeInTheDocument()
    await user.type(screen.getByLabelText('Household name (optional)'), 'Max and friends')
    await user.click(screen.getByRole('button', { name: 'Start household' }))

    // The person's page is refreshed, so the new household replaces "Not in a household".
    expect(await screen.findByText('Max and friends')).toBeInTheDocument()
    expect(screen.queryByText('Not in a household.')).not.toBeInTheDocument()
    expect(saved).toEqual([{ method: 'POST', request: { memberIds: ['p-max'], label: 'Max and friends' }, ifMatch: null }])
  })

  it('HouseholdSection_StartWithoutAName_LetsTheApiNameIt', async () => {
    const { saved } = mockHouseholdApi({ people: [max(noHousehold), max()] })
    const { user } = renderPerson()

    await user.click(await screen.findByRole('button', { name: 'Start a household' }))
    await user.click(screen.getByRole('button', { name: 'Start household' }))

    expect(await screen.findByText('Household started.')).toBeInTheDocument()
    expect(saved[0]?.request).toEqual({ memberIds: ['p-max'], label: null })
  })

  it('HouseholdSection_StartRefused_SaysThisPersonCantBePutInAHousehold', async () => {
    mockHouseholdApi({ people: [max(noHousehold)], save: () => problem(403, 'Access denied.') })
    const { user } = renderPerson()

    await user.click(await screen.findByRole('button', { name: 'Start a household' }))
    await user.click(screen.getByRole('button', { name: 'Start household' }))

    expect(await screen.findByText("You can't start a household for this person.")).toBeInTheDocument()
  })

  it('HouseholdSection_AddSomeoneWithoutAHousehold_SavesThemForTheVersionShownAndRefreshesTheProfile', async () => {
    const { saved, reads } = mockHouseholdApi({ lookup: [zoe()] })
    const { user } = renderPerson()

    await user.type(await addBox(), 'Zo')
    await user.click(await screen.findByRole('option', { name: /^Zoe Visitor/ }))

    expect(await screen.findByText('Zoe Visitor added.')).toBeInTheDocument()
    expect(saved).toEqual([{ method: 'PUT', request: { memberIds: ['p-max', 'p-mia', 'p-zoe'], label: null }, ifMatch: '"hv1"' }])
    // Each profile shows its household, so this person's page is asked for again.
    await waitFor(() => expect(reads.person).toBeGreaterThan(1))
    expect(screen.getByRole('heading', { name: 'Household' })).toHaveFocus()
  })

  it.each([
    ['WithALabel', zoe({ householdLabel: 'The Visitor Family', inHousehold: true }), /Zoe Visitor is in The Visitor Family/],
    ['WithoutALabel', zoe({ inHousehold: true }), /Zoe Visitor is in another household/],
  ])('HouseholdSection_AddSomeoneFromAnotherHousehold%s_MovesThemOnlyWhenConfirmed', async (_, match, question) => {
    const { saved } = mockHouseholdApi({ lookup: [match] })
    const { user } = renderPerson()

    await user.type(await addBox(), 'Zo')
    await user.click(await screen.findByRole('option', { name: /^Zoe Visitor/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Move to this household?' })
    expect(within(dialog).getByText(question)).toBeInTheDocument()
    expect(saved).toEqual([])
    await user.click(within(dialog).getByRole('button', { name: 'Move here' }))

    expect(await screen.findByText('Zoe Visitor moved here.')).toBeInTheDocument()
    expect(saved[0]?.request.memberIds).toEqual(['p-max', 'p-mia', 'p-zoe'])
  })

  it('HouseholdSection_MoveCancelled_SavesNothing', async () => {
    const { saved } = mockHouseholdApi({ lookup: [zoe({ householdLabel: 'The Visitor Family', inHousehold: true })] })
    const { user } = renderPerson()

    await user.type(await addBox(), 'Zo')
    await user.click(await screen.findByRole('option', { name: /^Zoe Visitor/ }))
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Cancel' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(saved).toEqual([])
  })

  it('HouseholdSection_AddSomeoneAlreadyInIt_SaysSoWithoutSaving', async () => {
    const { saved } = mockHouseholdApi({ lookup: [zoe({ id: 'p-mia', name: 'Mia Member', householdLabel: 'The Member Family', inHousehold: true })] })
    const { user } = renderPerson()

    await user.type(await addBox(), 'Mi')
    await user.click(await screen.findByRole('option', { name: /^Mia Member/ }))

    expect(await screen.findByText('Mia Member is already in this household.')).toBeInTheDocument()
    expect(saved).toEqual([])
  })

  it('HouseholdSection_SearchToAdd_DoesNotOfferToCreateSomeone', async () => {
    mockHouseholdApi()
    const { user } = renderPerson()

    await user.type(await addBox(), 'Nobody')

    expect(await screen.findByText('No one found')).toBeInTheDocument()
    expect(screen.queryByRole('option', { name: /Create new person/ })).not.toBeInTheDocument()
  })

  it('HouseholdSection_RemoveMember_SavesTheOthersAndKeepsFocusInTheSection', async () => {
    const { saved } = mockHouseholdApi()
    const { user } = renderPerson()

    await user.click(await screen.findByRole('button', { name: 'Remove Mia' }))

    expect(await screen.findByText('Mia removed.')).toBeInTheDocument()
    expect(saved).toEqual([{ method: 'PUT', request: { memberIds: ['p-max'], label: null }, ifMatch: '"hv1"' }])
    expect(screen.getByRole('heading', { name: 'Household' })).toHaveFocus()
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

  it('HouseholdSection_ChangedBySomeoneElseFirst_ReloadsTheirVersionAndSavesAgainstIt', async () => {
    const { saved } = mockHouseholdApi({
      households: [family(), family({ label: 'The Members', version: 'hv2' })],
      save: (_, attempt) => (attempt === 1 ? problem(412, 'The resource changed since it was read.') : json(family({ version: 'hv3' }))),
    })

    const { user } = renderPerson()

    await user.click(await screen.findByRole('button', { name: 'Remove Mia' }))
    expect(await screen.findByText(/Someone changed this household since you opened it/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Rename' })).toBeDisabled()
    await user.click(screen.getByRole('button', { name: 'Reload' }))
    expect(await screen.findByDisplayValue('The Members')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Remove Mia' }))

    expect(await screen.findByText('Mia removed.')).toBeInTheDocument()
    expect(saved.map(save => save.ifMatch)).toEqual(['"hv1"', '"hv2"'])
  })

  it.each([
    ['Remove', async (user: ReturnType<typeof renderPerson>['user']) => user.click(await screen.findByRole('button', { name: 'Remove Mia' }))],
    [
      'Rename',
      async (user: ReturnType<typeof renderPerson>['user']) => {
        await user.type(await screen.findByLabelText('Household name'), ' plus')
        await user.click(screen.getByRole('button', { name: 'Rename' }))
      },
    ],
  ])('HouseholdSection_%sRefused_ShowsTheServersReason', async (_, act) => {
    mockHouseholdApi({ save: () => problem(403, 'Access denied.') })
    const { user } = renderPerson()

    await act(user)

    expect(await screen.findByText('Access denied.')).toBeInTheDocument()
  })
})
