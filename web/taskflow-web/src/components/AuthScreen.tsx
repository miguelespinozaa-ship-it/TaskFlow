import { useState, type FormEvent } from 'react'
import { auth } from '../api/queries'
import { Button, ErrorText, Input } from './ui'

export function AuthScreen() {
  const [mode, setMode] = useState<'login' | 'register'>('login')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [error, setError] = useState<unknown>()
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(undefined)
    try {
      if (mode === 'login') await auth.login(email, password)
      else await auth.register(email, password, displayName)
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  return (
    <main className="mx-auto mt-[12vh] max-w-sm px-4">
      <h1 className="mb-6 text-center text-2xl font-bold">TaskFlow</h1>
      <form
        onSubmit={submit}
        className="flex flex-col gap-3 rounded-lg border border-slate-200 bg-white p-6 shadow-sm dark:border-slate-700 dark:bg-slate-900"
      >
        <h2 className="text-lg font-semibold">{mode === 'login' ? 'Iniciar sesión' : 'Crear cuenta'}</h2>
        {mode === 'register' && (
          <Input placeholder="Nombre" value={displayName} onChange={(e) => setDisplayName(e.target.value)} aria-label="Nombre" required />
        )}
        <Input type="email" placeholder="Email" value={email} onChange={(e) => setEmail(e.target.value)} aria-label="Email" required />
        <Input
          type="password"
          placeholder="Contraseña"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          aria-label="Contraseña"
          required
        />
        {mode === 'register' && <p className="text-xs text-slate-500">Mínimo 8 caracteres, con mayúscula, minúscula y número.</p>}
        <ErrorText error={error} />
        <Button type="submit" disabled={busy}>
          {busy ? '…' : mode === 'login' ? 'Entrar' : 'Registrarme'}
        </Button>
        <Button type="button" variant="ghost" onClick={() => setMode(mode === 'login' ? 'register' : 'login')}>
          {mode === 'login' ? '¿No tenés cuenta? Registrate' : '¿Ya tenés cuenta? Iniciá sesión'}
        </Button>
        {mode === 'login' && (
          <p className="text-center text-xs text-slate-500">
            Demo: demo@ · member@ · viewer@taskflow.dev — contraseña <code>Demo1234</code>
          </p>
        )}
      </form>
    </main>
  )
}
