FROM node:24-alpine
WORKDIR /app
ARG NEXT_PUBLIC_QUEUEFLOW_API_URL=http://localhost:8080
ENV NEXT_PUBLIC_QUEUEFLOW_API_URL=$NEXT_PUBLIC_QUEUEFLOW_API_URL
COPY package.json package-lock.json tsconfig.json ./
COPY apps/customer-web apps/customer-web
RUN --mount=type=cache,target=/root/.npm npm config set fetch-retries 5 \
    && npm config set fetch-retry-mintimeout 20000 \
    && npm config set fetch-retry-maxtimeout 120000 \
    && npm ci --prefer-offline --workspace @queueflow/customer-web \
    && npm run build --workspace @queueflow/customer-web
ENV NODE_ENV=production
EXPOSE 3000
CMD ["npm", "run", "start", "--workspace", "@queueflow/customer-web", "--", "--hostname", "0.0.0.0"]
