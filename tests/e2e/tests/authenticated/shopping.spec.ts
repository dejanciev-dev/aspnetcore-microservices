import { api, expect, products, test, users } from '../fixtures';

test.use({ storageState: users.grace.storageState });

test.describe('shopping as a customer', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/products');
    await api.clearBasket(page);
    await page.reload();
  });

  test('shows the customer in the header, without the admin role', async ({ page }) => {
    await expect(page.getByText(users.grace.displayName)).toBeVisible();
    await expect(page.locator('.topbar .chip', { hasText: 'admin' })).toHaveCount(0);
  });

  test('adds products, updates the badge and manages the basket', async ({ page }) => {
    const basketLink = page.getByRole('link', { name: 'Basket', exact: true });

    // From the list…
    await page.locator('app-product-card', { hasText: products.laptopStand.name }).getByRole('button', { name: 'Add to basket' }).click();
    await expect(page.getByText(`Added 1 × ${products.laptopStand.name}`)).toBeVisible();
    await expect(basketLink).toHaveText(/1/);

    // …and from a product page with a quantity.
    await page.goto(`/products/${products.wirelessMouse.id}`);
    await page.getByRole('group', { name: 'Quantity' }).getByRole('button', { name: '+' }).click();
    await page.getByRole('button', { name: 'Add to basket · $119.80' }).click();
    await expect(page.getByText(`Added 2 × ${products.wirelessMouse.name}`)).toBeVisible();
    await expect(basketLink).toHaveText(/3/);

    await basketLink.click();
    await expect(page.locator('.line')).toHaveCount(2);
    await expect(page.locator('.summary__total')).toHaveText(/\$159\.79/);

    await page.getByRole('button', { name: `Remove ${products.laptopStand.name}` }).click();
    await expect(page.locator('.line')).toHaveCount(1);
    await expect(page.locator('.summary__total')).toHaveText(/\$119\.80/);

    await page.getByRole('button', { name: 'Clear basket' }).click();
    await expect(page.getByText('Your basket is empty.')).toBeVisible();
    await expect(basketLink).toHaveText('🛒');
  });

  test('checkout validates the shipping address before calling the API', async ({ page }) => {
    await api.addToBasket(page, products.laptopStand.id);
    await page.goto('/checkout');

    await page.getByRole('button', { name: /^Place order/ }).click();

    for (const field of ['Full name', 'Street', 'City', 'ZIP / Postal code', 'Country'])
      await expect(page.getByText(`${field} is required.`)).toBeVisible();
    await expect(page).toHaveURL(/\/checkout$/);
  });

  test('places an order, which appears once Ordering has processed it', async ({ page }) => {
    await api.addToBasket(page, products.laptopStand.id, 2);
    await page.goto('/checkout');

    await page.getByLabel('Full name').fill(users.grace.displayName);
    await page.getByLabel('Street').fill('1 Navy Yard');
    await page.getByLabel('City').fill('Arlington');
    await page.getByLabel('ZIP / Postal code').fill('22202');
    await page.getByLabel('Country').fill('USA');
    await page.getByRole('button', { name: 'Place order · $79.98' }).click();

    // Checkout publishes basket.checked-out; Ordering creates the order from RabbitMQ and the page
    // polls until it exists.
    await expect(page).toHaveURL(/\/orders\/[0-9a-f-]{36}\?placed=true$/);
    await expect(page.getByText('Thanks! Your order has been placed.')).toBeVisible();
    const orderId = page.url().match(/\/orders\/([0-9a-f-]{36})/)![1];

    await expect(page.getByRole('heading', { name: `Order #${orderId.slice(0, 8)}` })).toBeVisible({ timeout: 30_000 });
    await expect(page.locator('.status')).toHaveText('Submitted');
    await expect(page.getByRole('row', { name: new RegExp(`${products.laptopStand.name}\\s+2`) })).toBeVisible();
    await expect(page.locator('address')).toContainText('1 Navy Yard');
    await expect(page.getByRole('link', { name: 'Basket', exact: true })).toHaveText('🛒');   // the basket was emptied

    await page.getByRole('link', { name: '← All orders' }).click();
    await expect(page.getByRole('link', { name: `#${orderId.slice(0, 8)}` })).toBeVisible();
  });
});
