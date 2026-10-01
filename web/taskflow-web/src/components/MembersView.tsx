import { useState, type FormEvent } from 'react'
import { useAddMember, useMembers } from '../api/queries'
import type { WorkspaceRole } from '../api/types'
import { Icon } from './icons'
import { toast, useErrorToast } from './toast'
import { Avatar, Button, cx, ErrorText, formatDate, Input, ListSkeleton, Select } from './ui'

const roleLabels: Record<WorkspaceRole, string> = { Owner: 'Owner', Admin: 'Admin', Member: 'Miembro', Viewer: 'Solo lectura' }

const roleStyles: Record<WorkspaceRole, string> = {
  Owner: 'border-neon/50 bg-neon/10 text-neon',
  Admin: 'border-mint/40 bg-mint/10 text-mint',
  Member: 'border-violet/50 bg-violet/15 text-violet',
  Viewer: 'border-line bg-raised/40 text-dim',
}

export function MembersView({ isAdmin, currentUserId }: { isAdmin: boolean; currentUserId: string }) {
  const { data: members = [], isLoading, error } = useMembers()
  const add = useAddMember()
  const [email, setEmail] = useState('')
  const [role, setRole] = useState<Exclude<WorkspaceRole, 'Owner'>>('Member')
  useErrorToast(add.error, 'No se pudo agregar')

  function submit(e: FormEvent) {
    e.preventDefault()
    add.mutate(
      { email, role },
      {
        onSuccess: (m) => {
          setEmail('')
          toast.success(`${m.displayName} se sumó como ${roleLabels[m.role]}`)
        },
      },
    )
  }

  return (
    <div className="flex max-w-2xl flex-col gap-4">
      {isAdmin && (
        <form onSubmit={submit} className="flex flex-col gap-2 rounded-xl border border-line bg-panel/60 p-3 backdrop-blur-sm">
          <div className="flex flex-wrap items-center gap-2">
            <Input
              type="email"
              placeholder="email@empresa.com"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              aria-label="Email del miembro"
              required
              className="min-w-48 flex-1"
            />
            <Select value={role} onChange={(e) => setRole(e.target.value as typeof role)} aria-label="Rol">
              <option value="Admin">Admin</option>
              <option value="Member">Miembro</option>
              <option value="Viewer">Solo lectura</option>
            </Select>
            <Button type="submit" disabled={add.isPending}>
              <Icon name="plus" />
              Agregar miembro
            </Button>
          </div>
          <p className="text-xs text-dim">La persona tiene que tener una cuenta. Las invitaciones por email llegan en la fase 4.</p>
        </form>
      )}
      <ErrorText error={error} />
      {isLoading ? (
        <ListSkeleton rows={3} />
      ) : (
        <>
          <p className="-mb-2 text-xs text-dim">
            {members.length} {members.length === 1 ? 'persona' : 'personas'} en este workspace
          </p>
          <ul className="stagger divide-y divide-line/70 overflow-hidden rounded-xl border border-line bg-panel/70 backdrop-blur-sm">
            {members.map((m) => (
              <li key={m.userId} className="flex items-center gap-3 px-3 py-2.5 text-sm transition duration-150 hover:bg-raised/60">
                <Avatar name={m.displayName} size="md" />
                <div className="min-w-0 flex-1">
                  <p className="truncate font-medium">
                    {m.displayName}
                    {m.userId === currentUserId && <span className="ml-1.5 font-normal text-dim">(vos)</span>}
                  </p>
                  <p className="truncate text-xs text-dim">{m.email}</p>
                </div>
                <span className="hidden text-xs text-dim sm:block">desde {formatDate(m.joinedAt)}</span>
                <span className={cx('shrink-0 rounded-full border px-2 py-0.5 font-display text-[11px] font-medium', roleStyles[m.role])}>{roleLabels[m.role]}</span>
              </li>
            ))}
          </ul>
        </>
      )}
    </div>
  )
}
