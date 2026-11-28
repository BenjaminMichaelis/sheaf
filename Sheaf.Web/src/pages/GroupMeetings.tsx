import { useParams } from 'react-router-dom'
import { useInfiniteQuery, useQuery } from '@tanstack/react-query'
import { Alert, Box, Button, Chip, List, ListItem, ListItemButton, ListItemText, Paper, Stack, Typography } from '@mui/material'
import { Loading, RetryButton } from '@/components/Feedback'
import PagedListFooter from '@/components/PagedList'
import { pagedState } from '@/utils/paging'
import { useChurch } from '@/contexts/ChurchContext'
import { groupQuery, meetingsQuery } from '@/services/churchQueries'
import { formatDate } from '@/utils/dates'
import { offersAttendance, phaseOf } from '@/utils/groups'

/** A group's page: its recorded meetings, newest first, and recording a new one (#188 UI-ATT-07). */
export default function GroupMeetings() {
  const { groupId = '' } = useParams()
  const { church } = useChurch()

  // The route only renders this with a church selected.
  return church ? <GroupPage churchId={church.churchId} groupId={groupId} /> : null
}

function GroupPage({ churchId, groupId }: { churchId: string; groupId: string }) {
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

  const phase = phaseOf(group.data, new Date())

  return (
    <Box sx={{ maxWidth: 720, mx: 'auto' }}>
      <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap', mb: 1 }}>
        <Typography variant="h4" component="h1">
          {group.data.name}
        </Typography>
        {phase === 'ended' && <Chip label="Ended" size="small" />}
      </Stack>
      <Typography color="text.secondary" gutterBottom>
        {labels.one}
      </Typography>

      {offersAttendance(phase, can) && (
        <Button variant="contained" href={`/groups/${groupId}/attendance`} sx={{ my: 2 }}>
          Take attendance
        </Button>
      )}

      <Meetings churchId={churchId} groupId={groupId} />
    </Box>
  )
}

function Meetings({ churchId, groupId }: { churchId: string; groupId: string }) {
  const result = useInfiniteQuery(meetingsQuery(churchId, groupId))
  const state = pagedState(result)
  const meetings = state.items

  return (
    <section aria-labelledby="meetings-heading">
      <Typography id="meetings-heading" variant="h6" component="h2" sx={{ mt: 2, mb: 1 }}>
        Meetings
      </Typography>

      {state.firstPageError && (
        <Alert severity="error" action={<RetryButton onClick={() => void result.refetch()} />}>
          {state.firstPageError}
        </Alert>
      )}

      {state.loaded && meetings.length === 0 && <Typography color="text.secondary">No meetings recorded yet.</Typography>}

      {meetings.length > 0 && (
        <Paper variant="outlined">
          <List disablePadding aria-labelledby="meetings-heading">
            {meetings.map((meeting, index) => (
              <ListItem key={meeting.id} disablePadding divider={index < meetings.length - 1}>
                <ListItemButton href={`/groups/${groupId}/meetings/${meeting.id}`}>
                  <ListItemText primary={formatDate(meeting.occurredOn)} secondary={`${meeting.attendeeCount} present`} />
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
