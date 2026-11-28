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
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  IconButton,
  List,
  ListItem,
  ListItemIcon,
  ListItemText,
  Skeleton,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import {
  ArrowBack,
  AutoAwesome,
  Block,
  Call,
  ChildCare,
  ContactPhone,
  Coffee,
  Edit,
  EmojiPeople,
  Handshake,
  Sms,
} from '@mui/icons-material'
import { useSnackbar } from 'notistack'
import { apiClient } from '@/services/apiClient'
import DraftTextDialog from './DraftTextDialog'
import { nudgeColor, timeAgo, toVCard, touchpointLabel, TouchpointKind, type PersonDetail, type Touchpoint } from './model'

const quickLogs = [
  { kind: TouchpointKind.Texted, label: 'Texted', icon: <Sms fontSize="small" /> },
  { kind: TouchpointKind.Called, label: 'Called', icon: <Call fontSize="small" /> },
  { kind: TouchpointKind.Coffee, label: 'Coffee', icon: <Coffee fontSize="small" /> },
  { kind: TouchpointKind.SawThem, label: 'Saw them', icon: <EmojiPeople fontSize="small" /> },
]

const touchpointIcons = [<Handshake key="met" />, <Sms key="texted" />, <Call key="called" />, <Coffee key="coffee" />, <EmojiPeople key="saw" />, <Edit key="other" />]

const sourceLabels = ['Added by a leader', 'From Planning Center', 'Church member']

