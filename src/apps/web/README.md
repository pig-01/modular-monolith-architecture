# Web frontend

Traditional Chinese account screens for the modular monolith, built with React,
React Router, TypeScript and Vite. Routes: `/login`, `/register`, `/verify-email`,
`/forgot-password`, `/reset-password`, and the protected `/dashboard`.

## Run the complete application

Use the repository's Docker Compose instructions. The browser entry point is
`https://localhost:8443`; Mailpit is `http://localhost:8025`. Authentication calls
use `/api/auth`, routed by the HTTPS proxy to the backend `/auth` endpoints.

## Frontend development

Requires Node.js 24+ and a running backend. From this directory:

```sh
npm ci
npm run dev
```

Vite reads `artifacts/certificates/https/tls.pem` and `tls.key` from the repository
root and serves `https://localhost:5173` when they exist. Generate the development
certificates using the root setup script and trust the development certificate.
Without them Vite can serve HTTP for UI work, but Secure authentication cookies
require HTTPS. The default API target is `https://localhost:7210`; set the server
process variable `API_PROXY_TARGET` if needed. Never put credentials in Vite
client environment variables.

The development CSP permits Vite's injected styles and HMR scripts. Production
HTML is served by nginx with a separate restrictive CSP and SPA route fallback;
`vite preview` is only a local asset preview, not the production server.

## Authentication behavior

JWT and refresh tokens remain in HttpOnly cookies. Only the CSRF request token
and the account profile are held in JavaScript memory. The shared fetch client
includes credentials, retries an unauthorized request once after refresh, renews
rejected CSRF tokens once, and preserves login state during temporary outages.
Refresh retries reuse the same operation ID. Web Locks serialize credential
changes across tabs; BroadcastChannel transmits status notifications only.
Tenant changes abort pending business requests and discard stale responses.

Email verification and password-reset links use fragment parameters `userId`
and `token`. They are consumed once before React mounts, removed from the URL,
and submitted only after an explicit user action. Refreshing the sanitized link
page requires reopening the original email link. No credentials are persisted
in localStorage or sessionStorage.

Supported browsers are current Chrome, Edge, Firefox and Safari over HTTPS with
Web Locks and BroadcastChannel support. Unsupported browsers display a clear
compatibility message.

## Validation

| Command | Purpose |
| --- | --- |
| `npm run build` | Typecheck app, tests and configuration; produce `dist/` |
| `npm run lint` | ESLint |
| `npm test` | Vitest + Testing Library component/client regressions |
| `npm run test:e2e` | Playwright against the real HTTPS API, SQL database and Mailpit |

Run `npx playwright install chromium` once before browser tests. Start the Compose
stack before `npm run test:e2e`. Defaults can be changed with `E2E_BASE_URL` and
`E2E_MAILPIT_URL`. To include the multi-tenant seed-account scenario, set
`E2E_ADMIN_EMAIL` and `E2E_ADMIN_PASSWORD` in the test process environment to an
account with at least two memberships; this scenario is reported as skipped if
those variables are absent. The registration scenario creates a unique account
in the actual test database and leaves its records for inspection.

Browser tests cover registration, explicit email confirmation, session restore,
simultaneous refresh across tabs, cross-tab logout, password reset, cookie flags,
CSP, CSRF rejection, and denied CORS origins. They save desktop/mobile screenshots
under ignored `test-results/`. Token-bearing traces and HAR recording are disabled.
Unit tests replace only the browser/network system boundary; backend security
is verified against the real server by Playwright.
