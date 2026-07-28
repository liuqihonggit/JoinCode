namespace Core.Bridge;

using JoinCode.Abstractions.Pipeline;

public interface IShutdownMiddleware : IMiddleware<ShutdownContext> { }
