import { useEffect, useState } from 'react'
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  CircularProgress,
  List,
  ListItem,
  ListItemText,
  MenuItem,
  Skeleton,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import { AutoAwesome, Lock } from '@mui/icons-material'
import { useSnackbar } from 'notistack'
import { apiClient } from '@/services/apiClient'
import type { components } from '@/api/schema'

type Overview = components['schemas']['StaffOverviewResponse']

type Brief = components['schemas']['BriefResponse']

function statusColor(status: string) {
  if (status === 'Growing') return 'success'

  if (status === 'Slipping') return 'warning'

  return status === 'No recent meetings' ? 'error' : 'default'
}

function daysAgo(iso: string) {
  return Math.max(0, Math.floor((Date.now() - Date.parse(iso)) / 86_400_000))
}

/** Where outreach stands this month, where follow-up is slipping, and how groups are doing. Counts, not scores. */
export default function StaffOverview() {
  const { enqueueSnackbar } = useSnackbar()
  const [leader, setLeader] = useState('')
  const [overview, setOverview] = useState<Overview | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [brief, setBrief] = useState<Brief | null>(null)
  const [writing, setWriting] = useState(false)

  useEffect(() => {
    let cancelled = false
    apiClient
      .GET('/api/church/poc/staff/overview', { params: { query: { leaderUserId: leader || undefined } } })
      .then(({ data }) => {
        if (!cancelled && data) setOverview(data)
      })
      .catch(() => {
        if (!cancelled) setError('Could not load the overview.')
      })

    return () => {
      cancelled = true
    }
  }, [leader])

  const writeBrief = async () => {
    setWriting(true)

    try {
      const { data } = await apiClient.POST('/api/church/poc/staff/brief')

      if (data) setBrief(data)
    } catch (reason) {
      enqueueSnackbar(reason instanceof Error ? reason.message : 'Could not write the brief.', { variant: 'error' })
    } finally {
      setWriting(false)
    }
  }

  if (error) return <Alert severity="error">{error}</Alert>

  if (!overview) {
    return (
      <Stack spacing={2}>
        <Skeleton variant="text" width="50%" height={40} />
        <Skeleton variant="rounded" height={160} />
        <Skeleton variant="rounded" height={240} />
      </Stack>
    )
  }

  const { funnel, gaps } = overview

  const steps = [
    { label: 'Met', value: Number(funnel.captured) },
    { label: 'Followed up', value: Number(funnel.contacted) },
    { label: 'Came to a group', value: Number(funnel.attended) },
    { label: 'Joined a group', value: Number(funnel.inGroup) },
  ]

  const top = Math.max(1, steps[0]?.value ?? 1)

  const tiles = [
    { label: 'Past 48 hours, no first text', value: gaps.overdueFirstContact, tone: 'error.main' },
    { label: 'Met but not in a group', value: gaps.unplaced, tone: 'text.primary' },
    { label: 'Group members quiet 2+ weeks', value: gaps.quietMembers, tone: 'warning.main' },
    { label: 'Missed last 2 meetings', value: gaps.missedLastTwo, tone: 'warning.main' },
  ]

  return (
    <Stack spacing={2.5}>
      <Typography variant="h5" sx={{ fontWeight: 700 }}>
        Overview
      </Typography>

      <Card variant="outlined">
        <CardContent>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 1 }}>
            <Typography variant="overline" color="text.secondary" sx={{ flexGrow: 1 }}>
              Weekly brief
            </Typography>
            {brief && <Chip size="small" icon={<AutoAwesome />} label={brief.aiLive ? 'AI-drafted by Claude' : 'AI-drafted (canned)'} variant="outlined" />}
          </Box>
          {brief ? (
            <Stack spacing={1}>
              {brief.text.split(/\n\s*\n/).map(paragraph => (
                <Typography key={paragraph.slice(0, 24)} variant="body2">
                  {paragraph}
                </Typography>
              ))}
            </Stack>
          ) : (
            <Button variant="contained" startIcon={writing ? <CircularProgress size={18} color="inherit" /> : <AutoAwesome />} disabled={writing} onClick={() => void writeBrief()}>
              {writing ? 'Writing…' : "Write this week's brief"}
            </Button>
          )}
          <Typography variant="caption" color="text.secondary" sx={{ display: 'flex', gap: 0.5, alignItems: 'center', mt: 1.5 }}>
            <Lock sx={{ fontSize: 14 }} /> Written from totals and group names only. No student names or notes are sent to AI.
          </Typography>
        </CardContent>
      </Card>

      <Card variant="outlined">
        <CardContent>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 1.5 }}>
            <Typography variant="overline" color="text.secondary" sx={{ flexGrow: 1 }}>
              Last {funnel.days} days
            </Typography>
            <TextField select size="small" value={leader} onChange={event => setLeader(event.target.value)} sx={{ minWidth: 150 }} slotProps={{ select: { displayEmpty: true } }}>
              <MenuItem value="">All leaders</MenuItem>
              {overview.leaders.map(row => (
                <MenuItem key={row.userId} value={row.userId}>
                  {row.name}
                </MenuItem>
              ))}
            </TextField>
          </Box>
          <Stack spacing={1}>
            {steps.map(step => (
              <Box key={step.label}>
                <Box sx={{ display: 'flex', justifyContent: 'space-between' }}>
                  <Typography variant="body2">{step.label}</Typography>
                  <Typography variant="body2" sx={{ fontWeight: 600 }}>
                    {step.value}
                  </Typography>
                </Box>
                <Box sx={{ height: 10, borderRadius: 5, bgcolor: 'action.hover', overflow: 'hidden' }}>
                  <Box sx={{ height: '100%', width: `${(step.value / top) * 100}%`, bgcolor: 'primary.main', borderRadius: 5 }} />
                </Box>
              </Box>
            ))}
          </Stack>
        </CardContent>
      </Card>

      <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 1.5 }}>
        {tiles.map(tile => (
          <Card key={tile.label} variant="outlined">
            <CardContent sx={{ py: 1.5, '&:last-child': { pb: 1.5 } }}>
              <Typography variant="h4" sx={{ fontWeight: 700, color: Number(tile.value) > 0 ? tile.tone : 'text.secondary' }}>
                {tile.value}
              </Typography>
              <Typography variant="caption" color="text.secondary">
                {tile.label}
              </Typography>
            </CardContent>
          </Card>
        ))}
      </Box>

      {overview.overdue.length > 0 && (
        <Box>
          <Typography variant="overline" color="text.secondary">
            Waiting for a first text
          </Typography>
          <Card variant="outlined">
            <List dense disablePadding>
              {overview.overdue.map(person => (
                <ListItem key={person.id} divider>
                  <ListItemText primary={person.name} secondary={`Met by ${person.addedBy} · ${daysAgo(person.addedAt)} days ago`} />
                </ListItem>
              ))}
            </List>
          </Card>
        </Box>
      )}

      <Box>
        <Typography variant="overline" color="text.secondary">
          Groups
        </Typography>
        <Card variant="outlined">
          <List dense disablePadding>
            {overview.groups.map(group => (
              <ListItem key={group.id} divider>
                <ListItemText
                  primary={group.name}
                  secondary={`${group.leaders} · ${group.rosterSize} on roster · avg ${group.recentAverage} (was ${group.priorAverage})${Number(group.missedLastTwo) > 0 ? ` · ${group.missedLastTwo} missed last 2` : ''}`}
                />
                <Chip size="small" label={group.status} color={statusColor(group.status)} variant="outlined" />
              </ListItem>
            ))}
          </List>
        </Card>
      </Box>

      <Box>
        <Typography variant="overline" color="text.secondary">
          Leaders · last {funnel.days} days
        </Typography>
        <Card variant="outlined">
          <List dense disablePadding>
            {overview.leaders.map(row => (
              <ListItem key={row.userId} divider>
                <ListItemText
                  primary={row.name}
                  secondary={`Met ${row.captured} · followed up ${row.contacted}${Number(row.groupMembers) > 0 ? ` · ${row.quietMembers} of ${row.groupMembers} group members quiet` : ''}`}
                />
                {Number(row.overdueFirstContact) > 0 && <Chip size="small" color="error" variant="outlined" label={`${row.overdueFirstContact} waiting`} />}
              </ListItem>
            ))}
          </List>
        </Card>
      </Box>
    </Stack>
  )
}
