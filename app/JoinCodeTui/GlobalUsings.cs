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
global using IO.FileSystem;

// App Builder (EngineSessionFactory)
global using JoinCode.App.Builder;

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
global using GuiColor = Terminal.Gui.Drawing.Color;
global using GuiAttribute = Terminal.Gui.Drawing.Attribute;
global using GuiTextStyle = Terminal.Gui.Drawing.TextStyle;
global using JoinCode.Tui.Rendering;
global using JoinCode.Tui.Views;
global using JoinCode.Tui.Commands;
global using JoinCode.Tui.Pipes;
global using TuiKey = Terminal.Gui.Input.Key;
