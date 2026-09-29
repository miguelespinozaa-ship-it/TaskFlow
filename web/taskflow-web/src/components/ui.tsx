import type { ComponentProps, InputHTMLAttributes, ReactNode, SelectHTMLAttributes, TextareaHTMLAttributes } from 'react'
import type { Label, TaskPriority } from '../api/types'

const cx = (...classes: (string | false | null | undefined)[]) => classes.filter(Boolean).join(' ')

type Variant = 'primary' | 'secondary' | 'ghost' | 'danger'

const variants: Record<Variant, string> = {
  primary: 'bg-indigo-600 text-white hover:bg-indigo-500 disabled:bg-indigo-400',
  secondary:
    'border border-slate-300 bg-white text-slate-800 hover:bg-slate-50 dark:border-slate-600 dark:bg-slate-800 dark:text-slate-100 dark:hover:bg-slate-700',
  ghost: 'text-slate-600 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-slate-800',
  danger: 'text-red-600 hover:bg-red-50 dark:text-red-400 dark:hover:bg-red-950',
}

export function Button({ variant = 'primary', className, ...props }: ComponentProps<'button'> & { variant?: Variant }) {
  return (
    <button
      className={cx(
        'inline-flex items-center justify-center gap-1 rounded-md px-3 py-1.5 text-sm font-medium transition disabled:cursor-not-allowed disabled:opacity-60',
        variants[variant],
        className,
      )}
      {...props}
    />
  )
}

const field =
  'rounded-md border border-slate-300 bg-white px-2.5 py-1.5 text-sm text-slate-900 placeholder:text-slate-400 focus:border-indigo-500 focus:outline-none focus:ring-2 focus:ring-indigo-500/30 disabled:bg-slate-100 dark:border-slate-600 dark:bg-slate-900 dark:text-slate-100 dark:disabled:bg-slate-800'

export const Input = ({ className, ...props }: InputHTMLAttributes<HTMLInputElement>) => (
  <input className={cx(field, className)} {...props} />
)

export const Select = ({ className, ...props }: SelectHTMLAttributes<HTMLSelectElement>) => (
  <select className={cx(field, className)} {...props} />
)

export const Textarea = ({ className, ...props }: TextareaHTMLAttributes<HTMLTextAreaElement>) => (
  <textarea className={cx(field, 'min-h-20', className)} {...props} />
)

const priorityStyles: Record<TaskPriority, string> = {
  Low: 'bg-slate-100 text-slate-600 dark:bg-slate-800 dark:text-slate-300',
  Medium: 'bg-sky-100 text-sky-700 dark:bg-sky-950 dark:text-sky-300',
  High: 'bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-300',
  Urgent: 'bg-red-100 text-red-700 dark:bg-red-950 dark:text-red-300',
}

const priorityLabels: Record<TaskPriority, string> = { Low: 'Baja', Medium: 'Media', High: 'Alta', Urgent: 'Urgente' }

export const PriorityBadge = ({ priority }: { priority: TaskPriority }) => (
  <span className={cx('rounded-full px-2 py-0.5 text-xs font-medium', priorityStyles[priority])}>{priorityLabels[priority]}</span>
)

export const LabelChip = ({ label, onClick, active = true }: { label: Label; onClick?: () => void; active?: boolean }) => (
  <button
    type="button"
    onClick={onClick}
    disabled={!onClick}
    aria-pressed={onClick ? active : undefined}
    className={cx(
      'inline-flex items-center gap-1 rounded-full border px-2 py-0.5 text-xs',
      active ? 'border-transparent text-white' : 'border-slate-300 bg-transparent text-slate-500 dark:border-slate-600',
      onClick && 'cursor-pointer',
    )}
    style={active ? { backgroundColor: label.color } : undefined}
  >
    {label.name}
  </button>
)

export const Spinner = ({ label = 'Cargando…' }: { label?: string }) => (
  <p role="status" className="py-6 text-center text-sm text-slate-500">
    {label}
  </p>
)

export const ErrorText = ({ error }: { error: unknown }) =>
  error ? (
    <p role="alert" className="text-sm text-red-600 dark:text-red-400">
      {error instanceof Error ? error.message : String(error)}
    </p>
  ) : null

export const Empty = ({ children }: { children: ReactNode }) => (
  <p className="py-4 text-center text-sm text-slate-500 dark:text-slate-400">{children}</p>
)

export function Avatar({ name, size = 'sm' }: { name: string; size?: 'sm' | 'md' }) {
  const initials = name
    .split(/\s+/)
    .map((p) => p[0])
    .slice(0, 2)
    .join('')
    .toUpperCase()
  return (
    <span
      title={name}
      className={cx(
        'inline-flex shrink-0 items-center justify-center rounded-full bg-indigo-100 font-semibold text-indigo-700 dark:bg-indigo-900 dark:text-indigo-200',
        size === 'sm' ? 'h-6 w-6 text-[10px]' : 'h-8 w-8 text-xs',
      )}
    >
      {initials}
    </span>
  )
}

export const formatDate = (iso: string) =>
  new Date(iso).toLocaleDateString('es', { day: 'numeric', month: 'short' })

export const formatDateTime = (iso: string) =>
  new Date(iso).toLocaleString('es', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' })

export { cx }
