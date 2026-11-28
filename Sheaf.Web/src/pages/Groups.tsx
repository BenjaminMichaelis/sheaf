import { useEffect, useState } from 'react'
import { Alert, Box, Button, Chip, CircularProgress, List, ListItem, ListItemText, Paper, Typography } from '@mui/material'
import type { components } from '@/api/schema'
import { useChurch } from '@/contexts/ChurchContext'
import { apiClient } from '@/services/apiClient'
import { formatChurchDate } from '@/utils/dates'

type Group = components['schemas']['GroupResponse']

const PAGE_SIZE = 50
const FALLBACK_ERROR = 'Could not load.'

/** Whether the group has ended by `now`; ended groups stay listed as history. */
function hasEnded(group: Group, now: Date) {
  return group.endsAt !== null && new Date(group.endsAt) <= now
}

/** Groups the user can reach: Staff see every group including ended ones, Leaders see the groups they lead. */
export default function Groups() {
  const { church, labels, timeZoneId } = useChurch()
  const [groups, setGroups] = useState<Group[]>([])
  const [nextOffset, setNextOffset] = useState<number | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [attempt, setAttempt] = useState(0)

  // The first page; reloads when the church changes or the user retries.
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
  }, [church?.churchId, attempt])

  const retry = () => {
    setLoading(true)
    setAttempt(value => value + 1)
  }

  const loadMore = async (offset: number) => {
    setLoading(true)

    try {
      const page = await fetchGroups(offset)
      setGroups(previous => [...previous, ...page.items])
      setNextOffset(page.nextOffset)
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : FALLBACK_ERROR)
    } finally {
      setLoading(false)
    }
  }

  const now = new Date()
  const current = groups.filter(group => !hasEnded(group, now))
  const ended = groups.filter(group => hasEnded(group, now))

  return (
    <Box sx={{ maxWidth: 720, mx: 'auto' }}>
      <Typography variant="h4" component="h1" gutterBottom>
        {labels.many}
      </Typography>

      {error && (
        <Alert severity="error" action={<Button onClick={retry}>Retry</Button>} sx={{ mb: 2 }}>
          {error}
        </Alert>
      )}

      {!loading && !error && groups.length === 0 && (
        <Typography color="text.secondary">No {labels.many} to show yet.</Typography>
      )}

      {current.length > 0 && <GroupList groups={current} label={`Current ${labels.many}`} timeZoneId={timeZoneId} />}

      {ended.length > 0 && (
        <>
          <Typography id="ended-groups-heading" variant="h6" component="h2" sx={{ mt: 3, mb: 1 }}>
            Ended
          </Typography>
          <GroupList groups={ended} labelledBy="ended-groups-heading" timeZoneId={timeZoneId} ended />
        </>
      )}

      {loading && (
        <Box sx={{ display: 'flex', justifyContent: 'center', py: 3 }}>
          <CircularProgress aria-label="Loading" />
        </Box>
      )}

      {!loading && nextOffset !== null && (
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

interface GroupListProps {
  groups: Group[]
  timeZoneId: string | null
  ended?: boolean
  /** Accessible name for the list when no visible heading names it. */
  label?: string
  /** Id of the visible heading that names the list. */
  labelledBy?: string
}

function GroupList({ groups, timeZoneId, ended = false, label, labelledBy }: GroupListProps) {
  const date = (instant: string) => formatChurchDate(instant, timeZoneId)

  return (
    <Paper variant="outlined">
      <List disablePadding aria-label={label} aria-labelledby={labelledBy}>
        {groups.map((group, index) => (
          <ListItem key={group.id} divider={index < groups.length - 1}>
            <ListItemText
              primary={group.name}
              secondary={
                ended && group.endsAt !== null
                  ? `${date(group.startsAt)} – ${date(group.endsAt)}`
                  : `Since ${date(group.startsAt)}`
              }
            />
            {ended && <Chip label="Ended" size="small" />}
          </ListItem>
        ))}
      </List>
    </Paper>
  )
}

