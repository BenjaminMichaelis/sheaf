import { useEffect, useRef, useState, type RefObject } from 'react'
import { useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useSnackbar } from 'notistack'
import { Alert, Box, Button, Link, List, ListItem, ListItemText, Paper, Stack, Typography } from '@mui/material'
import type { components } from '@/api/schema'
import AttendeesConfirmDialog from '@/components/attendance/AttendeesConfirmDialog'
import AttendeesEditor from '@/components/attendance/AttendeesEditor'
import { DescriptionField, SaveBar } from '@/components/attendance/MeetingFields'
import { useAttendees, type NewPerson } from '@/components/attendance/useAttendees'
import { Loading, RetryButton } from '@/components/Feedback'
import { useChurch } from '@/contexts/ChurchContext'
import { ApiError, confirmationReasons } from '@/services/apiClient'
import { correctMeeting, groupQuery, meetingQuery, meetingsQuery } from '@/services/churchQueries'
import { formatDate } from '@/utils/dates'

type SessionResponse = components['schemas']['SessionResponse']

type SessionPerson = components['schemas']['SessionPersonResponse']

/** One recorded meeting: who came, who on the roster did not, its description, and correcting it (#188 UI-ATT-04, UI-ATT-07). */
export default function Meeting() {
  const { groupId = '', sessionId = '' } = useParams()
  const { church } = useChurch()

  // The route only renders this with a church selected.
  return church ? <MeetingPage churchId={church.churchId} groupId={groupId} sessionId={sessionId} /> : null
}

function MeetingPage({ churchId, groupId, sessionId }: { churchId: string; groupId: string; sessionId: string }) {
  const meeting = useQuery(meetingQuery(churchId, groupId, sessionId))
  const group = useQuery(groupQuery(churchId, groupId))
  // The meeting as it was when the correction began. A background refresh never replaces it, so edits in progress are
  // never lost; if someone else saved meanwhile the API refuses (412) and the user reloads their version on purpose.
  const [correcting, setCorrecting] = useState<SessionResponse | null>(null)
  // Where focus goes when the editor closes, so it is never left nowhere (WCAG 2.4.3): back to Correct, or to the
  // heading when the meeting can no longer be corrected and that button is gone.
  const [closedTo, setClosedTo] = useState<FocusTarget | null>(null)
  const title = useRef<HTMLHeadingElement>(null)
  const correctButton = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    if (correcting !== null || closedTo === null) return
    ;(closedTo === 'correct' && correctButton.current ? correctButton.current : title.current)?.focus()
  }, [correcting, closedTo])

  // A failed background refresh keeps what is shown; only a meeting that never loaded shows the error.
  if (meeting.data === undefined) {
    return meeting.isError ? (
      <Alert severity="error" action={<RetryButton onClick={() => void meeting.refetch()} />}>
        {meeting.error.message}
      </Alert>
    ) : (
      <Loading />
    )
  }

  const shown = meeting.data
  const date = formatDate(shown.occurredOn)

  return (
    <Box sx={{ maxWidth: 720, mx: 'auto' }}>
      <Link href={`/groups/${groupId}`} variant="body2">
        {group.data ? `Back to ${group.data.name}` : 'Back to the group'}
      </Link>
      <Typography ref={title} tabIndex={-1} variant="h4" component="h1" sx={{ mt: 1, outline: 'none' }}>
        {group.data ? `${group.data.name}, ${date}` : date}
      </Typography>

      {correcting ? (
        <CorrectMeeting
          key={correcting.version}
          churchId={churchId}
          groupId={groupId}
          meeting={correcting}
          onReloaded={setCorrecting}
          onDone={focusTo => {
            setCorrecting(null)
            setClosedTo(focusTo)
          }}
        />
      ) : (
        <MeetingDetails meeting={shown} correctButton={correctButton} onCorrect={() => setCorrecting(shown)} />
      )}
    </Box>
  )
}

type FocusTarget = 'correct' | 'title'

interface MeetingDetailsProps {
  meeting: SessionResponse
  /** The Correct button, which takes focus back when the editor closes. */
  correctButton: RefObject<HTMLButtonElement | null>
  onCorrect: () => void
}

function MeetingDetails({ meeting, correctButton, onCorrect }: MeetingDetailsProps) {
  return (
    <Stack spacing={3} sx={{ mt: 2 }}>
      {meeting.description && <Typography sx={{ whiteSpace: 'pre-wrap' }}>{meeting.description}</Typography>}

      <PeopleSection id="came-heading" title={`Came (${meeting.attendees.length})`} people={meeting.attendees} empty="Nobody was marked present." />
      {/* Anyone on the roster that day who was not marked present; nothing is entered for absence (UI-ATT-04). */}
      <PeopleSection id="absent-heading" title={`Absent (${meeting.absent.length})`} people={meeting.absent} empty="Nobody on the roster was absent." />

      {/* Offered only while the API would accept a correction: Leaders for a week after recording, Staff always. */}
      {meeting.canEdit && (
        <Box>
          <Button ref={correctButton} variant="contained" onClick={onCorrect}>
            Correct attendance
          </Button>
        </Box>
      )}
    </Stack>
  )
}

