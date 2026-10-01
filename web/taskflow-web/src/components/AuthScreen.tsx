import { useState, type FormEvent } from 'react'
import { auth } from '../api/queries'
import { Icon, LogoMark, type IconName } from './icons'
import { Button, cx, ErrorText, Input } from './ui'

const DEMO_ACCOUNTS = [
  { role: 'Owner', email: 'demo@taskflow.dev', hint: 'todo' },
  { role: 'Member', email: 'member@taskflow.dev', hint: 'edita' },
  { role: 'Viewer', email: 'viewer@taskflow.dev', hint: 'solo lee' },
]

const FEATURES: { icon: IconName; title: string; text: string }[] = [
  { icon: 'board', title: 'Board con drag & drop', text: 'Mueve tareas entre columnas con el mouse o el teclado.' },
  { icon: 'shield', title: 'Workspaces aislados', text: 'Cada equipo ve solo lo suyo, con roles y permisos.' },
  { icon: 'activity', title: 'Historial de cambios', text: 'Quién cambió qué y cuándo, en cada tarea.' },
]

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
    <main className="mx-auto grid min-h-dvh max-w-5xl animate-rise items-center gap-10 px-4 py-10 lg:grid-cols-[1.1fr_1fr] lg:gap-16">
      <section className="flex flex-col items-center text-center lg:items-start lg:text-left">
        <div className="flex items-center gap-3">
          <LogoMark className="size-10 lg:size-12" />
          <h1 className="logo text-4xl font-bold tracking-tight lg:text-5xl">TaskFlow</h1>
        </div>
        <p className="mt-3 font-display text-xs tracking-widest text-dim uppercase">gestión de proyectos multi-tenant</p>
        <p className="mt-6 hidden max-w-md text-2xl leading-snug font-semibold text-ink lg:block">
          El trabajo de tu equipo, <span className="text-neon">ordenado</span> y a la vista.
        </p>
        <ul className="stagger mt-8 hidden flex-col gap-4 lg:flex">
          {FEATURES.map((f) => (
            <li key={f.title} className="flex items-start gap-3">
              <span className="flex size-9 shrink-0 items-center justify-center rounded-lg border border-line bg-panel/70 text-neon">
                <Icon name={f.icon} />
              </span>
              <div>
                <p className="text-sm font-medium text-ink">{f.title}</p>
                <p className="text-sm text-dim">{f.text}</p>
              </div>
            </li>
          ))}
        </ul>
      </section>

      <form
        onSubmit={submit}
        className="mx-auto flex w-full max-w-sm flex-col gap-4 self-start rounded-2xl border border-line bg-panel/80 p-6 shadow-violet backdrop-blur-sm lg:self-center"
      >
        <div>
          <h2 className="text-xl font-semibold">{mode === 'login' ? 'Iniciar sesión' : 'Crear cuenta'}</h2>
          <p className="mt-1 text-sm text-dim">{mode === 'login' ? 'Entra a tu workspace.' : 'Se crea un workspace personal para ti.'}</p>
        </div>
        {mode === 'register' && (
          <Field label="Nombre">
            <Input placeholder="Cómo te ven los demás" value={displayName} onChange={(e) => setDisplayName(e.target.value)} aria-label="Nombre" autoComplete="name" required />
          </Field>
        )}
        <Field label="Email">
          <Input type="email" placeholder="nombre@empresa.com" value={email} onChange={(e) => setEmail(e.target.value)} aria-label="Email" autoComplete="email" required />
        </Field>
        <Field label="Contraseña" hint={mode === 'register' ? 'Mínimo 8 caracteres, con mayúscula, minúscula y número.' : undefined}>
          <Input
            type="password"
            placeholder="••••••••"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            aria-label="Contraseña"
            autoComplete={mode === 'login' ? 'current-password' : 'new-password'}
            required
          />
        </Field>
        <ErrorText error={error} />
        <Button type="submit" disabled={busy} className="py-2">
          {busy ? '…' : mode === 'login' ? 'Entrar' : 'Registrarme'}
        </Button>
        <Button type="button" variant="ghost" onClick={() => setMode(mode === 'login' ? 'register' : 'login')}>
          {mode === 'login' ? '¿No tienes cuenta? Regístrate' : '¿Ya tienes cuenta? Inicia sesión'}
        </Button>
        {mode === 'login' && (
          <div className="border-t border-line/70 pt-4">
            <p className="mb-2 flex items-center gap-1.5 text-xs text-dim">
              <Icon name="bolt" className="size-3.5 text-amber" />
              Cuentas de demo — toca una para completar el formulario
            </p>
            <div className="grid grid-cols-3 gap-2">
              {DEMO_ACCOUNTS.map((a) => (
                <button
                  key={a.role}
                  type="button"
                  title={a.email}
                  onClick={() => {
                    setEmail(a.email)
                    setPassword('Demo1234')
                    setError(undefined)
                  }}
                  className={cx(
                    'flex flex-col items-center rounded-lg border px-2 py-1.5 transition duration-150 hover:border-violet hover:bg-raised/60 focus-visible:outline-2 focus-visible:outline-neon active:scale-95',
                    email === a.email ? 'border-neon/60 bg-neon/10' : 'border-line bg-void/40',
                  )}
                >
                  <span className="font-display text-xs font-semibold text-ink">{a.role}</span>
                  <span className="text-[11px] text-dim">{a.hint}</span>
                </button>
              ))}
            </div>
          </div>
        )}
      </form>
    </main>
  )
}

const Field = ({ label, hint, children }: { label: string; hint?: string; children: React.ReactNode }) => (
  <label className="flex flex-col gap-1.5 text-sm">
    <span className="text-dim">{label}</span>
    {children}
    {hint && <span className="text-xs text-dim">{hint}</span>}
  </label>
)
