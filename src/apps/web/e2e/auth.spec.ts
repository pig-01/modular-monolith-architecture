import { expect, test, type APIRequestContext, type Page } from '@playwright/test'

const mailpit = process.env.E2E_MAILPIT_URL ?? 'http://localhost:8025'
const password = 'a long test password for my account'

async function emailLink(request: APIRequestContext, email: string, path: string) {
  let link = ''
  await expect.poll(async () => {
    const listing = await request.get(`${mailpit}/api/v1/search`, { params: { query: `to:${email}` } })
    if (!listing.ok()) return false
    const data = await listing.json() as { messages: { ID: string }[] }
    for (const message of data.messages ?? []) {
      const detail = await request.get(`${mailpit}/api/v1/message/${message.ID}`)
      const body = await detail.json() as { Text: string; HTML: string }
      const links = `${body.Text ?? ''} ${body.HTML ?? ''}`.match(/https?:\/\/[^\s<>"']+/g) ?? []
      const match = links.find((candidate) => candidate.includes(path))
      if (match) { link = match.replaceAll('&amp;', '&'); return true }
    }
    return false
  }, { timeout: 30000, message: 'The requested email reaches Mailpit' }).toBe(true)
  return link
}

async function login(page: Page, email: string, secret = password) {
  await page.goto('/login')
  await page.getByLabel('電子郵件').fill(email)
  await page.getByLabel('密碼', { exact: true }).fill(secret)
  await page.getByRole('button', { name: '登入', exact: true }).click()
  await expect(page).toHaveURL(/\/dashboard$/)
}

test('register, confirm, restore cookies, synchronize logout, and reset password', async ({ page, context, request }, testInfo) => {
  const email = `browser-${crypto.randomUUID()}@example.test`
  await page.goto('/register')
  await page.getByLabel('姓名', { exact: true }).fill('測試成員')
  await page.getByLabel('電子郵件').fill(email)
  await page.getByLabel('密碼', { exact: true }).fill(password)
  await page.getByLabel('確認密碼', { exact: true }).fill(password)
  await page.getByRole('button', { name: '建立帳號', exact: true }).click()
  await expect(page.getByRole('status')).toContainText('請查看您的信箱')

  const confirmation = await emailLink(request, email, '/verify-email')
  await page.goto(confirmation)
  await expect(page).toHaveURL(/\/verify-email$/)
  await page.getByRole('button', { name: '確認電子郵件', exact: true }).click()
  await expect(page.getByRole('status')).toContainText('電子郵件驗證完成')
  await login(page, email)
  await page.reload()
  await expect(page.getByRole('heading', { name: '測試成員，歡迎回來' })).toBeVisible()
  const authCookies = (await context.cookies()).filter((cookie) => cookie.httpOnly && /access|refresh/i.test(cookie.name))
  expect(authCookies).toHaveLength(2)
  for (const cookie of authCookies) {
    expect(cookie.name).toMatch(/^__Host-/)
    expect(cookie.secure).toBe(true)
    expect(cookie.sameSite).toBe('Lax')
    expect(cookie.path).toBe('/')
  }
  expect(await page.evaluate(() => ({ local: localStorage.length, session: sessionStorage.length }))).toEqual({ local: 0, session: 0 })
  await page.screenshot({ path: testInfo.outputPath('dashboard-desktop.png'), fullPage: true })
  await page.setViewportSize({ width: 390, height: 844 })
  await page.screenshot({ path: testInfo.outputPath('dashboard-mobile.png'), fullPage: true })
  const otherTab = await context.newPage()
  await otherTab.goto('/dashboard')
  await expect(otherTab.getByRole('heading', { name: '測試成員，歡迎回來' })).toBeVisible()
  const accessCookie = authCookies.find((cookie) => /access/i.test(cookie.name))!
  await context.clearCookies({ name: accessCookie.name })
  await Promise.all([page.reload(), otherTab.reload()])
  await expect(page.getByRole('heading', { name: '測試成員，歡迎回來' })).toBeVisible()
  await expect(otherTab.getByRole('heading', { name: '測試成員，歡迎回來' })).toBeVisible()
  await page.getByRole('button', { name: '登出', exact: true }).click()
  await expect(page).toHaveURL(/\/login/)
  await expect(otherTab).toHaveURL(/\/login/)

  await page.goto('/forgot-password')
  await page.getByLabel('電子郵件').fill(email)
  await page.getByRole('button', { name: '寄送重設連結', exact: true }).click()
  await expect(page.getByRole('status')).toContainText('若此信箱有可用的帳號')
  await page.goto(await emailLink(request, email, '/reset-password'))
  await expect(page).toHaveURL(/\/reset-password$/)
  await page.getByLabel('新密碼', { exact: true }).fill(`${password} changed`)
  await page.getByLabel('確認密碼', { exact: true }).fill(`${password} changed`)
  await page.getByRole('button', { name: '更新密碼', exact: true }).click()
  await expect(page.getByRole('status')).toContainText('密碼已更新')
  await login(page, email, `${password} changed`)
  await otherTab.close()
})

test('switching tenant synchronizes all tabs of a seeded multi-tenant account', async ({ page, context }) => {
  const email = process.env.E2E_ADMIN_EMAIL
  const secret = process.env.E2E_ADMIN_PASSWORD
  test.skip(!email || !secret, 'Set E2E_ADMIN_EMAIL/PASSWORD to a seeded account with two memberships.')
  await login(page, email!, secret!)
  const selector = page.getByLabel('切換工作空間')
  const tenants = await selector.locator('option').evaluateAll((options) => options.map((option) => (option as HTMLOptionElement).value))
  expect(tenants.length).toBeGreaterThanOrEqual(2)
  const previous = await selector.inputValue()
  const next = tenants.find((tenant) => tenant !== previous)!
  const otherTab = await context.newPage()
  await otherTab.goto('/dashboard')
  await expect(otherTab.getByLabel('切換工作空間')).toHaveValue(previous)
  await selector.selectOption(next)
  await expect(selector).toHaveValue(next)
  await expect(otherTab.getByLabel('切換工作空間')).toHaveValue(next)
  await otherTab.close()
})

test('browser sees enforced CSP and the server rejects a mutation without CSRF', async ({ page, request }, testInfo) => {
  const response = await page.goto('/login')
  const csp = response?.headers()['content-security-policy'] ?? ''
  expect(csp).toContain("script-src 'self'")
  expect(csp).not.toContain("'unsafe-inline'")
  expect(csp).toContain("frame-ancestors 'none'")
  await expect(page.getByRole('heading', { name: '歡迎回來' })).toBeVisible()
  await page.evaluate(() => {
    const script = document.createElement('script')
    script.textContent = "document.documentElement.dataset.unsafeScript = 'executed'"
    document.body.append(script)
  })
  expect(await page.locator('html').getAttribute('data-unsafe-script')).toBeNull()
  const rejected = await request.post('/api/auth/login', { data: { email: 'nobody@example.test', password } })
  expect(rejected.status()).toBe(400)
  expect((await rejected.json()).code).toBe('csrf_failed')
  const cors = await request.fetch('/api/auth/me', {
    method: 'OPTIONS', headers: { Origin: 'https://untrusted.example', 'Access-Control-Request-Method': 'GET' },
  })
  expect(cors.headers()['access-control-allow-origin']).toBeUndefined()
  await page.screenshot({ path: testInfo.outputPath('login-desktop.png'), fullPage: true })
  await page.setViewportSize({ width: 390, height: 844 })
  await page.screenshot({ path: testInfo.outputPath('login-mobile.png'), fullPage: true })
})
