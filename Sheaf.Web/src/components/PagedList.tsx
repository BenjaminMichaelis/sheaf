import { Alert, Box, Button } from '@mui/material'
import type { PagedState } from '@/utils/paging'
import { Loading, RetryButton } from './Feedback'

/** Below a paged list: the error for a failed later page, the loading spinner, or "Load more". */
export default function PagedListFooter({ state, fetchNextPage }: { state: PagedState; fetchNextPage: () => void }) {
  return (
    <>
      {state.moreError && (
        <Alert severity="error" action={<RetryButton onClick={fetchNextPage} />} sx={{ mt: 2 }}>
          {state.moreError}
        </Alert>
      )}

      {state.loading && <Loading />}

      {state.canLoadMore && (
        <Box sx={{ display: 'flex', justifyContent: 'center', py: 2 }}>
          <Button onClick={fetchNextPage}>Load more</Button>
        </Box>
      )}
    </>
  )
}
