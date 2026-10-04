export interface Tenant {
  id: string
  name: string
}

export interface SessionProfile {
  user: { id: string; email: string; name: string }
  currentTenant: Tenant
  tenants: Tenant[]
  isPlatformAdmin: boolean
  accessExpiresAt: string
  sessionExpiresAt: string
}

export interface ProblemDetails {
  status: number
  code: string
  title?: string
  errors?: Record<string, string[]>
}

export class ApiError extends Error {
  readonly problem: ProblemDetails
  readonly retryAfter: number

  constructor(problem: ProblemDetails, retryAfter = 0) {
    super(problem.title ?? problem.code)
    this.problem = problem
    this.retryAfter = retryAfter
  }
}

export interface AuthSnapshot {
  status: 'loading' | 'ready' | 'unsupported'
  profile: SessionProfile | null
  error: ApiError | null
}

export class AuthClient {
  private readonly fetcher: typeof fetch
  private csrf: string | null = null
  private snapshot: AuthSnapshot = { status: 'loading', profile: null, error: null }
  private listeners = new Set<() => void>()
  private bootstrapPromise: Promise<void> | null = null
  private refreshPromise: Promise<SessionProfile> | null = null
  private channel: BroadcastChannel | null = null
  private tenantRequests = new AbortController()
  private generation = 0

  constructor(fetcher: typeof fetch = globalThis.fetch.bind(globalThis)) {
    this.fetcher = fetcher
  }

  getSnapshot = () => this.snapshot

  subscribe = (listener: () => void) => {
    this.listeners.add(listener)
    return () => { this.listeners.delete(listener) }
  }

  connect() {
    if (!navigator.locks || !globalThis.BroadcastChannel || !globalThis.isSecureContext) {
      this.snapshot = { status: 'unsupported', profile: null, error: null }
      this.listeners.forEach((listener) => listener())
      return () => {}
    }
    this.channel = new BroadcastChannel('modular-auth-status')
    this.channel.onmessage = () => {
      this.invalidateRequests()
      this.csrf = null
      this.snapshot = { status: 'loading', profile: null, error: null }
      this.listeners.forEach((listener) => listener())
      // Wait for any old bootstrap, whose generation is now invalid, then re-read cookies.
      void (this.bootstrapPromise ?? Promise.resolve()).then(() => this.bootstrap())
    }
    const recheck = () => {
      if (document.visibilityState === 'visible') void this.bootstrap()
    }
    document.addEventListener('visibilitychange', recheck)
    void this.bootstrap()
    return () => {
      this.channel?.close()
      this.channel = null
      document.removeEventListener('visibilitychange', recheck)
    }
  }

  private invalidateRequests() {
    this.generation++
    this.tenantRequests.abort()
    this.tenantRequests = new AbortController()
  }

  private changed(profile: SessionProfile | null) {
    this.invalidateRequests()
    this.csrf = null
    this.setProfile(profile)
    this.channel?.postMessage({ changed: true })
  }

  private setProfile(profile: SessionProfile | null) {
    this.snapshot = { status: 'ready', profile, error: null }
    this.listeners.forEach((listener) => listener())
  }

  private setError(error: ApiError) {
    if (error.problem.status === 403) {
      this.invalidateRequests()
      this.snapshot = { ...this.snapshot, profile: null }
    }
    this.snapshot = { ...this.snapshot, status: 'ready', error }
    this.listeners.forEach((listener) => listener())
  }

  bootstrap(): Promise<void> {
    if (this.bootstrapPromise) return this.bootstrapPromise
    const generation = this.generation
    this.bootstrapPromise = (async () => {
      try {
        let profile: SessionProfile
        try {
          profile = await this.http<SessionProfile>('/me')
        } catch (error) {
          if (!(error instanceof ApiError) || error.problem.status !== 401) throw error
          profile = await this.refresh()
        }
        if (generation === this.generation) this.setProfile(profile)
      } catch (error) {
        if (generation !== this.generation) return
        if (error instanceof ApiError && error.problem.status === 401) {
          if (this.snapshot.profile) this.changed(null)
          else this.setProfile(null)
        }
        else this.setError(asApiError(error))
      }
    })().finally(() => { this.bootstrapPromise = null })
    return this.bootstrapPromise
  }

  refresh(): Promise<SessionProfile> {
    if (this.refreshPromise) return this.refreshPromise
    const generation = this.generation
    this.refreshPromise = navigator.locks.request('modular-auth', () => this.refreshUnderLock(generation))
      .finally(() => { this.refreshPromise = null })
    return this.refreshPromise
  }

  private assertCurrent(generation: number) {
    if (generation !== this.generation) throw new DOMException('Authentication changed', 'AbortError')
  }

  // Only callers holding modular-auth may enter this method; never acquire another lock here.
  private async refreshUnderLock(generation: number): Promise<SessionProfile> {
    this.assertCurrent(generation)
    try {
      const profile = await this.http<SessionProfile>('/me')
      this.assertCurrent(generation)
      this.acceptRestoredProfile(profile)
      return profile
    } catch (error) {
      if (!(error instanceof ApiError) || error.problem.status !== 401) throw error
    }
    await this.freshCsrf()
    this.assertCurrent(generation)
    const requestId = crypto.randomUUID()
    const profile = await this.retryOperation<SessionProfile>('/refresh', { requestId })
    this.assertCurrent(generation)
    this.csrf = null
    this.acceptRestoredProfile(profile)
    this.channel?.postMessage({ changed: true })
    return profile
  }

