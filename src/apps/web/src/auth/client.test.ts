import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AuthClient } from './client'

const profile = {
  user: { id: 'person-1', email: 'person@example.com', name: '林小姐' },
  currentTenant: { id: 'north', name: '北部團隊' },
  tenants: [{ id: 'north', name: '北部團隊' }],
  isPlatformAdmin: false,
  accessExpiresAt: '2030-01-01T00:15:00Z',
  sessionExpiresAt: '2030-01-08T00:00:00Z',
}

const json = (data: unknown, status = 200) => new Response(JSON.stringify(data), {
  status, headers: { 'Content-Type': 'application/json' },
})

beforeEach(() => {
  Object.defineProperty(navigator, 'locks', {
    configurable: true,
    value: { request: (_name: string, callback: () => Promise<unknown>) => callback() },
  })
})

describe('cookie authentication client', () => {
  it('logs in with a CSRF header and exposes only the profile to the app', async () => {
    const requests: { path: string; init?: RequestInit }[] = []
    const server = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const path = input.toString()
      requests.push({ path, init })
      if (path.endsWith('/csrf')) return json({ requestToken: 'request-token' })
      if (path.endsWith('/login')) return json(profile)
      throw new Error(`Unexpected request ${path}`)
    })
    const client = new AuthClient(server)

    await client.login('person@example.com', 'a long enough password')

    expect(client.getSnapshot().profile).toEqual(profile)
    const login = requests.find((request) => request.path.endsWith('/login'))
    expect(login?.init?.credentials).toBe('include')
    expect(new Headers(login?.init?.headers).get('X-CSRF-TOKEN')).toBe('request-token')
  })

  it('restores an expired login through one shared refresh and recovers a lost response with the same request id', async () => {
    const refreshIds: string[] = []
    let restored = false
    const server = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const path = input.toString()
      if (path.endsWith('/csrf')) return json({ requestToken: 'fresh-token' })
      if (path.endsWith('/me')) return restored ? json(profile) : json({ code: 'session_invalid' }, 401)
      if (path.endsWith('/refresh')) {
        refreshIds.push(JSON.parse(String(init?.body)).requestId)
        if (refreshIds.length === 1) throw new TypeError('Connection lost after the server committed')
        restored = true
        return json(profile)
      }
      throw new Error(`Unexpected request ${path}`)
    })
    const client = new AuthClient(server)

    await Promise.all([client.bootstrap(), client.bootstrap()])

    expect(client.getSnapshot().profile?.user.name).toBe('林小姐')
    expect(refreshIds).toHaveLength(2)
    expect(refreshIds[0]).toBe(refreshIds[1])
  })

  it('renews a rejected CSRF token without changing the intended operation', async () => {
    let tokenVersion = 0
    const attempts: unknown[] = []
    const client = new AuthClient(async (input, init) => {
      if (input.toString().endsWith('/csrf')) return json({ requestToken: `token-${++tokenVersion}` })
      attempts.push(JSON.parse(String(init?.body)))
      return attempts.length === 1 ? json({ code: 'csrf_failed' }, 400) : json(profile)
    })

    await client.login('person@example.com', 'a long enough password')

    expect(attempts).toEqual([
      { email: 'person@example.com', password: 'a long enough password' },
      { email: 'person@example.com', password: 'a long enough password' },
    ])
    expect(client.getSnapshot().profile).toEqual(profile)
  })

  it('keeps the current profile on service interruption and cancels old tenant requests on a successful switch', async () => {
    let unavailable = true
    let signal: AbortSignal | null | undefined
    const client = new AuthClient(async (input, init) => {
      const path = input.toString()
      if (path.endsWith('/csrf')) return json({ requestToken: 'csrf' })
      if (path.endsWith('/login') || path.endsWith('/me')) return json(profile)
      if (path.endsWith('/switch-tenant')) return unavailable
        ? json({ code: 'service_unavailable' }, 503)
        : json({ ...profile, currentTenant: { id: 'south', name: '南部團隊' } })
      signal = init?.signal
      return new Promise<Response>((_resolve, reject) => {
        signal?.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')))
      })
    })
    await client.login('person@example.com', 'a long enough password')
    await expect(client.switchTenant('south')).rejects.toMatchObject({ problem: { status: 503 } })
    expect(client.getSnapshot().profile?.currentTenant.id).toBe('north')
    const pending = client.authorized('/api/orders').catch((error: unknown) => error)
    unavailable = false
    await client.switchTenant('south')
    expect(signal?.aborted).toBe(true)
    expect(await pending).toBeInstanceOf(DOMException)
    expect(client.getSnapshot().profile?.currentTenant.name).toBe('南部團隊')
  })

  it('refreshes an expired access cookie before switching tenant without nesting browser locks', async () => {
    let locked = false
    Object.defineProperty(navigator, 'locks', { configurable: true, value: {
      request: async (_name: string, callback: () => Promise<unknown>) => {
        if (locked) throw new Error('A nested lock would deadlock in the browser')
        locked = true
        try { return await callback() } finally { locked = false }
      },
    } })
    let refreshed = false
    const client = new AuthClient(async (input) => {
      const path = input.toString()
      if (path.endsWith('/csrf')) return json({ requestToken: 'csrf' })
      if (path.endsWith('/refresh')) { refreshed = true; return json(profile) }
      if (!refreshed) return json({ code: 'session_invalid' }, 401)
      return json({ ...profile, currentTenant: { id: 'south', name: '南部團隊' } })
    })
    await client.switchTenant('south')
    expect(client.getSnapshot().profile?.currentTenant.id).toBe('south')
  })

  it('does not restore a delayed refresh profile after the session changes', async () => {
    let finish: (response: Response) => void = () => { throw new Error('Missing pending response') }
    const client = new AuthClient(async (input) => {
      const path = input.toString()
      if (path.endsWith('/csrf')) return json({ requestToken: 'csrf' })
      if (path.endsWith('/logout')) return new Response(null, { status: 204 })
      return new Promise<Response>((resolve) => { finish = resolve })
    })
    const refresh = client.refresh().catch((error: unknown) => error)
    await client.logout()
    finish(json(profile))
    expect(await refresh).toBeInstanceOf(DOMException)
    expect(client.getSnapshot().profile).toBeNull()
  })

  it('renews CSRF once for an unsafe business request, preserving its body', async () => {
    const bodies: unknown[] = []
    let csrfVersion = 0
    const client = new AuthClient(async (input, init) => {
      if (input.toString().endsWith('/csrf')) return json({ requestToken: `csrf-${++csrfVersion}` })
      bodies.push(init?.body)
      return bodies.length === 1 ? json({ code: 'csrf_failed' }, 400) : json({ id: 'order-1' })
    })
    const result = await client.authorized('/api/orders', { method: 'POST', body: '{"quantity":2}' })
    expect(result).toEqual({ id: 'order-1' })
    expect(bodies).toEqual(['{"quantity":2}', '{"quantity":2}'])
  })
})
