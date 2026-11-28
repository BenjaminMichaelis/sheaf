import { useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { useMutation, useQuery } from '@tanstack/react-query'
import { useSnackbar } from 'notistack'
import {
  Alert,
  Box,
  Button,
  Checkbox,
  IconButton,
  List,
  ListItem,
  FormControlLabel,
  ListItemText,
  Paper,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import { Close as CloseIcon } from '@mui/icons-material'
import type { components } from '@/api/schema'
import ConfirmMeetingDialog, { type PossibleDuplicate } from '@/components/ConfirmMeetingDialog'
import { Loading, RetryButton } from '@/components/Feedback'
import NewPersonDialog from '@/components/NewPersonDialog'
import PersonSearch from '@/components/PersonSearch'
import { useChurch } from '@/contexts/ChurchContext'
import { confirmationReasons } from '@/services/apiClient'
import { groupQuery, recordMeeting, rosterQuery } from '@/services/churchQueries'
import { churchDateOf } from '@/utils/dates'
import { fullName } from '@/utils/names'

type Group = components['schemas']['GroupResponse']

type PersonRequest = components['schemas']['PersonRequest']

/** Someone named on the meeting: an existing person (roster or found by search) or one to create when it is saved. */
interface Named {
  id: string
  name: string
}

interface NewPerson {
  key: number
  request: PersonRequest
}

/** Most characters a meeting description may have, matching the API. */
const MAX_DESCRIPTION_LENGTH = 2000

/** Records a meeting of one group: who came from the roster, anyone else found or added, and an optional description. */
export default function TakeAttendance() {
  const { groupId = '' } = useParams()
  const { church, timeZoneId, settingsError, reloadSettings } = useChurch()

  if (church === null) return null

  if (settingsError !== null) {
    return (
      <Alert severity="error" action={<RetryButton onClick={reloadSettings} />}>
        Attendance needs the church's time zone, which could not be loaded: {settingsError}
      </Alert>
    )
  }

  // Meeting dates are church dates, so nothing is shown until the church's zone is known.
  if (timeZoneId === null) return <Loading />

  return <GroupAttendance churchId={church.churchId} groupId={groupId} timeZoneId={timeZoneId} />
}

function GroupAttendance({ churchId, groupId, timeZoneId }: { churchId: string; groupId: string; timeZoneId: string }) {
  const { labels, can } = useChurch()
  const group = useQuery(groupQuery(churchId, groupId))

  if (group.isError) {
    return (
      <Alert severity="error" action={<RetryButton onClick={() => void group.refetch()} />}>
        {group.error.message}
      </Alert>
    )
  }

  if (group.data === undefined) return <Loading />

  // Leaders lose a group once it ends (the API refuses their saves), so they are told now rather than after filling in the form.
  if (group.data.endsAt !== null && new Date(group.data.endsAt) <= new Date() && !can('recordEndedGroups')) {
    return <Alert severity="info">This {labels.one.toLowerCase()} has ended, so only Staff can record its meetings.</Alert>
  }

  return <MeetingForm churchId={churchId} group={group.data} timeZoneId={timeZoneId} />
}

function MeetingForm({ churchId, group, timeZoneId }: { churchId: string; group: Group; timeZoneId: string }) {
  const navigate = useNavigate()
  const { enqueueSnackbar } = useSnackbar()
  const { labels } = useChurch()

  // A meeting can be recorded for any date from the group's start up to today, or its last day if it has ended. The
  // end instant is when the group stopped (as the API treats it), so its last day is the day before that instant.
  const today = churchDateOf(new Date(), timeZoneId)
  const firstDay = churchDateOf(group.startsAt, timeZoneId)
  const endDay = group.endsAt === null ? today : churchDateOf(new Date(Date.parse(group.endsAt) - 1), timeZoneId)
  const lastDay = endDay < today ? endDay : today
  const groupWord = labels.one.toLowerCase()

  const [occurredOn, setOccurredOn] = useState(lastDay)
  const [present, setPresent] = useState<ReadonlySet<string>>(new Set())
  const [found, setFound] = useState<Named[]>([])
  const [newPeople, setNewPeople] = useState<NewPerson[]>([])
  // Only tells new people apart in lists and warnings; it never changes what is shown, so it is not state.
  const nextKey = useRef(0)
  const [description, setDescription] = useState('')
  const [creating, setCreating] = useState({ open: false, name: '' })
  const [confirmation, setConfirmation] = useState<{ sameDay: boolean; duplicates: PossibleDuplicate[] } | null>(null)

  const dateIsValid = occurredOn >= firstDay && occurredOn <= lastDay
  const roster = useQuery({ ...rosterQuery(churchId, group.id, occurredOn), enabled: dateIsValid })
  const rosterPeople = roster.data ?? []
  const rosterIds = new Set(rosterPeople.map(person => person.id))
  // People found by search who turn out to be on this date's roster are shown there instead.
  const others = found.filter(person => !rosterIds.has(person.id))
  // Only people listed on screen count, so changing the date never saves someone hidden from the new roster.
  const personIds = [...rosterPeople, ...others].flatMap(person => (present.has(person.id) ? [person.id] : []))
  const attendeeCount = personIds.length + newPeople.length
  // Until the date's roster has loaded, ticks made on it are not counted, so saving waits for it.
  const rosterReady = !roster.isPending && !roster.isError

  const save = useMutation({
    mutationFn: ({ confirm, people }: { confirm: boolean; people: NewPerson[] }) =>
      recordMeeting(churchId, group.id, {
        occurredOn,
        personIds,
        newPeople: people.map(person => person.request),
        confirm,
        description: description.trim() || null,
      }),
    onSuccess: () => {
      enqueueSnackbar('Attendance saved.', { variant: 'success' })
      navigate('/groups')
    },
    onError: (error, { people }) => {
      const reasons = confirmationReasons(error)

      if (reasons === null) return

      setConfirmation({
        sameDay: reasons.existingSessionIds.length > 0,
        duplicates: reasons.similarPeople.flatMap(similar => {
          const person = people[similar.index]

          return person ? [{ key: person.key, name: fullName(person.request), matches: similar.matches }] : []
        }),
      })
    },
  })

  const toggle = (id: string) =>
    setPresent(previous => {
      const next = new Set(previous)

      if (!next.delete(id)) next.add(id)

      return next
    })

  const markPresent = (person: Named) => {
    if (!rosterIds.has(person.id) && !found.some(other => other.id === person.id)) {
      setFound(previous => [...previous, { id: person.id, name: person.name }])
    }

    setPresent(previous => new Set(previous).add(person.id))
  }

  const removeFound = (id: string) => {
    setFound(previous => previous.filter(person => person.id !== id))
    setPresent(previous => {
      const next = new Set(previous)
      next.delete(id)

      return next
    })
  }

  const addNewPerson = (request: PersonRequest) => {
    const key = nextKey.current++
    setNewPeople(previous => [...previous, { key, request }])
    setCreating(previous => ({ ...previous, open: false }))
  }

  const removeNewPerson = (key: number) => setNewPeople(previous => previous.filter(person => person.key !== key))

  const useExisting = (duplicate: PossibleDuplicate, person: Named) => {
    removeNewPerson(duplicate.key)
    markPresent(person)
    setConfirmation(previous => {
      if (previous === null) return null
      const duplicates = previous.duplicates.filter(other => other.key !== duplicate.key)

      // Nothing left to confirm once every warning is resolved, so the dialog closes and Save is used as normal.
      return duplicates.length === 0 && !previous.sameDay ? null : { ...previous, duplicates }
    })
  }

  const saveError = save.error !== null && confirmationReasons(save.error) === null ? save.error.message : null

  return (
    // Not a form: Enter in the name box must choose a person, never save the meeting. Only the Save button saves.
    <Box sx={{ maxWidth: 720, mx: 'auto' }}>
      <Typography variant="h4" component="h1">
        Attendance
      </Typography>
      <Typography color="text.secondary" gutterBottom>
        {group.name} · {labels.one}
      </Typography>

      <Stack spacing={3} sx={{ mt: 2 }}>
        <TextField
          label="Meeting date"
          type="date"
          value={occurredOn}
          onChange={event => setOccurredOn(event.target.value)}
          required
          error={!dateIsValid}
          helperText={
            dateIsValid
              ? undefined
              : `Choose a date from when the ${groupWord} started up to ${group.endsAt === null ? 'today' : 'its last day'}.`
          }
          slotProps={{ htmlInput: { min: firstDay, max: lastDay }, inputLabel: { shrink: true } }}
        />

        <section aria-labelledby="roster-heading">
          <Typography id="roster-heading" variant="h6" component="h2">
            Roster
          </Typography>
          {roster.isError && (
            <Alert severity="error" action={<RetryButton onClick={() => void roster.refetch()} />}>
              {roster.error.message}
            </Alert>
          )}
          {dateIsValid && roster.isPending && <Loading />}
          {roster.data?.length === 0 && <Typography color="text.secondary">Nobody was on the roster that day.</Typography>}
          {rosterPeople.length > 0 && (
            <Paper variant="outlined">
              <List disablePadding aria-labelledby="roster-heading">
                {rosterPeople.map(person => (
                  <ListItem key={person.id} divider sx={{ py: 0 }}>
                    {/* One focusable checkbox per person, named by its visible label (mui.com/material-ui/react-checkbox/#label). */}
                    <FormControlLabel
                      control={<Checkbox checked={present.has(person.id)} onChange={() => toggle(person.id)} />}
                      label={person.name}
                      sx={{ flex: 1, mr: 0, py: 0.5 }}
                    />
                  </ListItem>
                ))}
              </List>
            </Paper>
          )}
        </section>

        <section aria-labelledby="others-heading">
          <Typography id="others-heading" variant="h6" component="h2" gutterBottom>
            Also came
          </Typography>
          <PersonSearch
            churchId={churchId}
            label="Find or add a person"
            onPick={markPresent}
            onCreate={name => setCreating({ open: true, name })}
          />
          {(others.length > 0 || newPeople.length > 0) && (
            <List aria-labelledby="others-heading">
              {others.map(person => (
                <RemovableItem key={person.id} primary={person.name} onRemove={() => removeFound(person.id)} />
              ))}
              {newPeople.map(person => (
                <RemovableItem
                  key={`new-${person.key}`}
                  primary={fullName(person.request)}
                  secondary="New person"
                  onRemove={() => removeNewPerson(person.key)}
                />
              ))}
            </List>
          )}
        </section>

        <TextField
          label="Description (optional)"
          value={description}
          onChange={event => setDescription(event.target.value)}
          multiline
          minRows={2}
          slotProps={{ htmlInput: { maxLength: MAX_DESCRIPTION_LENGTH } }}
        />

        {saveError && <Alert severity="error">{saveError}</Alert>}

        <Box sx={{ display: 'flex', alignItems: 'center', gap: 2 }}>
          {/* Disabled while saving so a meeting is never submitted twice. */}
          <Button
            variant="contained"
            onClick={() => save.mutate({ confirm: false, people: newPeople })}
            disabled={save.isPending || !dateIsValid || !rosterReady || attendeeCount === 0}
          >
            Save attendance
          </Button>
          {/* A status region, so screen readers announce the count as people are marked. */}
          <Typography role="status" color="text.secondary">
            {attendeeCount} present
          </Typography>
        </Box>
      </Stack>

      <NewPersonDialog
        open={creating.open}
        initialName={creating.name}
        onClose={() => setCreating(previous => ({ ...previous, open: false }))}
        onAdd={addNewPerson}
      />
      <ConfirmMeetingDialog
        open={confirmation !== null}
        groupLabel={groupWord}
        sameDay={confirmation?.sameDay ?? false}
        duplicates={confirmation?.duplicates ?? []}
        saving={save.isPending}
        onUseExisting={useExisting}
        onSaveAnyway={() => {
          setConfirmation(null)
          save.mutate({ confirm: true, people: newPeople })
        }}
        onClose={() => setConfirmation(null)}
      />
    </Box>
  )
}

function RemovableItem({ primary, secondary, onRemove }: { primary: string; secondary?: string; onRemove: () => void }) {
  return (
    <ListItem
      secondaryAction={
        <IconButton edge="end" onClick={onRemove} aria-label={`Remove ${primary}`}>
          <CloseIcon />
        </IconButton>
      }
    >
      <ListItemText primary={primary} secondary={secondary} />
    </ListItem>
  )
}

