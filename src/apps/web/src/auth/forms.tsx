import { createContext, useContext, useEffect, useId, useState, type FormEvent, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { ApiError, asApiError } from './client'

const FieldErrors = createContext<Record<string, string[]>>({})

function message(error: ApiError) {
  const messages: Record<string, string> = {
    invalid_credentials: '電子郵件或密碼不正確，請再試一次。',
    email_unconfirmed: '請先驗證電子郵件，再登入帳號。',
    csrf_failed: '頁面驗證已失效，請重新整理後再試一次。',
    session_invalid: '登入已到期，請重新登入。',
    tenant_forbidden: '您的帳號目前沒有這個工作空間的存取權。',
    rate_limited: '操作次數過多，請稍後再試。',
    service_unavailable: '服務暫時無法使用，請稍後重試。',
    network_error: '連線暫時中斷，請確認網路後重試。',
    credential_conflict: '登入資訊有衝突，請清除本站 Cookie 後重新登入。',
    validation_failed: '請確認輸入內容，再試一次。',
    invalid_token: '連結已失效或已使用，請重新申請。',
  }
  return messages[error.problem.code] ?? '無法完成操作，請稍後重試或重新申請連結。'
}

export function ErrorNotice({ error }: { error: ApiError }) {
  return <div className="notice error" role="alert"><p>{message(error)}</p>
    {error.problem.code === 'email_unconfirmed' && <Link to="/verify-email">重寄驗證信</Link>}
  </div>
}

export function AuthForm({ action, submitLabel, successMessage, children }: {
  action: (data: FormData) => Promise<unknown>; submitLabel: string; successMessage?: string; children: ReactNode
}) {
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<ApiError | null>(null)
  const [success, setSuccess] = useState(false)
  const [remaining, setRemaining] = useState(0)
  useEffect(() => {
    if (!remaining) return
    const timer = window.setTimeout(() => setRemaining((value) => Math.max(0, value - 1)), 1000)
    return () => window.clearTimeout(timer)
  }, [remaining])
  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (pending || remaining) return
    const data = new FormData(event.currentTarget)
    setPending(true)
    setError(null)
    setSuccess(false)
    try {
      if (data.has('confirmPassword') && data.get('password') !== data.get('confirmPassword')) {
        throw new ApiError({ status: 400, code: 'validation_failed', errors: { confirmPassword: ['兩次輸入的密碼不一致。'] } })
      }
      await action(data)
      setSuccess(true)
    } catch (cause) {
      const failure = asApiError(cause)
      setError(failure)
      setRemaining(Math.min(3600, failure.retryAfter || (failure.problem.status === 429 ? 60 : 0)))
    } finally { setPending(false) }
  }
  return <form onSubmit={(event) => void submit(event)} aria-busy={pending}>
    {error && <ErrorNotice error={error} />}
    {success && successMessage && <div className="notice success" role="status">{successMessage}</div>}
    <FieldErrors value={error?.problem.errors ?? {}}><fieldset disabled={pending || (success && Boolean(successMessage))}>{children}</fieldset></FieldErrors>
    <button className="primary full" disabled={pending || remaining > 0 || (success && Boolean(successMessage))} type="submit">
      {pending ? '處理中…' : remaining ? `請稍候 ${remaining} 秒` : submitLabel}<span aria-hidden="true">↗</span>
    </button>
  </form>
}

export function Field({ name, label, type = 'text', autoComplete, hint, minLength, maxLength = 254 }: {
  name: string; label: string; type?: 'text' | 'email' | 'password'; autoComplete?: string
  hint?: string; minLength?: number; maxLength?: number
}) {
  const id = useId()
  const [visible, setVisible] = useState(false)
  const errors = useContext(FieldErrors)
  const error = Object.entries(errors).find(([key]) => key.toLowerCase() === name.toLowerCase())?.[1]?.join(' ')
  return <div className="field"><label htmlFor={id}>{label}</label>
    <div className={type === 'password' ? 'password-input' : ''}>
      <input id={id} name={name} type={type === 'password' && visible ? 'text' : type} required
        autoComplete={autoComplete} minLength={minLength} maxLength={maxLength}
        aria-invalid={Boolean(error)} aria-describedby={error || hint ? `${id}-hint` : undefined} />
      {type === 'password' && <button type="button" className="reveal" aria-label={`${visible ? '隱藏' : '顯示'}${label}`} aria-pressed={visible} onClick={() => setVisible(!visible)}>{visible ? '隱藏' : '顯示'}</button>}
    </div>
    {(error || hint) && <p id={`${id}-hint`} className={error ? 'field-error' : 'hint'}>{error ?? hint}</p>}
  </div>
}
