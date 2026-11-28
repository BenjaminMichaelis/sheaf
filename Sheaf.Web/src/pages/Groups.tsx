import type { ReactNode } from 'react'
import { useInfiniteQuery } from '@tanstack/react-query'
import { Alert, Box, Button, Chip, CircularProgress, List, ListItem, ListItemText, Paper, Typography } from '@mui/material'
import type { components } from '@/api/schema'
import { useChurch } from '@/contexts/ChurchContext'
import { groupsQuery } from '@/services/churchQueries'
import { formatChurchDate } from '@/utils/dates'

type Group = components['schemas']['GroupResponse']

type Phase = 'upcoming' | 'current' | 'ended'

/** Where the group is in its life at `now`. Ended groups stay listed as history; Staff can create groups that start later. */
function phaseOf(group: Group, now: Date): Phase {
  if (group.endsAt !== null && new Date(group.endsAt) <= now) return 'ended'

  return new Date(group.startsAt) > now ? 'upcoming' : 'current'
}

/** Groups the user can reach: Staff see every group including ended ones, Leaders see the groups they lead. */
export default function Groups() {
  const { church } = useChurch()

  // The route only renders this with a church selected.
  return church ? <ChurchGroups churchId={church.churchId} /> : null
}

function ChurchGroups({ churchId }: { churchId: string }) {
  const { labels, timeZoneId, settingsError, reloadSettings } = useChurch()

  const { data, error, isError, isFetching, isFetchingNextPage, isFetchNextPageError, hasNextPage, fetchNextPage, refetch } =
    useInfiniteQuery(groupsQuery(churchId))

  const groups = data?.pages.flatMap(page => page.items) ?? []
  // Errors are hidden while their retry is in flight. A failed later page, or a failed background refresh, keeps the
  // pages already shown.
  const firstPageError = data === undefined && isError && !isFetching ? error.message : null
  const moreError = isFetchNextPageError && !isFetching ? error.message : null
  const loading = isFetching && (data === undefined || isFetchingNextPage)
  // TanStack Query recommends calling fetchNextPage only when nothing else is fetching, so a refresh is not overwritten.
  const canLoadMore = hasNextPage && !isFetching && moreError === null

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

      {firstPageError && (
        <Alert severity="error" action={<RetryButton onClick={() => void refetch()} />} sx={{ mb: 2 }}>
          {firstPageError}
        </Alert>
      )}

      {data !== undefined && groups.length === 0 && (
        <Typography color="text.secondary">No {labels.many} to show yet.</Typography>
      )}

      {current.length > 0 && (
        <GroupList groups={current} phase="current" label={`Current ${labels.many}`} timeZoneId={timeZoneId} />
      )}

      {upcoming.length > 0 && (
        <GroupSection id="upcoming-groups-heading" title="Upcoming">
          <GroupList groups={upcoming} phase="upcoming" labelledBy="upcoming-groups-heading" timeZoneId={timeZoneId} />
        </GroupSection>
      )}

      {ended.length > 0 && (
        <GroupSection id="ended-groups-heading" title="Ended">
          <GroupList groups={ended} phase="ended" labelledBy="ended-groups-heading" timeZoneId={timeZoneId} />
        </GroupSection>
      )}

      {moreError && (
        <Alert severity="error" action={<RetryButton onClick={() => void fetchNextPage()} />} sx={{ mt: 2 }}>
          {moreError}
        </Alert>
      )}

      {loading && (
        <Box sx={{ display: 'flex', justifyContent: 'center', py: 3 }}>
          <CircularProgress aria-label="Loading" />
        </Box>
      )}

      {canLoadMore && (
        <Box sx={{ display: 'flex', justifyContent: 'center', py: 2 }}>
          <Button onClick={() => void fetchNextPage()}>Load more</Button>
        </Box>
      )}
    </Box>
  )
}

/** An alert's action button, styled as MUI's Alert examples show. */
function RetryButton({ onClick }: { onClick: () => void }) {
  return (
    <Button color="inherit" size="small" onClick={onClick}>
      Retry
    </Button>
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
  /** Dates are left out until the church's time zone is known. */
  timeZoneId: string | null
  /** Accessible name for the list when no visible heading names it. */
  label?: string
  /** Id of the visible heading that names the list. */
  labelledBy?: string
}

function GroupList({ groups, phase, timeZoneId, label, labelledBy }: GroupListProps) {
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
            <ListItemText primary={group.name} secondary={dates(group)} />
            {phase === 'ended' && <Chip label="Ended" size="small" />}
          </ListItem>
        ))}
      </List>
    </Paper>
  )
}
