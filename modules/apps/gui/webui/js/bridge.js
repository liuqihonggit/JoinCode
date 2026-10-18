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

  // —— 会话流 v1 前端状态（P6-2/P6-3 最小切片，样式细化随区域对齐推进）——
  var messageList = document.querySelector('.message-list');
  var textarea = document.querySelector('.composer-textarea');
  var placeholder = document.querySelector('.composer-placeholder');
  var sendBtn = document.querySelector('.send-btn');
  var sidebarList = document.querySelector('.sidebar .list');
  var busy = false;
  var currentAssistant = null;

  function addMessage(role, text) {
    if (!messageList) {
      return null;
    }
    var node = document.createElement('div');
    node.className = 'msg msg-' + role;
    node.textContent = text; // textContent 而非 innerHTML：用户/模型数据不进入 HTML 解析
    messageList.appendChild(node);
    document.body.classList.add('has-messages');
    messageList.scrollTop = messageList.scrollHeight;
    return node;
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
    if (messageList) {
      messageList.replaceChildren();
    }
    document.body.classList.remove('has-messages');
    currentAssistant = null;
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
    currentAssistant = null;
    addMessage('user', prompt);
    textarea.value = '';
    refreshComposerState();
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
    document.documentElement.dataset.jccTheme = message.theme;
    console.info('jccBridge: app/init theme=' + message.theme + ' version=' + message.version);
  });

  // app/error：host 侧错误指引（为什么/是谁触发/怎么做）；若本轮发送失败则恢复输入态
  handlers.set('app/error', function (message) {
    console.error('jccBridge: app/error ' + message.reason + ' → ' + message.guidance);
    if (busy) {
      busy = false;
      currentAssistant = null;
      refreshComposerState();
    }
  });

  // smoke/ping：冒烟回环应答（host --webui-smoke 校验）
  handlers.set('smoke/ping', function (message) {
    send({ type: 'smoke/pong', nonce: message.nonce });
  });

  // sessions/list：真实会话列表渲染（P6-1）。首行=最近会话（selected）；点击发 session/select。
  // 空列表保留静态"新会话"占位行（全新状态）；渲染完成回 web/sessions-rendered（冒烟验收钩子）。
  handlers.set('sessions/list', function (message) {
    if (!sidebarList) {
      console.warn('jccBridge: 找不到侧栏列表容器 .sidebar .list');
      return;
    }
    var sessions = message.sessions || [];
    if (sessions.length > 0) {
      var iconTemplate = sidebarList.querySelector('.session-row .icon');
      sidebarList.querySelectorAll('.session-row').forEach(function (el) { el.remove(); });
      sessions.forEach(function (session, index) {
        var row = document.createElement('div');
        row.className = 'session-row' + (index === 0 ? ' selected' : '');
        row.dataset.sessionId = session.id;
        if (iconTemplate) {
          row.appendChild(iconTemplate.cloneNode(true));
        }
        var title = document.createElement('span');
        title.textContent = session.title; // textContent 而非 innerHTML：用户数据不进入 HTML 解析
        row.appendChild(title);
        row.addEventListener('click', function () {
          sidebarList.querySelectorAll('.session-row.selected').forEach(function (el) { el.classList.remove('selected'); });
          row.classList.add('selected');
          clearConversation();
          send({ type: 'session/select', id: session.id });
        });
        sidebarList.appendChild(row);
      });
    }
    send({ type: 'web/sessions-rendered', count: sessions.length });
  });

  // chat/delta：助手正文增量（v1 纯文本累积；Markdown/工具卡随 P6-3）
  handlers.set('chat/delta', function (message) {
    if (!currentAssistant) {
      currentAssistant = addMessage('assistant', '');
    }
    if (currentAssistant) {
      currentAssistant.textContent += message.text;
    }
    if (messageList) {
      messageList.scrollTop = messageList.scrollHeight;
    }
  });

  // chat/done：本轮结束（成功/异常/取消统一收尾），恢复发送态
  handlers.set('chat/done', function () {
    busy = false;
    currentAssistant = null;
    refreshComposerState();
  });

  // chat/history：历史会话快照（session/select 后下发），整体替换消息区
  handlers.set('chat/history', function (message) {
    clearConversation();
    var entries = message.messages || [];
    entries.forEach(function (entry) {
      addMessage(entry.role === 'user' ? 'user' : 'assistant', entry.content);
    });
  });

  // —— 握手：页面脚本就绪（ADR 0141 决策 4，host 收到后回 app/init）——
  send({ type: 'web/ready' });
})();
