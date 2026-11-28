import { useState } from 'react'
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
import { createHousehold, householdQuery, personKey, updateHousehold } from '@/services/churchQueries'

type PersonResponse = components['schemas']['PersonResponse']

type HouseholdResponse = components['schemas']['HouseholdResponse']

type PersonMatch = components['schemas']['PersonLookupResponse']

interface HouseholdSectionProps {
  churchId: string
  person: PersonResponse
  /** Whether the user may manage this person's household: those who may edit the person, as the API allows. */
  canManage: boolean
}

/**
 * A person's household (#188 UI-HH-01..03): its label and members by first name only, and for those who may, starting
 * one, renaming it, and adding or removing members. Members outside a Leader's groups stay listed but can't be added by
 * them; the API decides and its reason is shown.
 */
export default function HouseholdSection({ churchId, person, canManage }: HouseholdSectionProps) {
  return (
    <section aria-labelledby="household-heading">
      <Typography id="household-heading" variant="h6" component="h2" sx={{ mt: 3, mb: 1 }}>
        Household
      </Typography>
      {person.householdId === null ? (
        <NoHousehold churchId={churchId} person={person} canManage={canManage} />
      ) : (
        <Household churchId={churchId} householdId={person.householdId} person={person} canManage={canManage} />
      )}
    </section>
  )
}

/** Refreshes every person whose household changed, since each profile shows its household. */
function useRefreshPeople(churchId: string) {
  const queryClient = useQueryClient()

  return (personIds: Iterable<string>) => {
    for (const id of new Set(personIds)) void queryClient.invalidateQueries({ queryKey: personKey(churchId, id) })
  }
}

function NoHousehold({ churchId, person, canManage }: HouseholdSectionProps) {
  const { enqueueSnackbar } = useSnackbar()
  const refreshPeople = useRefreshPeople(churchId)
  const [starting, setStarting] = useState(false)
  const [label, setLabel] = useState('')

  const start = useMutation({
    // Without a label the API names the household after this person's last name.
    mutationFn: () => createHousehold(churchId, { memberIds: [person.id], label: label.trim() || null }),
    onSuccess: () => {
      refreshPeople([person.id])
      enqueueSnackbar('Household started.', { variant: 'success' })
    },
  })

  if (!canManage) return <Typography color="text.secondary">Not in a household.</Typography>

  if (!starting) {
    return (
      <Stack spacing={1} sx={{ alignItems: 'flex-start' }}>
        <Typography color="text.secondary">Not in a household.</Typography>
        <Button variant="outlined" onClick={() => setStarting(true)}>
          Start a household
        </Button>
      </Stack>
    )
  }

  return (
    <Stack spacing={2}>
      <TextField
        label="Household name (optional)"
        value={label}
        onChange={event => setLabel(event.target.value)}
        helperText={person.lastName ? `Left empty, it is called "The ${person.lastName} Family".` : undefined}
      />
      {start.error && <Alert severity="error">{start.error.message}</Alert>}
      <Box sx={{ display: 'flex', gap: 2 }}>
        <Button variant="contained" onClick={() => start.mutate()} disabled={start.isPending}>
          Start household
        </Button>
        <Button onClick={() => setStarting(false)} disabled={start.isPending}>
          Cancel
        </Button>
      </Box>
    </Stack>
  )
}

function Household({ churchId, householdId, person, canManage }: { churchId: string; householdId: string; person: PersonResponse; canManage: boolean }) {
  // Only those who may manage it load the household itself (for its version); everyone else sees what the profile says.
  const household = useQuery({ ...householdQuery(churchId, householdId), enabled: canManage })
  // The API can still refuse, for example a Leader with someone they created who is in none of their groups.
  const refused = household.error instanceof ApiError && household.error.status === 404

  if (!canManage || refused) return <ReadOnlyHousehold person={person} />

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
  return <HouseholdDetails key={household.data.version} churchId={churchId} household={household.data} person={person} canManage={canManage} />
}

interface HouseholdChange {
  memberIds: string[]
  label?: string
  /** Shown once the change is saved. */
  done: string
}

