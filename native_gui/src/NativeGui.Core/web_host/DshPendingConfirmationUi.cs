namespace JoinCode.Gui.WebHost;

/// <summary>
/// DSH 待确认收件箱前端资产 — 以独立脚本注入 DSH index.html，不修改第三方前端构建产物。
/// <para>后台任务因权限确认被 Defer 时票据在前端不可见，用户只能看到代理“已拒绝”的结果。
/// 本脚本轮询 <c>pendingConfirmations/list</c>，用右下角收件箱展示票据并调用
/// <c>pendingConfirmations/resolve|resolveAll</c> 结算；批准只代表复核通过，不自动恢复原调用。</para>
/// </summary>
internal static class DshPendingConfirmationUi {
    /// <summary>脚本注入标记（index.html 已注入时跳过，避免重复加载）。</summary>
    internal const string InjectionMarker = "pendingConfirmations/ui.js";

    /// <summary>注入到 index.html 的 script 标签。</summary>
    internal const string ScriptTag = "<script src=\"/api/pendingConfirmations/ui.js\"></script>";

    /// <summary>收件箱脚本正文（零依赖，textContent 渲染用户数据，避免 HTML 注入）。</summary>
    internal const string Script = """
(function () {
  'use strict';
  if (window.__jccPendingConfirmations) { return; }
  window.__jccPendingConfirmations = true;

  var state = { items: [], open: false, busy: false, message: '' };
  var toggle = null;
  var panel = null;
  var list = null;
  var status = null;

  function rpc(method, args) {
    var rpcId = 'pending-' + Date.now() + '-' + Math.random().toString(16).slice(2);
    return fetch('/api/' + method, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ type: 'client-request', rpcId: rpcId, method: method, payload: { args: args || {} } }),
    }).then(function (response) {
      return response.json();
    }).then(function (reply) {
      if (!reply || !reply.result) { throw new Error('宿主响应缺少 result'); }
      if (!reply.result.ok) {
        throw new Error((reply.result.error && reply.result.error.message) || '宿主拒绝了请求');
      }
      return reply.result.value;
    });
  }

  function el(tag, className, text) {
    var node = document.createElement(tag);
    if (className) { node.className = className; }
    if (text !== undefined && text !== null) { node.textContent = text; }
    return node;
  }

  function ensureUi() {
    if (toggle) { return; }
    var style = el('style');
    style.textContent = [
      '#jcc-pending-toggle{position:fixed;right:16px;bottom:96px;z-index:2147483000;border:1px solid rgba(255,255,255,.18);',
      'border-radius:14px;background:#1f2430;color:#e8ecf5;font:13px/1.4 system-ui,sans-serif;padding:10px 14px;',
      'box-shadow:0 8px 24px rgba(0,0,0,.35);cursor:pointer}',
      '#jcc-pending-toggle[hidden]{display:none}',
      '#jcc-pending-panel{position:fixed;right:16px;bottom:150px;z-index:2147483001;width:min(460px,calc(100vw - 32px));',
      'max-height:min(60vh,560px);overflow:auto;border:1px solid rgba(255,255,255,.18);border-radius:16px;',
      'background:#171b24;color:#e8ecf5;font:13px/1.5 system-ui,sans-serif;box-shadow:0 16px 44px rgba(0,0,0,.45);',
      'padding:14px}',
      '#jcc-pending-panel[hidden]{display:none}',
      '.jcc-pending-head{display:flex;align-items:center;gap:8px;margin-bottom:6px}',
      '.jcc-pending-title{font-weight:600;font-size:14px;flex:1}',
      '.jcc-pending-hint{color:#9aa4b2;font-size:12px;margin-bottom:10px}',
      '.jcc-pending-batch{display:flex;gap:8px;margin-bottom:10px}',
      '.jcc-pending-item{border:1px solid rgba(255,255,255,.1);border-radius:12px;padding:10px;margin-bottom:10px;background:#1d222d}',
      '.jcc-pending-tool{font-weight:600;color:#ffd479}',
      '.jcc-pending-reason{color:#c8d0dc;margin:4px 0}',
      '.jcc-pending-args{white-space:pre-wrap;word-break:break-all;background:#12161d;border-radius:8px;padding:8px;',
      'max-height:120px;overflow:auto;color:#9fb3c8;font:12px/1.45 ui-monospace,monospace;margin:6px 0}',
      '.jcc-pending-actions{display:flex;gap:8px;justify-content:flex-end}',
      '.jcc-pending-actions button,.jcc-pending-batch button,#jcc-pending-close{border:1px solid rgba(255,255,255,.16);',
      'border-radius:8px;padding:5px 10px;background:#2a3140;color:#e8ecf5;cursor:pointer;font:12px system-ui,sans-serif}',
      '.jcc-pending-approve{background:#1f5f46 !important;border-color:#2f8f68 !important}',
      '.jcc-pending-deny{background:#5a2730 !important;border-color:#8f3b49 !important}',
      '.jcc-pending-status{color:#9aa4b2;font-size:12px;min-height:16px;margin-top:6px}',
      '.jcc-pending-empty{color:#9aa4b2;padding:12px 0;text-align:center}',
    ].join('');
    document.head.appendChild(style);

    toggle = el('button', '', '待确认 0');
    toggle.id = 'jcc-pending-toggle';
    toggle.type = 'button';
    toggle.hidden = true;
    toggle.addEventListener('click', function () {
      state.open = !state.open;
      render();
    });

    panel = el('div');
    panel.id = 'jcc-pending-panel';
    panel.hidden = true;

    var head = el('div', 'jcc-pending-head');
    head.appendChild(el('div', 'jcc-pending-title', '后台待确认事项'));
    var close = el('button', '', '关闭');
    close.id = 'jcc-pending-close';
    close.type = 'button';
    close.addEventListener('click', function () {
      state.open = false;
      render();
    });
    head.appendChild(close);
    panel.appendChild(head);
    panel.appendChild(el('div', 'jcc-pending-hint',
      '批准只表示复核通过，不会自动恢复原后台调用；发起代理会收到通知，可在前台交互模式重试。'));

    var batch = el('div', 'jcc-pending-batch');
    batch.appendChild(batchButton('全部复核通过', 'approve', 'jcc-pending-approve'));
    batch.appendChild(batchButton('全部拒绝', 'deny', 'jcc-pending-deny'));
    panel.appendChild(batch);

    list = el('div');
    panel.appendChild(list);
    status = el('div', 'jcc-pending-status');
    panel.appendChild(status);

    document.body.appendChild(toggle);
    document.body.appendChild(panel);
  }

  function batchButton(label, decision, className) {
    var button = el('button', className, label);
    button.type = 'button';
    button.addEventListener('click', function () { resolveAll(decision); });
    return button;
  }

  function refresh() {
    ensureUi();
    return rpc('pendingConfirmations/list', {}).then(function (value) {
      state.items = (value && value.items) ? value.items : [];
      state.message = (value && value.message) ? value.message : '';
      render();
    }).catch(function (error) {
      state.items = [];
      state.message = '待确认收件箱不可用：' + error.message;
      render();
    });
  }

  function render() {
    if (!toggle) { return; }
    var count = state.items.length;
    toggle.hidden = count === 0 && !state.open;
    toggle.textContent = '待确认 ' + count;
    panel.hidden = !state.open;
    status.textContent = state.message || (state.busy ? '正在提交…' : '');
    list.replaceChildren();

    if (count === 0) {
      list.appendChild(el('div', 'jcc-pending-empty', '当前没有需要确认的事项。'));
      return;
    }

    state.items.forEach(function (item) {
      var card = el('div', 'jcc-pending-item');
      card.appendChild(el('div', 'jcc-pending-tool', item.toolName || '(未知工具)'));
      if (item.reason) { card.appendChild(el('div', 'jcc-pending-reason', item.reason)); }
      var meta = item.agentId ? ('代理 ' + item.agentId + ' · ') : '';
      if (item.createdAt) { meta += new Date(item.createdAt).toLocaleString(); }
      if (meta) { card.appendChild(el('div', 'jcc-pending-hint', meta)); }
      if (item.arguments) {
        card.appendChild(el('div', 'jcc-pending-args', JSON.stringify(item.arguments, null, 2)));
      }
      var actions = el('div', 'jcc-pending-actions');
      actions.appendChild(actionButton('复核通过', 'approve', item.id, 'jcc-pending-approve'));
      actions.appendChild(actionButton('拒绝', 'deny', item.id, 'jcc-pending-deny'));
      card.appendChild(actions);
      list.appendChild(card);
    });
  }

  function actionButton(label, decision, id, className) {
    var button = el('button', className, label);
    button.type = 'button';
    button.disabled = state.busy;
    button.addEventListener('click', function () { resolve(id, decision); });
    return button;
  }

  function resolve(id, decision) {
    if (state.busy) { return; }
    state.busy = true;
    render();
    rpc('pendingConfirmations/resolve', { id: id, decision: decision }).then(function (value) {
      state.message = value && value.message ? value.message : '已结算。';
    }).catch(function (error) {
      state.message = '结算失败：' + error.message;
    }).then(function () {
      state.busy = false;
      return refresh();
    });
  }

  function resolveAll(decision) {
    if (state.busy) { return; }
    state.busy = true;
    render();
    rpc('pendingConfirmations/resolveAll', { decision: decision }).then(function (value) {
      state.message = value && value.message ? value.message : '已批量结算。';
    }).catch(function (error) {
      state.message = '批量结算失败：' + error.message;
    }).then(function () {
      state.busy = false;
      return refresh();
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', refresh);
  } else {
    refresh();
  }
  setInterval(refresh, 2000);
  document.addEventListener('visibilitychange', function () {
    if (!document.hidden) { refresh(); }
  });
})();
""";
}
