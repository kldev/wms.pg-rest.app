// Screenshots of every page (docs/screens/*.png) from the running local stack with the demo data, signed in for real.
// Also walks the two forms whose errors come from the database: a duplicate SKU and an issue larger than the stock.
// Run through scripts/e2e/screens.sh.
const { chromium } = require("playwright");

const base = process.env.BASE_URL || "http://127.0.0.1:6500";
const out = process.argv[2];
const { APP_USERNAME: username, APP_PASSWORD: password } = process.env;

(async () => {
  // The system Chrome: no browser download.
  const browser = await chromium.launch({ channel: "chrome" });
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: "en-GB" });
  const page = await context.newPage();
  const shot = async (name) => {
    await page.waitForTimeout(400);
    await page.screenshot({ path: `${out}/${name}.png` });
    console.log(name);
  };
  // Interactive pages: wait until the circuit is up before clicking.
  const interactive = async (path, ready) => {
    await page.goto(base + path, { waitUntil: "networkidle" });
    await page.locator(ready).first().waitFor();
  };

  await page.goto(`${base}/login`);
  await shot("login");
  await page.fill("#username", username);
  await page.fill("#password", password);
  await Promise.all([page.waitForURL(`${base}/`), page.click("button[type=submit]")]);

  await interactive("/", "table[data-testid=stock]");
  await shot("stock");

  await interactive("/movements", "table[data-testid=movements]");
  await shot("movements");

  await interactive("/products", "table[data-testid=products]");
  await shot("products");
  await page.click("text=New product");
  await page.fill("#sku", "bolt-m8");
  await page.fill("#name", "Second bolt");
  await page.click("text=Create product");
  await page.locator("[data-field-error=sku]").waitFor();
  const skuError = await page.locator("[data-field-error=sku]").innerText();
  if (!skuError.includes("already exists")) throw new Error(`Unexpected SKU error: ${skuError}`);
  await shot("product-duplicate-sku");

  await interactive("/warehouses", "table[data-testid=warehouses]");
  await shot("warehouses");

  await interactive("/book?kind=issue", "form[data-testid=booking-form]");
  await page.selectOption("#product_id", { label: "BOLT-M8 · Hex bolt M8" });
  await page.selectOption("#warehouse_id", { label: "MAIN · Main warehouse" });
  await page.fill("#quantity", "100000");
  await page.click("form[data-testid=booking-form] button[type=submit]");
  await page.locator("[data-field-error=quantity]").waitFor();
  const stockError = await page.locator("[data-field-error=quantity]").innerText();
  if (!stockError.startsWith("Not enough stock")) throw new Error(`Unexpected stock error: ${stockError}`);
  await shot("issue-not-enough-stock");

  await browser.close();
})().catch((e) => {
  console.error(e);
  process.exit(1);
});
