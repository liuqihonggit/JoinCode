// Contracts 命名空间
global using Core.Utils;
global using JoinCode.Abstractions.Attributes;
global using JoinCode.Abstractions.Clock;
global using JoinCode.Abstractions.Configuration.AppData;
global using JoinCode.Abstractions.Configuration.Execution;
global using JoinCode.Abstractions.Interfaces;
global using JoinCode.Abstractions.Execution;
global using ExecutionContext = JoinCode.Abstractions.Execution.ExecutionContext;
global using JoinCode.Abstractions.Models;
global using JoinCode.Abstractions.Pipeline;
global using JoinCode.Abstractions.Security;
global using JoinCode.Abstractions.Security.Permission;
global using JoinCode.Abstractions.Tools;
global using JoinCode.Abstractions.Utils;

// Transport 命名空间
global using JoinCode.Transport;
global using JoinCode.Transport.Bridge;
global using JoinCode.Abstractions.Transport;

// Transport 迁移类型别名（Bridge 前缀 → Transport.Impl 新名）
global using BridgeNdjsonActivityType = JoinCode.Transport.Bridge.NdjsonActivityType;
global using BridgeNdjsonActivity = JoinCode.Transport.Bridge.NdjsonActivity;
global using BridgePermissionRequest = JoinCode.Transport.Bridge.NdjsonPermissionRequest;
global using BridgeNdjsonParser = JoinCode.Transport.Bridge.NdjsonParser;

// Bridge 内部子命名空间
global using Core.Bridge.Models;
global using Core.Bridge.Handlers;

// Microsoft.Extensions
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Logging;

// System（超出隐式 using）
global using System.Collections.Concurrent;
global using System.Diagnostics;
global using System.Globalization;
global using System.IO;
global using System.Net;
global using System.Net.Http;
global using System.Net.Http.Headers;
global using System.Net.Sockets;
global using System.Net.WebSockets;
global using System.Runtime.InteropServices;
global using System.Security.Cryptography;
global using System.Text;
global using System.Text.Json.Nodes;
global using System.Text.RegularExpressions;
global using Infrastructure.Pipeline;
global using QRCoder;
