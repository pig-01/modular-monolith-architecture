import { useState, type ReactNode } from 'react'
import { BrowserRouter, Link, Navigate, Route, Routes, useNavigate, useSearchParams } from 'react-router-dom'
import { AuthClient, asApiError, type ApiError } from './auth/client'
import { AuthProvider } from './auth/AuthProvider'
import { useAuth, useAuthClient } from './auth/context'
import { AuthForm, ErrorNotice, Field } from './auth/forms'
import { safeReturnPath, type EmailLink } from './auth/links'
import './App.css'

const browserClient = new AuthClient()
const value = (data: FormData, key: string) => String(data.get(key) ?? '')

function Brand() {
  return <Link className="brand" to="/"><span className="brand-mark" aria-hidden="true">m<span>·</span></span><span>Modular<span className="brand-subtitle">WORKSPACE</span></span></Link>
}

function AuthLayout({ eyebrow, title, description, children }: { eyebrow: string; title: string; description: string; children: ReactNode }) {
  return <main className="auth-shell">
    <aside className="intro-panel">
      <Brand />
      <div className="intro-copy"><p className="eyebrow">YOUR WORK, CONNECTED</p><h2>從這裡，<br />開始你的工作。</h2><p>一個帳號，連結你的工作空間。<br />讓每天的協作更簡單。</p></div>
      <div className="module-art" aria-hidden="true"><span /><span /><span /><span /></div>
      <p className="intro-footer">少一點繁瑣，多一點專注。</p>
    </aside>
    <section className="auth-content">
      <div className="mobile-brand"><Brand /></div>
      <div className="auth-card"><p className="eyebrow">{eyebrow}</p><h1>{title}</h1><p className="description">{description}</p>{children}</div>
      <footer>Modular Workspace <span>・</span> 你的日常工作入口</footer>
    </section>
  </main>
}

function Loading() {
  return <main className="state-page"><Brand /><div className="loader" aria-hidden="true" /><p role="status">正在確認登入狀態…</p></main>
}

function Availability({ children }: { children: ReactNode }) {
  const auth = useAuth()
  const client = useAuthClient()
  if (auth.status === 'unsupported') return <main className="state-page"><Brand /><h1>請更新瀏覽器</h1><p>請使用最新版 Chrome、Edge、Firefox 或 Safari，並透過 HTTPS 開啟網站。</p><p>這個頁面需要瀏覽器支援安全的多分頁登入同步。</p></main>
  if (auth.status === 'loading') return <Loading />
  if (auth.error && !auth.profile) return <main className="state-page"><Brand /><h1>{auth.error.problem.status === 403 ? '目前沒有存取權' : '暫時無法連線'}</h1><ErrorNotice error={auth.error} /><button className="primary" onClick={() => void client.bootstrap()}>重新連線</button>{auth.error.problem.status === 403 && <button className="subtle" onClick={() => void client.logout().catch(() => client.bootstrap())}>登出</button>}</main>
  return children
}

function Login() {
  const client = useAuthClient()
  const auth = useAuth()
  const [parameters] = useSearchParams()
  const navigate = useNavigate()
  if (auth.profile) return <Navigate to="/dashboard" replace />
  return <AuthLayout eyebrow="WELCOME BACK" title="歡迎回來" description="登入帳號，接著完成今天的工作。">
    <AuthForm submitLabel="登入" action={async (data) => {
      await client.login(value(data, 'email'), value(data, 'password'))
      navigate(safeReturnPath(parameters.get('returnTo')), { replace: true })
    }}>
      <Field name="email" label="電子郵件" type="email" autoComplete="username" />
      <Field name="password" label="密碼" type="password" autoComplete="current-password" maxLength={128} />
      <div className="form-links"><Link to="/forgot-password">忘記密碼？</Link></div>
    </AuthForm>
    <p className="form-footer">還沒有帳號？ <Link to="/register">建立帳號</Link></p>
  </AuthLayout>
}

