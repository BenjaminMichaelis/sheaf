import { useRef, useState, type RefObject } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useSnackbar } from 'notistack'
import {
  Alert,
  Box,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  IconButton,
  List,
  ListItem,
  ListItemButton,
  ListItemText,
  Paper,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import { Close as CloseIcon } from '@mui/icons-material'
import type { components } from '@/api/schema'
import { Loading, RetryButton } from '@/components/Feedback'
import PersonSearch from '@/components/PersonSearch'
import { ApiError } from '@/services/apiClient'
import { createHousehold, householdKey, householdQuery, personKey, updateHousehold } from '@/services/churchQueries'

type PersonResponse = components['schemas']['PersonResponse']

type HouseholdResponse = components['schemas']['HouseholdResponse']

type HouseholdMember = components['schemas']['HouseholdMemberResponse']

type PersonMatch = components['schemas']['PersonLookupResponse']

interface HouseholdSectionProps {
  churchId: string
  person: PersonResponse
  /** Whether the user may manage this person's household: those who may edit the person, as the API allows. */
  canManage: boolean
}

/**
 * A person's household (#188 UI-HH-01..03): its label and members by first name only, and for those who may, starting
 * one, renaming it, and adding or removing members. Each member's page shows only what the user may see of them
 * (UI-HH-02), and the API has the last word on what may change, its reason shown when it refuses.
 */
export default function HouseholdSection({ churchId, person, canManage }: HouseholdSectionProps) {
  // Focus comes back here after a change, since the control used is replaced when the household is saved.
  const heading = useRef<HTMLHeadingElement>(null)

  return (
    <section aria-labelledby="household-heading">
      <Typography ref={heading} id="household-heading" tabIndex={-1} variant="h6" component="h2" sx={{ mt: 3, mb: 1, outline: 'none' }}>
        Household
      </Typography>
      {person.householdId === null ? (
        <NoHousehold churchId={churchId} person={person} canManage={canManage} heading={heading} />
      ) : (
        <Household churchId={churchId} householdId={person.householdId} person={person} canManage={canManage} heading={heading} />
      )}
    </section>
  )
}

function NoHousehold({ churchId, person, canManage, heading }: HouseholdSectionProps & { heading: RefObject<HTMLHeadingElement | null> }) {
  const queryClient = useQueryClient()
  const { enqueueSnackbar } = useSnackbar()
  const [starting, setStarting] = useState(false)
  const [label, setLabel] = useState('')

  const start = useMutation({
    // Without a label the API names the household after this person's last name.
    mutationFn: () => createHousehold(churchId, { memberIds: [person.id], label: label.trim() || null }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: personKey(churchId, person.id) })
      enqueueSnackbar('Household started.', { variant: 'success' })
      // The buttons used are replaced by the new household, so focus moves to the section, as after other changes.
      heading.current?.focus()
    },
  })

  return (
    <Stack spacing={2} sx={{ alignItems: 'flex-start' }}>
      <Typography color="text.secondary">Not in a household.</Typography>
      {canManage && !starting && (
        <Button variant="outlined" onClick={() => setStarting(true)}>
          Start a household
        </Button>
      )}
      {canManage && starting && (
        <>
          <TextField
            label="Household name (optional)"
            value={label}
            onChange={event => setLabel(event.target.value)}
            helperText={person.lastName ? `Left empty, it is called "The ${person.lastName} Family".` : undefined}
            sx={{ alignSelf: 'stretch' }}
          />
          {start.error && <Alert severity="error">{start.error.message}</Alert>}
          <Box sx={{ display: 'flex', gap: 2 }}>
            {/* Stays disabled once started, until the page shows the new household, so it is never started twice. */}
            <Button variant="contained" onClick={() => start.mutate()} disabled={start.isPending || start.isSuccess}>
              Start household
            </Button>
            <Button onClick={() => setStarting(false)} disabled={start.isPending || start.isSuccess}>
              Cancel
            </Button>
          </Box>
        </>
      )}
    </Stack>
  )
}

interface HouseholdProps {
  churchId: string
  householdId: string
  person: PersonResponse
  canManage: boolean
  heading: RefObject<HTMLHeadingElement | null>
}

function Household({ churchId, householdId, person, canManage, heading }: HouseholdProps) {
  // Only those who may manage it load the household itself (for its version); everyone else sees what the profile says.
  const household = useQuery({ ...householdQuery(churchId, householdId), enabled: canManage })
  // The API can still refuse, for example a Leader with someone they created who is in none of their groups.
  const refused = household.error instanceof ApiError && household.error.status === 404

  if (!canManage || refused) {
    // The profile lists the other members; this person is shown too, as when managing it.
    const members = [{ id: person.id, firstName: person.firstName }, ...person.householdMembers]

    return (
      <Stack spacing={1}>
        <Typography>{person.householdLabel ?? 'Unnamed household'}</Typography>
        <MemberList members={members} personId={person.id} />
      </Stack>
    )
  }

  if (household.data === undefined) {
    return household.isError ? (
      <Alert severity="error" action={<RetryButton onClick={() => void household.refetch()} />}>
        {household.error.message}
      </Alert>
    ) : (
      <Loading />
    )
  }

  // Keyed by version, so the label being typed starts again from what was saved after any change, here or elsewhere.
  return <ManageHousehold key={household.data.version} churchId={churchId} household={household.data} person={person} heading={heading} />
}

interface MemberListProps {
  members: HouseholdMember[]
  personId: string
  /** Offers removing each member; left out when the household is only shown. */
  onRemove?: (member: HouseholdMember) => void
  removing?: boolean
}

