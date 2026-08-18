FROM node:24-alpine AS build
WORKDIR /app
COPY package.json package-lock.json tsconfig.json ./
COPY apps/customer-web apps/customer-web
RUN npm ci --workspace @queueflow/customer-web && npm run build --workspace @queueflow/customer-web
WORKDIR /app/apps/customer-web
EXPOSE 3000
CMD ["npm", "run", "dev", "--", "--hostname", "0.0.0.0"]
