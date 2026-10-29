import { test as base, expect, type Page } from '@playwright/test';

export { expect };

/** Demo users from deploy/keycloak/storefront-realm.json. */
export const users = {
  ada: { username: 'ada', password: 'ada', displayName: 'Ada Lovelace', storageState: '.auth/ada.json' },
  grace: { username: 'grace', password: 'grace', displayName: 'Grace Hopper', storageState: '.auth/grace.json' },
} as const;

export type User = (typeof users)[keyof typeof users];

/** Seeded products (Catalog.API SeedData). Only the admin test changes a price, and it restores it. */
export const products = {
  laptopStand: { id: '4f1c9a0e-0001-4a6e-9b1a-000000000007', name: 'Laptop Stand', price: '$39.99' },
  wirelessMouse: { id: '4f1c9a0e-0001-4a6e-9b1a-000000000002', name: 'Wireless Mouse', price: '$59.90' },
  usbMicrophone: { id: '4f1c9a0e-0001-4a6e-9b1a-000000000006', name: 'USB Microphone' },
} as const;

const PLACEHOLDER_IMAGE = '<svg xmlns="http://www.w3.org/2000/svg" width="480" height="320"><rect width="100%" height="100%" fill="#ddd"/></svg>';

export const test = base.extend({
  page: async ({ page }, use) => {
    // Product images come from picsum.photos; a local placeholder keeps the tests off the internet.
    await page.route('https://picsum.photos/**', (route) =>
      route.fulfill({ contentType: 'image/svg+xml', body: PLACEHOLDER_IMAGE }),
    );
    await use(page);
  },
});

/** Completes Keycloak's login form, which the gateway's /bff/login redirected to. */
export async function signInAtKeycloak(page: Page, user: User): Promise<void> {
  // The form lives under /realms/storefront/login-actions/… once Keycloak has taken the request.
  await expect(page).toHaveURL(/\/realms\/storefront\//);
  await page.locator('#username').fill(user.username);
  await page.locator('#password').fill(user.password);
  await page.locator('#kc-login').click();
}

/**
 * Calls the API as the signed-in browser does: the page's request context shares its cookies,
 * and the gateway requires the anti-CSRF header on cookie-authenticated /api calls.
 */
export const api = {
  clearBasket: async (page: Page) => expectOk(await page.request.delete('/api/basket', { headers: CSRF })),
  addToBasket: async (page: Page, productId: string, quantity = 1) =>
    expectOk(await page.request.post('/api/basket/items', { headers: CSRF, data: { productId, quantity } })),
};

export const CSRF = { 'X-CSRF': '1' };

export async function expectOk(response: { ok(): boolean; status(): number; url(): string }): Promise<void> {
  expect(response.ok(), `${response.url()} answered ${response.status()}`).toBe(true);
}