function PeopleSection({ id, title, people, empty }: { id: string; title: string; people: SessionPerson[]; empty: string }) {
  return (
    <section aria-labelledby={id}>
      <Typography id={id} variant="h6" component="h2" gutterBottom>
        {title}
      </Typography>
      {people.length === 0 ? (
        <Typography color="text.secondary">{empty}</Typography>
      ) : (
        <Paper variant="outlined">
          <List dense disablePadding aria-labelledby={id}>
            {people.map((person, index) => (
              <ListItem key={person.id} divider={index < people.length - 1}>
                <ListItemText primary={person.name} />
              </ListItem>
            ))}
          </List>
        </Paper>
      )}
    </section>
  )
}

interface CorrectMeetingProps {
  churchId: string
  groupId: string
  /** The meeting being corrected; its version is what the correction is checked against. */
  meeting: SessionResponse
  /** Restarts the correction from someone else's newer version. */
  onReloaded: (meeting: SessionResponse) => void
  /** Closes the editor, saying where focus should go. */
  onDone: (focusTo: FocusTarget) => void
}

/** Changes who came and the description; people taken off stay in the meeting's history on the server. */
function CorrectMeeting({ churchId, groupId, meeting, onReloaded, onDone }: CorrectMeetingProps) {
  const queryClient = useQueryClient()
  const { enqueueSnackbar } = useSnackbar()
  const heading = useRef<HTMLHeadingElement>(null)
  // Everyone already on the meeting, ticked if they came, so a correction can add or take anyone off.
  const listed = [...meeting.attendees, ...meeting.absent].sort((a, b) => a.name.localeCompare(b.name))
  const attendees = useAttendees(listed, meeting.attendees.map(person => person.id))
  const [description, setDescription] = useState(meeting.description ?? '')
  const meetingKey = meetingQuery(churchId, groupId, meeting.id).queryKey

  // Focus moves into the editor that replaced the button, as WCAG 2.4.3 asks.
  useEffect(() => {
    heading.current?.focus()
  }, [])

  const save = useMutation({
    mutationFn: ({ confirm, people }: { confirm: boolean; people: NewPerson[] }) =>
      correctMeeting(churchId, groupId, meeting.id, meeting.version, {
        personIds: attendees.personIds,
        newPeople: people.map(person => person.request),
        confirm,
        description: description.trim() || null,
      }),
    onSuccess: corrected => {
      // The editor closes first, so it cannot be used again against the version it just replaced.
      onDone('correct')
      queryClient.setQueryData(meetingKey, corrected)
      // The meeting list shows how many came, so it is refreshed too.
      void queryClient.invalidateQueries({ queryKey: meetingsQuery(churchId, groupId).queryKey })
      enqueueSnackbar('Attendance corrected.', { variant: 'success' })
    },
    onError: (error, { people }) => {
      // The week Leaders have to correct a meeting ran out while they were editing.
      if (error instanceof ApiError && error.status === 403) {
        void queryClient.invalidateQueries({ queryKey: meetingKey })
        closeAsNoLongerCorrectable()

        return
      }

      attendees.askToConfirm(error, people)
    },
  })

  function closeAsNoLongerCorrectable() {
    onDone('title')
    enqueueSnackbar('This meeting can no longer be corrected.', { variant: 'error' })
  }

  // Loads the version someone else saved and starts again from it, unless the user may no longer correct it. A
  // mutation, so the button shows it is working and a failure (such as being offline) is reported.
  const reload = useMutation({
    mutationFn: () => queryClient.fetchQuery({ ...meetingQuery(churchId, groupId, meeting.id), staleTime: 0 }),
    onSuccess: latest => (latest.canEdit ? onReloaded(latest) : closeAsNoLongerCorrectable()),
  })

  // Someone saved a change since the correction began; the API refuses rather than overwrite it.
  const changedElsewhere = save.error instanceof ApiError && save.error.status === 412

  const saveError =
    save.error !== null && !save.isPending && !changedElsewhere && confirmationReasons(save.error) === null ? save.error.message : null

  return (
    <Stack spacing={3} sx={{ mt: 2 }}>
      <Typography ref={heading} tabIndex={-1} variant="h5" component="h2" sx={{ outline: 'none' }}>
        Correct attendance
      </Typography>

      <AttendeesEditor churchId={churchId} attendees={attendees} listed={listed} heading="Marked on this meeting" headingLevel="h3" />

      <DescriptionField value={description} onChange={setDescription} />

      {changedElsewhere && (
        <Alert
          severity="warning"
          action={
            <Button color="inherit" size="small" onClick={() => reload.mutate()} disabled={reload.isPending}>
              Reload
            </Button>
          }
        >
          Someone changed this meeting since you opened it. Reload to see their changes, then correct it again.
        </Alert>
      )}
      {reload.error && <Alert severity="error">Could not reload the meeting: {reload.error.message}</Alert>}
      {saveError && <Alert severity="error">{saveError}</Alert>}

      <SaveBar
        label="Save correction"
        count={attendees.count}
        // Disabled while saving so a correction is never sent twice. Everyone may be taken off, so zero present is allowed.
        disabled={save.isPending || changedElsewhere}
        onSave={() => save.mutate({ confirm: false, people: attendees.newPeople })}
      >
        <Button onClick={() => onDone('correct')} disabled={save.isPending}>
          Cancel
        </Button>
      </SaveBar>

      <AttendeesConfirmDialog
        attendees={attendees}
        saving={save.isPending}
        onSaveAnyway={() => save.mutate({ confirm: true, people: attendees.newPeople })}
      />
    </Stack>
  )
}
