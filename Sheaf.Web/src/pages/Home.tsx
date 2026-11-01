import { Box, Typography } from '@mui/material'

export default function Home() {
  return (
    <Box sx={{ maxWidth: 600, mx: 'auto', mt: 4 }}>
      <Typography variant="h4" component="h1" gutterBottom>
        Sheaf
      </Typography>
      <Typography color="text.secondary">
        Leader-first relational capture for churches.
      </Typography>
    </Box>
  )
}
