import { useEffect, useSyncExternalStore } from 'react'
import { Icon } from './icons'
import { cx } from './cx'

type Kind = 'success' | 'error'

interface ToastItem {
  id: number
  kind: Kind
  message: string
}

// Store a nivel de módulo (sin contexto ni librería): cualquier componente llama a toast.success()
// o toast.error() y <Toaster /> se entera por useSyncExternalStore.
let items: ToastItem[] = []
let nextId = 1
const listeners = new Set<() => void>()
const timers = new Map<number, ReturnType<typeof setTimeout>>()
const MAX_VISIBLE = 4

function emit(next: ToastItem[]) {
  items = next
  listeners.forEach((l) => l())
}

function dismiss(id: number) {
  clearTimeout(timers.get(id))
  timers.delete(id)
  emit(items.filter((t) => t.id !== id))
}

function push(kind: Kind, message: string) {
  // El mismo aviso repetido (doble click, reintentos) no se apila: se renueva el que ya está.
  const existing = items.find((t) => t.kind === kind && t.message === message)
  const id = existing?.id ?? nextId++
  if (!existing) {
    // Un solo aviso de éxito a la vez: crear varias tareas seguidas no apila una torre de avisos.
    const kept = kind === 'success' ? items.filter((t) => t.kind !== 'success') : items
    items.filter((t) => !kept.includes(t)).forEach((t) => clearTimeout(timers.get(t.id)))
    const next = [...kept, { id, kind, message }]
    next.slice(0, -MAX_VISIBLE).forEach((t) => clearTimeout(timers.get(t.id)))
    emit(next.slice(-MAX_VISIBLE))
  }
  clearTimeout(timers.get(id))
  // Los errores quedan más tiempo: hay que llegar a leerlos.
  timers.set(id, setTimeout(() => dismiss(id), kind === 'error' ? 7000 : 3200))
}

const messageOf = (error: unknown) => (error instanceof Error ? error.message : String(error))

export const toast = {
  success: (message: string) => push('success', message),
  error: (error: unknown, prefix?: string) => push('error', prefix ? `${prefix}: ${messageOf(error)}` : messageOf(error)),
}

/** Avisa con un toast cada vez que una mutación (o cualquier otra cosa) deja un error nuevo. */
export function useErrorToast(error: unknown, prefix?: string) {
  useEffect(() => {
    if (error) toast.error(error, prefix)
  }, [error, prefix])
}

const subscribe = (listener: () => void) => {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

/** Los avisos van abajo a la izquierda: abajo a la derecha taparían el panel de detalle. */
export function Toaster() {
  const toasts = useSyncExternalStore(subscribe, () => items)
  return (
    <div
      role="region"
      aria-label="Avisos"
      className="pointer-events-none fixed inset-x-0 bottom-0 z-50 flex flex-col items-center gap-2 p-4 sm:items-start"
    >
      {toasts.map((t) => (
        <div
          key={t.id}
          role={t.kind === 'error' ? 'alert' : 'status'}
          className={cx(
            'pointer-events-auto flex w-full max-w-sm animate-toast items-start gap-2.5 rounded-lg border bg-panel/95 py-2.5 pr-2 pl-3 text-sm shadow-card backdrop-blur-md',
            t.kind === 'error' ? 'border-hot/60' : 'border-neon/50',
          )}
        >
          <span
            className={cx(
              'mt-0.5 flex size-5 shrink-0 items-center justify-center rounded-full',
              t.kind === 'error' ? 'bg-hot/20 text-hot' : 'bg-neon/15 text-neon',
            )}
          >
            <Icon name={t.kind === 'error' ? 'alert' : 'check'} className="size-3" />
          </span>
          <p className="min-w-0 flex-1 break-words text-ink">{t.message}</p>
          <button
            type="button"
            onClick={() => dismiss(t.id)}
            className="rounded p-1 text-dim transition duration-150 hover:bg-raised hover:text-ink focus-visible:outline-2 focus-visible:outline-neon"
          >
            <Icon name="x" className="size-3.5" />
            <span className="sr-only">Descartar aviso</span>
          </button>
        </div>
      ))}
    </div>
  )
}
