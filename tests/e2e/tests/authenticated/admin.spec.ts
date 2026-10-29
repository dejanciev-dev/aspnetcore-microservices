import { CSRF, api, expect, expectOk, products, test, users } from '../fixtures';

test.use({ storageState: users.ada.storageState });

interface Product {
  id: string;
  name: string;
  description: string;
  category: string;
  price: number;
  imageUrl: string;
}

test.describe('catalog administration', () => {
  let original: Product | undefined;

  test.beforeEach(async ({ page }) => {
    original = undefined;
    await page.goto('/products');
    // The session cookie goes along, so even a GET needs the anti-CSRF header.
    const response = await page.request.get(`/api/products/${products.usbMicrophone.id}`, { headers: CSRF });
    await expectOk(response);
    original = await response.json();
    await api.clearBasket(page);
  });

  // Put the seeded price back so reruns and other tests see the catalog as seeded.
  test.afterEach(async ({ page }) => {
    if (!original) return;   // setup failed before anything changed
    const { id, ...fields } = original;
    await expectOk(await page.request.put(`/api/products/${id}`, { headers: CSRF, data: fields }));
    await api.clearBasket(page);
  });

  test('shows the admin role in the header', async ({ page }) => {
    await expect(page.getByText(users.ada.displayName)).toBeVisible();
    await expect(page.locator('.topbar .chip', { hasText: 'admin' })).toBeVisible();
  });

  test('a price change reaches open baskets through RabbitMQ', async ({ page }) => {
    const product = original!;
    const newPrice = (product.price + 10).toFixed(2);
    await api.addToBasket(page, product.id);

    await page.goto(`/products/${product.id}`);
    await page.getByRole('spinbutton', { name: 'New price' }).fill(newPrice);
    await page.getByRole('button', { name: 'Update price' }).click();

    await expect(page.getByText('Price updated.')).toBeVisible();
    // The gateway evicted its cached catalog reads, so the page shows the new price straight away.
    await expect(page.locator('.price--lg')).toHaveText(`$${newPrice}`);

    // Catalog's outbox publishes catalog.product-price-changed; Basket reprices the cart.
    await page.goto('/basket');
    await expect(async () => {
      await page.reload();
      await expect(page.locator('.line', { hasText: product.name })).toContainText(`$${newPrice}`, { timeout: 1_000 });
    }).toPass({ timeout: 30_000 });
  });
});
