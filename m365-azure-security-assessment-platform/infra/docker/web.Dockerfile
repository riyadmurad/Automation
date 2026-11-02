FROM node:26.12.0-bookworm-slim AS build
WORKDIR /app/web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
COPY baselines/ /app/baselines/
RUN npm run build
RUN npm prune --omit=dev

FROM node:26.12.0-bookworm-slim AS runtime
ENV NODE_ENV=production
ENV HOSTNAME=0.0.0.0
ENV PORT=3000
WORKDIR /app/web
COPY --from=build --chown=node:node /app/web/package.json ./package.json
COPY --from=build --chown=node:node /app/web/node_modules ./node_modules
COPY --from=build --chown=node:node /app/web/.next ./.next
COPY --from=build --chown=node:node /app/web/public ./public
USER node
EXPOSE 3000
CMD ["npm", "run", "start"]