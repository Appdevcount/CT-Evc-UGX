# Gang of Four (GOF) Design Patterns in Ucx.AclService.Erss Solution

## Overview
This document provides a comprehensive analysis of Gang of Four (GOF) design patterns implemented throughout the Ucx.AclService.Erss solution. The document includes detailed descriptions, pattern categories, code snippets, and implementation examples.

---

## Table of Contents
1. [Creational Patterns](#creational-patterns)
   - [Singleton Pattern](#singleton-pattern)
   - [Factory Pattern](#factory-pattern)
2. [Structural Patterns](#structural-patterns)
   - [Adapter Pattern](#adapter-pattern)
   - [Facade Pattern](#facade-pattern)
3. [Behavioral Patterns](#behavioral-patterns)
   - [Observer Pattern](#observer-pattern)
   - [Strategy Pattern](#strategy-pattern)
   - [Template Method Pattern](#template-method-pattern)
   - [Chain of Responsibility Pattern](#chain-of-responsibility-pattern)

---

## Creational Patterns

### Singleton Pattern

#### Description
The Singleton pattern restricts the instantiation of a class to a single object and provides a global point of access to it. In this solution, it's used for cache repositories and service instances that should exist only once throughout the application lifecycle.

#### Implementation Details
- **Location**: `Ucx.AclService.Erss.Api/Program.cs`, `Ucx.AclService.Erss.Repo/Service/CacheRepository.cs`
- **Purpose**: Ensures single instances for cache management and user details caching

#### Code Snippet - Program.cs
```csharp
var builder = WebApplication.CreateBuilder(args);
builder
    .AddSerilogAndAppInsights("Ucx.AclService.Erss.Api", "UCX",
        builder.Configuration.GetValue<string>("APPINSIGHTS_INSTRUMENTATIONKEY")!);
builder.RegisterApplicationServices();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();
ErssFinalRepository.RegisterDependency(builder.Services);
CosmosConfiguration.RegisterErssDependency(builder.Configuration, builder.Services);
builder.Services.Configure<OktaConfiguration>(builder.Configuration.GetSection("OktaConfiguration"));
CosmosConfiguration.RegisterUserCacheDependency(builder.Configuration, builder.Services);

// Singleton registrations
builder.Services.AddSingleton<ICacheRepository, CacheRepository>();
builder.Services.AddSingleton<IUserCacheService, UserCacheService>();

var app = builder.Build();
```

**Key Points**:
- `AddSingleton<ICacheRepository, CacheRepository>()` ensures only one instance exists throughout the application lifetime
- The cache repository is used for storing user email caches globally
- All requests share the same cache instance, reducing memory overhead

#### Code Snippet - Service Registration in Observer
```csharp
public static void RegisterConsumerDependency(IServiceCollection services, ClientConfig clientConfig,
    AutoOffsetReset autoOffsetReset, string groupId, string eventTopic, int delayMs)
{
    // ... other registrations ...
    services.AddSingleton<CaseAssignmentChangedIOneEventObserver>();
    services.AddSingleton<ICacheRepository, CacheRepository>();
    services.AddSingleton<IGetUserDetailsService, GetUserDetailsService>();
}
```

---

### Factory Pattern

#### Description
The Factory pattern provides an interface for creating objects of various types without specifying their exact classes. This solution uses the Factory pattern through dependency injection and reflection-based registration for dynamic service instantiation.

#### Implementation Details
- **Location**: `Ucx.AclService.Erss.Services/Configuration/ServiceCollectionExtensions.cs`
- **Purpose**: Dynamically register and create service implementations based on interface types

#### Code Snippet - ServiceCollectionExtensions.cs
```csharp
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Ucx.AclService.Erss.Services.Configuration;

public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Extension method that adds transient registration of every class 
    ///     that implements the given generic `T` that is in the same assembly.
    /// </summary>
    /// <param name="services">this</param>
    /// <typeparam name="T">interface of implementing classes</typeparam>
    /// <returns></returns>
    public static IServiceCollection RegisterImplementingClasses<T>(this IServiceCollection services)
    {
        Assembly.GetAssembly(typeof(T))!
            .GetTypes()
            .Select(x => new { Type = x, Interfaces = x.GetInterfaces() })
            .Where(x => x.Interfaces.Any(interfaceType => interfaceType == typeof(T)))
            .ToList()
            .ForEach(service => services.AddTransient(typeof(T), service.Type));

        return services;
    }

    public static IServiceCollection AddAutoMapperConfig(this IServiceCollection services)
    {
        services.AddAutoMapper(AppDomain.CurrentDomain.GetAssemblies());
        return services;
    }
}
```

**Key Points**:
- Uses reflection to automatically discover and register all implementations of an interface
- Creates factory methods dynamically based on assembly scanning
- Reduces boilerplate code for service registration
- Supports loose coupling between components

#### Usage Pattern
```csharp
// Dynamic registration of all IChangeDetector implementations
services.RegisterImplementingClasses<IChangeDetector<ErssRecord>>();

// Dynamic registration of all IOrchestrate implementations
services.RegisterImplementingClasses<IOrchestrate<XplErssFinalEvent<FlattenedServiceRequest>>>();
```

---

## Structural Patterns

### Adapter Pattern

#### Description
The Adapter pattern converts the interface of a class into another interface clients expect, allowing incompatible interfaces to work together. This solution uses adapters to translate between different event formats and data structures.

#### Implementation Details
- **Location**: `Ucx.AclService.Erss.Services/Implement/XPLErssFinalTranslation.cs`, `Ucx.AclService.Erss.Services/Mappers/`
- **Purpose**: Convert between domain models, service requests, and database records

#### Code Snippet - XPLErssFinalTranslation.cs
```csharp
public class XplErssFinalTranslation : ITranslate<XplErssFinalEvent<ServiceRequest>, 
    IEnumerable<XplErssFinalEvent<FlattenedServiceRequest>>>
{
    private readonly ILogger<XplErssFinalTranslation> _logger;
    private readonly IMapper _mapper;

    public XplErssFinalTranslation(IMapper mapper, ILogger<XplErssFinalTranslation> logger)
    {
        _mapper = mapper;
        _logger = logger;
    }

    public IEnumerable<XplErssFinalEvent<FlattenedServiceRequest>> Translate(
        XplErssFinalEvent<ServiceRequest> xplErssFinalEvent)
    {
        try
        {
            var mappedXpl = xplErssFinalEvent.Erss.Episode.ServiceRequest.ListOfServiceRequests.Select(x =>
            {
                var xplErssFinalEventFsr = new XplErssFinalEvent<FlattenedServiceRequest>
                {
                    Key = xplErssFinalEvent.Key,
                    Name = xplErssFinalEvent.Name,
                    Sent = xplErssFinalEvent.Sent,
                    OriginSystem = xplErssFinalEvent.OriginSystem,
                    Version = xplErssFinalEvent.Version,
                    Erss = new Erss<FlattenedServiceRequest>
                    {
                        MessageFidelity = xplErssFinalEvent.Erss.MessageFidelity,
                        MessageKey = xplErssFinalEvent.Erss.MessageKey,
                        MessageTimeStamp = xplErssFinalEvent.Erss.MessageTimeStamp,
                        VersionNumber = xplErssFinalEvent.Erss.VersionNumber,
                        MessageType = xplErssFinalEvent.Erss.MessageType,
                        Episode = new Episode<FlattenedServiceRequest>
                        {
                            Authorization = xplErssFinalEvent.Erss.Episode.Authorization,
                            CaseFactor = xplErssFinalEvent.Erss.Episode.CaseFactor,
                            EpisodeKey = xplErssFinalEvent.Erss.Episode.EpisodeKey,
                            EpisodeStartDate = xplErssFinalEvent.Erss.Episode.EpisodeStartDate,
                            ServiceRequest = _mapper.Map<FlattenedServiceRequest>(
                                xplErssFinalEvent.Erss.Episode.ServiceRequest)
                        }
                    }
                };
                //Map The FlattenedServiceRequest
                _mapper.Map(x, xplErssFinalEventFsr.Erss.Episode.ServiceRequest);
                xplErssFinalEventFsr.Erss.Episode.ServiceRequest.ListOfServiceRequests.Remove(x);
                return xplErssFinalEventFsr;
            });
            return mappedXpl.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in conversion for XPLErssFinalEvent");
            return null;
        }
    }
}
```

**Key Points**:
- Converts `XplErssFinalEvent<ServiceRequest>` to `XplErssFinalEvent<FlattenedServiceRequest>`
- Uses AutoMapper for automatic property mapping
- Handles complex nested object transformations
- Provides error handling and logging

#### Code Snippet - ErssStatusMapper.cs
```csharp
public static class ErssStatusMapper
{
    private static readonly Dictionary<string, string> ImageOneOriginStatusToUcxStatuses = new()
    {
        { "approved", Outcome.Approved.ToString() },
        { "cancelled", Outcome.Canceled.ToString() },
        { "denied", Outcome.Denied.ToString() },
        { "partially approved".Replace(" ", ""), Outcome.PartialApproval.ToString() },
        { "withdrawn", Outcome.Withdrawn.ToString() },
        { "in eligibility, oon, or benefit check".Replace(" ", ""), Outcome.PendingEligibilityReview.ToString() },
        { "pending review of additional information received".Replace(" ", ""), Outcome.PendingNurseReview.ToString() },
        { "awaiting receipt of additional clinical information".Replace(" ", ""), Outcome.WaitingOnClinicalInfo.ToString() },
    };

    public static string GetUcxStatus(string imageOneStatus)
    {
        if (imageOneStatus == null)
        {
            return null;
        }
        // ... implementation ...
    }
}
```

**Key Points**:
- Adapts ImageOne status codes to UCX outcome enumerations
- Provides bidirectional mapping between different status representations
- Encapsulates domain-specific knowledge about status transformations

---

### Facade Pattern

#### Description
The Facade pattern provides a unified, simplified interface to a set of interfaces in a subsystem. This pattern is heavily used in the orchestration and repository layers to simplify complex operations.

#### Implementation Details
- **Location**: `Ucx.AclService.Erss.Repo/Service/CosmosDbService.cs`, `Ucx.AclService.Erss.Services/Implement/ErssOrchestrationService.cs`
- **Purpose**: Simplify Cosmos DB operations and complex orchestration workflows

#### Code Snippet - CosmosDbService.cs
```csharp
public class CosmosDbService<T>: ICosmosDbService<T> where T: class
{
    private readonly ILogger _logger;
    private readonly Container _container;

    public CosmosDbService(CosmosClient dbClient, string databaseName, string containerName, 
        ILogger<CosmosDbService<T>> logger)
    {
        _container = dbClient.GetContainer(databaseName, containerName);
        _logger = logger;
    }

    public async Task AddItemAsync(T item, string partitionKey)
    {
        if (item is null)
        { throw new ArgumentNullException(nameof(item)); }
        await _container.CreateItemAsync(item, new PartitionKey(partitionKey));
    }

    public async Task DeleteItemAsync(string id, string partitionKey)
    {
        await _container.DeleteItemAsync<T>(id, new PartitionKey(partitionKey));
        _logger.LogDebug("CosmosDbService: {Id} deleted", id);
    }

    public async Task<T> GetItemAsync(string id, string partitionKey)
    {
        try
        {
            var response = await _container.ReadItemAsync<T>(id, new PartitionKey(id));
            return response.Resource;
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("NotFound"))
            {
                return null;
            }
            _logger.LogError("CosmosDbService: {Id} Error . {Exception}", id, ex.ToString());
            throw;
        }
    }

    public async Task<IEnumerable<T>> GetItemsAsync(string query)
    {
        var iterator = _container.GetItemQueryIterator<T>(new QueryDefinition(query));
        var results = new List<T>();
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return results;
    }

    public async Task UpdateItemAsync(T item, string id, string partitionKey)
    {
        if (item is null)
        { throw new ArgumentNullException(nameof(item)); }
        await _container.ReplaceItemAsync(item, id, new PartitionKey(partitionKey));
        _logger.LogDebug("CosmosDbService: {Item} updated", item.GetType());
    }

    public async Task UpsertItemAsync(T item, string partitionKey)
    {
        if (item is null)
        { throw new ArgumentNullException(nameof(item)); }
        try
        {
            await _container.UpsertItemAsync(item, new PartitionKey(partitionKey));
            _logger.LogDebug("CosmosDbService: {Item} upserted", item.GetType());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CosmosDbService: Error in Upsert for {Item}", item.GetType());
            throw;
        }
    }
}
```

**Key Points**:
- Simplifies Cosmos DB SDK complexity with straightforward CRUD operations
- Encapsulates exception handling and logging
- Provides generic implementation for any entity type
- Hides partition key management from clients

#### Code Snippet - ErssFinalRepository.cs (Facade on top of CosmosDbService)
```csharp
public class ErssFinalRepository : IErssFinalRepository
{
    private readonly evicore.gravity.common.Interface.ICosmosDbService<ErssRecord> _cosmosDbService;
    private readonly ILogger<ErssFinalRepository> _logger;
    private readonly Dictionary<string, object> _scope = new();

    public ErssFinalRepository(evicore.gravity.common.Interface.ICosmosDbService<ErssRecord> cosmosDbService,
        ILogger<ErssFinalRepository> logger)
    {
        _scope.Add("Class", nameof(ErssFinalRepository));
        _cosmosDbService = cosmosDbService;
        _logger = logger;
    }

    public async Task<ErssRecord> GetAsync(string id)
    {
        return await _cosmosDbService.ReadItemAsync(id, id);
    }

    public async Task<ErssRecord> UpsertAsync(ErssRecord Erss)
    {
        var scope = new Dictionary<string, object>(_scope)
        {
            { "Method", "UpsertAsync" }
        };
        return await UpsertInternalAsync(existingRecord =>
        {
            if (existingRecord is not null &&
                existingRecord.Erss.MessageTimeStamp > Erss.Erss.MessageTimeStamp)
                throw new ErssTimestampException();
            return Erss;
        }, Erss.Id);
    }

    private async Task<ErssRecord> UpsertInternalAsync(Func<ErssRecord, ErssRecord> func, string id)
    {
        ErssRecord newRecord = null;
        try
        {
            _logger.LogDebug("Start upsert");
            newRecord = await _cosmosDbService.UpsertItemAsync(func, id, id);
            _logger.LogDebug("End upsert");
        }
        catch (ErssTimestampException e)
        {
            _logger.LogError(e, "Timestamp validation failed");
            throw;
        }
        return newRecord;
    }
}
```

**Key Points**:
- Provides domain-specific interface on top of generic CosmosDbService
- Adds business logic like timestamp validation
- Encapsulates error handling specific to ERSS records
- Serves as a facade for complex upsert operations with side effects

---

## Behavioral Patterns

### Observer Pattern

#### Description
The Observer pattern defines a one-to-many dependency between objects so that when one object changes state, all its dependents are notified automatically. In this solution, it's used for event-driven message consumption from Kafka.

#### Implementation Details
- **Location**: `Ucx.AclService.Erss.Services/Observer/CaseAssignmentChangedIOneEventObserver.cs`, `Ucx.AclService.Erss.Services/Observer/XrlErssFinalEventObserver.cs`
- **Purpose**: Listen to and react to Kafka events asynchronously

#### Code Snippet - CaseAssignmentChangedIOneEventObserver.cs
```csharp
/// <summary>
///     Listen to CaseAssignmentChangedIOne Event
/// </summary>
public class CaseAssignmentChangedIOneEventObserver : IDisposable
{
    private readonly ICaseAssignmentChangeIOneService caseAssignmentChangeIOneService;
    private readonly IConfiguration config;
    private readonly IMessageConsumer<CaseAssignmentChangedIOneEvent> _consumer;
    private bool _disposed;
    private readonly EventHandler<MessagePayload<CaseAssignmentChangedIOneEvent>> _eventHandler;
    private readonly ILogger<CaseAssignmentChangedIOneEventObserver> _logger;
    private readonly Dictionary<string, object> _scope = new();

    public CaseAssignmentChangedIOneEventObserver(
        ILogger<CaseAssignmentChangedIOneEventObserver> logger,
        IMessageConsumer<CaseAssignmentChangedIOneEvent> consumer,
        ICaseAssignmentChangeIOneService caseAssignmentChangeIOneService,
        IConfiguration config)
    {
        _scope.Add("Class", nameof(CaseAssignmentChangedIOneEventObserver));
        _consumer = consumer;
        this.caseAssignmentChangeIOneService = caseAssignmentChangeIOneService;
        this.config = config;
        _logger = logger;
        _eventHandler = MessageEventHandler;  // Register event handler
    }

    /// <summary>
    ///     Consume CaseAssignmentChangedIOne events
    /// </summary>
    public void StartConsuming()
    {
        _consumer.ValidMessageReceived += _eventHandler;  // Subscribe to events
        _consumer.ConsumeAll<CaseAssignmentChangedIOneEvent>();
    }

    /// <summary>
    ///     Event handler called when message is received
    /// </summary>
    private async void MessageEventHandler(object _, MessagePayload<CaseAssignmentChangedIOneEvent> message)
    {
        var scope = new Dictionary<string, object>(_scope)
        {
            { "Method", "MessageEventHandler" }
        };
        using (_logger.BeginScope(scope))
        {
            try
            {
                scope["EventName"] = "CaseAssignmentChangedIOne";
                scope["KafkaKey"] = message.KafkaKey;
                scope["Lag"] = message.Lag;
                scope["Offset"] = message.Offset;
                _logger.LogInformation("CaseAssignmentChangedIOne - Started");
                var payload = JsonConvert.DeserializeObject<CaseAssignmentChangedIOneEvent>(message.Payload);
                scope["EventKey"] = payload?.data?.QueueHistoryID;
                
                if (payload.data != null)
                {
                    await caseAssignmentChangeIOneService.ProcessMessage(payload);
                    _logger.LogInformation("CaseAssignmentChangedIOneEvent processing Completed");
                }
                else
                {
                    _logger.LogInformation("CaseAssignmentChangedIOneEvent - Payload data is null");
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error processing event CaseAssignmentChangedIOneEvent");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _consumer.ValidMessageReceived -= _eventHandler;  // Unsubscribe from events
        _disposed = true;
    }

    ~CaseAssignmentChangedIOneEventObserver()
    {
        Dispose();
    }

    public static void RegisterConsumerDependency(IServiceCollection services, ClientConfig clientConfig,
        AutoOffsetReset autoOffsetReset, string groupId, string eventTopic, int delayMs)
    {
        services.AddTransient<IMessageConsumer<CaseAssignmentChangedIOneEvent>, 
            KafkaMessageConsumer<CaseAssignmentChangedIOneEvent>>(x => 
                new KafkaMessageConsumer<CaseAssignmentChangedIOneEvent>(
                    x.GetRequiredService<ILogger<KafkaMessageConsumer<CaseAssignmentChangedIOneEvent>>>(),
                    x.GetRequiredService<TelemetryClient>(),
                    new ConsumerConfig(clientConfig)
                    {
                        GroupId = groupId,
                        AutoOffsetReset = autoOffsetReset,
                        FetchMaxBytes = 104857600
                    },
                    eventTopic, finalOffset: null, createGroupWithMessageName: false, 
                    consumerDelay: TimeSpan.FromMilliseconds(delayMs)));

        services.AddHttpClient();
        services.AddTransient<IOktaService, OktaService>();
        services.AddSingleton<CaseAssignmentChangedIOneEventObserver>();
        services.AddTransient<ICaseAssignmentChangeIOneService, CaseAssignmentChangeIOneService>();
        services.AddSingleton<ICacheRepository, CacheRepository>();
        services.AddSingleton<IGetUserDetailsService, GetUserDetailsService>();
    }
}
```

**Key Points**:
- Observer subscribes to `ValidMessageReceived` event from message consumer
- Event handler is invoked automatically when messages arrive
- Enables decoupled communication between message source and handler
- Supports multiple observers subscribing to the same event source
- Implements IDisposable pattern for proper cleanup

#### Participants in Observer Pattern:
- **Subject**: `IMessageConsumer<CaseAssignmentChangedIOneEvent>` - publishes events
- **Observer**: `CaseAssignmentChangedIOneEventObserver` - subscribes and responds to events
- **Event Handler**: `MessageEventHandler` - callback method invoked on state change

---

### Strategy Pattern

#### Description
The Strategy pattern defines a family of algorithms, encapsulates each one, and makes them interchangeable. This solution uses it for change detection and message translation strategies.

#### Implementation Details
- **Location**: `Ucx.AclService.Erss.Services/Interface/IChangeDetector.cs`, `Ucx.AclService.Erss.Services/Implement/ErssChangeDetectionService.cs`
- **Purpose**: Provide pluggable algorithms for detecting field changes

#### Code Snippet - IChangeDetectionService and ErssChangeDetectionService
```csharp
// Strategy Interface
namespace Ucx.AclService.Erss.Services.Interface;

public interface IChangeDetectionService<in T>
{
    IEnumerable<ChangeDetectionResult> DetectChanges(T original, T next);
}

// Strategy Implementations
public interface IChangeDetector<T>
{
    ChangeDetectionResult Detect(T original, T next);
}

// Concrete Strategy Executor
public class ErssChangeDetectionService : IChangeDetectionService<ErssRecord>
{
    private readonly IEnumerable<IChangeDetector<ErssRecord>> _changeDetectors;

    public ErssChangeDetectionService(IEnumerable<IChangeDetector<ErssRecord>> changeDetectors)
    {
        _changeDetectors = changeDetectors;
    }

    public IEnumerable<ChangeDetectionResult> DetectChanges(ErssRecord original, ErssRecord next)
    {
        return _changeDetectors.Select(changeDetector =>
            changeDetector.Detect(original, next))
            .Where(changeResult => changeResult != null)
            .ToList();
    }

    public static void Register(IServiceCollection serviceCollection)
    {
        serviceCollection.AddTransient<IChangeDetectionService<ErssRecord>, ErssChangeDetectionService>();
    }
}
```

**Key Points**:
- Multiple change detection strategies are injected into the service
- Each strategy implements the `IChangeDetector<T>` interface
- The service delegates change detection to appropriate strategies
- Strategies are interchangeable and can be added/removed independently
- Enables composition of different detection algorithms

#### Code Snippet - ITranslate Strategy Interface
```csharp
namespace Ucx.AclService.Erss.Services.Interface;

public interface ITranslate<in T, out TK>
{
    TK Translate(T xplErssFinalEvent);
}
```

**Usage Example - Multiple Translation Strategies**:
```csharp
public class ErssOrchestrationService : IOrchestrationService<XplErssFinalEvent<ServiceRequest>>
{
    protected ITranslate<XplErssFinalEvent<ServiceRequest>, 
        IEnumerable<XplErssFinalEvent<FlattenedServiceRequest>>>
        _translationService;  // Strategy 1: Flatten service request

    protected ITranslate<XplErssFinalEvent<FlattenedServiceRequest>, ErssRecord> 
        _translationForDbService;  // Strategy 2: Translate to database record

    public ErssOrchestrationService(
        ITranslate<XplErssFinalEvent<ServiceRequest>, 
            IEnumerable<XplErssFinalEvent<FlattenedServiceRequest>>> translationService,
        ITranslate<XplErssFinalEvent<FlattenedServiceRequest>, ErssRecord> translationForDbService)
    {
        _translationService = translationService;
        _translationForDbService = translationForDbService;
    }
}
```

---

### Template Method Pattern

#### Description
The Template Method pattern defines the skeleton of an algorithm in a base class but lets subclasses override specific steps without changing the algorithm's structure.

#### Implementation Details
- **Location**: Message handlers and orchestration services follow a template structure
- **Purpose**: Standardize message processing workflow across different event types

#### Code Snippet - MessageHandler.cs
```csharp
public class MessageHandler : 
    IMessageHandler<XplErssCaseEnrichedIone>, 
    IMessageHandler<XplErssFinalEventEventSource<ServiceRequest>>,
    IMessageHandler<XplErssFinalHistoryEvent<ServiceRequest>>, 
    IMessageHandler<XplErssCaseEnrichedIoneHistory>, 
    IMessageHandler<UcxMedicalDisciplineCorrected>
{
    private readonly ILogger<MessageHandler> _logger;
    private readonly ITranslate<XplErssFinalEvent<ServiceRequest>, 
        IEnumerable<XplErssFinalEvent<FlattenedServiceRequest>>> _translationService;
    private readonly ILockingService _lockingService;
    private readonly IEventSourceService<ErssCaseImageOneState> _eventSourceService;
    private readonly Dictionary<string, object> _scope = new();

    public MessageHandler(
        ILogger<MessageHandler> logger,
        ITranslate<XplErssFinalEvent<ServiceRequest>,
            IEnumerable<XplErssFinalEvent<FlattenedServiceRequest>>> translationService,
        ILockingService lockingService,
        IOptions<LockOptions> options,
        IEventSourceService<ErssCaseImageOneState> eventSourceService,
        IStateRepository<DiagnosisState> diagnosisStateRepository, 
        IEventSourceMessageRepository<ErssCaseImageOneState> eventSourceMessageRepository,
        IStateRepository<ErssCaseImageOneState> stateRepository)
    {
        _logger = logger;
        _translationService = translationService;
        _lockingService = lockingService;
        _eventSourceService = eventSourceService;
        _diagnosisStateRepository = diagnosisStateRepository;
        _eventSourceMessageRepository = eventSourceMessageRepository;
        _stateRepository = stateRepository;
        _acquireLockTimeout = options.Value.AcquireLockTimeout;
        _expireLockTimeout = options.Value.ExpireLockTimeout;
        _scope.Add("Class", nameof(MessageHandler));
    }

    // Template method - common workflow for all message types
    public async Task Handle(XplErssCaseEnrichedIone message, KafkaMetadata kafkaMetadata = null)
    {
        var scope = new Dictionary<string, object>(_scope)
        {
            { "EpisodeID", message?.EpisodeID },
            { "XplMessageKey", message?.MessageKey }
            // ... additional scope setup ...
        };

        using (_logger.BeginScope(scope))
        {
            try
            {
                // Step 1: Acquire lock
                using (var lockReleaser = await _lockingService.AcquireLockAsync(
                    message.EpisodeID, _acquireLockTimeout, _expireLockTimeout))
                {
                    // Step 2: Translate message
                    var translatedMessages = _translationService.Translate(message);

                    // Step 3: Process each translated message
                    foreach (var translatedMessage in translatedMessages)
                    {
                        // Step 4: Persist changes
                        await _eventSourceService.AppendEventAsync(translatedMessage);
                    }

                    // Step 5: Log completion
                    _logger.LogInformation("Message handling completed successfully");
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error handling message");
                throw;
            }
        }
    }
}
```

**Template Method Steps**:
1. Setup logging scope
2. Acquire distributed lock
3. Translate/transform message
4. Process translated messages
5. Persist to event store
6. Handle errors and logging

**Key Points**:
- The overall workflow structure is consistent across all message handlers
- Specific implementations vary based on message type
- Locking, logging, and error handling are standardized
- Reduces code duplication across different event handlers

---

### Chain of Responsibility Pattern

#### Description
The Chain of Responsibility pattern passes requests along a chain of handlers where each handler decides either to process the request or pass it along the chain.

#### Implementation Details
- **Location**: `Ucx.AclService.Erss.Services/Implement/ErssOrchestrationService.cs`
- **Purpose**: Route messages to appropriate orchestrator handlers based on message type

#### Code Snippet - ErssOrchestrationService.cs
```csharp
public class ErssOrchestrationService : IOrchestrationService<XplErssFinalEvent<ServiceRequest>>
{
    protected ILogger _logger;
    protected IErssFinalRepository _repo;
    protected ITranslate<XplErssFinalEvent<ServiceRequest>, 
        IEnumerable<XplErssFinalEvent<FlattenedServiceRequest>>> _translationService;
    protected ITranslate<XplErssFinalEvent<FlattenedServiceRequest>, ErssRecord> _translationForDbService;
    protected IChangeDetectionService<ErssRecord> _changeDetectionService;
    protected Dictionary<string, object> _scope = new();

    public ErssOrchestrationService(
        ILogger<ErssOrchestrationService> logger,
        IErssFinalRepository repo,
        IServiceProvider serviceProvider,
        ITranslate<XplErssFinalEvent<ServiceRequest>, 
            IEnumerable<XplErssFinalEvent<FlattenedServiceRequest>>> translationService,
        IChangeDetectionService<ErssRecord> changeDetectionService,
        ITranslate<XplErssFinalEvent<FlattenedServiceRequest>, ErssRecord> translationForDbService,
        IEnumerable<IOrchestrate<XplErssFinalEvent<FlattenedServiceRequest>>> orchestratorClasses)
    {
        _scope.Add("Class", nameof(ErssOrchestrationService));
        _logger = logger;
        _repo = repo;
        _translationService = translationService;
        _changeDetectionService = changeDetectionService;
        _translationForDbService = translationForDbService;
        
        // Register all handlers in the chain
        using (_logger.BeginScope(_scope))
        {
            foreach (var orchestratorClass in orchestratorClasses)
            {
                AddOrchestrator(orchestratorClass);
            }
            _logger.LogDebug("Initializing class");
        }
    }

    /// <summary>
    /// Chain repository: string for MessageType, List of handlers
    /// </summary>
    protected readonly Dictionary<string, List<IOrchestrate<XplErssFinalEvent<FlattenedServiceRequest>>>>
        _orchestratorClasses = new();

    // Message type constants that trigger different handler chains
    public const string CaseSubmitted = "casesubmitted";
    public const string CaseSaved = "casesaved";
    public const string CaseStatusChanged = "casestatuschanged";
    public const string HistoricalCaseSnapshot = "historicalcasesnapshot";
    public const string NoExistingRecord = "noexistingrecord";

    // Add handler to chain for specific message type
    private void AddOrchestrator(IOrchestrate<XplErssFinalEvent<FlattenedServiceRequest>> orchestratorObject)
    {
        foreach (var messageType in orchestratorObject.MessageTypes)
        {
            if (!_orchestratorClasses.ContainsKey(messageType.ToLower()))
            {
                _orchestratorClasses.TryAdd(messageType.ToLower(),
                    new List<IOrchestrate<XplErssFinalEvent<FlattenedServiceRequest>>>
                    {
                        orchestratorObject
                    });
            }
            else
            {
                if (_orchestratorClasses[messageType.ToLower()].All(x => x.GetType() != orchestratorObject.GetType()))
                    _orchestratorClasses[messageType.ToLower()].Add(orchestratorObject);
            }
        }
    }

    // Process message through appropriate handler chain
    public async Task Orchestrate(IDictionary<string, object> scope,
        XplErssFinalEvent<FlattenedServiceRequest> message, 
        MessagePassedFromEnum messagePassedFrom,
        CancellationToken cancellationToken = default)
    {
        var serviceRequestKey = message.Erss.Episode.ServiceRequest.ServiceRequestKey;
        scope["ServiceRequestKey"] = serviceRequestKey;

        try
        {
            _logger.LogDebug("Pulling data from db for Service Request Key: {ServiceRequestKey}", 
                serviceRequestKey);

            var orchestrationClasses = new List<IOrchestrate<XplErssFinalEvent<FlattenedServiceRequest>>>();
            IEnumerable<ChangeDetectionResult> changedFields = null;
            
            await _repo.UpsertAsync(record =>
            {
                if (record != null && messagePassedFrom == MessagePassedFromEnum.OrchestratorService)
                    message.Erss.Episode.ServiceRequest.RequestForServiceKey =
                        record.Erss.Episode.ServiceRequest.RequestForServiceKey == Guid.Empty
                            ? record.Erss.Episode.ServiceRequest.ServiceRequestKey.ConvertToGuid()
                            : record.Erss.Episode.ServiceRequest.RequestForServiceKey;
                else
                {
                    message.Erss.Episode.ServiceRequest.RequestForServiceKey = 
                        message.Erss.Episode.ServiceRequest.ServiceRequestKey.ConvertToGuid();
                }

                var dbRecord = _translationForDbService.Translate(message);
                
                // Detect which fields changed
                if (record != null)
                {
                    changedFields = _changeDetectionService.DetectChanges(record, dbRecord);
                    
                    // Determine message type based on detected changes
                    if (changedFields?.Any() == true)
                    {
                        // Get handlers for "CaseStatusChanged"
                        if (_orchestratorClasses.TryGetValue(CaseStatusChanged.ToLower(), 
                            out var handlers))
                        {
                            orchestrationClasses.AddRange(handlers);
                        }
                    }
                }
                else
                {
                    // Get handlers for "CaseSubmitted" when no existing record
                    if (_orchestratorClasses.TryGetValue(CaseSubmitted.ToLower(), out var handlers))
                    {
                        orchestrationClasses.AddRange(handlers);
                    }
                }

                return dbRecord;
            }, message.Erss.Episode.ServiceRequest.ServiceRequestKey.ToString());

            // Execute all handlers in the chain
            if (orchestrationClasses.Any())
            {
                var tasks = orchestrationClasses.Select(handler =>
                    handler.Handle(message, changedFields, messagePassedFrom));
                    
                await Task.WhenAll(tasks);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error in orchestration");
            throw;
        }
    }
}
```

**Key Points**:
- Messages are passed through a chain of handlers based on message type
- Each handler in the chain can process the message or pass it to the next handler
- The chain is built dynamically by registering orchestrator implementations
- Different message types route to different handler chains
- Enables flexible and extensible message processing workflows

**Handler Chain Structure**:
```
Message Type: "CaseSubmitted"
    ↓
Handler 1: ProcessCaseSubmittedHandler
    ↓
Handler 2: SendNotificationHandler
    ↓
Handler 3: UpdateCacheHandler

Message Type: "CaseStatusChanged"
    ↓
Handler 1: ProcessStatusChangeHandler
    ↓
Handler 2: TriggerWorkflowHandler
```

---

## Repository Pattern (Data Access Layer)

While technically not a GOF pattern, the Repository pattern is extensively used and worth documenting as it complements the structural patterns above.

#### Description
The Repository pattern mediates between the domain and data mapping layers, acting like an in-memory collection of domain objects.

#### Implementation Details
- **Location**: `Ucx.AclService.Erss.Repo/Interface/`, `Ucx.AclService.Erss.Repo/Service/`
- **Purpose**: Abstract data access logic from business logic

#### Code Snippet - IRepository Interface
```csharp
namespace UCX.AclService.Erss.Repo.Interface;

public interface IRepository<T> where T : class
{
    Task AddAsync(T item);
    Task DeleteAsync(T item);
    Task<T> GetAsync(string id, string key);
    Task UpdateAsync(T item);
    Task UpsertAsync(T item);
}
```

**Specific Repositories**:
- `IErssFinalRepository` - handles ERSS record persistence
- `ICacheRepository` - manages in-memory caching
- `IDocumentAttachedRepository` - handles document attachments
- `IGetRepository` - read-only operations
- `ICosmosDbService<T>` - generic Cosmos DB operations

---

## Dependency Injection Pattern

#### Description
While not a classic GOF pattern, Dependency Injection is crucial to the solution's architecture and enables many patterns to work effectively.

#### Implementation Details
- **Location**: `Ucx.AclService.Erss.Api/Program.cs`, throughout all service classes
- **Purpose**: Manage object creation and lifecycle, promote loose coupling

#### Code Snippet - DI Configuration
```csharp
var builder = WebApplication.CreateBuilder(args);

// Register singleton services
builder.Services.AddSingleton<ICacheRepository, CacheRepository>();
builder.Services.AddSingleton<IUserCacheService, UserCacheService>();
builder.Services.AddSingleton<CaseAssignmentChangedIOneEventObserver>();

// Register transient services
services.AddTransient<IMessageHandler<XplErssFinalEvent<ServiceRequest>>, MessageHandler>();
services.AddTransient<IOrchestrationService<XplErssFinalEvent<ServiceRequest>>, 
    ErssOrchestrationService>();

// Register all implementations of an interface
services.RegisterImplementingClasses<IChangeDetector<ErssRecord>>();
services.RegisterImplementingClasses<IOrchestrate<XplErssFinalEvent<FlattenedServiceRequest>>>();

// Register AutoMapper
services.AddAutoMapperConfig();
```

**Benefits**:
- Loose coupling between classes
- Easy testing with mock implementations
- Centralized configuration management
- Support for different lifetimes (singleton, transient, scoped)

---

## Pattern Summary Table

| Pattern | Category | Location | Purpose |
|---------|----------|----------|---------|
| Singleton | Creational | Program.cs, CacheRepository | Single instance for caches and services |
| Factory | Creational | ServiceCollectionExtensions | Dynamic service registration via reflection |
| Adapter | Structural | Translation services, Mappers | Convert between different data formats |
| Facade | Structural | CosmosDbService, ErssFinalRepository | Simplify complex subsystem operations |
| Observer | Behavioral | Event observers | Event-driven message consumption |
| Strategy | Behavioral | IChangeDetector, ITranslate | Pluggable algorithms for detection/translation |
| Template Method | Behavioral | MessageHandler | Standardize message processing workflow |
| Chain of Responsibility | Behavioral | ErssOrchestrationService | Route messages to appropriate handlers |
| Repository | Architectural | Repo layer | Abstract data access logic |
| Dependency Injection | Architectural | Throughout | Manage dependencies and object lifecycle |

---

## Best Practices Observed

1. **Interface Segregation**: Services implement specific, focused interfaces
2. **Single Responsibility**: Each class has one reason to change
3. **Open/Closed Principle**: Code is open for extension via interface implementations
4. **Loose Coupling**: Dependencies injected rather than hardcoded
5. **Composition Over Inheritance**: Uses composition with injected dependencies
6. **Explicit Logging**: Scoped logging context throughout for observability
7. **Error Handling**: Comprehensive try-catch blocks with logging
8. **Configuration Management**: Externalized configuration through appsettings

---

## Conclusion

The Ucx.AclService.Erss solution demonstrates sophisticated use of Gang of Four design patterns in a real-world microservices architecture. The patterns work together to create:

- **Maintainable Code**: Clear separation of concerns
- **Extensible Architecture**: Easy to add new handlers, mappers, and strategies
- **Testable Components**: Dependencies can be mocked for unit testing
- **Scalable Design**: Supports horizontal scaling with event-driven architecture
- **Observable Systems**: Comprehensive logging and scoped diagnostics

The combination of classic GOF patterns with modern architectural patterns (Repository, Dependency Injection) creates a robust, enterprise-grade solution.
