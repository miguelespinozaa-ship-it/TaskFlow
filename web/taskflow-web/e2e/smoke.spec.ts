import { expect, test } from '@playwright/test'
import { column, createProject, createTask, drag, login, register, titles } from './helpers'

test('registro, cierre de sesión e inicio: la sesión sobrevive a una recarga sin exponer tokens', async ({ page }) => {
  const email = await register(page)
  await expect(page.getByText('Este workspace no tiene proyectos')).toBeVisible()

  // El token de acceso vive en memoria y el de renovación en una cookie httpOnly: nada legible desde JavaScript.
  await page.reload()
  await expect(page.getByRole('navigation', { name: 'Secciones' })).toBeVisible()
  const readable = await page.evaluate(() => JSON.stringify(localStorage) + JSON.stringify(sessionStorage) + document.cookie)
  expect(readable).not.toMatch(/eyJ|tf_refresh/)

  await page.getByRole('button', { name: 'Salir' }).click()
  await expect(page.getByRole('heading', { name: 'Iniciar sesión' })).toBeVisible()
  await page.reload() // cerrar sesión revoca el token: recargar no la revive
  await expect(page.getByRole('heading', { name: 'Iniciar sesión' })).toBeVisible()

  await login(page, email)
})

test('contraseña incorrecta muestra un error y no inicia sesión', async ({ page }) => {
  await page.goto('/')
  await page.getByLabel('Email').fill('nadie@test.dev')
  await page.getByLabel('Contraseña').fill('Incorrecta123')
  await page.getByRole('button', { name: 'Entrar' }).click()

  await expect(page.getByRole('alert').filter({ hasText: 'incorrectos' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Iniciar sesión' })).toBeVisible()
})

test('board: arrastrar dentro de una columna y a otra columna, y que persista', async ({ page }) => {
  await register(page)
  await createProject(page, 'Proyecto board')
  for (const title of ['Alfa', 'Beta', 'Gamma']) await createTask(page, title)

  const todo = column(page, 'Por hacer')
  await drag(page, todo.getByLabel('Gamma', { exact: true }), todo.getByLabel('Alfa', { exact: true }))
  await expect.poll(() => titles(page, 'Por hacer')).toEqual(['Gamma', 'Alfa', 'Beta'])

  await drag(page, todo.getByLabel('Alfa', { exact: true }), column(page, 'En progreso'))
  await expect.poll(() => titles(page, 'En progreso')).toEqual(['Alfa'])

  await page.reload()
  await expect(column(page, 'En progreso').getByLabel('Alfa', { exact: true })).toBeVisible()
  expect(await titles(page, 'Por hacer')).toEqual(['Gamma', 'Beta'])
})

test('board con teclado: Espacio y flechas reordenan; Enter abre el detalle y el foco vuelve al cerrarlo', async ({ page }) => {
  await register(page)
  await createProject(page, 'Proyecto teclado')
  await createTask(page, 'Uno')
  await createTask(page, 'Dos')

  const dos = column(page, 'Por hacer').getByLabel('Dos', { exact: true })
  await dos.focus()
  // dnd-kit procesa cada tecla en el siguiente frame: sin la pausa, la flecha llega antes de que "agarre".
  for (const key of ['Space', 'ArrowUp', 'Space']) {
    await page.keyboard.press(key)
    await page.waitForTimeout(200)
  }
  await expect.poll(() => titles(page, 'Por hacer')).toEqual(['Dos', 'Uno'])

  const uno = column(page, 'Por hacer').getByLabel('Uno', { exact: true })
  await uno.focus()
  await page.keyboard.press('Enter')
  const dialog = page.getByRole('dialog', { name: 'Detalle de la tarea' })
  await expect(dialog.getByLabel('Título de la tarea')).toBeVisible()

  await page.keyboard.press('Escape')
  await expect(dialog).toBeHidden()
  await expect(uno).toBeFocused()
})

test('detalle de tarea: prioridad, etiqueta y comentario quedan guardados y en el historial', async ({ page }) => {
  await register(page, 'Lucía Pérez')
  await createProject(page, 'Proyecto detalle')
  await createTask(page, 'Revisar facturas')

  await column(page, 'Por hacer').getByLabel('Revisar facturas', { exact: true }).click()
  const dialog = page.getByRole('dialog', { name: 'Detalle de la tarea' })
  await dialog.getByLabel('Prioridad de la tarea').selectOption('Urgent')
  await dialog.getByLabel('Nombre de etiqueta').fill('contabilidad')
  await dialog.getByRole('button', { name: 'Crear', exact: true }).click()
  await dialog.getByRole('button', { name: 'contabilidad' }).click()
  await expect(dialog.getByRole('button', { name: 'contabilidad', pressed: true })).toBeVisible()
  await dialog.getByLabel('Comentario').fill('Faltan las de septiembre')
  await dialog.getByRole('button', { name: 'Comentar' }).click()
  await expect(dialog.getByText('Faltan las de septiembre')).toBeVisible()
  await expect(dialog.getByText(/prioridad: Media → Urgente/)).toBeVisible()
  await page.keyboard.press('Escape')

  const card = column(page, 'Por hacer').getByLabel('Revisar facturas', { exact: true })
  await expect(card.getByText('Urgente')).toBeVisible()
  await expect(card.getByText('contabilidad')).toBeVisible()

  // La búsqueda en español encuentra el plural a partir del singular.
  await page.getByRole('button', { name: 'Buscar' }).click()
  await page.getByLabel('Buscar tareas').fill('factura')
  await expect(page.getByRole('button', { name: /Revisar facturas/ })).toBeVisible()
})

test('un workspace no ve los proyectos de otro', async ({ browser }) => {
  const pageA = await (await browser.newContext()).newPage()
  const pageB = await (await browser.newContext()).newPage()

  await register(pageA, 'Empresa A')
  await createProject(pageA, 'Secreto de A')

  await register(pageB, 'Empresa B')
  await expect(pageB.getByText('Este workspace no tiene proyectos')).toBeVisible()
  await expect(pageB.getByText('Secreto de A')).toHaveCount(0)
  await pageB.getByRole('button', { name: 'Buscar' }).click()
  await expect(pageB.getByText('Secreto de A')).toHaveCount(0)
})

test('un Viewer ve el board pero no puede crear, arrastrar ni editar', async ({ browser }) => {
  const ownerPage = await (await browser.newContext()).newPage()
  const viewerPage = await (await browser.newContext()).newPage()

  const viewerEmail = await register(viewerPage, 'Vera Lectora')
  await register(ownerPage, 'Olga Dueña')
  await createProject(ownerPage, 'Proyecto compartido')
  await createTask(ownerPage, 'Tarea visible')

  // El Owner la suma como Viewer desde la pestaña Miembros.
  await ownerPage.getByRole('button', { name: 'Miembros' }).click()
  await ownerPage.getByLabel('Email del miembro').fill(viewerEmail)
  await ownerPage.getByLabel('Rol').selectOption('Viewer')
  await ownerPage.getByRole('button', { name: 'Agregar miembro' }).click()
  await expect(ownerPage.getByText('Vera Lectora', { exact: true })).toBeVisible()

  // La Viewer cambia al workspace de Olga desde el selector.
  await viewerPage.reload()
  const workspace = viewerPage.getByLabel('Workspace')
  const option = await workspace.locator('option', { hasText: 'Olga Dueña' }).textContent()
  await workspace.selectOption({ label: option!.trim() })
  const card = column(viewerPage, 'Por hacer').getByLabel('Tarea visible', { exact: true })
  await expect(card).toBeVisible()

  await expect(viewerPage.getByLabel('Título', { exact: true })).toHaveCount(0)
  await drag(viewerPage, card, column(viewerPage, 'Hecho'))
  expect(await titles(viewerPage, 'Hecho')).toEqual([])
  await card.click()
  await expect(viewerPage.getByRole('dialog').getByLabel('Título de la tarea')).toBeDisabled()
  await expect(viewerPage.getByRole('dialog').getByLabel('Comentario')).toHaveCount(0)
})
