/**
 * DSH 页面会话开启（公共模块）：导航 + 稳定等待 + 引导弹窗自动关闭
 * capture-dsh.mjs / capture-states-dsh.mjs 共用，保证捕获条件一致。
 */
export async function openDshSession(browser, { url, width, height, theme }) {
  const context = await browser.newContext({
    viewport: { width, height },
    deviceScaleFactor: 1,
    colorScheme: theme === 'light' ? 'light' : 'dark',
  });
  const page = await context.newPage();
  console.log('navigating...');
  await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 30000 });
  await settleDsh(page);
  return { context, page };
}

/** 等待路由/WS 稳定 + 关闭首次运行引导弹窗（保证场景干净） */
export async function settleDsh(page) {
  await page.waitForTimeout(3000);
  try {
    await page.waitForLoadState('networkidle', { timeout: 12000 });
  } catch {
    console.log('networkidle timeout (ws likely active), continuing');
  }
  for (let i = 0; i < 3; i++) {
    const btn = page.getByRole('button', { name: /继续|Continue|Got it|知道了|确定/ });
    if (await btn.first().isVisible().catch(() => false)) {
      console.log('dismissing onboarding modal');
      await btn.first().click().catch(() => {});
      await page.waitForTimeout(1500);
    } else break;
  }
  await page.waitForTimeout(2500);
}
