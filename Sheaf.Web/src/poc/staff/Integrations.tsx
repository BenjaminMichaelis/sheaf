import { useState } from 'react'
import {
  Avatar,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  LinearProgress,
  List,
  ListItem,
  ListItemIcon,
  ListItemText,
  Stack,
  Typography,
} from '@mui/material'
import { CallMade, CallReceived, CheckCircle, Sync, SyncAlt } from '@mui/icons-material'

// POC mock: the Planning Center connection is illustrative only (fixed numbers, no API calls).
// The real integration is being built separately.

const syncs = [
  { title: 'People and households', detail: '412 people · two-way', direction: 'both' },
  { title: 'Groups and memberships', detail: '18 groups · two-way', direction: 'both' },
  { title: 'Attendance', detail: 'Sent to Planning Center Groups after each meeting', direction: 'out' },
  { title: 'New people from leaders', detail: 'Created in Planning Center People once approved', direction: 'out' },
  { title: 'Campuses', detail: 'Main, North and Downtown campuses mapped', direction: 'in' },
] as const

const initialActivity = [
  { when: '6 min ago', text: 'Sent attendance for Smith Hall Freshman Guys (9 here, 1 visitor)' },
  { when: '22 min ago', text: 'Created Maya Chen in Planning Center People' },
  { when: '1 hr ago', text: 'Merged Chris Nguyen into Christopher Nguyen' },
  { when: '3 hr ago', text: 'Imported 3 people and 1 household from Planning Center' },
  { when: 'Yesterday', text: 'Updated memberships for Thursday Women’s Study' },
]

function DirectionIcon({ direction }: { direction: 'both' | 'in' | 'out' }) {
  if (direction === 'both') {
    return <SyncAlt color="primary" />
  }

  return direction === 'out' ? <CallMade color="primary" /> : <CallReceived color="primary" />
}

/** Staff integrations: the Planning Center connection (mocked for the demo). */
export default function Integrations() {
  const [syncing, setSyncing] = useState(false)
  const [lastSynced, setLastSynced] = useState('6 minutes ago')
  const [activity, setActivity] = useState(initialActivity)

  const syncNow = () => {
    setSyncing(true)

    window.setTimeout(() => {
      setSyncing(false)
      setLastSynced('just now')
      setActivity(previous => [{ when: 'Just now', text: 'Synced 412 people, 18 groups. No conflicts.' }, ...previous])
    }, 2200)
  }

  return (
    <Stack spacing={2.5}>
      <Box>
        <Typography variant="h5" sx={{ fontWeight: 700 }}>
          Integrations
        </Typography>
        <Typography color="text.secondary">Sheaf keeps your church database current, so leaders never enter anything twice.</Typography>
      </Box>

      <Card variant="outlined">
        <CardContent>
          <Stack direction="row" spacing={1.5} sx={{ alignItems: 'center' }}>
            <Avatar sx={{ bgcolor: '#2266f7', fontWeight: 700 }}>PC</Avatar>
            <Box sx={{ flexGrow: 1, minWidth: 0 }}>
              <Typography sx={{ fontWeight: 700 }}>Planning Center</Typography>
              <Typography variant="body2" color="text.secondary" noWrap>
                Northgate Church · People, Groups
              </Typography>
            </Box>
            <Chip size="small" color="success" icon={<CheckCircle />} label="Connected" />
          </Stack>

          <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mt: 2 }}>
            <Typography variant="body2" sx={{ flexGrow: 1 }}>
              {syncing ? 'Syncing…' : `Last synced ${lastSynced} · every 15 minutes`}
            </Typography>
            <Button size="small" variant="outlined" startIcon={<Sync />} disabled={syncing} onClick={syncNow} sx={{ whiteSpace: 'nowrap', flexShrink: 0 }}>
              Sync now
            </Button>
          </Stack>
          {syncing && <LinearProgress sx={{ mt: 1 }} />}
        </CardContent>
      </Card>

      <Box>
        <Typography variant="overline" color="text.secondary">
          What stays in sync
        </Typography>
        <Card variant="outlined">
          <List disablePadding>
            {syncs.map(item => (
              <ListItem key={item.title} divider>
                <ListItemIcon>
                  <DirectionIcon direction={item.direction} />
                </ListItemIcon>
                <ListItemText primary={item.title} secondary={item.detail} />
              </ListItem>
            ))}
          </List>
        </Card>
      </Box>

      <Box>
        <Typography variant="overline" color="text.secondary">
          Recent activity
        </Typography>
        <Card variant="outlined">
          <List disablePadding dense>
            {activity.map(item => (
              <ListItem key={`${item.when}-${item.text}`} divider>
                <ListItemText primary={item.text} secondary={item.when} />
              </ListItem>
            ))}
          </List>
        </Card>
      </Box>

      <Typography variant="caption" color="text.secondary">
        Sensitive notes, pastor inbox items and AI drafts stay in Sheaf and are never sent to Planning Center.
      </Typography>
    </Stack>
  )
}
