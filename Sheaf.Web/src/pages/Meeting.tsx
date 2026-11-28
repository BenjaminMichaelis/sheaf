import { useState } from 'react'
import { useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useSnackbar } from 'notistack'
import { Alert, Box, Button, List, ListItem, ListItemText, Paper, Stack, Typography } from '@mui/material'
import type { components } from '@/api/schema'
import AttendeesEditor from '@/components/attendance/AttendeesEditor'
import { DescriptionField, SaveBar } from '@/components/attendance/MeetingFields'
import { useAttendees, type NewPerson } from '@/components/attendance/useAttendees'
import ConfirmMeetingDialog from '@/components/ConfirmMeetingDialog'
import { Loading, RetryButton } from '@/components/Feedback'
import { useChurch } from '@/contexts/ChurchContext'
import { ApiError } from '@/services/apiClient'
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
  const [correcting, setCorrecting] = useState(false)

  if (meeting.isError) {
    return (
      <Alert severity="error" action={<RetryButton onClick={() => void meeting.refetch()} />}>
        {meeting.error.message}
      </Alert>
    )
  }

  if (meeting.data === undefined) return <Loading />

  return (
    <Box sx={{ maxWidth: 720, mx: 'auto' }}>
      <Typography variant="h4" component="h1">
        {formatDate(meeting.data.occurredOn)}
      </Typography>
      {group.data && (
        <Typography color="text.secondary" gutterBottom>
          {group.data.name}
        </Typography>
      )}

      {correcting ? (
        // Keyed by version, so a reload after someone else's change starts the correction again from what they saved.
        <CorrectMeeting
          key={meeting.data.version}
          churchId={churchId}
          groupId={groupId}
          meeting={meeting.data}
          onDone={() => setCorrecting(false)}
        />
      ) : (
        <MeetingDetails meeting={meeting.data} onCorrect={() => setCorrecting(true)} />
      )}
    </Box>
  )
}

function MeetingDetails({ meeting, onCorrect }: { meeting: SessionResponse; onCorrect: () => void }) {
  return (
    <Stack spacing={3} sx={{ mt: 2 }}>
      {meeting.description && <Typography sx={{ whiteSpace: 'pre-wrap' }}>{meeting.description}</Typography>}

      <PeopleSection id="came-heading" title={`Came (${meeting.attendees.length})`} people={meeting.attendees} empty="Nobody was marked present." />
      {/* Anyone on the roster that day who was not marked present; nothing is entered for absence (UI-ATT-04). */}
      <PeopleSection id="absent-heading" title={`Absent (${meeting.absent.length})`} people={meeting.absent} empty="Nobody on the roster was absent." />

      {/* Offered only while the API would accept a correction: Leaders for a week after recording, Staff always. */}
      {meeting.canEdit && (
        <Box>
          <Button variant="contained" onClick={onCorrect}>
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
  meeting: SessionResponse
  onDone: () => void
}

/** Changes who came and the description; people taken off stay in the meeting's history on the server. */
function CorrectMeeting({ churchId, groupId, meeting, onDone }: CorrectMeetingProps) {
  const queryClient = useQueryClient()
  const { enqueueSnackbar } = useSnackbar()
  const { labels } = useChurch()
  // Everyone already on the meeting, ticked if they came, so a correction can add or take anyone off.
  const [listed] = useState(() => [...meeting.attendees, ...meeting.absent].sort((a, b) => a.name.localeCompare(b.name)))
  const attendees = useAttendees(listed, meeting.attendees.map(person => person.id))
  const [description, setDescription] = useState(meeting.description ?? '')
  const meetingKey = meetingQuery(churchId, groupId, meeting.id).queryKey

  const save = useMutation({
    mutationFn: ({ confirm, people }: { confirm: boolean; people: NewPerson[] }) =>
      correctMeeting(churchId, groupId, meeting.id, meeting.version, {
        personIds: attendees.personIds,
        newPeople: people.map(person => person.request),
        confirm,
        description: description.trim() || null,
      }),
    onSuccess: async corrected => {
      queryClient.setQueryData(meetingKey, corrected)
      // The meeting list shows how many came, so it is refreshed too.
      await queryClient.invalidateQueries({ queryKey: meetingsQuery(churchId, groupId).queryKey })
      enqueueSnackbar('Attendance corrected.', { variant: 'success' })
      onDone()
    },
    onError: (error, { people }) => attendees.askToConfirm(error, people),
  })

  // Someone saved a change since this meeting was opened; the API refuses rather than overwrite it.
  const changedElsewhere = save.error instanceof ApiError && save.error.status === 412
  const saveError = save.error !== null && !changedElsewhere && attendees.confirmation === null && !save.isPending ? save.error.message : null

  return (
    <Stack spacing={3} sx={{ mt: 2 }}>
      <AttendeesEditor churchId={churchId} attendees={attendees} listed={listed} heading="Marked on this meeting" />

      <DescriptionField value={description} onChange={setDescription} />

      {changedElsewhere && (
        <Alert
          severity="warning"
          action={
            <Button color="inherit" size="small" onClick={() => void queryClient.invalidateQueries({ queryKey: meetingKey })}>
              Reload
            </Button>
          }
        >
          Someone changed this meeting since you opened it. Reload to see their changes, then correct it again.
        </Alert>
      )}
      {saveError && <Alert severity="error">{saveError}</Alert>}

      <SaveBar
        label="Save correction"
        count={attendees.count}
        // Disabled while saving so a correction is never sent twice. Everyone may be taken off, so zero present is allowed.
        disabled={save.isPending || changedElsewhere}
        onSave={() => save.mutate({ confirm: false, people: attendees.newPeople })}
      >
        <Button onClick={onDone} disabled={save.isPending}>
          Cancel
        </Button>
      </SaveBar>

      <ConfirmMeetingDialog
        open={attendees.confirmation !== null}
        groupLabel={labels.one.toLowerCase()}
        sameDay={attendees.confirmation?.sameDay ?? false}
        duplicates={attendees.confirmation?.duplicates ?? []}
        saving={save.isPending}
        onUseExisting={attendees.chooseExisting}
        onSaveAnyway={() => {
          attendees.closeConfirmation()
          save.mutate({ confirm: true, people: attendees.newPeople })
        }}
        onClose={attendees.closeConfirmation}
      />
    </Stack>
  )
}
