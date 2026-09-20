import { defineConfig } from 'vitest/config';

export default defineConfig({
  oxc: { jsx: { runtime: 'automatic' } },
  test: {
    environment: 'jsdom',
    env: {
      NEXT_PUBLIC_QUEUEFLOW_API_URL: 'https://api.example.test',
      NEXT_PUBLIC_QUEUEFLOW_ADMIN_URL: 'https://admin.example.test',
    },
  },
});
