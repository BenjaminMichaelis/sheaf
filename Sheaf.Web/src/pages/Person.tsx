import { useEffect, useRef, useState, type RefObject } from 'react'
import { useParams } from 'react-router-dom'
import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useSnackbar } from 'notistack'
import {
  Alert,
  Box,
  Button,
  Chip,
  FormControlLabel,
  Link,
  List,
  ListItem,
  ListItemButton,
  ListItemText,
  Paper,
  Stack,
  Switch,
  TextField,
  Typography,
} from '@mui/material'
import type { components } from '@/api/schema'
import { Loading, RetryButton } from '@/components/Feedback'
import HouseholdSection from '@/components/HouseholdSection'
import PagedListFooter from '@/components/PagedList'
import { useChurch } from '@/contexts/ChurchContext'
import { ApiError } from '@/services/apiClient'
import { personAttendanceQuery, personGroupsQuery, personKey, personQuery, updatePerson } from '@/services/churchQueries'
import { formatChurchDate, formatDate } from '@/utils/dates'
import { telHref } from '@/utils/contact'
import { fullName, splitName } from '@/utils/names'
import { pagedState } from '@/utils/paging'

type PersonResponse = components['schemas']['PersonResponse']

type PersonGroup = components['schemas']['PersonGroupResponse']

/** A person's page: their details, household, groups and attendance, and correcting their details (#188 4.2). */
export default function Person() {
  const { personId = '' } = useParams()
  const { church } = useChurch()

  // The route only renders this with a church selected.
  // Keyed by person, so moving from one person to another (a merged record's link) starts fresh.
  return church ? <PersonPage key={personId} churchId={church.churchId} personId={personId} /> : null
}

function PersonPage({ churchId, personId }: { churchId: string; personId: string }) {
  const person = useQuery(personQuery(churchId, personId))
  // The profile as it was when editing began, so a background refresh never resets edits in progress.
  const [editing, setEditing] = useState<PersonResponse | null>(null)
  const [closed, setClosed] = useState(false)
  const title = useRef<HTMLHeadingElement>(null)
  const editButton = useRef<HTMLButtonElement>(null)

  // Focus returns to Edit when the form closes, or to the heading if that button is gone (WCAG 2.4.3).
  useEffect(() => {
    if (editing !== null || !closed) return
    ;(editButton.current ?? title.current)?.focus()
  }, [editing, closed])

  if (person.data === undefined) {
    if (!person.isError) return <Loading />

    // People outside a Leader's reach read as not found, so the page never says whether they exist.
    return person.error instanceof ApiError && person.error.status === 404 ? (
      <Alert severity="info">You can't see this person's details.</Alert>
    ) : (
      <Alert severity="error" action={<RetryButton onClick={() => void person.refetch()} />}>
        {person.error.message}
      </Alert>
    )
  }

  const shown = person.data

  return (
    <Box sx={{ maxWidth: 720, mx: 'auto' }}>
      <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap', mb: 1 }}>
        <Typography ref={title} tabIndex={-1} variant="h4" component="h1" sx={{ outline: 'none' }}>
          {shown.name}
        </Typography>
        {!shown.isActive && <Chip label="Inactive" size="small" />}
      </Stack>

      {shown.mergedIntoId && (
        <Alert severity="info" sx={{ mb: 2 }}>
          This record was merged into <Link href={`/people/${shown.mergedIntoId}`}>another person</Link>, which now holds
          their history.
        </Alert>
      )}

      {editing ? (
        <EditPerson
          key={editing.version}
          churchId={churchId}
          person={editing}
          onReloaded={setEditing}
          onDone={() => {
            setEditing(null)
            setClosed(true)
          }}
        />
      ) : (
        <PersonDetails person={shown} editButton={editButton} onEdit={() => setEditing(shown)} />
      )}

      {/* A merged record belongs to no household: the merge moved it to the person kept. */}
      {!shown.mergedIntoId && <HouseholdSection churchId={churchId} person={shown} canManage={shown.canEdit} />}

      {/* History is part of the full profile, so it is shown to those who may edit the person, as the API allows. */}
      {shown.canEdit && !shown.mergedIntoId && (
        <>
          <PersonGroups churchId={churchId} personId={personId} />
          <PersonAttendance churchId={churchId} personId={personId} />
        </>
      )}
    </Box>
  )
}

