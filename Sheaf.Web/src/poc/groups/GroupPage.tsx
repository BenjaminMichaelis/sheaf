import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import {
  Alert,
  Avatar,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  IconButton,
  List,
  ListItem,
  ListItemAvatar,
  ListItemButton,
  ListItemText,
  Skeleton,
  Stack,
  Typography,
} from '@mui/material'
import { ArrowBack, ChevronRight, EditCalendar, WavingHand } from '@mui/icons-material'
import { formatDay, listNames, loadGroupOverview, TREND_WEEKS, type GroupOverview } from './groupData'
import AttendanceTrend from './AttendanceTrend'

/** A leader's view of one group: the trend, who to check on, visitors, the roster and past meetings. */
export default function GroupPage() {
  const { groupId = '' } = useParams()
  const navigate = useNavigate()
  const [overview, setOverview] = useState<GroupOverview | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    loadGroupOverview(groupId)
      .then(data => {
        if (!cancelled) setOverview(data)
      })
      .catch(() => {
        if (!cancelled) setError('Could not load this group.')
      })

    return () => {
      cancelled = true
    }
  }, [groupId])

  if (error) return <Alert severity="error">{error}</Alert>

  if (!overview) {
    return (
      <Stack spacing={2}>
        <Skeleton variant="text" width="60%" height={40} />
        <Skeleton variant="rounded" height={140} />
        <Skeleton variant="rounded" height={240} />
      </Stack>
    )
  }

  const { group, sessions, roster, visitors } = overview
  const drifting = roster.filter(person => !person.isLeader && person.missedInARow >= 2)
  const recent = [...sessions].reverse()

  return (
    <Stack spacing={2.5}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
        <IconButton edge="start" onClick={() => navigate('/app/groups')} aria-label="Back to groups">
          <ArrowBack />
        </IconButton>
        <Box sx={{ flexGrow: 1, minWidth: 0 }}>
          <Typography variant="h6" sx={{ fontWeight: 700, lineHeight: 1.2 }}>
            {group.name}
          </Typography>
          <Typography variant="caption" color="text.secondary">
            {roster.length} on the roster
          </Typography>
        </Box>
      </Box>

      <Button
        variant="contained"
        size="large"
        startIcon={<EditCalendar />}
        onClick={() => navigate(`/app/groups/${group.id}/record`)}
        sx={{ py: 1.5, borderRadius: 3 }}
      >
        Record tonight&apos;s attendance
      </Button>

      {drifting.length > 0 && (
        <Alert severity="warning" icon={<WavingHand />}>
          <Typography variant="body2" sx={{ fontWeight: 600 }}>
            Check in with {listNames(drifting.map(person => person.name.split(' ')[0] ?? person.name))}
          </Typography>
          <Typography variant="body2">
            {drifting.length === 1 ? 'They have' : 'They have each'} missed the last {Math.min(...drifting.map(p => p.missedInARow))} meetings.
          </Typography>
        </Alert>
      )}

      <Card variant="outlined">
        <CardContent>
          <Typography variant="overline" color="text.secondary">
            Last {Math.min(TREND_WEEKS, sessions.length)} meetings
          </Typography>
          <AttendanceTrend sessions={sessions} />
        </CardContent>
      </Card>

      {visitors.length > 0 && (
        <Box>
          <Typography variant="overline" color="text.secondary">
            Visitors
          </Typography>
          <Card variant="outlined">
            <List dense disablePadding>
              {visitors.map(visitor => (
                <ListItem key={visitor.id}>
                  <ListItemAvatar>
                    <Avatar sx={{ bgcolor: 'secondary.main' }}>{visitor.name.charAt(0)}</Avatar>
                  </ListItemAvatar>
                  <ListItemText
                    primary={visitor.name}
                    secondary={`Came ${visitor.visits === 1 ? 'once' : `${visitor.visits} times`} · last ${formatDay(visitor.lastVisit)}`}
                  />
                  {visitor.visits >= 2 && <Chip size="small" color="secondary" label="Invite to join?" />}
                </ListItem>
              ))}
            </List>
          </Card>
        </Box>
      )}

      <Box>
        <Typography variant="overline" color="text.secondary">
          Roster
        </Typography>
        <Card variant="outlined">
          <List dense disablePadding>
            {roster.map(person => (
              <ListItem key={person.id}>
                <ListItemAvatar>
                  <Avatar sx={{ bgcolor: person.missedInARow >= 2 && !person.isLeader ? 'warning.main' : undefined }}>
                    {person.name.charAt(0)}
                  </Avatar>
                </ListItemAvatar>
                <ListItemText
                  primary={person.name}
                  secondary={
                    person.held === 0
                      ? 'No meetings yet'
                      : `${person.attended} of ${person.held}${person.lastAttended ? ` · last came ${formatDay(person.lastAttended)}` : ''}`
                  }
                />
                {person.isLeader && <Chip size="small" label="Leader" variant="outlined" />}
              </ListItem>
            ))}
          </List>
        </Card>
      </Box>

      <Box>
        <Typography variant="overline" color="text.secondary">
          Past meetings
        </Typography>
        {recent.length === 0 && (
          <Typography variant="body2" color="text.secondary">
            No meetings recorded yet.
          </Typography>
        )}
        <Card variant="outlined">
          <List dense disablePadding>
            {recent.map(session => (
              <ListItemButton key={session.id} onClick={() => navigate(`/app/groups/${group.id}/sessions/${session.id}`)}>
                <ListItemText
                  primary={formatDay(session.occurredOn)}
                  secondary={`${session.attendees.length} came · ${session.absent.length} missed`}
                />
                <ChevronRight color="action" />
              </ListItemButton>
            ))}
          </List>
        </Card>
      </Box>
    </Stack>
  )
}
