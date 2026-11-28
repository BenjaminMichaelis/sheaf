import { useEffect, useState, type ReactNode } from 'react'
import { Alert, Box, Button, Chip, CircularProgress, List, ListItem, ListItemText, Paper, Typography } from '@mui/material'
import type { components } from '@/api/schema'
import { useChurch } from '@/contexts/ChurchContext'
import { apiClient } from '@/services/apiClient'
import { formatChurchDate } from '@/utils/dates'

type Group = components['schemas']['GroupResponse']

type Phase = 'upcoming' | 'current' | 'ended'

const PAGE_SIZE = 50
const FALLBACK_ERROR = 'Could not load.'

/** Where the group is in its life at `now`. Ended groups stay listed as history; Staff can create groups that start later. */
function phaseOf(group: Group, now: Date): Phase {
  if (group.endsAt !== null && new Date(group.endsAt) <= now) return 'ended'

  return new Date(group.startsAt) > now ? 'upcoming' : 'current'
}

/** Groups the user can reach: Staff see every group including ended ones, Leaders see the groups they lead. */
export default function Groups() {
  const { church } = useChurch()

  // A new church gets a fresh list: nothing loaded for the previous church can be shown or appended under it.
  return <ChurchGroups key={church?.churchId ?? 'none'} />
}

function ChurchGroups() {
  const { labels, timeZoneId, settingsError, reloadSettings } = useChurch()
  const [groups, setGroups] = useState<Group[]>([])
  const [nextOffset, setNextOffset] = useState<number | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [moreError, setMoreError] = useState<string | null>(null)
  const [attempt, setAttempt] = useState(0)

  // The first page; reloads when the user retries.
  useEffect(() => {
    let cancelled = false

    fetchGroups(0)
      .then(page => {
        if (cancelled) return
        setGroups(page.items)
        setNextOffset(page.nextOffset)
        setError(null)
      })
      .catch(caught => {
        if (!cancelled) setError(caught instanceof Error ? caught.message : FALLBACK_ERROR)
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })

    return () => {
      cancelled = true
    }
  }, [attempt])

  const retry = () => {
    setLoading(true)
    setAttempt(value => value + 1)
  }

  // A failed later page keeps the pages already shown; retrying asks for the same page again.
  const loadMore = async (offset: number) => {
    // Hides both "Load more" and the error's Retry, so one page is never requested twice at once.
    setLoading(true)
    setMoreError(null)

    try {
      const page = await fetchGroups(offset)
      setGroups(previous => [...previous, ...page.items])
      setNextOffset(page.nextOffset)
      setMoreError(null)
    } catch (caught) {
      setMoreError(caught instanceof Error ? caught.message : FALLBACK_ERROR)
    } finally {
      setLoading(false)
    }
  }

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

      {error && (
        <Alert severity="error" action={<RetryButton onClick={retry} />} sx={{ mb: 2 }}>
          {error}
        </Alert>
      )}

      {!loading && !error && groups.length === 0 && (
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

      {!loading && moreError && nextOffset !== null && (
        <Alert severity="error" action={<RetryButton onClick={() => void loadMore(nextOffset)} />} sx={{ mt: 2 }}>
          {moreError}
        </Alert>
      )}

      {loading && (
        <Box sx={{ display: 'flex', justifyContent: 'center', py: 3 }}>
          <CircularProgress aria-label="Loading" />
        </Box>
      )}

      {!loading && !moreError && nextOffset !== null && (
        <Box sx={{ display: 'flex', justifyContent: 'center', py: 2 }}>
          <Button onClick={() => void loadMore(nextOffset)}>Load more</Button>
        </Box>
      )}
    </Box>
  )
}

/** One page of groups; `nextOffset` is null on the last page. */
async function fetchGroups(offset: number): Promise<{ items: Group[]; nextOffset: number | null }> {
  const { data } = await apiClient.GET('/api/church/groups', { params: { query: { offset, limit: PAGE_SIZE } } })

  return { items: data?.items ?? [], nextOffset: data?.nextOffset == null ? null : Number(data.nextOffset) }
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
