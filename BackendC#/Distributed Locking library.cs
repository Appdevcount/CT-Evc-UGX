namespace ucx.locking.@base.models;

public class LockOptions
{
    public TimeSpan AcquireLockTimeout { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan ExpireLockTimeout { get; set; } = TimeSpan.FromMinutes(5);
}


using ucx.locking.@base.models;

namespace ucx.locking.@base.interfaces;

public interface ILockingService
{
    Task<IAsyncDisposable> LockAsync<T>(string key, TimeSpan acquireTimeout, TimeSpan lockExpiry, CancellationToken cancellationToken);
    Task<IAsyncDisposable> LockAsync<T>(string key, LockOptions lockOptions, CancellationToken cancellationToken);
}


using Medallion.Threading;
using Medallion.Threading.Redis;
using StackExchange.Redis;
using Ucx.Locking.interfaces;

namespace Ucx.Locking.services;

public class RedisDistributedLockFactory : IRedisDistributedLockFactory
{
    public IDistributedLock Create(string key, IDatabase db, Action<RedisDistributedSynchronizationOptionsBuilder> options) => new RedisDistributedLock(key, db, options);
}


using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using ucx.locking.@base.interfaces;
using ucx.locking.@base.models;
using Ucx.Locking.interfaces;
using Ucx.Locking.models;

namespace Ucx.Locking.services;

public class RedisLockService : ILockingService
{
    private readonly ILogger<RedisLockService> _logger;
    private readonly Dictionary<string, object> _scope = new();
    public static IConnectionMultiplexer Connection => _lazyConnection?.Value;
    private static ILazy<IConnectionMultiplexer>? _lazyConnection;
    private readonly RedisLockingOptions _options;
    private readonly IRedisDistributedLockFactory _lockFactory;

    /// <summary>
    ///
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="redis">Must be initialized using the static method ConnectionMultiplexer.Connect(options)</param>
    /// <param name="lockBuilder"></param>
    public RedisLockService(ILogger<RedisLockService> logger, RedisLockingOptions options, IRedisDistributedLockFactory lockFactory, ILazy<IConnectionMultiplexer> lazyConnection)
    {
        _logger = logger;
        _scope["Class"] = nameof(RedisLockService);
        _options = options;
        _lockFactory = lockFactory;
        _lazyConnection = lazyConnection;
    }

    public async Task<IAsyncDisposable> LockAsync<T>(string key, TimeSpan acquireTimeout, TimeSpan lockExpiry, CancellationToken cancellationToken)
    {
        _scope["Method"] = "Task Lock(string key, TimeSpan timeout, CancellationToken cancellationToken)";
        var compoundKey = $"{typeof(T).Name}-{key}";
        _scope["key"] = compoundKey;
        _scope["timeout"] = acquireTimeout.ToString();
        using (_logger.BeginScope(_scope))
        {
            var locker = _lockFactory.Create(key, Connection.GetDatabase(), builder => { builder.Expiry(lockExpiry); });
            _logger.LogDebug("Attempting to lock");
            var handle = await locker.AcquireAsync(acquireTimeout, cancellationToken);
            handle.HandleLostToken.Register(() => { _logger.LogError("The lock token was lost"); });
            _logger.LogDebug("Lock acquired");
            return handle;
        }
    }

    public async Task<IAsyncDisposable> LockAsync<T>(string key, LockOptions lockOptions, CancellationToken cancellationToken)
    {
        return await LockAsync<T>(key, lockOptions.AcquireLockTimeout, lockOptions.ExpireLockTimeout, cancellationToken);
    }

}


using StackExchange.Redis;

namespace Ucx.Locking.models;

public class RedisLockingOptions
{
    public string ConnectionString { get; set; }
    public IConnectionMultiplexer? CustomConnectionMultiplexer { get; set; }
    public bool SetMaxThreads { get; set; } = true;
    public bool AbortOnConnectTimeout { get; set; } = true;
    public TimeSpan AsyncTimeout { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan SyncTimeout { get; set; } = TimeSpan.FromMinutes(10);
}


using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;
using Ucx.Locking.services;

namespace Ucx.Locking.healthcheck;

public class RedisHealthCheck : IHealthCheck
{
    private readonly ILazy<IConnectionMultiplexer> _lazyConnectionMultiplexer;

    public RedisHealthCheck(ILazy<IConnectionMultiplexer> lazyConnectionMultiplexer)
    {
        _lazyConnectionMultiplexer = lazyConnectionMultiplexer;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var isHealthy = _lazyConnectionMultiplexer.Value.IsConnected;

        return Task.FromResult(isHealthy ? HealthCheckResult.Healthy("A healthy result.") : HealthCheckResult.Unhealthy("An unhealthy result."));
    }
}


using Medallion.Threading;
using Medallion.Threading.Redis;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using ucx.locking.@base.interfaces;
using Ucx.Locking.builders;
using Ucx.Locking.interfaces;
using Ucx.Locking.models;
using Ucx.Locking.services;

namespace Ucx.Locking.extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRedisLocking(this IServiceCollection serviceCollection, Action<RedisLockingOptions> options)
    {
        var config = new RedisLockingOptions();
        options.Invoke(config);
        if (string.IsNullOrWhiteSpace(config.ConnectionString) && config.CustomConnectionMultiplexer == null)
            throw new ArgumentNullException("ConnectionString or CustomConnectionMultiplexer must be set");

        if (config.SetMaxThreads)
        {
            ThreadPool.GetMaxThreads(out var workerThreads, out var completionPortThreads);
            ThreadPool.SetMinThreads(workerThreads, completionPortThreads);
        }

        if (config.CustomConnectionMultiplexer == null)
            serviceCollection.AddSingleton<ILazy<IConnectionMultiplexer>>(
                new services.Lazy<IConnectionMultiplexer>(() =>
                    ConnectionMultiplexer.Connect(config.ConnectionString, o =>
                    {
                        o.AbortOnConnectFail = config.AbortOnConnectTimeout;
                        o.AsyncTimeout = (int)config.AsyncTimeout.TotalMilliseconds;
                        o.ConnectTimeout = (int)config.ConnectTimeout.TotalMilliseconds;;
                        o.SyncTimeout = (int)config.SyncTimeout.TotalMilliseconds;;
                    })));
        else
            serviceCollection.AddSingleton<ILazy<IConnectionMultiplexer>>(new services.Lazy<IConnectionMultiplexer>(config.CustomConnectionMultiplexer));

        serviceCollection.AddSingleton(config);
        serviceCollection.AddSingleton<IRedisDistributedLockFactory, RedisDistributedLockFactory>();

        serviceCollection.AddSingleton<ILockingService, RedisLockService>();

        return serviceCollection;
    }
}


using Medallion.Threading;
using Medallion.Threading.Redis;
using StackExchange.Redis;
using Ucx.Locking.interfaces;

namespace Ucx.Locking.builders;

public class DistributedLockBuilder : IDistributedLockBuilder
{
    private static Dictionary<string, IDistributedLock> _distributedLocks = new();

    public IDistributedLock BuildRedisLock(string key, IDatabase database, Action<RedisDistributedSynchronizationOptionsBuilder>? options = null)
    {
        if (!_distributedLocks.ContainsKey(key))
            _distributedLocks[key] = new RedisDistributedLock(new RedisKey(key), database, options);
        return _distributedLocks[key];
    }

}



Thanks and Regards
Siraj

From: R, Sirajudeen (CTR) 
Sent: Tuesday, August 20, 2024 8:28 PM
To: R, Sirajudeen (CTR) <sirajudeen.r@evicore.com>
Subject: lklib



Thanks and Regards
Siraj

