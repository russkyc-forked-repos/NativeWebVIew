import { test, expect } from '@playwright/test';

test('docs home page renders key content', async ({ page }) => {
  await page.goto('/');

  await expect(page).toHaveTitle(/NativeWebView/i);
  await expect(page.getByRole('heading', { name: 'NativeWebView' }).first()).toBeVisible();
  await expect(page.getByText('gives Avalonia applications a consistent browser surface', { exact: false })).toBeVisible();
});

test('quickstart and release docs are reachable', async ({ page }) => {
  await page.goto('/articles/getting-started/quickstart/');
  await expect(page).toHaveURL(/articles\/getting-started\/quickstart/);
  await expect(page.getByRole('heading', { name: 'Quickstart' })).toBeVisible();

  await page.goto('/articles/diagnostics/ci-and-release/');
  await expect(page).toHaveURL(/articles\/diagnostics\/ci-and-release/);
  await expect(page.getByRole('heading', { name: 'CI and Release' })).toBeVisible();
  await expect(page.locator('code', { hasText: '.github/workflows/release.yml' }).first()).toBeVisible();
});
