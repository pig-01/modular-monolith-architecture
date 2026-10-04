export interface EmailLink { userId: string; token: string }

// Consume once before React mounts so StrictMode cannot consume the link twice.
export function consumeEmailLink(): EmailLink | null {
  if (!['/verify-email', '/reset-password'].includes(window.location.pathname)) return null
  const parameters = new URLSearchParams(window.location.hash.slice(1))
  const userId = parameters.get('userId')
  const token = parameters.get('token')
  window.history.replaceState(window.history.state, '', window.location.pathname)
  return userId && token ? { userId, token } : null
}

export function safeReturnPath(value: string | null) {
  if (!value || !value.startsWith('/') || value.startsWith('//') || /[\\\r\n]/.test(value)) return '/dashboard'
  const url = new URL(value, window.location.origin)
  return url.origin === window.location.origin && url.pathname === '/dashboard' ? url.pathname : '/dashboard'
}
