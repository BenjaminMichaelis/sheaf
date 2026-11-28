import type { Ref } from 'react'
import { Link as RouterLink, type LinkProps as RouterLinkProps } from 'react-router-dom'

type LinkBehaviorProps = Omit<RouterLinkProps, 'to'> & { href: RouterLinkProps['to']; ref?: Ref<HTMLAnchorElement> }

/** Renders MUI links and `href` buttons as router links, so they navigate inside the app (mui.com/material-ui/integrations/routing). */
export default function LinkBehavior({ href, ...props }: LinkBehaviorProps) {
  return <RouterLink to={href} {...props} />
}
