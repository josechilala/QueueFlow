FROM node:24-alpine AS build
WORKDIR /app
COPY package.json package-lock.json tsconfig.json ./
COPY apps/admin-web apps/admin-web
RUN npm ci --workspace @queueflow/admin-web && npm run build --workspace @queueflow/admin-web
WORKDIR /app/apps/admin-web
EXPOSE 3000
CMD ["npm", "run", "dev", "--", "--hostname", "0.0.0.0"]
