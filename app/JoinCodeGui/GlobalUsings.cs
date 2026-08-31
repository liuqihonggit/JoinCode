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
global using McpBridge;

// Diff 模型（工具调用结果渲染）
global using JoinCode.Abstractions.Models.Diff;
global using JoinCode.Abstractions.Models.Interactive;
global using JoinCode.Abstractions.Interfaces;
global using JoinCode.Abstractions.LLM.Chat;
global using JoinCode.Abstractions.Configuration.AppData;
global using IO.FileSystem;

// GUI ViewModel 层 DTO（SlashCommandItem 等）
global using JoinCode.Gui.ViewModels;

// GUI 斜杠命令核心逻辑（Trie 前缀树、光标解析、匹配排序）
global using JoinCode.Gui.SlashCommands;

// 共享斜杠命令执行器（与 TUI 同一链路）
global using JoinCode.Cli.Commands;

// Hosting 层读取 teammate 子会话列表
global using Core.Scheduling.Tasks;global using Avalonia;
global using Avalonia.Controls;
global using Avalonia.Controls.ApplicationLifetimes;
global using Avalonia.Controls.Documents;
global using Avalonia.Data.Converters;
global using Avalonia.Input;
global using Avalonia.Interactivity;
global using Avalonia.Layout;
global using Avalonia.Markup.Xaml;
global using Avalonia.Media;
global using Avalonia.Media.TextFormatting;
global using Avalonia.Media.Transformation;
global using Avalonia.Styling;
global using Avalonia.Themes.Fluent;
global using Avalonia.Threading;
global using Avalonia.VisualTree;
global using CommunityToolkit.Mvvm.ComponentModel;
global using CommunityToolkit.Mvvm.Input;
global using JoinCode.Abstractions.Configuration.Llm;
global using JoinCode.Abstractions.Configuration.Providers;
global using JoinCode.Abstractions.Configuration.Settings;
global using JoinCode.Abstractions.LLM;
global using JoinCode.Abstractions.Security;
global using JoinCode.Abstractions.Security.Permission;
global using JoinCode.Abstractions.Security.Shell;
global using JoinCode.Abstractions.Tools;
global using JoinCode.Abstractions.UI;
global using JoinCode.App.Builder;
global using JoinCode.Gui.Hosting;
global using JoinCode.Gui.Theming;
global using JoinCode.Gui.Views;
global using Markdig;
global using Markdig.Extensions.Tables;
global using Markdig.Syntax;
global using Markdig.Syntax.Inlines;
global using System.ComponentModel;
global using System.Text.Json;
global using System.Text.Json.Serialization;
