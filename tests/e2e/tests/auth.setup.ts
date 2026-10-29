import { expect, signInAtKeycloak, test as setup, users } from './fixtures';

// Each demo user signs in through the real flow once (storefront → gateway /bff/login → Keycloak →
// /signin-oidc). Tests reuse the saved session cookie, which also keeps them well inside the
// gateway's sign-in rate limit.
for (const user of Object.values(users)) {
  setup(`sign in as ${user.username}`, async ({ page }) => {
    await page.goto('/products');
    await page.getByRole('button', { name: 'Sign in' }).click();

    await signInAtKeycloak(page, user);

    await expect(page).toHaveURL(/\/products$/);
    await expect(page.getByText(user.displayName)).toBeVisible();
    await page.context().storageState({ path: user.storageState });
  });
}
