// Abstractions 门面 — TUI 组件依赖的接口/DTO
global using JoinCode.Abstractions.Attributes;
global using JoinCode.Abstractions.Configuration;
global using JoinCode.Abstractions.Interfaces;
global using JoinCode.Abstractions.LLM;
global using JoinCode.Abstractions.LLM.Chat;
global using JoinCode.Abstractions.LLM.Execution;
global using JoinCode.Abstractions.Models.Agent;
global using JoinCode.Abstractions.Security;
global using JoinCode.Abstractions.Security.Permission;
global using JoinCode.Abstractions.Tools;
global using IO.FileSystem;

// App Builder (EngineSessionFactory)
global using JoinCode.App.Builder;

// 共享斜杠命令执行器（与 GUI 同一链路）
global using JoinCode.Cli.Commands;

// 底层命令系统 — CmdMap/ChatCommandRegistry/CommandServices（转发斜杠命令，不自己实现一套）
global using JoinCode.ChatCommands;
global using JoinCode.Abstractions.Cmd;
global using JoinCode.Abstractions.Configuration.Execution;
global using JoinCode.Abstractions.Interfaces.Scheduling;
global using Core.CostTracking;
global using Core.Goal;
global using Core.Hooks.Configuration;
global using Core.Memdir;
global using Core.Permission;
global using Core.Plugins;
global using Core.Query;
global using Core.Scheduling.Cron;
global using Core.Scheduling.Tasks;
global using Core.Security.Services;
global using Core.Bridge;
global using Services.Api;
global using Services.OAuth;
global using Services.Shell;
global using Services.Web;
global using JoinCode.Services;
global using Tools.Shell;

// Queue (CommandQueue/QueueSnapshot)
global using JoinCode.Queue;

// FrozenDictionary
global using System.Collections.Frozen;
global using System.Collections.ObjectModel;
global using System.Text;

// TUI 渲染层 — Terminal.Gui v2
global using Terminal.Gui.App;
global using Terminal.Gui.ViewBase;
global using Terminal.Gui.Views;
global using Terminal.Gui.Drawing;
global using Terminal.Gui.Editor;
global using Terminal.Gui.Input;
global using GuiColor = Terminal.Gui.Drawing.Color;
global using GuiAttribute = Terminal.Gui.Drawing.Attribute;
global using GuiTextStyle = Terminal.Gui.Drawing.TextStyle;
global using JoinCode.Tui.Rendering;
global using JoinCode.Tui.Views;
global using JoinCode.Tui.Commands;
global using JoinCode.Tui.Pipes;
global using JoinCode.Tui.Diagnostics;
global using TuiKey = Terminal.Gui.Input.Key;