function Register() {
  const client = useAuthClient()
  return <AuthLayout eyebrow="GET STARTED" title="建立你的帳號" description="只需幾個步驟，就能開始使用工作空間。">
    <AuthForm submitLabel="建立帳號" successMessage="請查看您的信箱。若此信箱可以註冊，您將收到一封驗證信。" action={(data) => client.register(value(data, 'name'), value(data, 'email'), value(data, 'password'))}>
      <Field name="name" label="姓名" autoComplete="name" maxLength={100} />
      <Field name="email" label="電子郵件" type="email" autoComplete="email" />
      <Field name="password" label="密碼" type="password" autoComplete="new-password" minLength={15} maxLength={128} hint="使用 15–128 個字元，建議選擇容易記住的長句。" />
      <Field name="confirmPassword" label="確認密碼" type="password" autoComplete="new-password" minLength={15} maxLength={128} />
    </AuthForm>
    <p className="form-footer">已有帳號？ <Link to="/login">前往登入</Link></p>
  </AuthLayout>
}

function VerifyEmail({ credentials }: { credentials: EmailLink | null }) {
  const client = useAuthClient()
  return <AuthLayout eyebrow="CHECK YOUR INBOX" title="驗證電子郵件" description={credentials ? '確認這是你的電子郵件，即可啟用帳號。' : '輸入註冊時的電子郵件，重新取得驗證連結。'}>
    {credentials && <div className="verify-action"><AuthForm submitLabel="確認電子郵件" successMessage="電子郵件驗證完成，現在可以登入帳號。" action={() => client.confirmEmail(credentials.userId, credentials.token)}><p className="hint">請點擊下方按鈕完成驗證。</p></AuthForm></div>}
    <AuthForm submitLabel="重寄驗證信" successMessage="若此信箱需要驗證，您將收到一封新的驗證信。" action={(data) => client.resendConfirmation(value(data, 'email'))}>
      <Field name="email" label="電子郵件" type="email" autoComplete="email" />
    </AuthForm>
    <p className="form-footer"><Link to="/login">返回登入</Link></p>
  </AuthLayout>
}

function ForgotPassword() {
  const client = useAuthClient()
  return <AuthLayout eyebrow="LET’S GET YOU BACK" title="忘記密碼？" description="別擔心，我們會寄送連結，協助你設定新密碼。">
    <AuthForm submitLabel="寄送重設連結" successMessage="若此信箱有可用的帳號，您將收到密碼重設信。請查看收件匣與垃圾郵件。" action={(data) => client.forgotPassword(value(data, 'email'))}>
      <Field name="email" label="電子郵件" type="email" autoComplete="email" />
    </AuthForm>
    <p className="form-footer"><Link to="/login">返回登入</Link></p>
  </AuthLayout>
}

function ResetPassword({ credentials }: { credentials: EmailLink | null }) {
  const client = useAuthClient()
  return <AuthLayout eyebrow="A FRESH START" title="設定新密碼" description="選擇一組新密碼，重新登入你的帳號。">
    {credentials ? <AuthForm submitLabel="更新密碼" successMessage="密碼已更新，所有裝置已登出。請使用新密碼重新登入。" action={(data) => client.resetPassword(credentials.userId, credentials.token, value(data, 'password'))}>
      <Field name="password" label="新密碼" type="password" autoComplete="new-password" minLength={15} maxLength={128} hint="使用 15–128 個字元，避免與其他網站共用密碼。" />
      <Field name="confirmPassword" label="確認密碼" type="password" autoComplete="new-password" minLength={15} maxLength={128} />
    </AuthForm> : <div className="notice error" role="alert"><p>此連結不完整或已關閉。請重新開啟信件中的連結，或申請新的重設信。</p><Link to="/forgot-password">重新申請重設密碼</Link></div>}
    <p className="form-footer"><Link to="/login">返回登入</Link></p>
  </AuthLayout>
}

