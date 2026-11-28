import ConfirmMeetingDialog from '@/components/ConfirmMeetingDialog'
import { useChurch } from '@/contexts/ChurchContext'
import type { Attendees } from './useAttendees'

interface AttendeesConfirmDialogProps {
  attendees: Attendees
  saving: boolean
  /** Saves again with confirm, after the user has seen every warning. */
  onSaveAnyway: () => void
}

/** The "Check before saving" dialog for a meeting being recorded or corrected, wired to its attendees. */
export default function AttendeesConfirmDialog({ attendees, saving, onSaveAnyway }: AttendeesConfirmDialogProps) {
  const { labels } = useChurch()

  return (
    <ConfirmMeetingDialog
      open={attendees.confirmation !== null}
      groupLabel={labels.one.toLowerCase()}
      sameDay={attendees.confirmation?.sameDay ?? false}
      duplicates={attendees.confirmation?.duplicates ?? []}
      saving={saving}
      onUseExisting={attendees.chooseExisting}
      onSaveAnyway={() => {
        attendees.closeConfirmation()
        onSaveAnyway()
      }}
      onClose={attendees.closeConfirmation}
    />
  )
}
