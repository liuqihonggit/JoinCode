/**
 * 交互态捕获的目标映射（唯一数据源，DSH golden 与本地 WebUI 双向共用）
 *
 * dsh:  DSH 0.2.0-rc.2 哈希类名选择器（构建号锚定，见 dsh-meta.json）
 * ours: 本项目 webui 的选择器
 * focus: true 表示该目标参与 focus 态捕获（点击后读取）
 * reloadAfterClick: 点击可能导航/开面板/改布局 → 读完后 reload 复位
 */

export const STATE_PROPS = [
  'backgroundColor', 'backgroundImage', 'color', 'opacity',
  'borderTopWidth', 'borderTopStyle', 'borderTopColor',
  'borderTopLeftRadius', 'borderTopRightRadius', 'borderBottomRightRadius', 'borderBottomLeftRadius',
  'boxShadow', 'outlineWidth', 'outlineStyle', 'outlineColor', 'outlineOffset',
  'transform', 'cursor', 'caretColor',
  'transitionProperty', 'transitionDuration', 'transitionTimingFunction', 'transitionDelay',
  'filter', 'display', 'visibility',
];

export const STATE_TARGETS = [
  { name: 'brand', dsh: 'button.hHd-Xa_brand', ours: '.brand' },
  { name: 'toggle', dsh: 'button.hHd-Xa_toggle', ours: '.icon-btn[aria-label="收起侧栏"]' },
  { name: 'newSession', dsh: 'button.hHd-Xa_newSession', ours: '.new-session' },
  { name: 'panelRow', dsh: 'button.hHd-Xa_panelRow', ours: '.panel-row' },
  { name: 'searchBtn', dsh: 'button.bhn1Oq_searchButton', ours: '.icon-btn[aria-label="搜索"]' },
  { name: 'sortBtn', dsh: 'button.bhn1Oq_iconButton.bhn1Oq_wide', ours: '.icon-btn[aria-label="排序"]' },
  { name: 'panelBtn', dsh: 'button.bhn1Oq_iconButton:not(.bhn1Oq_wide)', ours: '.icon-btn[aria-label="面板"]' },
  { name: 'projectRow', dsh: '.YDXeBa_projectRow', ours: '.project-row' },
  { name: 'sessionRow', dsh: '.YDXeBa_sessionRow', ours: '.session-row' },
  { name: 'settings', dsh: 'button.VOzbGW_trigger', ours: '.settings-btn' },
  { name: 'corner', dsh: '.wSkVaW_headerCorner button', ours: '.corner' },
  { name: 'wsChip', dsh: 'button.pXSMma_workspace', ours: '.workspace-row .chip:nth-of-type(1)' },
  { name: 'modeChip', dsh: 'button.cubgiG_seat', ours: '.workspace-row .chip:nth-of-type(2)' },
  { name: 'addBtn', dsh: 'button.uV2eYG_add', ours: '.add-btn' },
  { name: 'modeTrigger', dsh: 'button.iWlSmW_trigger', ours: '.mode-chip' },
  { name: 'modelTrigger', dsh: 'button._7KE1Ra_trigger', ours: '.model-chip' },
  { name: 'sendBtn', dsh: 'button.uV2eYG_primary', ours: '.send-btn' },
  { name: 'composerCard', dsh: '.uV2eYG_card', ours: '.composer-card', focus: true },
  { name: 'input', dsh: '.uV2eYG_input', ours: '.composer-input', focus: true },
  { name: 'placeholder', dsh: '.uV2eYG_placeholder', ours: '.composer-placeholder', focus: true, hoverVia: '.uV2eYG_input', hoverViaOurs: '.composer-input' },
];

/** 中性指针位置（页面留白区，避开一切交互元素与悬浮物） */
export const NEUTRAL_POINT = { x: 700, y: 20 };
