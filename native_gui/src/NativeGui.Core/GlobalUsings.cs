// NativeGui.Core 全局 using（ADR 0145）：与旧 JoinCodeGui 共享清单剔除 Avalonia 19 条 + WebView2 + 
// JoinCode.App.Builder（引擎装配独占 Host）——协议层零 UI 依赖由编译零警告兜底（引入即 CS0246）。
// 用户 settings.json 的 DTO 与路径（DSH 宿主启动期读模型目录）
global using Core.Configuration;
// 引擎 DI 组装（Composition 契约）
global using Core.DependencyInjection;
// Hosting 契约读取 teammate 子会话列表
global using Core.Scheduling.Tasks;
global using Core.Utils;
global using Infrastructure.IO.Services.FileOps;
global using Infrastructure.IO.Services.Diff;
global using IO.FileSystem;
global using JoinCode.Abstractions.Attributes;
global using JoinCode.Abstractions.Configuration.AppData;
global using JoinCode.Abstractions.Configuration.Llm;
global using JoinCode.Abstractions.Configuration.Providers;
global using JoinCode.Abstractions.Configuration.Settings;
global using JoinCode.Abstractions.Interfaces;
global using JoinCode.Abstractions.LLM;
global using JoinCode.Abstractions.LLM.Chat;
global using JoinCode.Abstractions.LLM.Execution;
global using JoinCode.Abstractions.State;
global using JoinCode.Abstractions.Models.Diff;
global using JoinCode.Abstractions.Models.Goal;
global using JoinCode.Abstractions.Models.Interactive;
global using JoinCode.Abstractions.Security;
global using JoinCode.Abstractions.Security.Permission;
global using JoinCode.Abstractions.Security.Shell;
global using JoinCode.Abstractions.Tools;
global using JoinCode.Abstractions.UI;
global using JoinCode.Abstractions.Utils;
global using JoinCode.BclBridge;
global using JoinCode.Gui.Hosting;
global using JoinCode.Gui.Theming;
global using System;
global using System.Collections.Concurrent;
global using System.Collections.Frozen;
global using System.Collections.Generic;
global using System.Collections.ObjectModel;
global using System.ComponentModel;
global using System.Diagnostics.CodeAnalysis;
global using System.IO.Compression;
global using System.Linq;
global using System.Net;
global using System.Net.Sockets;
global using System.Net.WebSockets;
global using System.Runtime.CompilerServices;
global using System.Text;
global using System.Text.Json;
global using System.Text.Json.Serialization;
global using System.Threading;
global using System.Threading.Tasks;
// webview 协议面（BridgeProtocol/PermissionPromptBroker/WebChatFlow，ADR 0145 随迁 Core）
global using JoinCode.Gui.WebView;
// DSH 前端宿主协议层（ADR 0143/0145）
global using JoinCode.Gui.WebHost;
