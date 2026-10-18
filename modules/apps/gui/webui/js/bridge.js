// WebUI 桥接客户端（ADR 0141）：与 modules/apps/gui/webview/BridgeProtocol.cs 的消息注册对照维护。
// 零第三方依赖；host 经 PostWebMessageAsJson 下发对象，本站经 chrome.webview.postMessage 上发对象。
(function () {
  'use strict';

  var handlers = new Map();
  var webview = window.chrome && window.chrome.webview;

  function send(message) {
    if (!webview) {
      // 非 WebView2 环境（如浏览器直接打开调试页）：丢弃并提示，不抛错
      console.warn('jccBridge: 非 WebView2 环境，消息丢弃', message);
      return;
    }
    webview.postMessage(message);
  }

  if (webview) {
    webview.addEventListener('message', function (e) {
      var message = e.data;
      var handler = message && handlers.get(message.type);
      if (handler) {
        handler(message);
        return;
      }
      console.warn('jccBridge: 未注册的消息类型', message);
    });
  }

  window.jccBridge = {
    send: send,
    on: function (type, handler) {
      handlers.set(type, handler);
    },
  };

  // —— 会话流前端状态（P6-3：消息渲染对齐 DSH 消息态） ——
  var messageList = document.querySelector('.message-list');
  var msgColumn = document.querySelector('.msg-column') || messageList;
  var textarea = document.querySelector('.composer-textarea');
  var placeholder = document.querySelector('.composer-placeholder');
  var sendBtn = document.querySelector('.send-btn');
  var sidebarList = document.querySelector('.sidebar .list');
  var staticSessionRow = sidebarList ? sidebarList.querySelector('.session-row') : null;
  var selectedSessionId = null;
  var busy = false;
  var live = null; // 本轮发送的活动状态 { userTs, shell, text }

  // —— 图标（路径逐字节取自 DSH 组件 outerHTML 快照，reference/dsh-messages-html-1440x900.json）——
  var ICON = {
    copy: '<svg width="16" height="16" viewBox="0 0 16 16" fill="none" aria-hidden="true" stroke-width="1"><rect x="1.52075" y="4.07373" width="10.3932" height="10.3932" rx="2" stroke="currentColor"></rect><path d="M11.9792 1.53296C13.36 1.53296 14.4792 2.65225 14.4792 4.03296V9.42847C14.4792 10.3756 13.9521 11.1987 13.1755 11.6228V10.3298C13.3652 10.0787 13.4792 9.7674 13.4792 9.42847V4.03296C13.4792 3.20453 12.8077 2.53296 11.9792 2.53296H6.58374C6.27966 2.53301 5.99684 2.6235 5.7605 2.77905H4.42358C4.85652 2.03463 5.66056 1.53304 6.58374 1.53296H11.9792Z" fill="currentColor"></path></svg>',
    thumbUp: '<svg width="16" height="16" viewBox="0 0 16 16" fill="none" aria-hidden="true" stroke-width="1"><path d="M13.537 8.12098L12.3983 12.8455C12.1818 13.7438 11.378 14.3769 10.454 14.3769L9.35595 14.3769H7.43799H5.16577C3.50892 14.3769 2.16577 13.0337 2.16577 11.3769V7.88668C2.16577 7.33439 2.61349 6.88668 3.16577 6.88668H4.02665C5.84943 6.88668 7.38083 3.28711 7.67689 2.54578C7.71259 2.45639 7.73501 2.36373 7.77922 2.27824C7.86506 2.11221 8.08228 1.87578 8.59039 2.07775C10.3291 2.76886 9.23144 6.04071 8.96955 6.75058C8.94502 6.81707 8.99495 6.88668 9.06581 6.88668H12.5648C13.2119 6.88668 13.6886 7.49192 13.537 8.12098Z" stroke="currentColor"></path></svg>',
    thumbDown: '<svg width="16" height="16" viewBox="0 0 16 16" fill="none" aria-hidden="true" stroke-width="1"><path d="M2.46302 8.06749L3.60171 3.34299C3.81822 2.44467 4.62196 1.81162 5.546 1.8116L6.64406 1.81158L8.56202 1.81158L10.8342 1.81158C12.4911 1.81158 13.8342 3.15473 13.8342 4.81158L13.8342 8.3018C13.8342 8.85408 13.3865 9.3018 12.8342 9.3018L11.9734 9.3018C10.1506 9.3018 8.61918 12.9014 8.32311 13.6427C8.28741 13.7321 8.26499 13.8247 8.22078 13.9102C8.13494 14.0763 7.91772 14.3127 7.40961 14.1107C5.67089 13.4196 6.76856 10.1478 7.03045 9.43789C7.05498 9.37141 7.00505 9.3018 6.93419 9.3018L3.43519 9.3018C2.78811 9.3018 2.31141 8.69656 2.46302 8.06749Z" stroke="currentColor"></path></svg>',
    branch: '<svg width="16" height="16" viewBox="0 0 16 16" fill="none" aria-hidden="true" stroke-width="1"><path d="M1.01503 8.0001L5.6964 8.0001C6.41913 8.0001 6.78049 8.0001 7.12115 7.91951C7.4232 7.84804 7.71233 7.73014 7.97821 7.57C8.27809 7.38939 8.5364 7.13669 9.05303 6.63129L11.3281 4.40564" stroke="currentColor"></path><path d="M1.01221 7.9999L5.6964 7.9999C6.41913 7.9999 6.78049 7.9999 7.12115 8.08049C7.4232 8.15196 7.71233 8.26986 7.97821 8.43C8.27809 8.61061 8.5364 8.86331 9.05303 9.36871L11.3281 11.5944" stroke="currentColor"></path><circle cx="12.4502" cy="3.3079" r="1.56962" stroke="currentColor"></circle><circle cx="12.4502" cy="12.6921" r="1.56962" stroke="currentColor"></circle></svg>',
    wrap: '<svg width="14" height="14" viewBox="0 0 16 16" fill="none" aria-hidden="true"><path d="M2 15H1V1H2V15Z" fill="currentColor"></path><path d="M12.3535 7.64645C12.5487 7.84171 12.5487 8.15829 12.3535 8.35355L9.85352 10.8535L9.14648 10.1465L10.793 8.5H3.5V7.5H10.793L9.14648 5.85352L9.85352 5.14648L12.3535 7.64645Z" fill="currentColor"></path><path d="M15 15H14V1H15V15Z" fill="currentColor"></path></svg>',
  };

  function createEl(tag, className) {
    var el = document.createElement(tag);
    if (className) {
      el.className = className;
    }
    return el;
  }

  function iconButton(kind, label, extraClass) {
    var btn = createEl('button', extraClass || 'msg-action');
    btn.type = 'button';
    btn.setAttribute('aria-label', label);
    btn.innerHTML = ICON[kind]; // 静态图标常量（非用户/模型数据），不经过 HTML 解析的风险面
    return btn;
  }

  function formatTime(value) {
    if (!value) {
      return '';
    }
    var date = value instanceof Date ? value : new Date(value);
    if (isNaN(date.getTime())) {
      return '';
    }
    var h = String(date.getHours());
    var m = String(date.getMinutes());
    return (h.length < 2 ? '0' + h : h) + ':' + (m.length < 2 ? '0' + m : m);
  }

  function copyText(text) {
    try {
      if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(text).catch(function () {});
      }
    } catch (err) {
      console.warn('jccBridge: 复制失败', err);
    }
  }

  function buildCopyButton(getText) {
    var btn = iconButton('copy', '复制');
    btn.addEventListener('click', function () {
      copyText(getText());
    });
    return btn;
  }

  function scrollToBottom() {
    if (messageList) {
      messageList.scrollTop = messageList.scrollHeight;
    }
  }

  // —— Markdown 最小安全渲染（段落 / 有序·无序列表 / 行内代码 / 粗体 / 围栏代码块；DOM 构建不走 innerHTML） ——

  function appendInline(parent, text) {
    var buf = '';
    function flush() {
      if (buf) {
        parent.appendChild(document.createTextNode(buf));
        buf = '';
      }
    }
    var i = 0;
    while (i < text.length) {
      if (text[i] === '`') {
        var end = text.indexOf('`', i + 1);
        if (end > i + 1) {
          flush();
          var code = document.createElement('code');
          code.textContent = text.slice(i + 1, end);
          parent.appendChild(code);
          i = end + 1;
          continue;
        }
      } else if (text[i] === '*' && text[i + 1] === '*') {
        var endBold = text.indexOf('**', i + 2);
        if (endBold > i + 2) {
          flush();
          var strong = document.createElement('strong');
          strong.textContent = text.slice(i + 2, endBold);
          parent.appendChild(strong);
          i = endBold + 2;
          continue;
        }
      } else if (text[i] === '\n') {
        flush();
        parent.appendChild(document.createElement('br'));
        i++;
        continue;
      }
      buf += text[i];
      i++;
    }
    flush();
  }

  function buildCodeBlock(lang, code) {
    var block = createEl('div', 'md-code-block');
    var banner = createEl('div', 'md-code-banner');
    var head = createEl('div', 'md-code-head');
    var langEl = createEl('span', 'md-code-lang');
    langEl.textContent = lang || '';
    head.appendChild(langEl);
    var actions = createEl('span', 'md-code-actions');
    var pre = document.createElement('pre');
    pre.textContent = code;
    var wrapBtn = iconButton('wrap', '自动换行', 'md-code-action');
    wrapBtn.addEventListener('click', function () {
      pre.classList.toggle('is-nowrap');
    });
    var copyBtn = iconButton('copy', '复制', 'md-code-action');
    copyBtn.addEventListener('click', function () {
      copyText(code);
    });
    actions.appendChild(wrapBtn);
    actions.appendChild(copyBtn);
    head.appendChild(actions);
    banner.appendChild(head);
    block.appendChild(banner);
    block.appendChild(pre);
    return block;
  }

  function renderMarkdown(text) {
    var frag = document.createDocumentFragment();
    var lines = String(text == null ? '' : text).split('\n');
    var para = [];
    function flushPara() {
      if (!para.length) {
        return;
      }
      var p = document.createElement('p');
      appendInline(p, para.join('\n'));
      frag.appendChild(p);
      para = [];
    }
    var i = 0;
    while (i < lines.length) {
      var line = lines[i];
      var fence = /^\s*```(\S*)\s*$/.exec(line);
      if (fence) {
        flushPara();
        var lang = fence[1] || '';
        var codeLines = [];
        i++;
        while (i < lines.length && !/^\s*```\s*$/.test(lines[i])) {
          codeLines.push(lines[i]);
          i++;
        }
        if (i < lines.length) {
          i++;
        }
        frag.appendChild(buildCodeBlock(lang, codeLines.join('\n')));
        continue;
      }
      var ordered = /^\s*\d+\.\s+/.test(line);
      var bullet = /^\s*[-*]\s+/.test(line);
      if (ordered || bullet) {
        flushPara();
        var list = document.createElement(ordered ? 'ol' : 'ul');
        while (i < lines.length && (ordered ? /^\s*\d+\.\s+/.test(lines[i]) : /^\s*[-*]\s+/.test(lines[i]))) {
          var li = document.createElement('li');
          appendInline(li, lines[i].replace(/^\s*(?:\d+\.|[-*])\s+/, ''));
          list.appendChild(li);
          i++;
        }
        frag.appendChild(list);
        continue;
      }
      if (!line.trim()) {
        flushPara();
        i++;
        continue;
      }
      para.push(line);
      i++;
    }
    flushPara();
    return frag;
  }

  // —— 消息节点构建（结构与 CSS 对应 DSH 消息态实测值） ——

  function appendUserMessage(container, text, timeText) {
    var row = createEl('div', 'msg-user');
    var stack = createEl('div', 'user-stack');
    var bubble = createEl('div', 'bubble');
    bubble.textContent = text; // textContent 而非 innerHTML：用户数据不进入 HTML 解析
    stack.appendChild(bubble);
    row.appendChild(stack);
    var actions = createEl('div', 'user-actions');
    if (timeText) {
      var time = createEl('span', 'msg-time');
      time.textContent = timeText;
      actions.appendChild(time);
    }
    actions.appendChild(buildCopyButton(function () {
      return text;
    }));
    row.appendChild(actions);
    container.appendChild(row);
    document.body.classList.add('has-messages');
    return row;
  }

  function buildDivider(seconds) {
    var divider = createEl('div', 'turn-divider');
    if (seconds == null) {
      divider.appendChild(document.createTextNode('已完成'));
      return divider;
    }
    divider.appendChild(document.createTextNode('已完成，用时 '));
    var num = createEl('span', 'duration-number');
    num.textContent = String(seconds);
    divider.appendChild(num);
    divider.appendChild(document.createTextNode('秒'));
    return divider;
  }

  function appendDivider(container, seconds) {
    var divider = buildDivider(seconds);
    container.appendChild(divider);
    return divider;
  }

  function buildAssistantShell(container) {
    var node = createEl('div', 'msg-assistant');
    var markdown = createEl('div', 'markdown');
    node.appendChild(markdown);
    container.appendChild(node);
    document.body.classList.add('has-messages');
    return { node: node, markdown: markdown };
  }

  function buildAssistantActions(getText, timeText) {
    var footer = createEl('div', 'assistant-footer');
    var actions = createEl('div', 'assistant-actions');
    actions.appendChild(buildCopyButton(getText));
    var up = iconButton('thumbUp', '好的回答');
    up.setAttribute('aria-pressed', 'false');
    actions.appendChild(up);
    var down = iconButton('thumbDown', '不好的回答');
    down.setAttribute('aria-pressed', 'false');
    actions.appendChild(down);
    actions.appendChild(iconButton('branch', '分支'));
    if (timeText) {
      var time = createEl('span', 'msg-time');
      time.textContent = timeText;
      actions.appendChild(time);
    }
    footer.appendChild(actions);
    return footer;
  }

  function appendAssistantMessage(container, text, timeText) {
    var shell = buildAssistantShell(container);
    shell.markdown.appendChild(renderMarkdown(text));
    shell.node.after(buildAssistantActions(function () {
      return text;
    }, timeText));
    return shell.node;
  }

  function durationSeconds(from, to) {
    var start = from && from.timestamp ? new Date(from.timestamp) : null;
    var end = to && to.timestamp ? new Date(to.timestamp) : null;
    if (!start || !end || isNaN(start.getTime()) || isNaN(end.getTime())) {
      return null;
    }
    return Math.max(0, Math.round((end - start) / 1000));
  }

  function refreshComposerState() {
    if (textarea && placeholder) {
      placeholder.classList.toggle('is-hidden', textarea.value.length > 0);
    }
    if (sendBtn && textarea) {
      sendBtn.disabled = busy || textarea.value.trim().length === 0;
    }
  }

  function clearConversation() {
    if (msgColumn) {
      msgColumn.replaceChildren();
    }
    document.body.classList.remove('has-messages');
    live = null;
  }

  function sendPrompt() {
    if (busy || !textarea) {
      return;
    }
    var prompt = textarea.value.trim();
    if (!prompt) {
      return;
    }
    busy = true;
    appendUserMessage(msgColumn, prompt, formatTime(new Date()));
    live = { userTs: Date.now(), shell: null, text: '' };
    textarea.value = '';
    refreshComposerState();
    scrollToBottom();
    send({ type: 'chat/send', prompt: prompt });
  }

  if (textarea && sendBtn) {
    textarea.addEventListener('input', refreshComposerState);
    textarea.addEventListener('keydown', function (e) {
      if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) {
        e.preventDefault();
        sendPrompt();
      }
    });
    sendBtn.addEventListener('click', sendPrompt);
    refreshComposerState();
  }

  // —— host→web 消息注册（对照 BridgeProtocol.cs 的 [JsonDerivedType]）——

  // app/init：握手应答（主题/版本注入，后续区域重构消费）
  handlers.set('app/init', function (message) {
    document.documentElement.dataset.jccTheme = String(message.theme || '').indexOf('dark') >= 0 ? 'dark' : 'light';
    console.info('jccBridge: app/init theme=' + message.theme + ' version=' + message.version);
  });

  // app/error：host 侧错误指引（为什么/是谁触发/怎么做）；若本轮发送失败则恢复输入态（部分渲染的助手正文保留）
  handlers.set('app/error', function (message) {
    console.error('jccBridge: app/error ' + message.reason + ' → ' + message.guidance);
    if (busy) {
      busy = false;
      refreshComposerState();
    }
  });

  // smoke/ping：冒烟回环应答（host --webui-smoke 校验）
  handlers.set('smoke/ping', function (message) {
    send({ type: 'smoke/pong', nonce: message.nonce });
  });

  // sessions/list：真实会话列表渲染（P6-1）。点击发 session/select；流式回复期间忽略点击（防跨会话混流）。
  // 选区跨重渲染保持（重渲染不重置用户选择）；空列表回填静态"新会话"占位行；渲染完成恒回 web/sessions-rendered（冒烟验收钩子）。
  handlers.set('sessions/list', function (message) {
    var sessions = message.sessions || [];
    if (sidebarList) {
      var iconTemplate = staticSessionRow ? staticSessionRow.querySelector('.icon') : null;
      sidebarList.querySelectorAll('.session-row').forEach(function (el) { el.remove(); });
      if (sessions.length === 0) {
        if (staticSessionRow) {
          sidebarList.appendChild(staticSessionRow);
        }
      } else {
        if (!sessions.some(function (s) { return s.id === selectedSessionId; })) {
          selectedSessionId = sessions[0].id;
        }
        sessions.forEach(function (session) {
          var row = document.createElement('div');
          row.className = 'session-row' + (session.id === selectedSessionId ? ' selected' : '');
          row.dataset.sessionId = session.id;
          if (iconTemplate) {
            row.appendChild(iconTemplate.cloneNode(true));
          }
          var title = document.createElement('span');
          title.textContent = session.title; // textContent 而非 innerHTML：用户数据不进入 HTML 解析
          row.appendChild(title);
          row.addEventListener('click', function () { onSessionClick(session, row); });
          sidebarList.appendChild(row);
        });
      }
    } else {
      console.warn('jccBridge: 找不到侧栏列表容器 .sidebar .list');
    }
    send({ type: 'web/sessions-rendered', count: sessions.length });
  });

  function onSessionClick(session, row) {
    if (busy) {
      console.info('jccBridge: 回复进行中，暂不切换会话（等待 chat/done）');
      return;
    }
    selectedSessionId = session.id;
    sidebarList.querySelectorAll('.session-row.selected').forEach(function (el) { el.classList.remove('selected'); });
    row.classList.add('selected');
    clearConversation();
    send({ type: 'session/select', id: session.id });
  }

  // chat/delta：助手正文增量（累积全文重渲染 markdown；围栏/列表/行内码/粗体增量安全）
  handlers.set('chat/delta', function (message) {
    if (!live || !msgColumn) {
      return;
    }
    if (!live.shell) {
      live.shell = buildAssistantShell(msgColumn);
    }
    live.text += message.text;
    live.shell.markdown.replaceChildren(renderMarkdown(live.text));
    scrollToBottom();
  });

  // chat/done：本轮结束（成功/异常/取消统一收尾）— 有内容则补分隔行 + 动作行，恢复发送态
  handlers.set('chat/done', function () {
    if (live && live.shell) {
      var text = live.text;
      var previous = live.shell.node.previousElementSibling;
      if (previous && previous.classList.contains('msg-user')) {
        var divider = buildDivider(Math.round((Date.now() - live.userTs) / 1000));
        live.shell.node.parentNode.insertBefore(divider, live.shell.node);
      }
      live.shell.node.after(buildAssistantActions(function () {
        return text;
      }, formatTime(new Date())));
    }
    live = null;
    busy = false;
    refreshComposerState();
    scrollToBottom();
  });

  // chat/history：历史会话快照（session/select 后下发），整体替换消息区；回合间插分隔行（用时=助手·用户时间戳差）
  handlers.set('chat/history', function (message) {
    clearConversation();
    var entries = message.messages || [];
    if (entries.length) {
      var previous = null;
      entries.forEach(function (entry) {
        var isUser = entry.role === 'user';
        if (!isUser && previous && previous.role === 'user') {
          appendDivider(msgColumn, durationSeconds(previous, entry));
        }
        if (isUser) {
          appendUserMessage(msgColumn, entry.content, formatTime(entry.timestamp));
        } else {
          appendAssistantMessage(msgColumn, entry.content, formatTime(entry.timestamp));
        }
        previous = entry;
      });
      document.body.classList.add('has-messages');
    }
    scrollToBottom();
  });

  // —— 握手：页面脚本就绪（ADR 0141 决策 4，host 收到后回 app/init）——
  send({ type: 'web/ready' });
})();
