import type { ReactNode } from 'react'
import { useInfiniteQuery } from '@tanstack/react-query'
import { Alert, Box, Button, Chip, Link, List, ListItem, ListItemText, Paper, Typography } from '@mui/material'
import { RetryButton } from '@/components/Feedback'
import PagedListFooter from '@/components/PagedList'
import { pagedState } from '@/utils/paging'
import type { components } from '@/api/schema'
import { useChurch } from '@/contexts/ChurchContext'
import { groupsQuery } from '@/services/churchQueries'
import { formatChurchDate } from '@/utils/dates'
import { offersAttendance, phaseOf, type Phase } from '@/utils/groups'

type Group = components['schemas']['GroupResponse']

/** Groups the user can reach: Staff see every group including ended ones, Leaders see the groups they lead. */
export default function Groups() {
  const { church } = useChurch()

  // The route only renders this with a church selected.
  return church ? <ChurchGroups churchId={church.churchId} /> : null
}

function ChurchGroups({ churchId }: { churchId: string }) {
  const { labels, timeZoneId, settingsError, reloadSettings, can } = useChurch()

  const result = useInfiniteQuery(groupsQuery(churchId))
  const state = pagedState(result)
  const groups = state.items

  const now = new Date()
  const byPhase = (phase: Phase) => groups.filter(group => phaseOf(group, now) === phase)
  const current = byPhase('current')
  const upcoming = byPhase('upcoming')
  const ended = byPhase('ended')

  return (
    <Box sx={{ maxWidth: 720, mx: 'auto' }}>
      <Typography variant="h4" component="h1" gutterBottom>
        {labels.many}
      </Typography>

      {settingsError && (
        <Alert severity="warning" action={<RetryButton onClick={reloadSettings} />} sx={{ mb: 2 }}>
          Dates are hidden because the church settings could not be loaded: {settingsError}
        </Alert>
      )}

      {state.firstPageError && (
        <Alert severity="error" action={<RetryButton onClick={() => void result.refetch()} />} sx={{ mb: 2 }}>
          {state.firstPageError}
        </Alert>
      )}

      {state.loaded && groups.length === 0 && (
        <Typography color="text.secondary">No {labels.many} to show yet.</Typography>
      )}

      {current.length > 0 && (
        <GroupList groups={current} phase="current" canRecord={offersAttendance('current', can)} label={`Current ${labels.many}`} timeZoneId={timeZoneId} />
      )}

      {upcoming.length > 0 && (
        <GroupSection id="upcoming-groups-heading" title="Upcoming">
          <GroupList groups={upcoming} phase="upcoming" labelledBy="upcoming-groups-heading" timeZoneId={timeZoneId} />
        </GroupSection>
      )}

      {ended.length > 0 && (
        <GroupSection id="ended-groups-heading" title="Ended">
          <GroupList groups={ended} phase="ended" canRecord={offersAttendance('ended', can)} labelledBy="ended-groups-heading" timeZoneId={timeZoneId} />
        </GroupSection>
      )}

      <PagedListFooter state={state} fetchNextPage={() => void result.fetchNextPage()} />
    </Box>
  )
}


function GroupSection({ id, title, children }: { id: string; title: string; children: ReactNode }) {
  return (
    <>
      <Typography id={id} variant="h6" component="h2" sx={{ mt: 3, mb: 1 }}>
        {title}
      </Typography>
      {children}
    </>
  )
}

interface GroupListProps {
  groups: Group[]
  phase: Phase
  /** Whether the user may record attendance for these groups: Leaders lose that once a group ends, Staff never do. */
  canRecord?: boolean
  /** Dates are left out until the church's time zone is known. */
  timeZoneId: string | null
  /** Accessible name for the list when no visible heading names it. */
  label?: string
  /** Id of the visible heading that names the list. */
  labelledBy?: string
}

function GroupList({ groups, phase, canRecord = false, timeZoneId, label, labelledBy }: GroupListProps) {
  const dates = (group: Group) => {
    if (timeZoneId === null) return undefined
    const date = (instant: string) => formatChurchDate(instant, timeZoneId)

    if (phase === 'upcoming') return `Starts ${date(group.startsAt)}`

    return phase === 'ended' && group.endsAt !== null
      ? `${date(group.startsAt)} – ${date(group.endsAt)}`
      : `Since ${date(group.startsAt)}`
  }

  return (
    <Paper variant="outlined">
      <List disablePadding aria-label={label} aria-labelledby={labelledBy}>
        {groups.map((group, index) => (
          <ListItem key={group.id} divider={index < groups.length - 1}>
            <ListItemText
              primary={
                <Link href={`/groups/${group.id}`}>
                  {group.name}
                </Link>
              }
              secondary={dates(group)}
            />
            {phase === 'ended' && <Chip label="Ended" size="small" />}
            {canRecord && (
              <Button
                href={`/groups/${group.id}/attendance`}
                size="small"
                sx={{ ml: 1, flexShrink: 0 }}
                aria-label={`Take attendance for ${group.name}`}
              >
                Take attendance
              </Button>
            )}
          </ListItem>
        ))}
      </List>
    </Paper>
  )
}
