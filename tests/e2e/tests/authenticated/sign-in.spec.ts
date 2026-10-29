import { expect, products, signInAtKeycloak, test, users } from '../fixtures';

// These start signed out and go through Keycloak themselves.
test.use({ storageState: { cookies: [], origins: [] } });

test.describe('signing in and out', () => {
  test('a protected page sends the visitor to Keycloak and back to that page', async ({ page }) => {
    await page.goto('/basket');

    await signInAtKeycloak(page, users.grace);

    await expect(page).toHaveURL(/\/basket$/);
    await expect(page.getByRole('heading', { name: 'Your basket' })).toBeVisible();
    await expect(page.getByText(users.grace.displayName)).toBeVisible();
  });

  test('adding to the basket while signed out asks the visitor to sign in first', async ({ page }) => {
    await page.goto(`/products/${products.laptopStand.id}`);
    await page.getByRole('button', { name: /^Add to basket/ }).click();

    await signInAtKeycloak(page, users.grace);

    await expect(page).toHaveURL(`/products/${products.laptopStand.id}`);
    await expect(page.getByText(users.grace.displayName)).toBeVisible();
  });

  test('signing out ends the gateway session and the Keycloak session', async ({ page }) => {
    await page.goto('/orders');
    await signInAtKeycloak(page, users.grace);
    await expect(page.getByRole('heading', { name: 'Your orders' })).toBeVisible();

    await page.getByRole('button', { name: 'Sign out' }).click();

    await expect(page).toHaveURL(/\/products$/);
    await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible();

    // Without a Keycloak SSO session, a protected page shows the login form again instead of
    // silently signing the user back in.
    await page.goto('/orders');
    await expect(page.locator('#kc-login')).toBeVisible();
  });
});
