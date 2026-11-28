import { useEffect, useState } from 'react'
import { Alert, Box, Button, Card, CardContent, Chip, List, ListItem, ListItemText, Skeleton, Stack, Typography } from '@mui/material'
import { AutoAwesome, EditNote, Lock } from '@mui/icons-material'
import { apiClient } from '@/services/apiClient'
import type { components } from '@/api/schema'

type SensitiveNote = components['schemas']['SensitiveNoteResponse']

type AuditEntry = components['schemas']['AuditEntryResponse']

function formatWhen(iso: string) {
  return new Date(iso).toLocaleString(undefined, { weekday: 'short', month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' })
}

/** Notes kept from AI because they mention something sensitive. Staff only. */
export function PastorInbox() {
  const [notes, setNotes] = useState<SensitiveNote[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    apiClient
      .GET('/api/church/poc/inbox')
      .then(({ data }) => {
        if (!cancelled) setNotes(data ?? [])
      })
      .catch(() => {
        if (!cancelled) setError('Could not load the inbox.')
      })

    return () => {
      cancelled = true
    }
  }, [])

  const markReviewed = async (id: string) => {
    await apiClient.POST('/api/church/poc/inbox/{id}/reviewed', { params: { path: { id } } })
    setNotes(previous => previous?.map(n => (n.id === id ? { ...n, reviewed: true } : n)) ?? null)
  }

  return (
    <Stack spacing={2}>
      <Box>
        <Typography variant="h5" sx={{ fontWeight: 700 }}>
          Pastor inbox
        </Typography>
        <Typography color="text.secondary" sx={{ display: 'flex', gap: 0.75, alignItems: 'center' }}>
          <Lock fontSize="small" /> Only staff see these. AI never read them.
        </Typography>
      </Box>
      {error && <Alert severity="error">{error}</Alert>}
      {notes === null && !error && <Skeleton variant="rounded" height={140} />}
      {notes?.length === 0 && <Typography color="text.secondary">Nothing here.</Typography>}
      {notes?.map(note => (
        <Card key={note.id} variant="outlined" sx={{ opacity: note.reviewed ? 0.6 : 1 }}>
          <CardContent>
            <Stack direction="row" spacing={1} sx={{ mb: 1, flexWrap: 'wrap', rowGap: 0.5 }}>
              {note.categories.split(', ').map(category => (
                <Chip key={category} size="small" label={category} color="warning" variant="outlined" />
              ))}
            </Stack>
            <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap' }}>
              {note.originalText}
            </Typography>
            <Box sx={{ display: 'flex', alignItems: 'center', mt: 1.5 }}>
              <Typography variant="caption" color="text.secondary" sx={{ flexGrow: 1 }}>
                From {note.leaderName} · {formatWhen(note.createdAt)}
              </Typography>
              {note.reviewed ? (
                <Typography variant="caption" color="text.secondary">
                  Reviewed
                </Typography>
              ) : (
                <Button size="small" onClick={() => void markReviewed(note.id)}>
                  Mark reviewed
                </Button>
              )}
            </Box>
          </CardContent>
        </Card>
      ))}
    </Stack>
  )
}

/** What AI proposed and what people did about it, newest first. Staff only. */
export function AuditLog() {
  const [entries, setEntries] = useState<AuditEntry[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    apiClient
      .GET('/api/church/poc/audit')
      .then(({ data }) => {
        if (!cancelled) setEntries(data ?? [])
      })
      .catch(() => {
        if (!cancelled) setError('Could not load the audit log.')
      })

    return () => {
      cancelled = true
    }
  }, [])

  return (
    <Stack spacing={2}>
      <Box>
        <Typography variant="h5" sx={{ fontWeight: 700 }}>
          Audit log
        </Typography>
        <Typography color="text.secondary">What AI suggested, and who approved what.</Typography>
      </Box>
      {error && <Alert severity="error">{error}</Alert>}
      {entries === null && !error && <Skeleton variant="rounded" height={240} />}
      {entries && (
        <Card variant="outlined">
          <List dense disablePadding>
            {entries.map(entry => (
              <ListItem key={entry.id} divider alignItems="flex-start">
                <ListItemText
                  primary={entry.summary}
                  secondary={
                    <Box component="span" sx={{ display: 'flex', gap: 1, alignItems: 'center', flexWrap: 'wrap', mt: 0.5 }}>
                      <span>
                        {entry.actorName} · {formatWhen(entry.at)}
                      </span>
                      {entry.aiInvolved && <Chip component="span" size="small" icon={<AutoAwesome />} label="AI" variant="outlined" />}
                      {entry.humanEdited && <Chip component="span" size="small" icon={<EditNote />} label="Edited by a person" variant="outlined" />}
                    </Box>
                  }
                />
              </ListItem>
            ))}
          </List>
        </Card>
      )}
    </Stack>
  )
}
