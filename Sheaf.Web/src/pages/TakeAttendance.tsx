import { useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useSnackbar } from 'notistack'
import { Alert, Box, Stack, TextField, Typography } from '@mui/material'
import type { components } from '@/api/schema'
import AttendeesEditor from '@/components/attendance/AttendeesEditor'
import { DescriptionField, SaveBar } from '@/components/attendance/MeetingFields'
import { useAttendees, type NewPerson } from '@/components/attendance/useAttendees'
import ConfirmMeetingDialog from '@/components/ConfirmMeetingDialog'
import { Loading, RetryButton } from '@/components/Feedback'
import { useChurch } from '@/contexts/ChurchContext'
import { groupQuery, meetingsKey, recordMeeting, rosterQuery } from '@/services/churchQueries'
import { churchDateOf } from '@/utils/dates'

type Group = components['schemas']['GroupResponse']

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
  const queryClient = useQueryClient()
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
  const [description, setDescription] = useState('')

  const dateIsValid = occurredOn >= firstDay && occurredOn <= lastDay
  const roster = useQuery({ ...rosterQuery(churchId, group.id, occurredOn), enabled: dateIsValid })
  const attendees = useAttendees(roster.data ?? [])
  // Until the date's roster has loaded, ticks made on it are not counted, so saving waits for it.
  const rosterReady = !roster.isPending && !roster.isError

  const save = useMutation({
    mutationFn: ({ confirm, people }: { confirm: boolean; people: NewPerson[] }) =>
      recordMeeting(churchId, group.id, {
        occurredOn,
        personIds: attendees.personIds,
        newPeople: people.map(person => person.request),
        confirm,
        description: description.trim() || null,
      }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: meetingsKey(churchId, group.id) })
      enqueueSnackbar('Attendance saved.', { variant: 'success' })
      navigate(`/groups/${group.id}`)
    },
    onError: (error, { people }) => attendees.askToConfirm(error, people),
  })

  const saveError = save.error !== null && attendees.confirmation === null && !save.isPending ? save.error.message : null

  return (
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

        <AttendeesEditor
          churchId={churchId}
          attendees={attendees}
          listed={roster.data ?? []}
          heading="Roster"
          status={
            <>
              {roster.isError && (
                <Alert severity="error" action={<RetryButton onClick={() => void roster.refetch()} />}>
                  {roster.error.message}
                </Alert>
              )}
              {dateIsValid && roster.isPending && <Loading />}
              {roster.data?.length === 0 && <Typography color="text.secondary">Nobody was on the roster that day.</Typography>}
            </>
          }
        />

        <DescriptionField value={description} onChange={setDescription} />

        {saveError && <Alert severity="error">{saveError}</Alert>}

        <SaveBar
          label="Save attendance"
          count={attendees.count}
          // Disabled while saving so a meeting is never submitted twice.
          disabled={save.isPending || !dateIsValid || !rosterReady || attendees.count === 0}
          onSave={() => save.mutate({ confirm: false, people: attendees.newPeople })}
        />
      </Stack>

      <ConfirmMeetingDialog
        open={attendees.confirmation !== null}
        groupLabel={groupWord}
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
    </Box>
  )
}
