// Merged from Tui.Tests + Core.Tests.Terminal
// Xunit, Moq, FluentAssertions, Microsoft.Extensions.Logging are in tests/Directory.Build.props

global using System.Collections.Frozen;
global using System.Text;
global using System.Text.RegularExpressions;

global using JoinCode.Abstractions.Interfaces;
global using JoinCode.Abstractions.Models.Agent;
global using JoinCode.Abstractions.Models.Diff;
global using JoinCode.Abstractions.Security;
global using JoinCode.Abstractions.Security.Shell;
global using JoinCode.Abstractions.Configuration;
global using JoinCode.Abstractions.Configuration.AppData;
global using JoinCode.Abstractions.Configuration.Providers;
global using JoinCode.Abstractions.Configuration.Execution;
global using JoinCode.Abstractions.Configuration.Llm;
global using JoinCode.Abstractions.Configuration.Settings;
global using JoinCode.Abstractions.LLM.Chat;
global using JoinCode.Abstractions.Onboarding;
global using JoinCode.Abstractions.Tools;
global using JoinCode.Abstractions.UI;
global using JoinCode.Abstractions.Utils;
global using JoinCode.Abstractions.ChatCommands;

global using JoinCode.Abstractions.Localization;
global using JoinCode.Abstractions.Hooks;

global using Core.Configuration;
global using Core.Context;
global using Core.Context.Modality;
global using Core.Agents.Coordinator;
global using JoinCode.Abstractions.LLM.Execution;
global using Core.Hooks.Configuration;
global using Core.Hooks.Events;
global using Core.Memdir;
global using Core.Plugins;
global using Core.Utils;

global using Infrastructure.Localization;
global using Infrastructure.Utils.Text;
global using IO.FileSystem;
global using IO.Services;
global using Services.Api;

global using JoinCode.ChatCommands;
global using JoinCode.Services;
global using Core.CostTracking;
global using Microsoft.Extensions.DependencyInjection;
global using ServiceLifetime = JoinCode.Abstractions.Attributes.ServiceLifetime;

global using System.Runtime.CompilerServices;

global using Microsoft.Extensions.Time.Testing;

global using JoinCode.Pipe;
global using Testing.Common;

global using JoinCode.Adapters;
global using JoinCode.Cli;
global using JoinCode.Queue;
global using JoinCode.Tui.Pipes;
global using JoinCode.Tui.Rendering;
global using JoinCode.Tui.Views;
global using JoinCode.Tui.Commands;
global using JoinCode.Tui;
global using JoinCode.Tui.Session;
global using Host.Tests.Tui.Rendering;
global using Terminal.Gui.App;
global using Terminal.Gui.ViewBase;
global using Terminal.Gui.Views;
global using JoinCode.Cli.Interaction;
global using JoinCode.Abstractions.Models.Interactive;
global using IMcpClient = JoinCode.Abstractions.Mcp.Client.IMcpClient;
global using IMcpToolRegistry = JoinCode.Abstractions.Mcp.Registry.IMcpToolRegistry;
global using Core.Bridge;
global using Core.Configuration.Providers;
global using Core.Policy;
global using Infrastructure.Time;
global using JoinCode;
global using JoinCode.Abstractions.Clock;
global using JoinCode.Abstractions.Exceptions;
global using JoinCode.Abstractions.Interfaces.Scheduling;
global using JoinCode.Abstractions.LLM;
global using JoinCode.Abstractions.Models;
global using JoinCode.Abstractions.Models.ErrorRecovery;
global using JoinCode.Abstractions.Models.Goal;
global using JoinCode.Abstractions.Models.Policy;
global using JoinCode.Abstractions.Models.Task;
global using JoinCode.Abstractions.Models.Telemetry;
global using JoinCode.Abstractions.Models.Todo;
global using JoinCode.Abstractions.Prompts;
global using JoinCode.Abstractions.Prompts.ToolPrompts;
global using JoinCode.Abstractions.Security.Permission;
global using JoinCode.Abstractions.Utils.Diagnostics;
global using JoinCode.App.Builder;
global using JoinCode.ChatCommands.Bridge;
global using JoinCode.Entry;
global using JoinCode.Tui.Interaction;
global using JoinCode.Tui.Tui;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Http;
global using Microsoft.Extensions.Options;
global using Moq;
global using Services.OAuth;
global using System.Reflection;
global using System.Text.Json;
global using TestInMemFs = Testing.Common.Services.InMemoryFileSystem;
