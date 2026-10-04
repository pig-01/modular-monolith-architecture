import { createContext, useContext, useSyncExternalStore } from 'react'
import { AuthClient } from './client'

export const AuthContext = createContext<AuthClient | null>(null)

export function useAuthClient() {
  const client = useContext(AuthContext)
  if (!client) throw new Error('AuthProvider is required')
  return client
}

export function useAuth() {
  const client = useAuthClient()
  return useSyncExternalStore(client.subscribe, client.getSnapshot)
}
