global using System.Collections.Concurrent;
global using System.Diagnostics;
global using System.Text;
global using Core.Permission;
global using Core.Utils;
global using JoinCode.Abstractions.Entity;
global using JoinCode.Abstractions.Interfaces;
global using JoinCode.Abstractions.LLM.Chat;
global using JoinCode.Abstractions.Models.Diff;
global using JoinCode.Abstractions.Utils;
global using JoinCode.Adapters;
global using JoinCode.App.Builder;
global using JoinCode.Cli;
global using Microsoft.Extensions.DependencyInjection;
global using NativeTui.Core;
global using NativeTui.Core.Ansi;
global using NativeTui.Core.Bridge;
global using NativeTui.Core.Components;
global using NativeTui.Core.Grid;
global using NativeTui.Core.Repl;
global using NativeTui.Core.Terminal;
// 引擎侧权限类型别名 — 引擎 JoinCode.Abstractions.Security.PermissionMode 与 Core NativeTui.Core.Repl.PermissionMode
// 同名（枚举集合一致，各属两侧），不能同时裸导入命名空间，Security 侧统一走别名消除歧义
global using EnginePermissionMode = JoinCode.Abstractions.Security.PermissionMode;
global using EnginePermissionModeExtensions = JoinCode.Abstractions.Security.PermissionModeExtensions;
global using SecurityPermissionChecker = JoinCode.Abstractions.Security.Permission.IPermissionChecker;
global using PermissionPendingException = JoinCode.Abstractions.Security.PermissionPendingConfirmationException;
