import { useCallback, useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { onSessionChange, refreshSession } from './api/client'
import { auth, useWorkspaces } from './api/queries'
import type { AuthResponse } from './api/types'
import { ActivityList } from './components/ActivityFeed'
import { AuthScreen } from './components/AuthScreen'
import { BoardView } from './components/BoardView'
import { MembersView } from './components/MembersView'
import { SearchView } from './components/SearchView'
import { TaskDetail } from './components/TaskDetail'
import { Button, cx, ErrorText, Select, Spinner } from './components/ui'

export default function App() {
  const qc = useQueryClient()
  const [session, setSession] = useState<AuthResponse | null>(null)
  const [booting, setBooting] = useState(true)

  const currentWorkspace = useRef<string | undefined>(undefined)

  useEffect(() => {
    const unsubscribe = onSessionChange((next) => {
      // Cambió el tenant (o se cerró la sesión): la cache tiene datos de OTRO workspace. Se descarta
      // entera para que nunca se pinte, ni por un instante, información del tenant anterior.
      if (currentWorkspace.current !== next?.workspace.id) qc.clear()
      currentWorkspace.current = next?.workspace.id
      setSession(next)
    })
    // Al cargar: si hay cookie de refresh válida, recuperamos la sesión sin pedir login.
    refreshSession().finally(() => setBooting(false))
    return () => {
      unsubscribe()
    }
  }, [qc])

  if (booting) return <Spinner />
  if (!session) return <AuthScreen />
  // key: al cambiar de workspace se remonta todo con los datos del tenant nuevo.
  return <Shell key={session.workspace.id} session={session} />
}

type Tab = 'board' | 'search' | 'activity' | 'members'

const TABS: { id: Tab; label: string }[] = [
  { id: 'board', label: 'Board' },
  { id: 'search', label: 'Buscar' },
  { id: 'activity', label: 'Actividad' },
  { id: 'members', label: 'Miembros' },
]

function Shell({ session }: { session: AuthResponse }) {
  const [tab, setTab] = useState<Tab>('board')
  const [openTaskId, setOpenTaskId] = useState<string | null>(null)
  const [switchError, setSwitchError] = useState<unknown>()
  const { data: workspaces } = useWorkspaces()
  const role = session.workspace.role
  const canWrite = role !== 'Viewer' // la UI solo oculta; quien decide es la API (403)
  const isAdmin = role === 'Owner' || role === 'Admin'
  const closeTask = useCallback(() => setOpenTaskId(null), [])

  return (
    <div className="min-h-screen">
      <header className="sticky top-0 z-30 border-b border-line bg-panel/80 backdrop-blur-md">
        <div className="mx-auto flex max-w-7xl flex-wrap items-center gap-3 px-4 py-3">
          <h1 className="logo text-xl font-bold tracking-tight">TaskFlow</h1>
          <nav className="flex gap-1" aria-label="Secciones">
            {TABS.map((t) => (
              <button
                key={t.id}
                type="button"
                onClick={() => setTab(t.id)}
                aria-current={tab === t.id ? 'page' : undefined}
                className={cx(
                  // Subrayado neón que crece desde el centro en la pestaña activa (y al pasar el mouse).
                  'relative rounded-md px-3 py-1.5 text-sm transition duration-150 after:absolute after:inset-x-2 after:-bottom-px after:h-0.5 after:origin-center after:rounded-full after:bg-neon after:shadow-neon after:transition-transform after:duration-200',
                  tab === t.id
                    ? 'font-semibold text-neon after:scale-x-100'
                    : 'text-dim after:scale-x-0 hover:bg-raised hover:text-ink hover:after:scale-x-50',
                )}
              >
                {t.label}
              </button>
            ))}
          </nav>
          <div className="ml-auto flex items-center gap-2">
            <Select
              value={session.workspace.id}
              onChange={(e) => auth.switchWorkspace(e.target.value).catch(setSwitchError)}
              aria-label="Workspace"
            >
              {(workspaces ?? [session.workspace]).map((w) => (
                <option key={w.id} value={w.id}>
                  {w.name} ({w.role})
                </option>
              ))}
            </Select>
            <span className="hidden text-sm text-dim sm:inline">{session.user.displayName}</span>
            <Button variant="secondary" onClick={() => auth.logout()}>
              Salir
            </Button>
          </div>
        </div>
      </header>

      {/* key: cada cambio de pestaña vuelve a montar el contenido y dispara la animación de entrada. */}
      <main key={tab} className="mx-auto max-w-7xl animate-rise px-4 py-5">
        <ErrorText error={switchError} />
        {tab === 'board' && <BoardView canWrite={canWrite} onOpen={setOpenTaskId} />}
        {tab === 'search' && <SearchView onOpen={setOpenTaskId} />}
        {tab === 'activity' && <ActivityList />}
        {tab === 'members' && <MembersView isAdmin={isAdmin} />}
      </main>

      {openTaskId && (
        <TaskDetail taskId={openTaskId} user={session.user} canWrite={canWrite} isAdmin={isAdmin} onClose={closeTask} />
      )}
    </div>
  )
}