interface PersonDetailsProps {
  person: PersonResponse
  editButton: RefObject<HTMLButtonElement | null>
  onEdit: () => void
}

function PersonDetails({ person, editButton, onEdit }: PersonDetailsProps) {
  return (
    <Stack spacing={2} sx={{ mt: 1 }}>
      <Box component="dl" sx={{ m: 0, display: 'grid', gridTemplateColumns: 'max-content 1fr', columnGap: 2, rowGap: 1 }}>
        <Typography component="dt" color="text.secondary">
          Email
        </Typography>
        <Typography component="dd" sx={{ m: 0 }}>
          {/* Encoded, so an address the server never checked can't add mail headers to the link. */}
          {person.email ? <Link href={`mailto:${encodeURIComponent(person.email).replace(/%40/g, '@')}`}>{person.email}</Link> : 'None'}
        </Typography>
        <Typography component="dt" color="text.secondary">
          Phone
        </Typography>
        <Typography component="dd" sx={{ m: 0 }}>
          <PhoneNumber phone={person.phone} />
        </Typography>
      </Box>

      {/* Offered only when the API would accept a change: Staff for anyone, Leaders for people in their groups. */}
      {person.canEdit && !person.mergedIntoId && (
        <Box>
          <Button ref={editButton} variant="outlined" onClick={onEdit}>
            Edit details
          </Button>
        </Box>
      )}
    </Stack>
  )
}

/** A phone number as entered, linked for dialling when it can be dialled as written. */
function PhoneNumber({ phone }: { phone: string | null }) {
  if (!phone) return 'None'
  const href = telHref(phone)

  return href ? <Link href={href}>{phone}</Link> : phone
}

interface EditPersonProps {
  churchId: string
  /** The profile being edited; its version is what the change is checked against. */
  person: PersonResponse
  /** Restarts the edit from someone else's newer version. */
  onReloaded: (person: PersonResponse) => void
  onDone: () => void
}

