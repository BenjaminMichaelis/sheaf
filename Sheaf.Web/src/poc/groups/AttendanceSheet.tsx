import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import {
  Alert,
  Avatar,
  Box,
  Button,
  Card,
  Checkbox,
  Chip,
  CircularProgress,
  IconButton,
  InputAdornment,
  List,
  ListItemAvatar,
  ListItemButton,
  ListItemText,
  Paper,
  Skeleton,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import { ArrowBack, Close, PersonAdd, Search } from '@mui/icons-material'
import { useSnackbar } from 'notistack'
import { apiClient, confirmationReasons } from '@/services/apiClient'
import type { components } from '@/api/schema'
import {
  formatDay,
  loadGroupOverview,
  todayIso,
  type Confirmation,
  type ConfirmationChoice,
  type GroupOverview,
  type NewPerson,
} from './groupData'
import ConfirmAttendanceDialog from './ConfirmAttendanceDialog'

type LookupPerson = components['schemas']['PersonLookupResponse']

interface Extra {
  id: string
  name: string
}

/** Records a new meeting, or changes who came to one already recorded when a session id is in the route. */
export default function AttendanceSheet() {
  const { groupId = '', sessionId } = useParams()
  const navigate = useNavigate()
  const { enqueueSnackbar } = useSnackbar()
  const [overview, setOverview] = useState<GroupOverview | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [occurredOn, setOccurredOn] = useState(todayIso())
  const [version, setVersion] = useState<string | null>(null)
  const [present, setPresent] = useState<Set<string>>(new Set())
  const [extras, setExtras] = useState<Extra[]>([])
  const [newPeople, setNewPeople] = useState<NewPerson[]>([])
  const [query, setQuery] = useState('')
  const [results, setResults] = useState<LookupPerson[] | null>(null)
  const [saving, setSaving] = useState(false)
  const [confirmation, setConfirmation] = useState<Confirmation | null>(null)

  useEffect(() => {
    let cancelled = false

    const load = async () => {
      const data = await loadGroupOverview(groupId)

      if (cancelled) return

      setOverview(data)

      if (!sessionId) return

      const { data: session } = await apiClient.GET('/api/church/groups/{groupId}/sessions/{sessionId}', {
        params: { path: { groupId, sessionId } },
      })

      if (cancelled || !session) return

      const rosterIds = new Set(data.roster.map(person => person.id))

      setOccurredOn(session.occurredOn)
      setVersion(session.version)
      setPresent(new Set(session.attendees.map(person => person.id)))
      setExtras(session.attendees.filter(person => !rosterIds.has(person.id)))
    }

    load().catch(() => {
      if (!cancelled) setLoadError('Could not load this group.')
    })

    return () => {
      cancelled = true
    }
  }, [groupId, sessionId])

  // Name lookup, debounced; the API needs at least two characters.
  useEffect(() => {
    const term = query.trim()

    if (term.length < 2) return

    let cancelled = false

    const timer = window.setTimeout(() => {
      apiClient
        .GET('/api/church/people/lookup', { params: { query: { name: term } } })
        .then(({ data }) => {
          if (!cancelled) setResults(data ?? [])
        })
        .catch(() => {
          if (!cancelled) setResults([])
        })
    }, 250)

    return () => {
      cancelled = true
      window.clearTimeout(timer)
    }
  }, [query])

  const rosterIds = useMemo(() => new Set(overview?.roster.map(person => person.id) ?? []), [overview])
  const suggestedVisitors = (overview?.visitors ?? []).filter(visitor => !extras.some(extra => extra.id === visitor.id))
  const count = present.size + newPeople.length

  const toggle = (id: string) => {
    setPresent(previous => {
      const next = new Set(previous)

      if (next.has(id)) next.delete(id)
      else next.add(id)

      return next
    })
  }

  const addExisting = (person: Extra) => {
    if (!rosterIds.has(person.id) && !extras.some(extra => extra.id === person.id)) setExtras(previous => [...previous, person])

    setPresent(previous => new Set(previous).add(person.id))
    setQuery('')
  }

  const addNew = () => {
    const [firstName = '', ...rest] = query.trim().split(/\s+/)

    setNewPeople(previous => [...previous, { key: crypto.randomUUID(), firstName, lastName: rest.join(' ') }])
    setQuery('')
  }

  const removeExtra = (id: string) => {
    setExtras(previous => previous.filter(extra => extra.id !== id))
    setPresent(previous => {
      const next = new Set(previous)

      next.delete(id)

      return next
    })
  }

  const save = async (confirm: boolean, people: NewPerson[] = newPeople, ids: Set<string> = present) => {
    setSaving(true)

    const body = {
      personIds: [...ids],
      newPeople: people.map(person => ({ firstName: person.firstName, lastName: person.lastName || null })),
      confirm,
    }

    try {
      const response = sessionId
        ? await apiClient.PUT('/api/church/groups/{groupId}/sessions/{sessionId}', {
            params: { path: { groupId, sessionId }, header: { 'If-Match': `"${version}"` } },
            body,
          })
        : await apiClient.POST('/api/church/groups/{groupId}/sessions', { params: { path: { groupId } }, body: { ...body, occurredOn } })

      const saved = response.data

      enqueueSnackbar(saved ? `Saved · ${saved.attendees.length} came, ${saved.absent.length} missed` : 'Saved', { variant: 'success' })
      navigate(`/app/groups/${groupId}`)
    } catch (error) {
      const reasons = error instanceof Error ? confirmationReasons(error) : null

      if (reasons) {
        setConfirmation({ ...reasons, newPeople: people })
      } else {
        enqueueSnackbar(error instanceof Error ? error.message : 'Could not save attendance.', { variant: 'error' })
      }
    } finally {
      setSaving(false)
    }
  }

  const resolveConfirmation = (choice: ConfirmationChoice) => {
    const pending = confirmation

    setConfirmation(null)

    if (!pending || choice.kind === 'cancel') return

    if (choice.kind === 'open-existing') {
      navigate(`/app/groups/${groupId}/sessions/${choice.sessionId}`)

      return
    }

    // Swap any new person the leader matched to someone already in the church, then save for real.
    const ids = new Set(present)
    const matched: Extra[] = []

    const remaining = pending.newPeople.filter((_, index) => {
      const match = choice.matches.get(index)

      if (!match) return true

      ids.add(match.id)
      matched.push(match)

      return false
    })

    setNewPeople(remaining)
    setPresent(ids)
    setExtras(previous => [...previous, ...matched.filter(m => !rosterIds.has(m.id) && !previous.some(e => e.id === m.id))])
    void save(true, remaining, ids)
  }

  if (loadError) return <Alert severity="error">{loadError}</Alert>

  if (!overview) {
    return (
      <Stack spacing={2}>
        <Skeleton variant="text" width="60%" height={40} />
        <Skeleton variant="rounded" height={320} />
      </Stack>
    )
  }

  const trimmed = query.trim()

  return (
    <Stack spacing={2} sx={{ pb: 8 }}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
        <IconButton edge="start" onClick={() => navigate(`/app/groups/${groupId}`)} aria-label="Back to group">
          <ArrowBack />
        </IconButton>
        <Box sx={{ flexGrow: 1, minWidth: 0 }}>
          <Typography variant="h6" sx={{ fontWeight: 700, lineHeight: 1.2 }}>
            {sessionId ? 'Edit attendance' : 'Who came?'}
          </Typography>
          <Typography variant="caption" color="text.secondary">
            {overview.group.name}
          </Typography>
        </Box>
      </Box>

      {sessionId ? (
        <Typography color="text.secondary">{formatDay(occurredOn, { weekday: 'long', month: 'long', day: 'numeric' })}</Typography>
      ) : (
        <TextField
          type="date"
          label="Meeting date"
          value={occurredOn}
          onChange={event => setOccurredOn(event.target.value)}
          slotProps={{ htmlInput: { max: todayIso() }, inputLabel: { shrink: true } }}
          size="small"
        />
      )}

      <Box>
        <Box sx={{ display: 'flex', alignItems: 'center' }}>
          <Typography variant="overline" color="text.secondary" sx={{ flexGrow: 1 }}>
            Roster · tap who came
          </Typography>
          <Button size="small" onClick={() => setPresent(previous => new Set([...previous, ...overview.roster.map(person => person.id)]))}>
            Everyone
          </Button>
          <Button size="small" color="inherit" onClick={() => setPresent(previous => new Set([...previous].filter(id => !rosterIds.has(id))))}>
            Clear
          </Button>
        </Box>
        <Card variant="outlined">
          <List disablePadding>
            {overview.roster.map(person => (
              <ListItemButton key={person.id} onClick={() => toggle(person.id)} selected={present.has(person.id)} sx={{ py: 1 }}>
                <ListItemAvatar>
                  <Avatar>{person.name.charAt(0)}</Avatar>
                </ListItemAvatar>
                <ListItemText primary={person.name} secondary={person.isLeader ? 'Leader' : undefined} />
                <Checkbox edge="end" checked={present.has(person.id)} tabIndex={-1} disableRipple slotProps={{ input: { 'aria-label': person.name } }} />
              </ListItemButton>
            ))}
          </List>
        </Card>
      </Box>

      <Box>
        <Typography variant="overline" color="text.secondary">
          Anyone else?
        </Typography>
        {suggestedVisitors.length > 0 && (
          <Stack direction="row" spacing={1} sx={{ mb: 1, flexWrap: 'wrap', rowGap: 1 }}>
            {suggestedVisitors.map(visitor => (
              <Chip key={visitor.id} icon={<PersonAdd />} label={visitor.name} onClick={() => addExisting(visitor)} variant="outlined" />
            ))}
          </Stack>
        )}
        <TextField
          fullWidth
          size="small"
          placeholder="Search by name"
          value={query}
          onChange={event => {
            setQuery(event.target.value)
            setResults(null)
          }}
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
        {trimmed.length >= 2 && (
          <Card variant="outlined" sx={{ mt: 1 }}>
            <List dense disablePadding>
              {results === null && (
                <Box sx={{ p: 1.5, display: 'flex', justifyContent: 'center' }}>
                  <CircularProgress size={20} />
                </Box>
              )}
              {results?.map(person => (
                <ListItemButton key={person.id} onClick={() => addExisting(person)}>
                  <ListItemText primary={person.name} secondary={person.householdLabel ?? undefined} />
                  {present.has(person.id) && <Chip size="small" label="Here" color="success" />}
                </ListItemButton>
              ))}
              <ListItemButton onClick={addNew}>
                <PersonAdd fontSize="small" sx={{ mr: 1.5 }} color="primary" />
                <ListItemText primary={`Add "${trimmed}" as someone new`} slotProps={{ primary: { color: 'primary' } }} />
              </ListItemButton>
            </List>
          </Card>
        )}
        {(extras.length > 0 || newPeople.length > 0) && (
          <Stack direction="row" spacing={1} sx={{ mt: 1.5, flexWrap: 'wrap', rowGap: 1 }}>
            {extras.map(extra => (
              <Chip key={extra.id} label={extra.name} color={present.has(extra.id) ? 'primary' : 'default'} onDelete={() => removeExtra(extra.id)} />
            ))}
            {newPeople.map(person => (
              <Chip
                key={person.key}
                label={`${person.firstName} ${person.lastName} · new`.replace('  ', ' ')}
                color="secondary"
                deleteIcon={<Close />}
                onDelete={() => setNewPeople(previous => previous.filter(p => p.key !== person.key))}
              />
            ))}
          </Stack>
        )}
      </Box>

      <Paper
        elevation={6}
        sx={{ position: 'fixed', bottom: 64, left: '50%', transform: 'translateX(-50%)', width: 'calc(100% - 32px)', maxWidth: 528, borderRadius: 3, p: 1 }}
      >
        <Button fullWidth variant="contained" size="large" disabled={saving} onClick={() => void save(false)}>
          {saving ? <CircularProgress size={24} color="inherit" /> : `Save · ${count} here`}
        </Button>
      </Paper>

      {confirmation && <ConfirmAttendanceDialog confirmation={confirmation} occurredOn={occurredOn} onClose={resolveConfirmation} />}
    </Stack>
  )
}
