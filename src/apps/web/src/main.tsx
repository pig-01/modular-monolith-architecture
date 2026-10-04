import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { consumeEmailLink } from './auth/links'

const credentials = consumeEmailLink()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App credentials={credentials} />
  </StrictMode>,
)