/** Corrects a person's name and contacts; Staff can also mark them active or inactive. */
function EditPerson({ churchId, person, onReloaded, onDone }: EditPersonProps) {
  const queryClient = useQueryClient()
  const { enqueueSnackbar } = useSnackbar()
  const { can } = useChurch()
  // Leaders may correct details; activating or deactivating someone is a Staff decision, as the API enforces.
  const managesPeople = can('managePeople')
  const heading = useRef<HTMLHeadingElement>(null)
  const [name, setName] = useState(fullName(person))
  const [email, setEmail] = useState(person.email ?? '')
  const [phone, setPhone] = useState(person.phone ?? '')
  const [isActive, setIsActive] = useState(person.isActive)
  const profileKey = personQuery(churchId, person.id).queryKey

  useEffect(() => {
    heading.current?.focus()
  }, [])

  // The name box shows the stored parts joined, so they are split again only when the name was changed: otherwise a
  // first name of two words or a last name like "van der Berg" would be re-split on every save.
  const nameChanged = name.trim() !== fullName(person)

  // The user lost the right to edit while editing (no longer in their group, merged, or the role ended).
  function closeAsNoLongerEditable(error?: Error) {
    // A 404 means the user can no longer see the person at all, so what is cached is dropped rather than refreshed:
    // a failed refresh would keep the old details, and Edit with them.
    if (error instanceof ApiError && error.status === 404) void queryClient.resetQueries({ queryKey: personKey(churchId, person.id) })
    else void queryClient.invalidateQueries({ queryKey: personKey(churchId, person.id) })

    onDone()
    enqueueSnackbar('You can no longer edit these details.', { variant: 'error' })
  }

  const lostAccess = (error: Error) => error instanceof ApiError && (error.status === 403 || error.status === 404)

  const save = useMutation({
    mutationFn: () =>
      updatePerson(churchId, person.id, person.version, {
        ...(nameChanged ? splitName(name) : { firstName: person.firstName, lastName: person.lastName }),
        email: email.trim() || null,
        phone: phone.trim() || null,
        // Left out for Leaders, so the API keeps the current value.
        isActive: managesPeople ? isActive : null,
      }),
    onSuccess: updated => {
      onDone()
      queryClient.setQueryData(profileKey, updated)

      // The rest of the household's pages show this person's first name, so those are refreshed.
      for (const member of updated.householdMembers) void queryClient.invalidateQueries({ queryKey: personKey(churchId, member.id) })

      // A new name also shows on meetings, rosters and search results.
      if (updated.name !== person.name) {
        void queryClient.invalidateQueries({ queryKey: ['church', churchId, 'groups'] })
        void queryClient.invalidateQueries({ queryKey: ['church', churchId, 'people', 'lookup'] })
      }

      enqueueSnackbar('Details saved.', { variant: 'success' })
    },
    onError: error => {
      if (lostAccess(error)) closeAsNoLongerEditable(error)
    },
  })

  const reload = useMutation({
    mutationFn: () => queryClient.fetchQuery({ ...personQuery(churchId, person.id), staleTime: 0 }),
    onSuccess: latest => (latest.canEdit ? onReloaded(latest) : closeAsNoLongerEditable()),
    onError: error => {
      if (lostAccess(error)) closeAsNoLongerEditable(error)
    },
  })

  // Someone saved a change since editing began; the API refuses rather than overwrite it.
  const changedElsewhere = save.error instanceof ApiError && save.error.status === 412
  const saveError = save.error !== null && !save.isPending && !changedElsewhere && !lostAccess(save.error) ? save.error.message : null
  const nameMissing = !name.trim()

  return (
    <Stack spacing={2} sx={{ mt: 1 }}>
      <Typography ref={heading} tabIndex={-1} variant="h6" component="h2" sx={{ outline: 'none' }}>
        Edit details
      </Typography>
      <TextField
        label="Name"
        value={name}
        onChange={event => setName(event.target.value)}
        required
        error={nameMissing}
        helperText={nameMissing ? 'Enter a name.' : undefined}
      />
      <TextField label="Email (optional)" type="email" value={email} onChange={event => setEmail(event.target.value)} />
      <TextField label="Phone (optional)" type="tel" value={phone} onChange={event => setPhone(event.target.value)} />
      {managesPeople && (
        <FormControlLabel control={<Switch checked={isActive} onChange={event => setIsActive(event.target.checked)} />} label="Active" />
      )}

      {changedElsewhere && (
        <Alert
          severity="warning"
          action={
            <Button color="inherit" size="small" onClick={() => reload.mutate()} disabled={reload.isPending}>
              Reload
            </Button>
          }
        >
          Someone changed these details since you opened them. Reload to see their changes, then edit again.
        </Alert>
      )}
      {reload.error && <Alert severity="error">Could not reload the details: {reload.error.message}</Alert>}
      {saveError && <Alert severity="error">{saveError}</Alert>}

      <Box sx={{ display: 'flex', gap: 2 }}>
        <Button variant="contained" onClick={() => save.mutate()} disabled={save.isPending || changedElsewhere || nameMissing}>
          Save details
        </Button>
        <Button onClick={onDone} disabled={save.isPending}>
          Cancel
        </Button>
      </Box>
    </Stack>
  )
}

