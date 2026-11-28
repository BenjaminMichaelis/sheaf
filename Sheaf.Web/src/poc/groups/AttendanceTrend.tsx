import { Box, Tooltip, Typography } from '@mui/material'
import { formatDay, type Session } from './groupData'

/** Simple bar chart of how many came to each recent meeting, oldest on the left. */
export default function AttendanceTrend({ sessions }: { sessions: Session[] }) {
  const bars = sessions.map(session => ({ id: session.id, occurredOn: session.occurredOn, count: session.attendees.length }))
  const first = bars[0]
  const last = bars[bars.length - 1]

  if (!first || !last) {
    return (
      <Typography variant="body2" color="text.secondary">
        Record a meeting to start the trend.
      </Typography>
    )
  }

  const max = Math.max(...bars.map(bar => bar.count), 1)
  const average = bars.reduce((sum, bar) => sum + bar.count, 0) / bars.length
  const change = last.count - first.count

  return (
    <Box>
      <Box sx={{ display: 'flex', alignItems: 'baseline', gap: 1, mb: 1.5 }}>
        <Typography variant="h4" sx={{ fontWeight: 700 }}>
          {last.count}
        </Typography>
        <Typography variant="body2" color="text.secondary">
          came last time · avg {average.toFixed(1)}
          {bars.length > 1 && change !== 0 && ` · ${change > 0 ? '+' : ''}${change} since ${formatDay(first.occurredOn, { month: 'short', day: 'numeric' })}`}
        </Typography>
      </Box>
      <Box sx={{ display: 'flex', alignItems: 'flex-end', gap: 1, height: 96 }} role="img" aria-label={`Attendance: ${bars.map(bar => bar.count).join(', ')}`}>
        {bars.map(bar => {
          const latest = bar === last

          return (
            <Tooltip key={bar.id} title={`${formatDay(bar.occurredOn)}: ${bar.count} came`}>
              <Box sx={{ flex: 1, display: 'flex', flexDirection: 'column', alignItems: 'center', height: '100%', justifyContent: 'flex-end' }}>
                <Typography variant="caption" color="text.secondary">
                  {bar.count}
                </Typography>
                <Box
                  sx={{
                    width: '100%',
                    maxWidth: 36,
                    height: `${Math.max(4, (bar.count / max) * 72)}px`,
                    borderRadius: 1,
                    bgcolor: latest ? 'primary.main' : 'primary.light',
                    opacity: latest ? 1 : 0.6,
                  }}
                />
              </Box>
            </Tooltip>
          )
        })}
      </Box>
      <Box sx={{ display: 'flex', gap: 1, mt: 0.5 }}>
        {bars.map(bar => (
          <Typography key={bar.id} variant="caption" color="text.secondary" sx={{ flex: 1, textAlign: 'center' }}>
            {formatDay(bar.occurredOn, { month: 'numeric', day: 'numeric' })}
          </Typography>
        ))}
      </Box>
    </Box>
  )
}