function MemberList({ members, personId, onRemove, removing }: MemberListProps) {
  return (
    <Paper variant="outlined">
      <List disablePadding aria-label="Household members">
        {members.map((member, index) => (
          <ListItem
            key={member.id}
            disablePadding
            divider={index < members.length - 1}
            secondaryAction={
              onRemove && (
                <IconButton edge="end" aria-label={`Remove ${member.firstName}`} disabled={removing} onClick={() => onRemove(member)}>
                  <CloseIcon />
                </IconButton>
              )
            }
          >
            {/* First names only; each page shows only what the user may see of that person. */}
            <ListItemButton href={`/people/${member.id}`}>
              <ListItemText primary={member.id === personId ? `${member.firstName} (this person)` : member.firstName} />
            </ListItemButton>
          </ListItem>
        ))}
      </List>
    </Paper>
  )
}

interface HouseholdChange {
  memberIds: string[]
  label?: string
  /** Someone joined from another household, whose remaining members' pages change too. */
  moved?: boolean
  /** Shown once the change is saved. */
  done: string
}

interface ManageHouseholdProps {
  churchId: string
  household: HouseholdResponse
  person: PersonResponse
  heading: RefObject<HTMLHeadingElement | null>
}

function ManageHousehold({ churchId, household, person, heading }: ManageHouseholdProps) {
  const queryClient = useQueryClient()
  const { enqueueSnackbar } = useSnackbar()
  const [label, setLabel] = useState(household.label ?? '')
  // Who is being asked about; kept while the dialog closes so its text doesn't empty mid-fade.
  const [moving, setMoving] = useState<{ match: PersonMatch; open: boolean } | null>(null)
  // Set on the click itself, so a quick double click can't send two changes before the mutation reports it is pending.
  const sending = useRef(false)
  const memberIds = household.members.map(member => member.id)

  const change = useMutation({
    mutationFn: ({ memberIds: ids, label: newLabel }: HouseholdChange) =>
      updateHousehold(churchId, household.id, household.version, { memberIds: ids, label: newLabel ?? null }),
    onSuccess: (updated, { moved, done }) => {
      queryClient.setQueryData(householdKey(churchId, household.id), updated)

      // Everyone who joined or left shows a different household on their page.
      for (const id of new Set([...memberIds, ...updated.members.map(member => member.id)])) {
        void queryClient.invalidateQueries({ queryKey: personKey(churchId, id) })
      }

      // Someone moved here left another household, whose remaining members (unknown here) list them too.
      if (moved) {
        void queryClient.invalidateQueries({ queryKey: ['church', churchId, 'people'] })
        void queryClient.invalidateQueries({ queryKey: ['church', churchId, 'households'], predicate: query => query.queryKey[3] !== household.id })
      }

      enqueueSnackbar(done, { variant: 'success' })
      heading.current?.focus()
    },
    onSettled: () => {
      sending.current = false
    },
  })

  const send = (next: HouseholdChange) => {
    if (sending.current) return

    sending.current = true
    change.mutate(next)
  }

  const closeMoving = () => setMoving(previous => previous && { ...previous, open: false })

  // Someone changed the household since it was shown; the API refuses rather than overwrite their change.
  const changedElsewhere = change.error instanceof ApiError && change.error.status === 412
  const changeError = change.error !== null && !change.isPending && !changedElsewhere ? change.error.message : null
  const labelChanged = label.trim() !== '' && label.trim() !== (household.label ?? '')

  const add = (match: PersonMatch) => {
    if (memberIds.includes(match.id)) {
      enqueueSnackbar(`${match.name} is already in this household.`, { variant: 'info' })

      return
    }

    // Someone in another household leaves it on joining this one, so the user is asked first.
    if (match.inHousehold) {
      setMoving({ match, open: true })

      return
    }

    send({ memberIds: [...memberIds, match.id], done: `${match.name} added.` })
  }

  return (
    <Stack spacing={2}>
      <Typography>{household.label ?? 'Unnamed household'}</Typography>
      <MemberList
        members={household.members}
        personId={person.id}
        removing={change.isPending}
        onRemove={member => send({ memberIds: memberIds.filter(id => id !== member.id), done: `${member.firstName} removed.` })}
      />

      {changedElsewhere && (
        <Alert
          severity="warning"
          action={
            <Button color="inherit" size="small" onClick={() => void queryClient.invalidateQueries({ queryKey: householdKey(churchId, household.id) })}>
              Reload
            </Button>
          }
        >
          Someone changed this household since you opened it. Reload to see their changes, then try again.
        </Alert>
      )}
      {changeError && <Alert severity="error">{changeError}</Alert>}

      <Box sx={{ display: 'flex', gap: 1, alignItems: 'flex-start' }}>
        <TextField label="Household name" value={label} onChange={event => setLabel(event.target.value)} size="small" sx={{ flex: 1 }} />
        <Button onClick={() => send({ memberIds, label: label.trim(), done: 'Household renamed.' })} disabled={change.isPending || changedElsewhere || !labelChanged}>
          Rename
        </Button>
      </Box>
      <PersonSearch churchId={churchId} label="Add someone to this household" onPick={add} />

      <Dialog open={moving?.open ?? false} onClose={closeMoving}>
        <DialogTitle>Move to this household?</DialogTitle>
        <DialogContent>
          <DialogContentText>
            {moving?.match.name} is in {moving?.match.householdLabel ?? 'another household'}. Adding them here takes them out of it.
          </DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={closeMoving}>Cancel</Button>
          <Button
            variant="contained"
            // Waits for any change still saving, so the move is never dropped by the double-click guard.
            disabled={change.isPending}
            onClick={() => {
              if (moving) send({ memberIds: [...memberIds, moving.match.id], moved: true, done: `${moving.match.name} moved here.` })
              closeMoving()
            }}
          >
            Move here
          </Button>
        </DialogActions>
      </Dialog>
    </Stack>
  )
}
