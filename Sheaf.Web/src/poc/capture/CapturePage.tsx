import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  FormControlLabel,
  LinearProgress,
  Stack,
  Switch,
  Tab,
  Tabs,
  TextField,
  Typography,
} from '@mui/material'
import { AutoAwesome, CheckCircle, Mic, Shield, Stop } from '@mui/icons-material'
import { useSnackbar } from 'notistack'
import { apiClient } from '@/services/apiClient'
import { usePocStatus } from '../usePocStatus'
import { CaptureKind, SAMPLE_NOTES, type Capture, type Proposal } from './model'
import ProposalCard from './ProposalCard'
import { useDictation } from './useDictation'

const emptyQuickAdd = { firstName: '', lastName: '', phone: '', howWeMet: '', isMinor: false, guardianName: '', guardianPhone: '' }

/** Add someone you met: a typed or spoken note that AI reads, or a quick form. */
export default function CapturePage() {
  const navigate = useNavigate()
  const { enqueueSnackbar } = useSnackbar()
  const status = usePocStatus()
  const [tab, setTab] = useState<'note' | 'quick'>('note')
  const [text, setText] = useState('')
  const [spoken, setSpoken] = useState(false)
  const [quick, setQuick] = useState(emptyQuickAdd)
  const [working, setWorking] = useState(false)
  const [result, setResult] = useState<Capture | null>(null)
  const [added, setAdded] = useState<{ name: string; personId: string | null; isMinor: boolean } | null>(null)

  const dictation = useDictation(phrase => {
    setSpoken(true)
    setText(previous => (previous ? `${previous} ${phrase}` : phrase))
  })

  const submitNote = async () => {
    setWorking(true)

    try {
      const { data } = await apiClient.POST('/api/church/poc/captures/note', {
        body: { text, kind: spoken ? CaptureKind.Voice : CaptureKind.Note },
      })

      if (data) {
        setResult(data)
        setText('')
        setSpoken(false)
      }
    } catch (error) {
      enqueueSnackbar(error instanceof Error ? error.message : 'Could not read that note.', { variant: 'error' })
    } finally {
      setWorking(false)
    }
  }

  const submitQuickAdd = async () => {
    setWorking(true)

    try {
      const { data } = await apiClient.POST('/api/church/poc/captures/quick-add', {
        body: {
          firstName: quick.firstName,
          lastName: quick.lastName || null,
          phone: quick.isMinor ? null : quick.phone || null,
          howWeMet: quick.howWeMet || null,
          isMinor: quick.isMinor,
          guardianName: quick.isMinor ? quick.guardianName || null : null,
          guardianPhone: quick.isMinor ? quick.guardianPhone || null : null,
        },
      })

      if (data) {
        setAdded({ name: [quick.firstName, quick.lastName].filter(Boolean).join(' '), personId: data.personId, isMinor: quick.isMinor })
        setQuick(emptyQuickAdd)
        setResult(data.proposals.length > 0 ? data : null)
      }
    } catch (error) {
      enqueueSnackbar(error instanceof Error ? error.message : 'Could not add them.', { variant: 'error' })
    } finally {
      setWorking(false)
    }
  }

  const updateProposal = (updated: Proposal) => {
    setResult(previous => previous && { ...previous, proposals: previous.proposals.map(p => (p.id === updated.id ? updated : p)) })
  }

  if (added && !result) {
    return (
      <Stack spacing={2}>
        <Alert severity="success" icon={<CheckCircle />}>
          <Typography sx={{ fontWeight: 600 }}>{added.name} is in My people</Typography>
          <Typography variant="body2">
            {added.isMinor
              ? 'Under 18: no phone stored. Reach them through the parent or guardian you added.'
              : `You'll get a reminder to reach out within 48 hours.`}
          </Typography>
        </Alert>
        <Stack direction="row" spacing={1}>
          {added.personId && (
            <Button variant="contained" onClick={() => navigate(`/app/people/${added.personId}`)}>
              Open {added.name.split(' ')[0]}
            </Button>
          )}
          <Button onClick={() => setAdded(null)}>Add another</Button>
        </Stack>
      </Stack>
    )
  }

  if (result) {
    const pending = result.proposals.filter(p => p.status === 0).length

    return (
      <Stack spacing={2}>
        <Box>
          <Typography variant="h5" sx={{ fontWeight: 700 }}>
            {result.kind === CaptureKind.QuickAdd ? 'Quick check' : 'Here is what I found'}
          </Typography>
          {result.summary && (
            <Typography color="text.secondary" sx={{ display: 'flex', gap: 0.75, alignItems: 'flex-start', mt: 0.5 }}>
              {result.kind !== CaptureKind.QuickAdd && <AutoAwesome fontSize="small" color="primary" sx={{ mt: 0.25 }} />}
              {result.summary}
            </Typography>
          )}
        </Box>

        {result.redacted && (
          <Alert severity="info" icon={<Shield />}>
            A sentence about {result.redactedCategories.join(' and ')} was kept from AI and sent privately to your campus pastor.
          </Alert>
        )}

        {result.kind !== CaptureKind.QuickAdd && !result.aiLive && (
          <Alert severity="warning">Read by the built-in fallback, not Claude. Set an Anthropic API key to use Claude.</Alert>
        )}

        {result.proposals.length === 0 && <Typography color="text.secondary">Nothing to review.</Typography>}

        {result.proposals.map(proposal => (
          <ProposalCard key={proposal.id} proposal={proposal} onDecided={updateProposal} />
        ))}

        <Stack direction="row" spacing={1}>
          <Button
            variant="contained"
            onClick={() => {
              setResult(null)
              setAdded(null)
            }}
          >
            Add another
          </Button>
          <Button onClick={() => navigate(pending > 0 ? '/app/review' : '/app')}>{pending > 0 ? 'Review later' : 'Done'}</Button>
        </Stack>
      </Stack>
    )
  }

  return (
    <Stack spacing={2}>
      <Typography variant="h5" sx={{ fontWeight: 700 }}>
        Add someone you met
      </Typography>

      <Tabs value={tab} onChange={(_, value: 'note' | 'quick') => setTab(value)} variant="fullWidth">
        <Tab value="note" label="Tell me about it" />
        <Tab value="quick" label="Quick add" />
      </Tabs>

      {tab === 'note' ? (
        <Stack spacing={1.5}>
          <Typography variant="body2" color="text.secondary">
            Type or talk like you would text a friend. {status?.aiLive ? 'Claude' : 'AI'} pulls out names and details, and nothing is saved until you
            approve it.
          </Typography>
          <TextField
            multiline
            minRows={5}
            placeholder="Met Marcus at the BBQ, freshman in Smith Hall…"
            value={dictation.interim ? `${text} ${dictation.interim}`.trim() : text}
            onChange={event => setText(event.target.value)}
            disabled={working}
          />
          <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
            {dictation.supported && (
              <Button
                variant={dictation.listening ? 'contained' : 'outlined'}
                color={dictation.listening ? 'error' : 'primary'}
                startIcon={dictation.listening ? <Stop /> : <Mic />}
                onClick={dictation.listening ? dictation.stop : dictation.start}
                disabled={working}
              >
                {dictation.listening ? 'Stop' : 'Speak'}
              </Button>
            )}
            <Box sx={{ flexGrow: 1 }} />
            <Button variant="contained" startIcon={<AutoAwesome />} disabled={working || !text.trim()} onClick={() => void submitNote()}>
              Read my note
            </Button>
          </Stack>
          {working && (
            <Card variant="outlined">
              <CardContent>
                <Typography variant="body2" sx={{ mb: 1 }}>
                  {status?.aiLive ? 'Claude is reading your note…' : 'Reading your note…'}
                </Typography>
                <LinearProgress />
              </CardContent>
            </Card>
          )}
          {status?.demoTools && !working && (
            <Box>
              <Typography variant="overline" color="text.secondary">
                Try a sample
              </Typography>
              <Stack direction="row" spacing={1} sx={{ flexWrap: 'wrap', rowGap: 1 }}>
                {SAMPLE_NOTES.map(sample => (
                  <Chip key={sample.label} label={sample.label} variant="outlined" onClick={() => setText(sample.text)} />
                ))}
              </Stack>
            </Box>
          )}
        </Stack>
      ) : (
        <Stack spacing={1.5}>
          <Stack direction="row" spacing={1}>
            <TextField label="First name" required fullWidth value={quick.firstName} onChange={event => setQuick({ ...quick, firstName: event.target.value })} />
            <TextField label="Last name" fullWidth value={quick.lastName} onChange={event => setQuick({ ...quick, lastName: event.target.value })} />
          </Stack>
          <FormControlLabel
            control={<Switch checked={quick.isMinor} onChange={event => setQuick({ ...quick, isMinor: event.target.checked })} />}
            label="Under 18"
          />
          {quick.isMinor ? (
            <>
              <Alert severity="info">We don&apos;t store a minor&apos;s phone. Add a parent or guardian to contact instead.</Alert>
              <TextField label="Parent or guardian name" value={quick.guardianName} onChange={event => setQuick({ ...quick, guardianName: event.target.value })} />
              <TextField
                label="Parent or guardian phone"
                type="tel"
                value={quick.guardianPhone}
                onChange={event => setQuick({ ...quick, guardianPhone: event.target.value })}
              />
            </>
          ) : (
            <TextField label="Phone" type="tel" value={quick.phone} onChange={event => setQuick({ ...quick, phone: event.target.value })} />
          )}
          <TextField label="How you met" placeholder="Welcome week BBQ" value={quick.howWeMet} onChange={event => setQuick({ ...quick, howWeMet: event.target.value })} />
          <Button variant="contained" size="large" disabled={working || !quick.firstName.trim()} onClick={() => void submitQuickAdd()}>
            Add
          </Button>
        </Stack>
      )}
    </Stack>
  )
}
