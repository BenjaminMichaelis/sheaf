import { useState } from 'react'
import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import { AutoAwesome, ContentCopy, Refresh, Sms } from '@mui/icons-material'
import { useSnackbar } from 'notistack'
import { apiClient } from '@/services/apiClient'
import { TouchpointKind, type MyPerson, type Touchpoint } from './model'

const intents = ['Invite to group', 'Check in', 'Coffee this week']

/**
 * AI drafts, the leader edits and sends from their own phone. Sending opens the Messages app (sms: link) or copies
 * the text; either way the touchpoint is logged as AI-drafted.
 */
export default function DraftTextDialog({ person, onClose, onSent }: { person: MyPerson; onClose: () => void; onSent: (touchpoint: Touchpoint) => void }) {
  const { enqueueSnackbar } = useSnackbar()
  const [text, setText] = useState('')
  const [aiLive, setAiLive] = useState(false)
  const [loading, setLoading] = useState(false)
  const [drafted, setDrafted] = useState(false)

  const draft = async (intent: string | null) => {
    setLoading(true)

    try {
      const { data } = await apiClient.POST('/api/church/poc/people/{id}/draft', { params: { path: { id: person.id } }, body: { intent } })

      if (data) {
        setText(data.text)
        setAiLive(data.aiLive)
        setDrafted(true)
      }
    } catch (error) {
      enqueueSnackbar(error instanceof Error ? error.message : 'Could not draft a text.', { variant: 'error' })
    } finally {
      setLoading(false)
    }
  }

  const logSent = async () => {
    const { data } = await apiClient.POST('/api/church/poc/people/{id}/touchpoints', {
      params: { path: { id: person.id } },
      body: { kind: TouchpointKind.Texted, note: text, aiDrafted: true },
    })

    if (data) onSent(data)
  }

  const openMessages = async () => {
    if (person.phone) window.open(`sms:${person.phone.replace(/[^\d+]/g, '')}?&body=${encodeURIComponent(text)}`, '_self')

    await logSent()
    enqueueSnackbar('Logged as texted', { variant: 'success' })
  }

  const copy = async () => {
    await navigator.clipboard.writeText(text).catch(() => undefined)
    await logSent()
    enqueueSnackbar('Copied. Logged as texted.', { variant: 'success' })
  }

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>Text {person.name.split(' ')[0]}</DialogTitle>
      <DialogContent>
        {!drafted ? (
          <Stack spacing={1.5}>
            <Typography variant="body2" color="text.secondary">
              AI writes a first draft in your voice. You edit it and send it from your own phone.
            </Typography>
            <Stack direction="row" spacing={1} sx={{ flexWrap: 'wrap', rowGap: 1 }}>
              <Chip icon={<AutoAwesome />} label="Draft for me" color="primary" onClick={() => void draft(null)} disabled={loading} />
              {intents.map(intent => (
                <Chip key={intent} label={intent} variant="outlined" onClick={() => void draft(intent)} disabled={loading} />
              ))}
            </Stack>
            {loading && (
              <Box sx={{ display: 'flex', justifyContent: 'center', py: 2 }}>
                <CircularProgress size={28} />
              </Box>
            )}
          </Stack>
        ) : (
          <Stack spacing={1.5} sx={{ pt: 0.5 }}>
            <Chip size="small" icon={<AutoAwesome />} label={aiLive ? 'AI-drafted by Claude' : 'AI-drafted (canned)'} variant="outlined" sx={{ alignSelf: 'flex-start' }} />
            <TextField multiline minRows={4} value={text} onChange={event => setText(event.target.value)} disabled={loading} />
            {!person.phone && <Alert severity="info">No phone number saved. Copy the text instead.</Alert>}
          </Stack>
        )}
      </DialogContent>
      <DialogActions sx={{ flexWrap: 'wrap', gap: 1, px: 3, pb: 2 }}>
        {drafted && (
          <>
            <Button startIcon={<Refresh />} disabled={loading} onClick={() => void draft(null)}>
              Redo
            </Button>
            <Box sx={{ flexGrow: 1 }} />
            <Button startIcon={<ContentCopy />} disabled={!text.trim()} onClick={() => void copy()}>
              Copy
            </Button>
            {person.phone && (
              <Button variant="contained" startIcon={<Sms />} disabled={!text.trim()} onClick={() => void openMessages()}>
                Open Messages
              </Button>
            )}
          </>
        )}
        {!drafted && <Button onClick={onClose}>Cancel</Button>}
      </DialogActions>
    </Dialog>
  )
}
