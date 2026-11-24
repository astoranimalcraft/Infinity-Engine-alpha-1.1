// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Infinity.Services;

/// <summary>The service families the engine knows about. Custom is for a project's own backend.</summary>
public enum ServiceKind
{
    Custom,
    Server,
    Steam,
    EpicGames,
    Discord,
    PlayFab,
    GameJolt,
}

/// <summary>Lifecycle of a service. A service that is NotConfigured still exposes its local APIs.</summary>
public enum ServiceState
{
    NotConfigured,
    Ready,
    Running,
    Failed,
    Disabled,
}

/// <summary>Result of a service operation. Never throws for an expected failure such as a missing key.</summary>
public sealed class ServiceResult
{
    /// <summary>True when the operation completed.</summary>
    public bool Success { get; init; }

    /// <summary>Human-readable failure reason, or null on success.</summary>
    public string? Error { get; init; }

    /// <summary>Provider-specific status code. Zero means no code was supplied.</summary>
    public int Code { get; init; }

    /// <summary>Optional response body, JSON, or provider payload.</summary>
    public string? Payload { get; init; }

    /// <summary>Creates a successful result.</summary>
    public static ServiceResult Ok(string? payload = null) => new() { Success = true, Payload = payload };

    /// <summary>Creates a failed result.</summary>
    public static ServiceResult Fail(string error, int code = 0) => new() { Success = false, Error = error, Code = code };
}

/// <summary>A service the engine and game code can initialize, tick and shut down.</summary>
public interface IGameService : IDisposable
{
    /// <summary>Stable display name, such as "Steam" or "Game Server".</summary>
    string Name { get; }

    /// <summary>Family used by tools and the documentation window.</summary>
    ServiceKind Kind { get; }

    /// <summary>Current lifecycle state.</summary>
    ServiceState State { get; }

    /// <summary>Starts the service. Returns a failed result instead of throwing when configuration is missing.</summary>
    ServiceResult Initialize();

    /// <summary>Pumps the service. Called once per frame by the game or by <see cref="ServiceManager"/>.</summary>
    void Tick(float deltaTime);

    /// <summary>Stops the service and releases its resources.</summary>
    void Shutdown();
}

/// <summary>Base class for a service with safe defaults: initialization succeeds and shutdown disables it.</summary>
public abstract class GameServiceBase : IGameService
{
    protected GameServiceBase(string name, ServiceKind kind)
    {
        Name = name;
        Kind = kind;
    }

    /// <inheritdoc />
    public string Name { get; protected set; }

    /// <inheritdoc />
    public ServiceKind Kind { get; protected set; }

    /// <inheritdoc />
    public ServiceState State { get; protected set; } = ServiceState.NotConfigured;

    /// <inheritdoc />
    public virtual ServiceResult Initialize()
    {
        State = ServiceState.Ready;
        return ServiceResult.Ok();
    }

    /// <inheritdoc />
    public virtual void Tick(float deltaTime)
    {
    }

    /// <inheritdoc />
    public virtual void Shutdown()
    {
        State = ServiceState.Disabled;
    }

    /// <inheritdoc />
    public virtual void Dispose() => Shutdown();
}

/// <summary>
/// Keeps the game's online services in one place. Register Steam, Epic, a game server or a custom backend once,
/// then tick the manager from the game loop. All operations are safe when a service is not configured.
/// </summary>
public sealed class ServiceManager : IDisposable
{
    private readonly List<IGameService> _services = [];
    private readonly object _gate = new();

    /// <summary>The process-wide manager. Games with several worlds can create their own instance instead.</summary>
    public static ServiceManager Shared { get; } = new();

    /// <summary>Snapshot of the registered services.</summary>
    public IReadOnlyList<IGameService> Services
    {
        get { lock (_gate) return _services.ToArray(); }
    }

    /// <summary>Registers a service and returns it, so calls can be chained.</summary>
    public T Register<T>(T service) where T : IGameService
    {
        ArgumentNullException.ThrowIfNull(service);
        lock (_gate)
        {
            if (!_services.Contains(service)) _services.Add(service);
        }
        return service;
    }

    /// <summary>Finds the first service of a type.</summary>
    public T? Get<T>() where T : class, IGameService
    {
        lock (_gate)
        {
            foreach (IGameService service in _services)
                if (service is T typed) return typed;
        }
        return null;
    }

    /// <summary>Finds a service by type without throwing when it is absent.</summary>
    public bool TryGet<T>(out T service) where T : class, IGameService
    {
        service = Get<T>()!;
        return service != null;
    }

    /// <summary>Initializes every registered service, collecting failures instead of throwing.</summary>
    public IReadOnlyList<ServiceResult> InitializeAll()
    {
        var results = new List<ServiceResult>();
        foreach (IGameService service in Services)
        {
            try { results.Add(service.Initialize()); }
            catch (Exception e) { results.Add(ServiceResult.Fail($"{service.Name}: {e.Message}")); }
        }
        return results;
    }

    /// <summary>Ticks every registered service. One failing service does not stop the others.</summary>
    public void Tick(float deltaTime)
    {
        foreach (IGameService service in Services)
        {
            try { service.Tick(deltaTime); }
            catch { /* a service must not take the game loop down */ }
        }
    }

    /// <summary>Shuts down every registered service and clears the registry.</summary>
    public void ShutdownAll()
    {
        IGameService[] services;
        lock (_gate)
        {
            services = _services.ToArray();
            _services.Clear();
        }
        foreach (IGameService service in services)
        {
            try { service.Shutdown(); } catch { }
            try { service.Dispose(); } catch { }
        }
    }

    /// <inheritdoc />
    public void Dispose() => ShutdownAll();
}
