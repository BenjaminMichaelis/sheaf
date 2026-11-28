import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  Alert,
  Avatar,
  Box,
  Button,
  Card,
  CardActionArea,
  CardContent,
  List,
  ListItemAvatar,
  ListItemButton,
  ListItemText,
  Skeleton,
  Stack,
  Typography,
} from '@mui/material'
import { AddCircleOutlined, ChevronRight, FactCheck, Groups } from '@mui/icons-material'
import { apiClient } from '@/services/apiClient'
import { useAuth } from '@/contexts/AuthContext'
import type { components } from '@/api/schema'
import { displayName } from '../roles'
import { nudgeColor, type MyPerson } from '../people/model'

type Group = components['schemas']['GroupResponse']

const FOLLOW_UPS_SHOWN = 4

export default function LeaderHome() {
  const navigate = useNavigate()
  const { user } = useAuth()
  const [groups, setGroups] = useState<Group[] | null>(null)
  const [people, setPeople] = useState<MyPerson[] | null>(null)
  const [pendingReview, setPendingReview] = useState(0)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false

    Promise.all([apiClient.GET('/api/church/groups'), apiClient.GET('/api/church/poc/people'), apiClient.GET('/api/church/poc/proposals')])
      .then(([groupsResult, peopleResult, proposalsResult]) => {
        if (cancelled) return

        setGroups(groupsResult.data?.items ?? [])
        setPeople(peopleResult.data ?? [])
        setPendingReview(proposalsResult.data?.length ?? 0)
      })
      .catch(() => {
        if (!cancelled) setError('Could not load your home page.')
      })

    return () => {
      cancelled = true
    }
  }, [])

  const followUps = people?.filter(person => person.nudge !== null) ?? []

  return (
    <Stack spacing={2.5}>
      <Box>
        <Typography variant="h5" sx={{ fontWeight: 700 }}>
          Hi, {displayName(user)}
        </Typography>
        <Typography color="text.secondary">Who did you meet today?</Typography>
      </Box>

      <Button
        variant="contained"
        size="large"
        startIcon={<AddCircleOutlined />}
        onClick={() => navigate('/app/capture')}
        sx={{ py: 1.5, borderRadius: 3 }}
      >
        Add someone you met
      </Button>

      {error && <Alert severity="error">{error}</Alert>}

      {pendingReview > 0 && (
        <Card variant="outlined">
          <CardActionArea onClick={() => navigate('/app/review')}>
            <CardContent sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
              <FactCheck color="primary" />
              <Typography sx={{ flexGrow: 1 }}>
                {pendingReview} {pendingReview === 1 ? 'suggestion' : 'suggestions'} to review
              </Typography>
              <ChevronRight color="action" />
            </CardContent>
          </CardActionArea>
        </Card>
      )}

      <Box>
        <Box sx={{ display: 'flex', alignItems: 'center' }}>
          <Typography variant="overline" color="text.secondary" sx={{ flexGrow: 1 }}>
            Follow up today
          </Typography>
          {followUps.length > FOLLOW_UPS_SHOWN && (
            <Button size="small" onClick={() => navigate('/app/people')}>
              All {followUps.length}
            </Button>
          )}
        </Box>
        {people === null && !error && <Skeleton variant="rounded" height={160} />}
        {people && followUps.length === 0 && (
          <Typography color="text.secondary" variant="body2">
            You&apos;re all caught up.
          </Typography>
        )}
        {followUps.length > 0 && (
          <Card variant="outlined">
            <List disablePadding>
              {followUps.slice(0, FOLLOW_UPS_SHOWN).map(person => (
                <ListItemButton key={person.id} divider onClick={() => navigate(`/app/people/${person.id}`)}>
                  <ListItemAvatar>
                    <Avatar sx={{ bgcolor: `${nudgeColor(person.nudge?.urgency)}.main` }}>{person.name.charAt(0)}</Avatar>
                  </ListItemAvatar>
                  <ListItemText primary={person.name} secondary={[person.nudge?.label, person.nextStep && `Next: ${person.nextStep}`].filter(Boolean).join(' · ')} />
                  <ChevronRight color="action" />
                </ListItemButton>
              ))}
            </List>
          </Card>
        )}
      </Box>

      <Box>
        <Typography variant="overline" color="text.secondary">
          Your groups
        </Typography>
        {groups === null && !error && <Skeleton variant="rounded" height={64} />}
        {groups?.length === 0 && (
          <Typography color="text.secondary" variant="body2">
            You are not leading a group right now.
          </Typography>
        )}
        <Stack spacing={1}>
          {groups?.map(group => (
            <Card key={group.id} variant="outlined">
              <CardActionArea onClick={() => navigate(`/app/groups/${group.id}`)}>
                <CardContent sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
                  <Groups color="primary" />
                  <Typography sx={{ flexGrow: 1, fontWeight: 500 }}>{group.name}</Typography>
                  <ChevronRight color="action" />
                </CardContent>
              </CardActionArea>
            </Card>
          ))}
        </Stack>
      </Box>
    </Stack>
  )
}
