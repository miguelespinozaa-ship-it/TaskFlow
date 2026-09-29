import { useEffect, useState, type FormEvent } from 'react'
import {
  api,
  refreshSession,
  subscribeSession,
  type AuthResponse,
  type Project,
  type Task,
  type TaskPriority,
  type TaskStatus,
  type WorkspaceSummary,
} from './api'

const COLUMNS: { status: TaskStatus; label: string }[] = [
  { status: 'Todo', label: 'Por hacer' },
  { status: 'InProgress', label: 'En progreso' },
  { status: 'InReview', label: 'En revisión' },
  { status: 'Done', label: 'Hecho' },
]

const PRIORITIES: TaskPriority[] = ['Low', 'Medium', 'High', 'Urgent']

export default function App() {
  const [session, setSession] = useState<AuthResponse | null>(null)
  const [booting, setBooting] = useState(true)

  useEffect(() => {
    subscribeSession(setSession)
    // Al cargar: si hay cookie de refresh válida, recuperamos la sesión sin pedir login.
    refreshSession().finally(() => setBooting(false))
  }, [])

  if (booting) return <p className="app muted">Cargando…</p>
  if (!session) return <AuthScreen />
  // key: al cambiar de workspace se remonta todo el board con datos del tenant nuevo.
  return <Workspace key={session.workspace.id} session={session} />
}

function AuthScreen() {
  const [mode, setMode] = useState<'login' | 'register'>('login')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [error, setError] = useState<string>()
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(undefined)
    try {
      if (mode === 'login') await api.login(email, password)
      else await api.register(email, password, displayName)
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="auth">
      <h1>TaskFlow</h1>
      <form className="auth-form" onSubmit={submit}>
        <h2>{mode === 'login' ? 'Iniciar sesión' : 'Crear cuenta'}</h2>
        {mode === 'register' && (
          <input placeholder="Nombre" value={displayName} onChange={(e) => setDisplayName(e.target.value)} aria-label="Nombre" />
        )}
        <input type="email" placeholder="Email" value={email} onChange={(e) => setEmail(e.target.value)} aria-label="Email" />
        <input
          type="password"
          placeholder="Contraseña"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          aria-label="Contraseña"
        />
        {mode === 'register' && <p className="muted small">Mínimo 8 caracteres, con mayúscula, minúscula y número.</p>}
        {error && <p className="error small" role="alert">{error}</p>}
        <button type="submit" disabled={busy}>
          {busy ? '…' : mode === 'login' ? 'Entrar' : 'Registrarme'}
        </button>
        <button type="button" className="link" onClick={() => setMode(mode === 'login' ? 'register' : 'login')}>
          {mode === 'login' ? '¿No tenés cuenta? Registrate' : '¿Ya tenés cuenta? Iniciá sesión'}
        </button>
        {mode === 'login' && (
          <p className="muted small">Demo: demo@taskflow.dev · member@taskflow.dev · viewer@taskflow.dev — contraseña Demo1234</p>
        )}
      </form>
    </div>
  )
}

function Workspace({ session }: { session: AuthResponse }) {
  const [workspaces, setWorkspaces] = useState<WorkspaceSummary[]>([])
  const [projects, setProjects] = useState<Project[]>([])
  const [projectId, setProjectId] = useState<string>()
  const [tasks, setTasks] = useState<Task[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string>()
  const canWrite = session.workspace.role !== 'Viewer'

  useEffect(() => {
    api.listWorkspaces().then(setWorkspaces).catch(() => {})
    api
      .listProjects()
      .then((list) => {
        setProjects(list)
        setProjectId(list[0]?.id)
        if (list.length === 0) setLoading(false)
      })
      .catch((e: Error) => {
        setError(e.message)
        setLoading(false)
      })
  }, [])

  useEffect(() => {
    if (!projectId) return
    setLoading(true)
    api
      .listTasks(projectId)
      .then(setTasks)
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false))
  }, [projectId])

  const project = projects.find((p) => p.id === projectId)

  return (
    <div className="app">
      <header className="topbar">
        <h1>TaskFlow</h1>
        <div className="topbar-right">
          <select
            value={session.workspace.id}
            onChange={(e) => api.switchWorkspace(e.target.value).catch((err: Error) => setError(err.message))}
            aria-label="Workspace"
          >
            {(workspaces.length ? workspaces : [session.workspace]).map((w) => (
              <option key={w.id} value={w.id}>
                {w.name} ({w.role})
              </option>
            ))}
          </select>
          <span className="muted small">{session.user.displayName}</span>
          <button type="button" className="secondary" onClick={() => api.logout()}>
            Salir
          </button>
        </div>
      </header>

      {error && <p className="error" role="alert">{error}</p>}

      <div className="toolbar">
        {projects.length > 0 && (
          <select value={projectId} onChange={(e) => setProjectId(e.target.value)} aria-label="Proyecto">
            {projects.map((p) => (
              <option key={p.id} value={p.id}>
                {p.keyPrefix} · {p.name}
              </option>
            ))}
          </select>
        )}
        {canWrite && (
          <CreateProjectForm
            onCreated={(p) => {
              setProjects((prev) => [...prev, p])
              setProjectId(p.id)
            }}
          />
        )}
        {!canWrite && <span className="badge">Solo lectura (Viewer)</span>}
      </div>

      {project && (
        <>
          {canWrite && (
            <CreateTaskForm projectId={project.id} onCreated={(task) => setTasks((prev) => [...prev, task])} />
          )}
          {loading ? <p className="muted">Cargando tareas…</p> : <Board tasks={tasks} keyPrefix={project.keyPrefix} />}
        </>
      )}

      {!loading && projects.length === 0 && (
        <p className="muted">Este workspace no tiene proyectos todavía.{canWrite && ' Creá uno arriba.'}</p>
      )}
    </div>
  )
}

