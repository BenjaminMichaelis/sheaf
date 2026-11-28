import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { CHURCH_CONFLICT_PROBLEM_TYPE, apiClient } from '@/services/apiClient'
import ChurchConflictDialog from './ChurchConflictDialog'

describe('ChurchConflictDialog', () => {
  it('ChurchConflictDialog_ChangeRefusedForOtherChurch_ExplainsAndOffersReload', async () => {
    const user = userEvent.setup()
    const reload = vi.fn()
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      type: CHURCH_CONFLICT_PROBLEM_TYPE,
      title: 'Church selection does not match this request.',
      status: 409,
    }), { status: 409, headers: { 'content-type': 'application/problem+json' } }))
    render(<ChurchConflictDialog reload={reload} />)
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()

    await apiClient.POST('/api/church/households', { body: { memberIds: [] } }).catch(() => {})

    expect(await screen.findByRole('dialog', { name: 'Your church changed' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Reload' }))
    expect(reload).toHaveBeenCalledOnce()
  })
})
