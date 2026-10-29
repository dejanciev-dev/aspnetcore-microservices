import { expect, products, test } from '../fixtures';

test.describe('catalog (signed out)', () => {
  test('lists the seeded products and their categories', async ({ page }) => {
    await page.goto('/');

    await expect(page).toHaveURL(/\/products$/);
    await expect(page.getByRole('heading', { name: 'Gear for your desk' })).toBeVisible();
    await expect(page.locator('app-product-card')).toHaveCount(8);

    const categories = page.getByRole('navigation', { name: 'Categories' }).getByRole('link');
    await expect(categories).toHaveText(['All', 'Accessories', 'Audio', 'Displays', 'Peripherals']);
    await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible();
  });

  test('filters by category and search, keeping the filters in the URL', async ({ page }) => {
    await page.goto('/products');

    await page.getByRole('navigation', { name: 'Categories' }).getByRole('link', { name: 'Audio' }).click();
    await expect(page).toHaveURL(/category=Audio/);
    await expect(page.locator('app-product-card h3')).toHaveText(['Noise-Cancelling Headphones', 'USB Microphone']);

    await page.getByRole('navigation', { name: 'Categories' }).getByRole('link', { name: 'All' }).click();
    await page.getByRole('searchbox', { name: 'Search' }).fill('monitor');
    await page.getByRole('button', { name: 'Search' }).click();
    await expect(page).toHaveURL(/search=monitor/);
    await expect(page.locator('app-product-card h3')).toHaveText(['4K Monitor', 'Ultrawide Monitor']);

    // A reload restores the same view from the URL alone.
    await page.reload();
    await expect(page.locator('app-product-card h3')).toHaveText(['4K Monitor', 'Ultrawide Monitor']);

    await page.getByRole('searchbox', { name: 'Search' }).fill('no-such-product');
    await page.getByRole('button', { name: 'Search' }).click();
    await expect(page.getByText('No products match your filters.')).toBeVisible();
  });

  test('opens a product page from the list', async ({ page }) => {
    await page.goto('/products');

    await page.getByRole('heading', { name: products.laptopStand.name }).getByRole('link').click();

    await expect(page).toHaveURL(`/products/${products.laptopStand.id}`);
    await expect(page.getByRole('heading', { level: 1, name: products.laptopStand.name })).toBeVisible();
    await expect(page.locator('.price--lg')).toHaveText(products.laptopStand.price);
    await expect(page.getByRole('spinbutton', { name: 'New price' })).toHaveCount(0);   // admin-only form
  });

  test('reports an unknown product', async ({ page }) => {
    await page.goto('/products/00000000-0000-4000-8000-000000000000');

    await expect(page.getByRole('alert')).toHaveText('Product not found.');
  });
});