function CreateProjectForm({ onCreated }: { onCreated: (p: Project) => void }) {
  const [name, setName] = useState('')
  const [keyPrefix, setKeyPrefix] = useState('')
  const [error, setError] = useState<string>()

  async function submit(e: FormEvent) {
    e.preventDefault()
    setError(undefined)
    try {
      onCreated(await api.createProject(name, keyPrefix))
      setName('')
      setKeyPrefix('')
    } catch (err) {
      setError((err as Error).message)
    }
  }

  return (
    <form className="inline-form" onSubmit={submit}>
      <input placeholder="Nuevo proyecto" value={name} onChange={(e) => setName(e.target.value)} aria-label="Nombre del proyecto" />
      <input
        placeholder="PREFIJO"
        value={keyPrefix}
        onChange={(e) => setKeyPrefix(e.target.value.toUpperCase())}
        maxLength={10}
        className="prefix"
        aria-label="Prefijo"
      />
      <button type="submit" className="secondary">
        + Proyecto
      </button>
      {error && <span className="error small" role="alert">{error}</span>}
    </form>
  )
}

function Board({ tasks, keyPrefix }: { tasks: Task[]; keyPrefix: string }) {
  return (
    <div className="board">
      {COLUMNS.map((col) => {
        const items = tasks.filter((t) => t.status === col.status).sort((a, b) => a.position - b.position)
        return (
          <section key={col.status} className="column">
            <h2>
              {col.label} <span className="count">{items.length}</span>
            </h2>
            {items.length === 0 && <p className="muted small">Sin tareas</p>}
            {items.map((t) => (
              <article key={t.id} className="card">
                <div className="card-head">
                  <span className="muted small">{keyPrefix}</span>
                  <span className={`badge badge-${t.priority.toLowerCase()}`}>{t.priority}</span>
                </div>
                <h3>{t.title}</h3>
                {t.description && <p className="small">{t.description}</p>}
              </article>
            ))}
          </section>
        )
      })}
    </div>
  )
}

function CreateTaskForm({ projectId, onCreated }: { projectId: string; onCreated: (t: Task) => void }) {
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [priority, setPriority] = useState<TaskPriority>('Medium')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string>()

  async function submit(e: FormEvent) {
    e.preventDefault()
    setSaving(true)
    setError(undefined)
    try {
      const task = await api.createTask(projectId, {
        title,
        description: description || undefined,
        priority,
      })
      onCreated(task)
      setTitle('')
      setDescription('')
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setSaving(false)
    }
  }

  return (
    <form className="create-form" onSubmit={submit}>
      <input
        placeholder="Nueva tarea…"
        value={title}
        onChange={(e) => setTitle(e.target.value)}
        maxLength={200}
        aria-label="Título"
      />
      <input
        placeholder="Descripción (opcional)"
        value={description}
        onChange={(e) => setDescription(e.target.value)}
        aria-label="Descripción"
      />
      <select value={priority} onChange={(e) => setPriority(e.target.value as TaskPriority)} aria-label="Prioridad">
        {PRIORITIES.map((p) => (
          <option key={p}>{p}</option>
        ))}
      </select>
      <button type="submit" disabled={saving}>
        {saving ? 'Creando…' : 'Crear'}
      </button>
      {error && <p className="error small" role="alert">{error}</p>}
    </form>
  )
}