function PersonGroups({ churchId, personId }: { churchId: string; personId: string }) {
  const groups = useQuery(personGroupsQuery(churchId, personId))
  const { labels, timeZoneId } = useChurch()

  const current = groups.data?.filter(group => group.isCurrent) ?? []
  const past = groups.data?.filter(group => !group.isCurrent) ?? []

  return (
    <section aria-labelledby="person-groups-heading">
      <Typography id="person-groups-heading" variant="h6" component="h2" sx={{ mt: 3, mb: 1 }}>
        {labels.many}
      </Typography>
      {groups.data === undefined && groups.isError && (
        <Alert severity="error" action={<RetryButton onClick={() => void groups.refetch()} />}>
          {groups.error.message}
        </Alert>
      )}
      {groups.isPending && <Loading />}
      {groups.data?.length === 0 && <Typography color="text.secondary">Not in any {labels.many.toLowerCase()} you can see.</Typography>}
      {current.length > 0 && (
        <>
          <Typography id="person-current-groups-heading" variant="subtitle1" component="h3" sx={{ mb: 1 }}>
            Current
          </Typography>
          <RoleList labelledBy="person-current-groups-heading" roles={current} timeZoneId={timeZoneId} />
        </>
      )}
      {past.length > 0 && (
        <>
          <Typography id="person-past-groups-heading" variant="subtitle1" component="h3" sx={{ mt: 2, mb: 1 }}>
            Past and upcoming
          </Typography>
          <RoleList labelledBy="person-past-groups-heading" roles={past} timeZoneId={timeZoneId} />
        </>
      )}
    </section>
  )
}

interface RoleListProps {
  roles: PersonGroup[]
  /** Dates are left out until the church's time zone is known. */
  timeZoneId: string | null
  /** Id of the visible heading that names the list. */
  labelledBy: string
}

function RoleList({ roles, timeZoneId, labelledBy }: RoleListProps) {
  const dates = (role: PersonGroup) => {
    if (timeZoneId === null) return role.role
    const date = (instant: string) => formatChurchDate(instant, timeZoneId)
    const span = role.endsAt === null ? `since ${date(role.startsAt)}` : `${date(role.startsAt)} – ${date(role.endsAt)}`

    return `${role.role}, ${span}`
  }

  return (
    <Paper variant="outlined">
      <List disablePadding aria-labelledby={labelledBy}>
        {roles.map((role, index) => (
          <ListItem key={`${role.groupId}-${role.startsAt}`} disablePadding divider={index < roles.length - 1}>
            <ListItemButton href={`/groups/${role.groupId}`}>
              <ListItemText primary={role.groupName} secondary={dates(role)} />
            </ListItemButton>
          </ListItem>
        ))}
      </List>
    </Paper>
  )
}

function PersonAttendance({ churchId, personId }: { churchId: string; personId: string }) {
  const result = useInfiniteQuery(personAttendanceQuery(churchId, personId))
  const state = pagedState(result)

  return (
    <section aria-labelledby="person-attendance-heading">
      <Typography id="person-attendance-heading" variant="h6" component="h2" sx={{ mt: 3, mb: 1 }}>
        Attendance
      </Typography>
      {state.firstPageError && (
        <Alert severity="error" action={<RetryButton onClick={() => void result.refetch()} />}>
          {state.firstPageError}
        </Alert>
      )}
      {state.loaded && state.items.length === 0 && <Typography color="text.secondary">No meetings recorded yet.</Typography>}
      {state.items.length > 0 && (
        <Paper variant="outlined">
          <List disablePadding aria-labelledby="person-attendance-heading">
            {state.items.map((meeting, index) => (
              <ListItem key={meeting.sessionId} disablePadding divider={index < state.items.length - 1}>
                <ListItemButton
                  href={`/groups/${meeting.groupId}/meetings/${meeting.sessionId}`}
                  aria-label={`${formatDate(meeting.occurredOn)}, ${meeting.groupName}`}
                >
                  <ListItemText primary={formatDate(meeting.occurredOn)} secondary={meeting.groupName} />
                </ListItemButton>
              </ListItem>
            ))}
          </List>
        </Paper>
      )}
      <PagedListFooter state={state} fetchNextPage={() => void result.fetchNextPage()} />
    </section>
  )
}
