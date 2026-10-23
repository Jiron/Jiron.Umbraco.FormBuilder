import { defineConfig } from 'vitest/config';

// One run for every client package plus the build scripts.
export default defineConfig({
  test: {
    projects: [
      'packages/*',
      {
        test: {
          name: 'scripts',
          include: ['scripts/**/*.test.mjs'],
          environment: 'node',
        },
      },
    ],
  },
});
