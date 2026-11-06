import { connectDshPage, until, autoDismissWelcome, bodyText } from '../non_deliverables_tools/dsh_parity/dumps/_e2e.mjs';
const { cdp, page } = await connectDshPage(9223);
await autoDismissWelcome(page);
await until('render', async () => (await bodyText(page)).length > 20, 20000);
await page.locator('[role=treeitem]').filter({ hasText: '未命名' }).first().click();
await page.waitForTimeout(1200);
const labels = await page.locator('button').evaluateAll((els) => els.map((e) => ({ a: e.getAttribute('aria-label'), t: (e.textContent || '').trim().slice(0, 20), c: e.className })).filter((x) => x.a || x.t));
console.log(JSON.stringify(labels, null, 1));
await cdp.close();
