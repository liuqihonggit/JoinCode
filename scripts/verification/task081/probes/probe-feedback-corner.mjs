import { connectDshPage, until, autoDismissWelcome } from '../non_deliverables_tools/dsh_parity/dumps/_e2e.mjs';
const { cdp, page } = await connectDshPage(9223);
await autoDismissWelcome(page);
await until('render', async () => (await page.locator('button').count()) > 3, 20000);
await page.locator('[role=treeitem]').filter({ hasText: '未命名' }).first().click();
await page.waitForTimeout(1200);
const info = await page.evaluate(() => {
  const at = (x, y) => {
    const el = document.elementFromPoint(x, y);
    let cur = el;
    const chain = [];
    for (let i = 0; i < 5 && cur; i++) { chain.push({ tag: cur.tagName, cls: cur.className, label: cur.getAttribute?.('aria-label'), text: (cur.textContent || '').slice(0, 30) }); cur = cur.parentElement; }
    return { el: el?.outerHTML?.slice(0, 300), chain };
  };
  return { tl: at(60, 28), tr: at(1368, 28), r1: at(1300, 28), r2: at(1250, 60) };
});
console.log(JSON.stringify(info, null, 1));
const utilities = await page.locator('[class*=headerUtilities]').evaluateAll((els) => els.map((e) => e.outerHTML.slice(0, 500)));
console.log('UTIL', JSON.stringify(utilities, null, 1));
await cdp.close();
