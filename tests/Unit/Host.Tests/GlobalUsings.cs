// Merged from Tui.Tests + Core.Tests.Terminal
// Xunit, Moq, FluentAssertions, Microsoft.Extensions.Logging are in tests/Directory.Build.props

global using System.Collections.Frozen;
global using System.Text;
global using System.Text.RegularExpressions;

global using JoinCode.Abstractions.Interfaces;
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
global using Core.Hooks.Configuration;
global using Core.Hooks.Events;
global using Core.Memdir;
global using Core.Plugins;
global using Core.Utils;

global using Infrastructure.Localization;
global using Infrastructure.Utils.Text;
global using IO.FileSystem;
global using Services.Api;

global using JoinCode.ChatCommands;
global using JoinCode.Services;
global using Core.CostTracking;
global using Microsoft.Extensions.DependencyInjection;

global using System.Runtime.CompilerServices;

global using Microsoft.Extensions.Time.Testing;

global using JoinCode.Pipe;
global using Testing.Common;

global using JoinCode.Adapters;
global using JoinCode.Cli;
global using IMcpClient = JoinCode.Abstractions.Mcp.Client.IMcpClient;
global using IMcpToolRegistry = JoinCode.Abstractions.Mcp.Registry.IMcpToolRegistry;
