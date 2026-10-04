import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, expect, it, vi } from 'vitest'
import { AuthClient } from './auth/client'
import { StrictMode } from 'react'
import { consumeEmailLink } from './auth/links'
import App from './App'

beforeEach(() => {
  window.history.replaceState({}, '', '/login')
  vi.stubGlobal('isSecureContext', true)
  Object.defineProperty(navigator, 'locks', {
    configurable: true, value: { request: (_name: string, callback: () => Promise<unknown>) => callback() },
  })
})

it('allows a user to log in and view their current tenant', async () => {
  const user = userEvent.setup()
  let authenticated = false
  const profile = {
    user: { id: '1', email: 'lin@example.com', name: '小林' },
    currentTenant: { id: 'north', name: '北部團隊' }, tenants: [{ id: 'north', name: '北部團隊' }],
    isPlatformAdmin: false, accessExpiresAt: '2030-01-01T00:15:00Z', sessionExpiresAt: '2030-01-08T00:00:00Z',
  }
  const client = new AuthClient(async (input) => {
    const path = input.toString()
    if (path.endsWith('/csrf')) return Response.json({ requestToken: 'csrf' })
    if (path.endsWith('/login')) authenticated = true
    return authenticated ? Response.json(profile) : Response.json({ code: 'session_invalid' }, { status: 401 })
  })
  render(<App client={client} />)
  await user.type(await screen.findByLabelText('電子郵件'), 'lin@example.com')
  await user.type(screen.getByLabelText('密碼', { exact: true }), 'a long enough password')
  await user.click(screen.getByRole('button', { name: '登入' }))
  expect(await screen.findByRole('heading', { name: '小林，歡迎回來' })).toBeVisible()
  expect(screen.getByText('北部團隊', { selector: 'strong' })).toBeVisible()
})

it('keeps an email link in memory through StrictMode and requires explicit confirmation', async () => {
  window.history.replaceState({}, '', '/verify-email#userId=person-1&token=private-link-token')
  const credentials = consumeEmailLink()
  const confirmations: unknown[] = []
  const client = new AuthClient(async (input, init) => {
    const path = input.toString()
    if (path.endsWith('/csrf')) return Response.json({ requestToken: 'csrf' })
    if (path.endsWith('/confirm-email')) {
      confirmations.push(JSON.parse(String(init?.body)))
      return new Response(null, { status: 204 })
    }
    return Response.json({ code: 'session_invalid' }, { status: 401 })
  })
  render(<StrictMode><App client={client} credentials={credentials} /></StrictMode>)
  const button = await screen.findByRole('button', { name: '確認電子郵件' })
  expect(window.location.hash).toBe('')
  expect(confirmations).toHaveLength(0)
  await userEvent.setup().click(button)
  expect(await screen.findByRole('status')).toHaveTextContent('電子郵件驗證完成')
  expect(confirmations).toEqual([{ userId: 'person-1', token: 'private-link-token' }])
})

it('shows a field error and does not register when password confirmation differs', async () => {
  window.history.replaceState({}, '', '/register')
  const registrations: string[] = []
  const client = new AuthClient(async (input) => {
    const path = input.toString()
    if (path.endsWith('/csrf')) return Response.json({ requestToken: 'csrf' })
    if (path.endsWith('/register')) registrations.push(path)
    return Response.json({ code: 'session_invalid' }, { status: 401 })
  })
  const user = userEvent.setup()
  render(<App client={client} />)
  await user.type(await screen.findByLabelText('姓名'), '小林')
  await user.type(screen.getByLabelText('電子郵件'), 'lin@example.com')
  await user.type(screen.getByLabelText('密碼', { exact: true }), 'a long enough password')
  await user.type(screen.getByLabelText('確認密碼', { exact: true }), 'a different long password')
  await user.click(screen.getByRole('button', { name: '建立帳號' }))
  expect(await screen.findByText('兩次輸入的密碼不一致。')).toBeVisible()
  expect(screen.getByLabelText('確認密碼', { exact: true })).toHaveAttribute('aria-invalid', 'true')
  expect(registrations).toHaveLength(0)
})
