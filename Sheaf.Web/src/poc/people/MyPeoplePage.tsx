import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  Alert,
  Avatar,
  Box,
  Card,
  Chip,
  InputAdornment,
  List,
  ListItemAvatar,
  ListItemButton,
  ListItemText,
  Skeleton,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import { Block, ChildCare, Search } from '@mui/icons-material'
import { apiClient } from '@/services/apiClient'
import { nudgeColor, timeAgo, touchpointLabel, type MyPerson } from './model'

type Filter = 'all' | 'follow-up' | 'new' | 'groups'

const filters: { value: Filter; label: string }[] = [
  { value: 'follow-up', label: 'Needs follow-up' },
  { value: 'new', label: 'New' },
  { value: 'groups', label: 'In my groups' },
  { value: 'all', label: 'Everyone' },
]

/** Everyone a leader is looking after, most urgent first. */
export default function MyPeoplePage() {
  const navigate = useNavigate()
  const [people, setPeople] = useState<MyPerson[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [filter, setFilter] = useState<Filter>('follow-up')
  const [query, setQuery] = useState('')
  // "New" means added in the week before the page opened.
  const [loadedAt] = useState(() => Date.now())

  useEffect(() => {
    let cancelled = false
    apiClient
      .GET('/api/church/poc/people')
      .then(({ data }) => {
        if (!cancelled) setPeople(data ?? [])
      })
      .catch(() => {
        if (!cancelled) setError('Could not load your people.')
      })

    return () => {
      cancelled = true
    }
  }, [])

  const shown = useMemo(() => {
    const term = query.trim().toLowerCase()

    return (people ?? []).filter(person => {
      if (term && !person.name.toLowerCase().includes(term)) return false

      if (term || filter === 'all') return true

      if (filter === 'follow-up') return person.nudge !== null

      if (filter === 'new') return person.addedByMe && loadedAt - new Date(person.addedAt).getTime() < 7 * 86_400_000

      return person.groupName !== null
    })
  }, [people, filter, query, loadedAt])

  const followUps = people?.filter(person => person.nudge !== null).length ?? 0

  return (
    <Stack spacing={2}>
      <Box>
        <Typography variant="h5" sx={{ fontWeight: 700 }}>
          My people
        </Typography>
        <Typography color="text.secondary">
          {people === null ? ' ' : `${people.length} people · ${followUps} to follow up with`}
        </Typography>
      </Box>

      <TextField
        size="small"
        placeholder="Search your people"
        value={query}
        onChange={event => setQuery(event.target.value)}
        slotProps={{
          input: {
            startAdornment: (
              <InputAdornment position="start">
                <Search />
              </InputAdornment>
            ),
          },
        }}
      />

      <Stack direction="row" spacing={1} sx={{ overflowX: 'auto', pb: 0.5 }}>
        {filters.map(option => (
          <Chip
            key={option.value}
            label={option.label}
            color={filter === option.value ? 'primary' : 'default'}
            variant={filter === option.value ? 'filled' : 'outlined'}
            onClick={() => setFilter(option.value)}
          />
        ))}
      </Stack>

      {error && <Alert severity="error">{error}</Alert>}
      {people === null && !error && <Skeleton variant="rounded" height={320} />}
      {people && shown.length === 0 && (
        <Typography color="text.secondary" variant="body2">
          {filter === 'follow-up' && !query ? "You're all caught up." : 'No one here.'}
        </Typography>
      )}

      {shown.length > 0 && (
        <Card variant="outlined">
          <List disablePadding>
            {shown.map(person => (
              <ListItemButton key={person.id} divider onClick={() => navigate(`/app/people/${person.id}`)} sx={{ alignItems: 'flex-start' }}>
                <ListItemAvatar>
                  <Avatar sx={{ bgcolor: person.nudge ? `${nudgeColor(person.nudge.urgency)}.main` : undefined }}>{person.name.charAt(0)}</Avatar>
                </ListItemAvatar>
                <ListItemText
                  primary={
                    <Box component="span" sx={{ display: 'flex', alignItems: 'center', gap: 0.75 }}>
                      {person.name}
                      {person.isMinor && <ChildCare fontSize="inherit" color="warning" />}
                      {person.doNotContact && <Block fontSize="inherit" color="error" />}
                    </Box>
                  }
                  secondary={
                    <>
                      {person.nudge && (
                        <Box component="span" sx={{ display: 'block', color: `${nudgeColor(person.nudge.urgency)}.main`, fontWeight: 500 }}>
                          {person.nudge.label}
                        </Box>
                      )}
                      <Box component="span" sx={{ display: 'block' }}>
                        {[
                          person.groupName ?? person.howWeMet,
                          person.lastTouchpoint && `${touchpointLabel(person.lastTouchpoint.kind)} ${timeAgo(person.lastTouchpoint.at)}`,
                        ]
                          .filter(Boolean)
                          .join(' · ')}
                      </Box>
                    </>
                  }
                />
              </ListItemButton>
            ))}
          </List>
        </Card>
      )}
    </Stack>
  )
}
