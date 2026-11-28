import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Alert, Card, CardActionArea, CardContent, Skeleton, Stack, Typography } from '@mui/material'
import { ChevronRight, Groups } from '@mui/icons-material'
import { apiClient } from '@/services/apiClient'
import type { Group } from './groupData'

/** The groups this person can see: their own as a Leader, every group as Staff. */
export default function GroupsList() {
  const navigate = useNavigate()
  const [groups, setGroups] = useState<Group[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    apiClient
      .GET('/api/church/groups')
      .then(({ data }) => {
        if (!cancelled) setGroups((data?.items ?? []).filter(group => group.endsAt === null))
      })
      .catch(() => {
        if (!cancelled) setError('Could not load groups.')
      })

    return () => {
      cancelled = true
    }
  }, [])

  return (
    <Stack spacing={2}>
      <Typography variant="h5" sx={{ fontWeight: 700 }}>
        Groups
      </Typography>
      {error && <Alert severity="error">{error}</Alert>}
      {groups === null && !error && <Skeleton variant="rounded" height={64} />}
      {groups?.length === 0 && (
        <Typography color="text.secondary" variant="body2">
          You are not leading a group right now.
        </Typography>
      )}
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
  )
}
