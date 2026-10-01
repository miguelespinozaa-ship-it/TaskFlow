import { useCallback, useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { onSessionChange, refreshSession } from './api/client'
import { auth, useWorkspaces } from './api/queries'
import type { AuthResponse, WorkspaceRole } from './api/types'
import { ActivityList } from './components/ActivityFeed'
import { AuthScreen } from './components/AuthScreen'
import { BoardView } from './components/BoardView'
import { MembersView } from './components/MembersView'
import { SearchView } from './components/SearchView'
import { TaskDetail } from './components/TaskDetail'
import { Icon, LogoMark, type IconName } from './components/icons'
import { toast, Toaster } from './components/toast'
import { Avatar, Button, cx, Select, Spinner } from './components/ui'

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

  return (
    <>
      {booting ? (
        <div className="flex min-h-dvh flex-col items-center justify-center gap-2">
          <p className="logo text-3xl font-bold tracking-tight">TaskFlow</p>
          <Spinner label="Iniciando…" />
        </div>
      ) : !session ? (
        <AuthScreen />
      ) : (
        // key: al cambiar de workspace se remonta todo con los datos del tenant nuevo.
        <Shell key={session.workspace.id} session={session} />
      )}
      {/* Fuera del Shell: los avisos sobreviven al cambio de workspace y al login. */}
      <Toaster />
    </>
  )
}

type Tab = 'board' | 'search' | 'activity' | 'members'

const TABS: { id: Tab; label: string; icon: IconName }[] = [
  { id: 'board', label: 'Board', icon: 'board' },
  { id: 'search', label: 'Buscar', icon: 'search' },
  { id: 'activity', label: 'Actividad', icon: 'activity' },
  { id: 'members', label: 'Miembros', icon: 'users' },
]

const roleLabels: Record<WorkspaceRole, string> = { Owner: 'Owner', Admin: 'Admin', Member: 'Miembro', Viewer: 'Solo lectura' }

function Shell({ session }: { session: AuthResponse }) {
  const [tab, setTab] = useState<Tab>('board')
  const [openTaskId, setOpenTaskId] = useState<string | null>(null)
  const { data: workspaces } = useWorkspaces()
  const role = session.workspace.role
  const canWrite = role !== 'Viewer' // la UI solo oculta; quien decide es la API (403)
  const isAdmin = role === 'Owner' || role === 'Admin'

  // Quién abrió el detalle (una tarjeta del board o una fila de la búsqueda), para devolverle el foco.
  const opener = useRef<{ element: Element | null; taskId: string } | null>(null)
  const openTask = useCallback((taskId: string) => {
    opener.current = { element: document.activeElement, taskId }
    setOpenTaskId(taskId)
  }, [])
  const closeTask = useCallback(() => setOpenTaskId(null), [])

  useEffect(() => {
    if (openTaskId || !opener.current) return
    const { element, taskId } = opener.current
    opener.current = null
    // Si la tarea cambió de columna desde el detalle, su tarjeta se volvió a montar: el elemento
    // guardado ya no está en el documento y se la busca por id.
    const target = element?.isConnected ? element : document.querySelector(`[data-task-id="${taskId}"]`)
    if (target instanceof HTMLElement) target.focus()
  }, [openTaskId])

  return (
    <div className="min-h-dvh">
      <header className="sticky top-0 z-30 border-b border-line bg-panel/80 backdrop-blur-md after:absolute after:inset-x-0 after:-bottom-px after:h-px after:bg-gradient-to-r after:from-transparent after:via-neon/60 after:to-transparent">
        <div className="mx-auto flex max-w-7xl flex-wrap items-center gap-x-4 gap-y-1 px-4 pt-2.5 md:py-2.5">
          <div className="flex items-center gap-2">
            <LogoMark />
            <h1 className="logo text-xl font-bold tracking-tight">TaskFlow</h1>
          </div>

          {/* En móvil las pestañas bajan a una segunda fila a todo el ancho. */}
          <nav className="order-last -mx-1 flex w-full gap-1 md:order-none md:mx-0 md:w-auto" aria-label="Secciones">
            {TABS.map((t) => (
              <button
                key={t.id}
                type="button"
                onClick={() => setTab(t.id)}
                aria-current={tab === t.id ? 'page' : undefined}
                className={cx(
                  // Subrayado neón que crece desde el centro en la pestaña activa (y al pasar el mouse).
                  'relative flex flex-1 flex-col items-center justify-center gap-0.5 rounded-md px-1 pt-1.5 pb-2 text-xs transition duration-150 md:flex-none md:flex-row md:gap-1.5 md:px-3 md:py-1.5 md:text-sm',
                  'after:absolute after:inset-x-2 after:bottom-0 after:h-0.5 after:origin-center after:rounded-full after:bg-neon after:shadow-neon after:transition-transform after:duration-200 md:after:-bottom-px',
                  'focus-visible:outline-2 focus-visible:outline-neon',
                  tab === t.id
                    ? 'bg-neon/10 font-semibold text-neon after:scale-x-100'
                    : 'text-dim after:scale-x-0 hover:bg-raised hover:text-ink hover:after:scale-x-50',
                )}
              >
                <Icon name={t.icon} />
                {t.label}
              </button>
            ))}
          </nav>

          <div className="ml-auto flex min-w-0 items-center gap-2">
            <Select
              value={session.workspace.id}
              onChange={(e) =>
                auth
                  .switchWorkspace(e.target.value)
                  .then((s) => toast.success(`Ahora estás en «${s.workspace.name}»`))
                  .catch((err) => toast.error(err, 'No se pudo cambiar de workspace'))
              }
              aria-label="Workspace"
              className="max-w-40 truncate sm:max-w-56"
            >
              {(workspaces ?? [session.workspace]).map((w) => (
                <option key={w.id} value={w.id}>
                  {w.name} ({w.role})
                </option>
              ))}
            </Select>
            <div className="hidden items-center gap-2 rounded-full border border-line bg-raised/40 p-0.5 sm:flex lg:pr-3">
              <Avatar name={session.user.displayName} size="md" />
              <div className="hidden leading-tight lg:block">
                <p className="max-w-40 truncate text-sm font-medium text-ink">{session.user.displayName}</p>
                <p className={cx('font-display text-[10px] tracking-wider uppercase', canWrite ? 'text-mint' : 'text-violet')}>{roleLabels[role]}</p>
              </div>
            </div>
            <Button variant="ghost" onClick={() => auth.logout()} className="px-2 sm:px-3">
              <Icon name="logout" />
              <span className="sr-only sm:not-sr-only">Salir</span>
            </Button>
          </div>
        </div>
      </header>

      {/* key: cada cambio de pestaña vuelve a montar el contenido y dispara la animación de entrada. */}
      <main key={tab} className="mx-auto max-w-7xl animate-rise px-4 py-5">
        {tab === 'board' && <BoardView canWrite={canWrite} onOpen={openTask} />}
        {tab === 'search' && <SearchView onOpen={openTask} />}
        {tab === 'activity' && <ActivityList />}
        {tab === 'members' && <MembersView isAdmin={isAdmin} currentUserId={session.user.id} />}
      </main>

      {openTaskId && (
        <TaskDetail taskId={openTaskId} user={session.user} canWrite={canWrite} isAdmin={isAdmin} onClose={closeTask} />
      )}
    </div>
  )
}
