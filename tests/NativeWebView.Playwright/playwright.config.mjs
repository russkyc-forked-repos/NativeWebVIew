import { defineConfig } from '@playwright/test';

const pythonBin = process.env.NATIVEWEBVIEW_DOCS_PYTHON_BIN || 'python3';

export default defineConfig({
  testDir: './specs',
  timeout: 30_000,
  expect: {
    timeout: 10_000,
  },
  use: {
    baseURL: 'http://127.0.0.1:8080',
    trace: 'on-first-retry',
  },
  webServer: {
    command: `"${pythonBin}" -m http.server 8080 --directory ../../site/.lunet/build/www`,
    cwd: '.',
    url: 'http://127.0.0.1:8080/index.html',
    reuseExistingServer: !process.env.CI,
  },
});
