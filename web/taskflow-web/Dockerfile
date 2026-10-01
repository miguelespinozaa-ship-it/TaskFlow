# syntax=docker/dockerfile:1

# ---------- build: Node compila el frontend a archivos estáticos ----------
FROM node:22-alpine AS build
WORKDIR /app

# package*.json primero: `npm ci` queda en caché mientras no cambien las dependencias.
COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npm run build

# ---------- runtime: nginx sirve los estáticos y reenvía /api a la API ----------
# Variante "unprivileged": nginx corre sin root y escucha en el 8080.
FROM nginxinc/nginx-unprivileged:1.29-alpine AS final
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 8080
