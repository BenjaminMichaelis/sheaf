import { useEffect, useState } from 'react'
import { Alert, Skeleton, Stack, Typography } from '@mui/material'
import { apiClient } from '@/services/apiClient'
import type { Proposal } from './model'
import ProposalCard from './ProposalCard'

/** Suggestions waiting for a decision: the leader's own, or every leader's in the staff area. */
export default function ReviewPage({ all = false }: { all?: boolean }) {
  const [proposals, setProposals] = useState<Proposal[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    apiClient
      .GET('/api/church/poc/proposals', { params: { query: { all } } })
      .then(({ data }) => {
        if (!cancelled) setProposals(data ?? [])
      })
      .catch(() => {
        if (!cancelled) setError('Could not load suggestions.')
      })

    return () => {
      cancelled = true
    }
  }, [all])

  const update = (updated: Proposal) => setProposals(previous => previous?.map(p => (p.id === updated.id ? updated : p)) ?? null)

  const pending = proposals?.filter(p => p.status === 0).length ?? 0

  return (
    <Stack spacing={2}>
      <div>
        <Typography variant="h5" sx={{ fontWeight: 700 }}>
          {all ? 'Review across leaders' : 'Review'}
        </Typography>
        <Typography color="text.secondary">
          {proposals === null ? ' ' : pending === 0 ? 'All caught up.' : `${pending} ${pending === 1 ? 'suggestion' : 'suggestions'} to check. Nothing changes until you approve.`}
        </Typography>
      </div>
      {error && <Alert severity="error">{error}</Alert>}
      {proposals === null && !error && <Skeleton variant="rounded" height={180} />}
      {proposals?.map(proposal => (
        <ProposalCard key={proposal.id} proposal={proposal} showOwner={all} onDecided={update} />
      ))}
    </Stack>
  )
}