/** One person: what you know, why to reach out now, a drafted text, and every touchpoint so far. */
export default function PersonPage() {
  const { personId = '' } = useParams()
  const navigate = useNavigate()
  const { enqueueSnackbar } = useSnackbar()
  const [detail, setDetail] = useState<PersonDetail | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [drafting, setDrafting] = useState(false)
  const [confirmBlock, setConfirmBlock] = useState(false)
  const [editingStep, setEditingStep] = useState(false)
  const [step, setStep] = useState('')

  useEffect(() => {
    let cancelled = false
    apiClient
      .GET('/api/church/poc/people/{id}', { params: { path: { id: personId } } })
      .then(({ data }) => {
        if (!cancelled && data) setDetail(data)
      })
      .catch(() => {
        if (!cancelled) setError('Could not load this person.')
      })

    return () => {
      cancelled = true
    }
  }, [personId])

  if (error) return <Alert severity="error">{error}</Alert>

  if (!detail) {
    return (
      <Stack spacing={2}>
        <Skeleton variant="text" width="60%" height={40} />
        <Skeleton variant="rounded" height={220} />
      </Stack>
    )
  }

  const { person } = detail

  const addTouchpoint = (touchpoint: Touchpoint) => {
    // A touchpoint answers the nudge; the server works out the next one on the next load.
    setDetail(previous => previous && { ...previous, touchpoints: [touchpoint, ...previous.touchpoints], person: { ...previous.person, lastTouchpoint: touchpoint, nudge: null } })
  }

  const log = async (kind: number) => {
    const { data } = await apiClient.POST('/api/church/poc/people/{id}/touchpoints', { params: { path: { id: person.id } }, body: { kind, note: null, aiDrafted: false } })

    if (data) {
      addTouchpoint(data)
      enqueueSnackbar(`Logged: ${touchpointLabel(kind)}`, { variant: 'success' })
    }
  }

  const setDoNotContact = async (value: boolean) => {
    setConfirmBlock(false)

    const { data } = await apiClient.PUT('/api/church/poc/people/{id}/do-not-contact', { params: { path: { id: person.id } }, body: { doNotContact: value } })

    if (data) {
      setDetail(previous => previous && { ...previous, person: data, draftBlockedReason: value ? "Marked Don't contact." : person.isMinor ? previous.draftBlockedReason : null })
    }
  }

  const saveStep = async () => {
    const { data } = await apiClient.PUT('/api/church/poc/people/{id}/next-step', { params: { path: { id: person.id } }, body: { nextStep: step || null } })

    if (data) setDetail(previous => previous && { ...previous, person: data })

    setEditingStep(false)
  }

  const downloadContact = () => {
    const url = URL.createObjectURL(new Blob([toVCard(detail)], { type: 'text/vcard' }))
    const link = document.createElement('a')

    link.href = url
    link.download = `${person.name}.vcf`
    link.click()
    URL.revokeObjectURL(url)
  }

  const facts = [
    person.phone,
    detail.email,
    [person.classYear, person.dorm].filter(Boolean).join(' · '),
    person.howWeMet && `Met: ${person.howWeMet}`,
    person.groupName && `${person.groupName} · came to ${detail.meetingsAttended} of ${detail.meetingsHeld} recent meetings`,
    person.source !== null && sourceLabels[Number(person.source)],
  ].filter(Boolean)

  return (
    <Stack spacing={2}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
        <IconButton edge="start" onClick={() => navigate('/app/people')} aria-label="Back to my people">
          <ArrowBack />
        </IconButton>
        <Avatar sx={{ width: 44, height: 44 }}>{person.name.charAt(0)}</Avatar>
        <Box sx={{ flexGrow: 1, minWidth: 0 }}>
          <Typography variant="h6" sx={{ fontWeight: 700, lineHeight: 1.2 }}>
            {person.name}
          </Typography>
          <Stack direction="row" spacing={0.5} sx={{ mt: 0.5 }}>
            {person.isMinor && <Chip size="small" icon={<ChildCare />} label="Under 18" color="warning" />}
            {person.doNotContact && <Chip size="small" icon={<Block />} label="Don't contact" color="error" />}
          </Stack>
        </Box>
      </Box>

      {person.nudge && (
        <Alert severity={nudgeColor(person.nudge.urgency)} sx={{ alignItems: 'center' }}>
          {person.nudge.label}
        </Alert>
      )}

      <Card variant="outlined">
        <CardContent>
          <Typography variant="overline" color="text.secondary">
            Next step
          </Typography>
          {editingStep ? (
            <Stack direction="row" spacing={1}>
              <TextField size="small" fullWidth autoFocus value={step} onChange={event => setStep(event.target.value)} placeholder="Invite to Tuesday group" />
              <Button onClick={() => void saveStep()}>Save</Button>
            </Stack>
          ) : (
            <Box sx={{ display: 'flex', alignItems: 'center' }}>
              <Typography sx={{ flexGrow: 1 }} color={person.nextStep ? 'text.primary' : 'text.secondary'}>
                {person.nextStep ?? 'Nothing planned yet'}
              </Typography>
              <IconButton
                size="small"
                aria-label="Edit next step"
                onClick={() => {
                  setStep(person.nextStep ?? '')
                  setEditingStep(true)
                }}
              >
                <Edit fontSize="small" />
              </IconButton>
            </Box>
          )}
        </CardContent>
      </Card>

      {detail.draftBlockedReason ? (
        <Alert severity="info" icon={person.isMinor ? <ChildCare /> : <Block />}>
          No AI drafts. {detail.draftBlockedReason}
        </Alert>
      ) : (
        <Button variant="contained" size="large" startIcon={<AutoAwesome />} onClick={() => setDrafting(true)} sx={{ py: 1.25, borderRadius: 3 }}>
          Draft a text
        </Button>
      )}

      {detail.guardian && (
        <Card variant="outlined">
          <CardContent>
            <Typography variant="overline" color="text.secondary">
              Parent or guardian
            </Typography>
            <Typography sx={{ fontWeight: 500 }}>{detail.guardian.name}</Typography>
            {detail.guardian.phone && (
              <Button size="small" startIcon={<Call />} href={`tel:${detail.guardian.phone.replace(/[^\d+]/g, '')}`} sx={{ mt: 0.5, ml: -0.5 }}>
                {detail.guardian.phone}
              </Button>
            )}
          </CardContent>
        </Card>
      )}

      {!person.doNotContact && (
        <Box>
          <Typography variant="overline" color="text.secondary">
            Log a touchpoint
          </Typography>
          <Stack direction="row" spacing={1} sx={{ flexWrap: 'wrap', rowGap: 1 }}>
            {quickLogs.map(item => (
              <Chip key={item.kind} icon={item.icon} label={item.label} variant="outlined" onClick={() => void log(item.kind)} />
            ))}
          </Stack>
        </Box>
      )}

      <Card variant="outlined">
        <CardContent>
          <Typography variant="overline" color="text.secondary">
            About
          </Typography>
          <Stack spacing={0.5}>
            {facts.map(fact => (
              <Typography key={String(fact)} variant="body2">
                {fact}
              </Typography>
            ))}
            {detail.notes && (
              <Typography variant="body2" color="text.secondary" sx={{ whiteSpace: 'pre-wrap' }}>
                {detail.notes}
              </Typography>
            )}
          </Stack>
        </CardContent>
      </Card>

      <Box>
        <Typography variant="overline" color="text.secondary">
          Touchpoints
        </Typography>
        <Card variant="outlined">
          <List dense disablePadding>
            {detail.touchpoints.length === 0 && (
              <ListItem>
                <ListItemText secondary="None yet." />
              </ListItem>
            )}
            {detail.touchpoints.map(touchpoint => (
              <ListItem key={touchpoint.id} divider alignItems="flex-start">
                <ListItemIcon sx={{ minWidth: 36, mt: 0.5 }}>{touchpointIcons[Number(touchpoint.kind)]}</ListItemIcon>
                <ListItemText
                  primary={
                    <Box component="span" sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                      {touchpointLabel(touchpoint.kind)} · {timeAgo(touchpoint.at)}
                      {touchpoint.aiDrafted && <Chip component="span" size="small" icon={<AutoAwesome />} label="AI-drafted" variant="outlined" />}
                    </Box>
                  }
                  secondary={[touchpoint.note, `by ${touchpoint.byName}`].filter(Boolean).join(' · ')}
                />
              </ListItem>
            ))}
          </List>
        </Card>
      </Box>

      <Stack direction="row" spacing={1} sx={{ flexWrap: 'wrap', rowGap: 1 }}>
        <Button startIcon={<ContactPhone />} onClick={downloadContact}>
          Add to my phone
        </Button>
        <Box sx={{ flexGrow: 1 }} />
        {person.doNotContact ? (
          <Button color="inherit" onClick={() => void setDoNotContact(false)}>
            Clear Don&apos;t contact
          </Button>
        ) : (
          <Button color="error" startIcon={<Block />} onClick={() => setConfirmBlock(true)}>
            Don&apos;t contact
          </Button>
        )}
      </Stack>

      {drafting && (
        <DraftTextDialog
          person={person}
          onClose={() => setDrafting(false)}
          onSent={touchpoint => {
            addTouchpoint(touchpoint)
            setDrafting(false)
          }}
        />
      )}

      <Dialog open={confirmBlock} onClose={() => setConfirmBlock(false)}>
        <DialogTitle>Mark {person.name.split(' ')[0]} Don&apos;t contact?</DialogTitle>
        <DialogContent>
          <DialogContentText>
            Every leader stops getting nudges and AI drafts for them. Use this when someone asks not to be contacted. Staff can see who set it.
          </DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setConfirmBlock(false)}>Cancel</Button>
          <Button color="error" variant="contained" onClick={() => void setDoNotContact(true)}>
            Don&apos;t contact
          </Button>
        </DialogActions>
      </Dialog>
    </Stack>
  )
}
