import { expect, type Locator, type Page } from '@playwright/test'

export const PASSWORD = 'Passw0rd123'

// Cada prueba usa un usuario nuevo (y, por lo tanto, un workspace personal vacío): no dependen de datos
// previos, no se pisan entre sí aunque corran en paralelo y no ensucian los datos de nadie.
export const uniqueEmail = () => `e2e-${Date.now()}-${Math.random().toString(36).slice(2, 8)}@test.dev`

export const uniquePrefix = () =>
  Array.from({ length: 6 }, () => String.fromCharCode(65 + Math.floor(Math.random() * 26))).join('')

export async function register(page: Page, name = 'Ana Prueba') {
  const email = uniqueEmail()
  await page.goto('/')
  await page.getByRole('button', { name: /Regístrate/ }).click()
  await page.getByLabel('Nombre', { exact: true }).fill(name)
  await page.getByLabel('Email').fill(email)
  await page.getByLabel('Contraseña').fill(PASSWORD)
  await page.getByRole('button', { name: 'Registrarme' }).click()
  await expect(page.getByRole('navigation', { name: 'Secciones' })).toBeVisible()
  return email
}

export async function login(page: Page, email: string) {
  await page.goto('/')
  await page.getByLabel('Email').fill(email)
  await page.getByLabel('Contraseña').fill(PASSWORD)
  await page.getByRole('button', { name: 'Entrar' }).click()
  await expect(page.getByRole('navigation', { name: 'Secciones' })).toBeVisible()
}

export async function createProject(page: Page, name: string) {
  const prefix = uniquePrefix()
  await page.getByLabel('Nombre del proyecto').fill(name)
  await page.getByLabel('Prefijo').fill(prefix)
  await page.getByRole('button', { name: '+ Proyecto' }).click()
  // Esperar al selector, no a "Sin tareas": ese texto también puede estar en el proyecto anterior.
  await expect(page.getByLabel('Proyecto', { exact: true }).locator('option:checked')).toContainText(name)
  return prefix
}

export const column = (page: Page, label: string) => page.locator(`section[aria-label="${label}"]`)

export async function createTask(page: Page, title: string) {
  await page.getByLabel('Título', { exact: true }).fill(title)
  await page.getByRole('button', { name: 'Crear', exact: true }).click()
  await expect(column(page, 'Por hacer').getByLabel(title, { exact: true })).toBeVisible()
}

export const titles = (page: Page, label: string) =>
  column(page, label)
    .getByTestId('task-card')
    .evaluateAll((cards) => cards.map((c) => c.getAttribute('aria-label')))

/** Arrastre con el mouse en pasos: dnd-kit necesita ver el movimiento para activar y ubicar la tarjeta. */
export async function drag(page: Page, source: Locator, target: Locator) {
  // hover() espera a que la tarjeta deje de moverse (tras un movimiento anterior, las tarjetas se reacomodan
  // con una animación): medirla antes haría que el clic cayera en un hueco.
  await source.hover()
  const from = (await source.boundingBox())!
  const to = (await target.boundingBox())!
  await page.mouse.move(from.x + from.width / 2, from.y + from.height / 2)
  await page.mouse.down()
  await page.mouse.move(from.x + from.width / 2, from.y + from.height / 2 + 12, { steps: 4 })
  await page.mouse.move(to.x + to.width / 2, to.y + 6, { steps: 25 })
  await page.mouse.up()
}