function Dashboard() {
  const { profile, error: connectionError } = useAuth()
  const client = useAuthClient()
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<ApiError | null>(null)
  if (!profile) return <Navigate to="/login?returnTo=%2Fdashboard" replace />
  const perform = async (action: () => Promise<unknown>) => {
    setPending(true)
    setError(null)
    try { await action() } catch (cause) { setError(asApiError(cause)) } finally { setPending(false) }
  }
  const expires = new Intl.DateTimeFormat('zh-TW', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(profile.sessionExpiresAt))
  return <div className="dashboard-shell">
    <header className="dashboard-header"><Brand /><div className="header-actions"><span className="avatar" aria-hidden="true">{profile.user.name.slice(0, 1)}</span><span>{profile.user.name}</span><button className="subtle" disabled={pending} onClick={() => void perform(() => client.logout())}>{pending ? '處理中…' : '登出'}</button></div></header>
    <main className="dashboard-main">
      <p className="eyebrow">YOUR WORKSPACE</p><h1>{profile.user.name}，歡迎回來</h1><p className="description">帳號已準備就緒，從你的工作空間開始。</p>
      {(error || connectionError) && <ErrorNotice error={(error ?? connectionError)!} />}
      {connectionError && <button className="subtle" disabled={pending} onClick={() => void perform(() => client.bootstrap())}>重新連線</button>}
      <div className="dashboard-grid">
        <section className="workspace-card"><div className="card-kicker"><span className="status-dot" />目前工作空間</div><strong>{profile.currentTenant.name}</strong><p>選擇你要使用的團隊，帳號會同步到所有分頁。</p><label htmlFor="tenant">切換工作空間</label><select id="tenant" value={profile.currentTenant.id} disabled={pending || profile.tenants.length < 2} onChange={(event) => void perform(() => client.switchTenant(event.target.value))}>{profile.tenants.map((tenant) => <option key={tenant.id} value={tenant.id}>{tenant.name}</option>)}</select>{profile.tenants.length < 2 && <p className="hint">你目前有一個可用的工作空間。</p>}</section>
        <section className="account-card"><h2>帳號資訊</h2><dl><div><dt>姓名</dt><dd>{profile.user.name}</dd></div><div><dt>電子郵件</dt><dd>{profile.user.email}</dd></div><div><dt>帳號權限</dt><dd>{profile.isPlatformAdmin ? '平台管理員' : '一般成員'}</dd></div><div><dt>本次登入有效至</dt><dd>{expires}</dd></div></dl></section>
      </div>
      <section className="dashboard-note"><span aria-hidden="true">↗</span><div><h2>保持專注，安心工作</h2><p>使用共用電腦時，完成工作後請記得登出。</p></div></section>
    </main>
    <footer>Modular Workspace <span>・</span> 你的日常工作入口</footer>
  </div>
}

function RootRedirect() {
  const auth = useAuth()
  return <Navigate to={auth.profile ? '/dashboard' : '/login'} replace />
}

export default function App({ client = browserClient, credentials = null }: { client?: AuthClient; credentials?: EmailLink | null }) {
  return <AuthProvider client={client}><BrowserRouter><Availability><Routes>
    <Route path="/" element={<RootRedirect />} />
    <Route path="/login" element={<Login />} />
    <Route path="/register" element={<Register />} />
    <Route path="/verify-email" element={<VerifyEmail credentials={credentials} />} />
    <Route path="/forgot-password" element={<ForgotPassword />} />
    <Route path="/reset-password" element={<ResetPassword credentials={credentials} />} />
    <Route path="/dashboard" element={<Dashboard />} />
    <Route path="*" element={<AuthLayout eyebrow="404" title="找不到這個頁面" description="網址可能已變更，請回到工作空間繼續。"><Link className="primary" to="/">返回首頁</Link></AuthLayout>} />
  </Routes></Availability></BrowserRouter></AuthProvider>
}