  private acceptRestoredProfile(profile: SessionProfile) {
    const previous = this.snapshot.profile
    if (previous && (previous.user.id !== profile.user.id || previous.currentTenant.id !== profile.currentTenant.id)) this.invalidateRequests()
    this.setProfile(profile)
  }

  private async retryOperation<T>(path: string, body: unknown): Promise<T> {
    const started = Date.now()
    try { return await this.post<T>(path, body) } catch (error) {
      if (!(error instanceof ApiError) || ![0, 503].includes(error.problem.status) || Date.now() - started >= 25000) throw error
      return this.post<T>(path, body)
    }
  }

  private async http<T>(path: string, init: RequestInit = {}, authPath = true): Promise<T> {
    let response: Response
    try {
      response = await this.fetcher(authPath ? `/api/auth${path}` : path, {
        ...init, credentials: 'include', cache: 'no-store',
        signal: init.signal ?? AbortSignal.timeout(10000),
      })
    } catch (error) {
      if (error instanceof DOMException && error.name === 'AbortError') throw error
      throw new ApiError({ status: 0, code: 'network_error' })
    }
    if (!response.ok) {
      const fallback = { status: response.status, code: 'request_failed' }
      const problem: ProblemDetails = await response.json().catch(() => fallback)
      throw new ApiError({ ...problem, status: response.status }, Number(response.headers.get('Retry-After')) || 0)
    }
    let content: string
    try { content = await response.text() } catch { throw new ApiError({ status: 0, code: 'network_error' }) }
    return content ? JSON.parse(content) as T : undefined as T
  }

  private async freshCsrf() {
    this.csrf = null
    this.csrf = (await this.http<{ requestToken: string }>('/csrf')).requestToken
  }

  private async post<T>(path: string, body: unknown, retryCsrf = true): Promise<T> {
    if (!this.csrf) await this.freshCsrf()
    try {
      return await this.http<T>(path, {
        method: 'POST', headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': this.csrf! },
        body: JSON.stringify(body),
      })
    } catch (error) {
      if (retryCsrf && error instanceof ApiError && error.problem.code === 'csrf_failed') {
        await this.freshCsrf()
        return this.post<T>(path, body, false)
      }
      throw error
    }
  }

  async login(email: string, password: string) {
    return navigator.locks.request('modular-auth', async () => {
      await this.freshCsrf()
      const profile = await this.post<SessionProfile>('/login', { email, password })
      this.changed(profile)
      return profile
    })
  }

  async logout() {
    return navigator.locks.request('modular-auth', async () => {
      await this.freshCsrf()
      await this.post<void>('/logout', {})
      this.changed(null)
    })
  }

  async switchTenant(tenantId: string) {
    return navigator.locks.request('modular-auth', async () => {
      await this.freshCsrf()
      const body = { tenantId, requestId: crypto.randomUUID() }
      let profile: SessionProfile
      try { profile = await this.retryOperation<SessionProfile>('/switch-tenant', body) } catch (error) {
        if (!(error instanceof ApiError) || error.problem.status !== 401) throw error
        try { await this.refreshUnderLock(this.generation) } catch (failure) {
          if (failure instanceof ApiError && failure.problem.status === 401) this.changed(null)
          throw failure
        }
        profile = await this.retryOperation<SessionProfile>('/switch-tenant', body)
      }
      this.changed(profile)
      return profile
    })
  }

  async authorized<T>(path: string, init: RequestInit = {}): Promise<T> {
    if (!path.startsWith('/api/') || path.includes('\\')) throw new Error('Only same-origin API paths are supported.')
    const generation = this.generation
    const signal = init.signal
      ? AbortSignal.any([init.signal, this.tenantRequests.signal, AbortSignal.timeout(10000)])
      : AbortSignal.any([this.tenantRequests.signal, AbortSignal.timeout(10000)])
    let retriedCsrf = false
    const send = async (): Promise<T> => {
      this.assertCurrent(generation)
      const method = (init.method ?? 'GET').toUpperCase()
      const headers = new Headers(init.headers)
      if (!['GET', 'HEAD', 'OPTIONS'].includes(method)) {
        if (!this.csrf) await this.freshCsrf()
        headers.set('X-CSRF-TOKEN', this.csrf!)
      }
      try {
        const result = await this.http<T>(path, { ...init, headers, signal }, false)
        this.assertCurrent(generation)
        return result
      } catch (error) {
        if (!retriedCsrf && error instanceof ApiError && error.problem.code === 'csrf_failed') {
          retriedCsrf = true
          await this.freshCsrf()
          return send()
        }
        throw error
      }
    }
    try {
      return await send()
    } catch (error) {
      if (!(error instanceof ApiError) || error.problem.status !== 401) throw error
      try {
        await this.refresh()
      } catch (refreshError) {
        if (refreshError instanceof ApiError && refreshError.problem.status === 401) this.changed(null)
        throw refreshError
      }
      return send()
    }
  }

  register(name: string, email: string, password: string) {
    return this.post<void>('/register', { name, email, password })
  }

  confirmEmail(userId: string, token: string) {
    return this.post<void>('/confirm-email', { userId, token })
  }

  resendConfirmation(email: string) {
    return this.post<void>('/resend-confirmation', { email })
  }

  forgotPassword(email: string) {
    return this.post<void>('/forgot-password', { email })
  }

  async resetPassword(userId: string, token: string, password: string) {
    return navigator.locks.request('modular-auth', async () => {
      await this.freshCsrf()
      await this.post<void>('/reset-password', { userId, token, password })
      this.changed(null)
    })
  }
}

export function asApiError(error: unknown): ApiError {
  return error instanceof ApiError ? error : new ApiError({ status: 0, code: 'network_error' })
}
