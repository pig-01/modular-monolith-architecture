import { useEffect, type ReactNode } from 'react'
import { AuthClient } from './client'
import { AuthContext } from './context'

export function AuthProvider({ client, children }: { client: AuthClient; children: ReactNode }) {
  useEffect(() => client.connect(), [client])
  return <AuthContext value={client}>{children}</AuthContext>
}
