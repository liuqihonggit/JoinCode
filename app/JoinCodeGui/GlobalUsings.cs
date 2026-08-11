global using System;
global using System.Linq;
global using System.Collections.Generic;
global using System.Collections.ObjectModel;
global using System.Collections.Frozen;
global using System.Runtime.CompilerServices;
global using System.Text;
global using System.Threading;
global using System.Threading.Tasks;

// 引擎 DI 组装（Composition + 共享管道）
global using Core.DependencyInjection;
global using JoinCode.Pipelines;
global using JoinCode.Pipelines.Middlewares;

// Diff 模型（工具调用结果渲染）
global using JoinCode.Abstractions.Models.Diff;
global using JoinCode.Abstractions.Interfaces;

// GUI ViewModel 层 DTO（SlashCommandItem 等）
global using JoinCode.Gui.ViewModels;

// GUI 斜杠命令核心逻辑（Trie 前缀树、光标解析、匹配排序）
global using JoinCode.Gui.SlashCommands;