function HouseholdDetails({ churchId, household, person, canManage }: { churchId: string; household: HouseholdResponse; person: PersonResponse; canManage: boolean }) {
  const queryClient = useQueryClient()
  const { enqueueSnackbar } = useSnackbar()
  const refreshPeople = useRefreshPeople(churchId)
  const [label, setLabel] = useState(household.label ?? '')
  const [moving, setMoving] = useState<PersonMatch | null>(null)
  const householdKey = householdQuery(churchId, household.id).queryKey
  const memberIds = household.members.map(member => member.id)

  const change = useMutation({
    mutationFn: ({ memberIds: ids, label: newLabel }: HouseholdChange) =>
      updateHousehold(churchId, household.id, household.version, { memberIds: ids, label: newLabel ?? null }),
    onSuccess: (updated, { done }) => {
      queryClient.setQueryData(householdKey, updated)
      // Everyone who joined or left shows a different household now, and a person moved here left another household.
      refreshPeople([...memberIds, ...updated.members.map(member => member.id)])
      void queryClient.invalidateQueries({ queryKey: ['church', churchId, 'households'] })
      enqueueSnackbar(done, { variant: 'success' })
    },
  })

  // Someone changed the household since it was shown; the API refuses rather than overwrite their change.
  const changedElsewhere = change.error instanceof ApiError && change.error.status === 412
  const changeError = change.error !== null && !change.isPending && !changedElsewhere ? change.error.message : null
  const labelChanged = label.trim() !== '' && label.trim() !== (household.label ?? '')

  const add = (match: PersonMatch) => {
    if (memberIds.includes(match.id)) return enqueueSnackbar(`${match.name} is already in this household.`, { variant: 'info' })

    // Someone in another household leaves it on joining this one, so the user is asked first.
    if (match.householdLabel !== null) return setMoving(match)

    change.mutate({ memberIds: [...memberIds, match.id], done: `${match.name} added.` })
  }

  return (
    <Stack spacing={2}>
      <Typography>{household.label ?? 'Unnamed household'}</Typography>
      <Paper variant="outlined">
        <List disablePadding aria-labelledby="household-heading">
          {household.members.map((member, index) => (
            <ListItem
              key={member.id}
              disablePadding
              divider={index < household.members.length - 1}
              secondaryAction={
                canManage && (
                  <IconButton
                    edge="end"
                    aria-label={`Remove ${member.firstName}`}
                    disabled={change.isPending}
                    onClick={() =>
                      change.mutate({ memberIds: memberIds.filter(id => id !== member.id), done: `${member.firstName} removed.` })
                    }
                  >
                    <CloseIcon />
                  </IconButton>
                )
              }
            >
              {/* First names only; each page shows only what the user may see of that person. */}
              <ListItemButton href={`/people/${member.id}`}>
                <ListItemText primary={member.id === person.id ? `${member.firstName} (this person)` : member.firstName} />
              </ListItemButton>
            </ListItem>
          ))}
        </List>
      </Paper>

      {changedElsewhere && (
        <Alert
          severity="warning"
          action={
            <Button color="inherit" size="small" onClick={() => void queryClient.invalidateQueries({ queryKey: householdKey })}>
              Reload
            </Button>
          }
        >
          Someone changed this household since you opened it. Reload to see their changes, then try again.
        </Alert>
      )}
      {changeError && <Alert severity="error">{changeError}</Alert>}

      {canManage && (
        <>
          <Box sx={{ display: 'flex', gap: 1, alignItems: 'flex-start' }}>
            <TextField label="Household name" value={label} onChange={event => setLabel(event.target.value)} size="small" sx={{ flex: 1 }} />
            <Button
              onClick={() => change.mutate({ memberIds, label: label.trim(), done: 'Household renamed.' })}
              disabled={change.isPending || changedElsewhere || !labelChanged}
            >
              Rename
            </Button>
          </Box>
          <PersonSearch churchId={churchId} label="Add someone to this household" onPick={add} />
        </>
      )}

      <Dialog open={moving !== null} onClose={() => setMoving(null)}>
        <DialogTitle>Move to this household?</DialogTitle>
        <DialogContent>
          <DialogContentText>
            {moving?.name} is in {moving?.householdLabel}. Adding them here takes them out of that household.
          </DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setMoving(null)}>Cancel</Button>
          <Button
            variant="contained"
            onClick={() => {
              if (moving) change.mutate({ memberIds: [...memberIds, moving.id], done: `${moving.name} moved here.` })
              setMoving(null)
            }}
          >
            Move here
          </Button>
        </DialogActions>
      </Dialog>
    </Stack>
  )
}

/** The household as the person's profile shows it: its label and the other members' first names. */
function ReadOnlyHousehold({ person }: { person: PersonResponse }) {
  return (
    <Stack spacing={1}>
      <Typography>{person.householdLabel ?? 'Unnamed household'}</Typography>
      {person.householdMembers.length > 0 && (
        <Paper variant="outlined">
          <List dense disablePadding aria-labelledby="household-heading">
            {person.householdMembers.map((member, index) => (
              <ListItem key={member.id} divider={index < person.householdMembers.length - 1}>
                <ListItemText primary={member.firstName} />
              </ListItem>
            ))}
          </List>
        </Paper>
      )}
    </Stack>
  )
}
