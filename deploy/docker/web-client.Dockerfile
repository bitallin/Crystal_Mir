FROM node:24-alpine AS build
WORKDIR /app

COPY web-client/package.json web-client/package-lock.json ./
RUN npm ci

COPY web-client/ ./
RUN npm run build

FROM nginx:1.27-alpine

COPY deploy/nginx/web-client.conf /etc/nginx/conf.d/default.conf
COPY deploy/nginx/security-headers.conf /etc/nginx/snippets/security-headers.conf
COPY --from=build /app/dist/ /usr/share/nginx/html/game/

RUN mkdir -p /usr/share/nginx/html/assets

EXPOSE 80
