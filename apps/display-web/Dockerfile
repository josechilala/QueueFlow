FROM node:24-alpine AS build
WORKDIR /app
COPY package.json package-lock.json tsconfig.json ./
COPY apps/display-web apps/display-web
RUN npm ci --workspace @queueflow/display-web && npm run build --workspace @queueflow/display-web
WORKDIR /app/apps/display-web
EXPOSE 3000
CMD ["npm", "run", "dev", "--", "--hostname", "0.0.0.0"]
