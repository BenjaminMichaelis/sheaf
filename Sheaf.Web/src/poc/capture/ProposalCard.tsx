import { useState } from 'react'
import {
  Alert,
  Box,
  Button,
  Card,
  CardActions,
  CardContent,
  Chip,
  Collapse,
  FormControlLabel,
  Stack,
  Switch,
  TextField,
  Typography,
} from '@mui/material'
import { AutoAwesome, CheckCircle, ChildCare, FormatQuote, Merge, PersonAdd, Search } from '@mui/icons-material'
import { useSnackbar } from 'notistack'
import { apiClient } from '@/services/apiClient'
import { ProposalKind, ProposalStatus, sourceLabel, type PersonSummary, type Proposal, type ProposedPerson } from './model'

const fields: { key: keyof ProposedPerson; label: string }[] = [
  { key: 'firstName', label: 'First name' },
  { key: 'lastName', label: 'Last name' },
  { key: 'phone', label: 'Phone' },
  { key: 'email', label: 'Email' },
  { key: 'dorm', label: 'Dorm' },
  { key: 'classYear', label: 'Class year' },
  { key: 'howWeMet', label: 'How we met' },
  { key: 'nextStep', label: 'Next step' },
  { key: 'notes', label: 'Notes' },
]

/** One suggestion to approve, edit or reject, showing why it was suggested and the words it came from. */
export default function ProposalCard({ proposal, showOwner = false, onDecided }: { proposal: Proposal; showOwner?: boolean; onDecided: (updated: Proposal) => void }) {
  const { enqueueSnackbar } = useSnackbar()
  const [editing, setEditing] = useState(false)
  const [draft, setDraft] = useState<ProposedPerson>(proposal.person)
  const [busy, setBusy] = useState(false)

  const decided = proposal.status !== ProposalStatus.Pending
  const kind = Number(proposal.kind)

  const decide = async (approve: boolean, asNewPerson = false) => {
    setBusy(true)

    try {
      const { data } = await apiClient.POST('/api/church/poc/proposals/{id}/decision', {
        params: { path: { id: proposal.id } },
        body: { approve, asNewPerson, edited: editing ? draft : null },
      })

      if (data) {
        onDecided(data)
        enqueueSnackbar(approve ? 'Saved' : 'Dismissed', { variant: approve ? 'success' : 'info' })
      }
    } catch (error) {
      enqueueSnackbar(error instanceof Error ? error.message : 'Could not save that decision.', { variant: 'error' })
    } finally {
      setBusy(false)
    }
  }

  const details = editing ? draft : proposal.person
  const shown = fields.filter(field => field.key !== 'firstName' && field.key !== 'lastName' && details[field.key])

  return (
    <Card variant="outlined" sx={{ opacity: decided ? 0.7 : 1 }}>
      <CardContent sx={{ pb: 1 }}>
        <Stack direction="row" spacing={1} sx={{ mb: 1, alignItems: 'center', flexWrap: 'wrap', rowGap: 0.5 }}>
          {proposal.aiGenerated ? (
            <Chip size="small" icon={<AutoAwesome />} label="AI-suggested" color="primary" variant="outlined" />
          ) : (
            <Chip size="small" icon={<Search />} label="Duplicate check" variant="outlined" />
          )}
          {proposal.person.isMinor && <Chip size="small" icon={<ChildCare />} label="Under 18" color="warning" />}
          {showOwner && (
            <Typography variant="caption" color="text.secondary">
              for {proposal.ownerName}
            </Typography>
          )}
        </Stack>

        <Typography variant="subtitle1" sx={{ fontWeight: 600, lineHeight: 1.3 }}>
          {proposal.title}
        </Typography>

        {proposal.sourceExcerpt && (
          <Box sx={{ display: 'flex', gap: 1, mt: 1, p: 1, borderRadius: 1, bgcolor: 'action.hover' }}>
            <FormatQuote fontSize="small" color="action" />
            <Typography variant="body2" color="text.secondary" sx={{ fontStyle: 'italic' }}>
              {proposal.sourceExcerpt}
            </Typography>
          </Box>
        )}

        <Typography variant="caption" color="text.secondary" component="div" sx={{ mt: 1 }}>
          Why suggested: {proposal.reasons.join(' · ')}
        </Typography>

        {(proposal.target || proposal.duplicate) && (
          <Stack spacing={1} sx={{ mt: 1.5 }}>
            {proposal.duplicate && <ExistingPerson label="Just added" person={proposal.duplicate} />}
            {proposal.target && <ExistingPerson label="Already in the church" person={proposal.target} />}
          </Stack>
        )}

        {kind !== ProposalKind.MergeDuplicates && (
          <Box sx={{ mt: 1.5 }}>
            {!editing && (
              <Stack spacing={0.25}>
                <Typography variant="body2" sx={{ fontWeight: 500 }}>
                  {[details.firstName, details.lastName].filter(Boolean).join(' ')}
                </Typography>
                {shown.map(field => (
                  <Typography key={field.key} variant="body2" color="text.secondary">
                    <Box component="span" sx={{ color: 'text.primary' }}>
                      {field.label}:
                    </Box>{' '}
                    {String(details[field.key])}
                  </Typography>
                ))}
              </Stack>
            )}
            <Collapse in={editing}>
              <Stack spacing={1.25} sx={{ pt: 1 }}>
                {fields.map(field => (
                  <TextField
                    key={field.key}
                    size="small"
                    label={field.label}
                    value={draft[field.key] ?? ''}
                    disabled={draft.isMinor && (field.key === 'phone' || field.key === 'email' || field.key === 'nextStep')}
                    onChange={event => setDraft(previous => ({ ...previous, [field.key]: event.target.value || (field.key === 'firstName' ? '' : null) }))}
                    multiline={field.key === 'notes'}
                  />
                ))}
                <FormControlLabel
                  control={<Switch checked={draft.isMinor} onChange={event => setDraft(previous => ({ ...previous, isMinor: event.target.checked }))} />}
                  label="Under 18 (no phone or email stored)"
                />
              </Stack>
            </Collapse>
          </Box>
        )}

        {details.isMinor && !decided && (
          <Alert severity="info" sx={{ mt: 1.5 }} icon={<ChildCare fontSize="inherit" />}>
            Under 18: no phone, email or AI-drafted messages. Reach them through a parent or guardian.
          </Alert>
        )}
      </CardContent>

      {decided ? (
        <CardActions sx={{ px: 2, pb: 1.5 }}>
          <CheckCircle fontSize="small" color={proposal.status === ProposalStatus.Approved ? 'success' : 'disabled'} />
          <Typography variant="body2" color="text.secondary">
            {proposal.status === ProposalStatus.Approved ? 'Approved' : 'Dismissed'}
          </Typography>
        </CardActions>
      ) : (
        <CardActions sx={{ px: 2, pb: 1.5, flexWrap: 'wrap', gap: 1 }}>
          {kind === ProposalKind.NewPerson && (
            <Button variant="contained" startIcon={<PersonAdd />} disabled={busy} onClick={() => void decide(true)}>
              Add
            </Button>
          )}
          {kind === ProposalKind.MatchExisting && (
            <>
              <Button variant="contained" disabled={busy} onClick={() => void decide(true)}>
                Same person, update
              </Button>
              <Button variant="outlined" disabled={busy} onClick={() => void decide(true, true)}>
                Someone new
              </Button>
            </>
          )}
          {kind === ProposalKind.MergeDuplicates && (
            <Button variant="contained" startIcon={<Merge />} disabled={busy} onClick={() => void decide(true)}>
              Merge
            </Button>
          )}
          {kind !== ProposalKind.MergeDuplicates && (
            <Button disabled={busy} onClick={() => setEditing(value => !value)}>
              {editing ? 'Done editing' : 'Edit'}
            </Button>
          )}
          <Button color="inherit" disabled={busy} onClick={() => void decide(false)} sx={{ ml: 'auto' }}>
            {kind === ProposalKind.MergeDuplicates ? 'Keep both' : 'Dismiss'}
          </Button>
        </CardActions>
      )}
    </Card>
  )
}

function ExistingPerson({ label, person }: { label: string; person: PersonSummary }) {
  const details = [sourceLabel(person.source), person.classYear, person.dorm, person.howWeMet, person.addedBy && `added by ${person.addedBy}`].filter(Boolean)

  return (
    <Box sx={{ p: 1, borderRadius: 1, border: 1, borderColor: 'divider' }}>
      <Typography variant="caption" color="text.secondary">
        {label}
      </Typography>
      <Typography variant="body2" sx={{ fontWeight: 500 }}>
        {person.name}
      </Typography>
      <Typography variant="caption" color="text.secondary">
        {details.join(' · ')}
      </Typography>
    </Box>
  )
}
