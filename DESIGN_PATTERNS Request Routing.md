# Gang of Four (GOF) Design Patterns in RequestRouting Solution

## Overview
This document provides a comprehensive analysis of Gang of Four design patterns implemented in the RequestRouting solution. The solution is a sophisticated medical request routing system built with C# and .NET, utilizing various structural, behavioral, and creational design patterns.

---

## Table of Contents
1. [Creational Patterns](#creational-patterns)
   - [Factory Pattern](#factory-pattern)
   - [Singleton Pattern](#singleton-pattern)
   - [Builder Pattern](#builder-pattern)

2. [Structural Patterns](#structural-patterns)
   - [Repository Pattern](#repository-pattern)
   - [Adapter Pattern](#adapter-pattern)
   - [Facade Pattern](#facade-pattern)
   - [Proxy Pattern](#proxy-pattern)

3. [Behavioral Patterns](#behavioral-patterns)
   - [Observer Pattern](#observer-pattern)
   - [Command Pattern](#command-pattern)
   - [Strategy Pattern](#strategy-pattern)
   - [State Pattern](#state-pattern)
   - [Chain of Responsibility Pattern](#chain-of-responsibility-pattern)
   - [Template Method Pattern](#template-method-pattern)

4. [Architectural Patterns](#architectural-patterns)
   - [Domain-Driven Design (DDD)](#domain-driven-design)
   - [Event Sourcing](#event-sourcing)

---

## CREATIONAL PATTERNS

### Factory Pattern

**Purpose**: Encapsulates the creation logic for complex objects.

**Implementation in Solution**:
The `RequestFactory` class in the Domain layer provides a centralized way to create `RequestAggregate` objects.

**File**: `RequestRouting.Domain/Factory/RequestFactory.cs`

```csharp
using RequestRouting.Domain.Aggregates;

namespace RequestRouting.Domain.Factory;

public class RequestFactory
{
    public static RequestAggregate CreateEmptyRequest()
    {
        return new RequestAggregate();
    }
}
```

**Benefit**: 
- Centralizes the instantiation logic for creating request aggregates
- Makes it easy to change creation logic without affecting client code
- Supports future enhancements like complex initialization or caching

**Usage Location**: Domain services and repositories that need to create new RequestAggregate instances.

---

### Singleton Pattern

**Purpose**: Ensures only one instance of a class exists throughout the application.

**Implementation in Solution**:
Various services are registered as singleton dependencies in the DI container:

**File**: `RequestRouting.Api/Startup.cs` and `RequestRouting.Observer/Startup.cs`

```csharp
// From Startup.cs ConfigureServices method
services.AddSingleton(Configuration);
services.AddSingleton<IOrchestrator, Orchestrator>();
services.AddSingleton<IEventHistoryRepository, EventHistoryRepository>();
services.AddSingleton<IRequestIndexingRepository, RequestIndexingRepository>();
```

**Benefit**:
- Shared state across the application (e.g., configuration, orchestrator)
- Thread-safe singleton instances managed by .NET's dependency injection
- Reduced memory overhead for expensive-to-create objects

**Key Singleton Services**:
- `IOrchestrator` - Main orchestration service for request routing
- `IEventHistoryRepository` - Manages event history persistence
- `IRequestIndexingRepository` - Handles request indexing operations
- Configuration objects

---

### Builder Pattern (Implicit)

**Purpose**: Constructs complex objects step by step.

**Implementation in Solution**:
The Polly Policy Registry builder pattern is used to construct complex retry policies:

**File**: `RequestRouting.Application/Policy/RoutingRetryPolicy.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;
using Polly;
using RequestRouting.Application.Constants;

namespace RequestRouting.Application.Policy;

public static class Policies
{
    public static void InitiatePolicyRegistry(this IServiceCollection services)
    {
        var registry = services.AddPolicyRegistry();

        // Builder pattern: Constructing policy step by step
        var policy = Polly.Policy
            .Handle<Exception>()
            .OrResult<bool>(x => x == false)
            .WaitAndRetryAsync(5,
                retryCount => TimeSpan.FromMilliseconds(50),
                (exception, timeSpan, retryCount, context) =>
                {
                    if (!context.TryGetLogger(out var logger)) return;
                    context[PolicyConstants.RetryCount] = retryCount;
                    logger.LogInformation("Error causing Retry: {exception}", exception?.Exception?.Message);
                });

        var generalPolicy = Polly.Policy
            .Handle<Exception>()
            .RetryAsync(3, onRetry: (exception, retryCount, context) =>
            {
                // Retry logic
            });

        registry.Add(PolicyConstants.RetryPolicy, policy);
        registry.Add(PolicyConstants.GeneralRetryPolicy, generalPolicy);
    }
}
```

**Benefit**:
- Fluent API for constructing complex policies
- Clear, readable policy definitions
- Flexibility to add conditions and handlers progressively

---

## STRUCTURAL PATTERNS

### Repository Pattern

**Purpose**: Encapsulates data access logic and provides an abstraction over the data source.

**Implementation in Solution**:
Multiple repository interfaces and implementations follow the Repository pattern:

**File**: `RequestRouting.Application/Interface/` (Interface definitions)

```csharp
// From IEventHistoryRepository.cs
namespace RequestRouting.Application.Interface;

public interface IEventHistoryRepository
{
    Task<RequestEventHistoryRequest> GetRequestEventHistory(string requestForServiceKey);
    Task UpdateEventHistoryAsync(RequestEventHistoryRequest eventHistory);
    // ... other methods
}

// From IRequestIndexingRepository.cs
public interface IRequestIndexingRepository
{
    Task InsertRequest(RequestIndexingRequest request);
    Task UpdateRequest(RequestIndexingRequest request);
}

// From ICacheRepository.cs
public interface ICacheRepository
{
    void AddToCache(string key, string value, string Category);
    Task<string> GetFromCache(string key);
}
```

**Implementation Example**:

**File**: `RequestRouting.Infrastructure/Repositories/EventHistoryRepository.cs`

```csharp
using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using RequestRouting.Application.Interface;

namespace RequestRouting.Infrastructure.Repositories;

public class EventHistoryRepository : IEventHistoryRepository
{
    private readonly Container _container;
    private readonly ILogger<EventHistoryRepository> _logger;

    public EventHistoryRepository(CosmosClient dbClient, string databaseName, string containerName,
        ILogger<EventHistoryRepository> logger)
    {
        _container = dbClient.GetContainer(databaseName, containerName);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<RequestEventHistoryRequest> GetRequestEventHistory(string requestForServiceKey)
    {
        RequestEventHistoryRequest requestEventHistory = new();
        try
        {
            var response = await _container.ReadItemAsync<RequestEventHistoryDto>(
                requestForServiceKey,
                new PartitionKey(requestForServiceKey));

            if (response is not null)
            {
                requestEventHistory = new RequestEventHistoryRequest
                {
                    ETag = response.Resource.ETag,
                    Id = response.Resource.Id,
                    RequestForServiceKey = response.Resource.RequestForServiceKey,
                    InsuranceCarrierKey = response.Resource.InsuranceCarrierKey,
                    History = response.Resource.History
                };
            }
        }
        catch (CosmosException ce)
        {
            if (ce.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogInformation("History not found: {RequestForServiceKey}", requestForServiceKey);
                return requestEventHistory;
            }
            throw;
        }
        return requestEventHistory;
    }
}
```

**Benefits**:
- Decouples business logic from data access logic
- Makes testing easier through mock repositories
- Changes to data access strategy don't affect business logic
- Centralized data access with consistent error handling

**All Repository Implementations**:
- `EventHistoryRepository` - Azure Cosmos DB operations
- `RequestIndexingRepository` - Request indexing operations
- `RequestSearchRepository` - Search functionality
- `UserProfileRepository` - User profile data access
- `StateConfigurationRepository` - Configuration state management
- `CaseDrawerRepository` - Case drawer operations

---

### Adapter Pattern

**Purpose**: Converts the interface of a class into another interface the client expects.

**Implementation in Solution**:
The Kafka consumer abstraction acts as an adapter, providing a consistent interface over Kafka's complex consumer API:

**File**: `RequestRouting.Infrastructure/Kafka/` (Configuration/Interface)

```csharp
// Adapter interface that simplifies Kafka consumer usage
public interface IKafkaConsumer
{
    IConsumer<string, string> GetConsumer();
}

public interface IKafkaRoutableDeterminationConsumer
{
    IConsumer<string, string> GetConsumer();
}

// BaseObserver uses the simplified adapter interface
public class BaseObserver
{
    private readonly IConsumer<string, string> _consumer;
    
    protected BaseObserver(ILogger<BaseObserver> logger, IKafkaConsumer consumer, string topicName)
    {
        _consumer = consumer.GetConsumer();
        _topicName = topicName;
    }

    public void StartConsumer(CancellationToken stoppingToken)
    {
        Task.Run(async () => await ConsumeEvents(stoppingToken), stoppingToken);
    }

    private async Task ConsumeEvents(CancellationToken stoppingToken)
    {
        // Simplified consumption logic using the adapter
        _consumer.Subscribe(_topicName);
        while (!stoppingToken.IsCancellationRequested)
        {
            var consumerResult = _consumer.Consume(stoppingToken);
            // Process message
        }
    }
}
```

**Benefits**:
- Simplifies Kafka consumer interactions
- Abstracts Kafka's complexity from observers
- Makes testing easier by mocking the IKafkaConsumer interface
- Centralizes Kafka configuration and setup

---

### Facade Pattern

**Purpose**: Provides a unified, simplified interface to a set of interfaces in a subsystem.

**Implementation in Solution**:
The `Orchestrator` class acts as a facade, coordinating multiple complex operations for request routing:

**File**: `RequestRouting.Application/Orchestrator/Orchestrator.cs`

```csharp
public class Orchestrator : IOrchestrator
{
    private readonly IEventHistoryRepository _historyRepository;
    private readonly IReplayService _replayService;
    private readonly IRequestIndexingRepository _requestIndexingRepository;
    private readonly IUserProfileCacheService _userProfileCacheService;
    private readonly IHandlerQueue _handlerQueue;
    private readonly IRequestSearchRepository _requestSearchRepository;
    private readonly ILockingService _lockingService;
    private readonly AsyncRetryPolicy<bool> _cosmosPolicy;

    public Orchestrator(
        IEventHistoryRepository historyRepository,
        IReplayService replayService,
        ILogger<Orchestrator> logger,
        IReadOnlyPolicyRegistry<string> policyRegistry,
        IRequestIndexingRepository requestIndexingRepository,
        IOptions<AppSettings> options,
        IUserProfileCacheService userProfileCacheService,
        IHandlerQueue handlerQueue,
        IRequestSearchRepository requestSearchRepository,
        ILockingService lockingService,
        IOptions<LockOptions> loptions)
    {
        _historyRepository = historyRepository ?? throw new ArgumentNullException(nameof(historyRepository));
        _replayService = replayService ?? throw new ArgumentNullException(nameof(replayService));
        _requestIndexingRepository = requestIndexingRepository ?? throw new ArgumentNullException();
        _userProfileCacheService = userProfileCacheService;
        _handlerQueue = handlerQueue ?? throw new ArgumentNullException(nameof(handlerQueue));
        _requestSearchRepository = requestSearchRepository ?? throw new ArgumentNullException();
        _lockingService = lockingService;
    }

    public async Task OrchestrateAsync(UCXEvent evt, CancellationToken cancellationToken)
    {
        // Coordinates multiple services:
        // - Event history retrieval
        // - Request state management
        // - Event replay
        // - Handler queue operations
        // - Cache updates
        // - Search index updates
        // - Locking mechanisms
        
        var result = await _cosmosPolicy.ExecuteAndCaptureAsync(
            ctx => OrchestrateInternal_HandleConcurrency(evt), context);
    }
}
```

**Benefits**:
- Hides complexity of coordinating multiple services
- Provides simplified interface for handling request routing
- Centralizes orchestration logic
- Makes complex workflows understandable through a single interface

**Facade Responsibilities**:
- Coordinate event processing
- Manage request state transitions
- Handle concurrency with locking
- Trigger replay operations
- Update various data stores

---

### Proxy Pattern

**Purpose**: Provides a surrogate or placeholder for another object to control access to it.

**Implementation in Solution**:
The caching services (CacheRepository and UserProfileCacheService) act as proxies, controlling access to the underlying cache:

**File**: `RequestRouting.Application/Services/CacheRepository.cs`

```csharp
public class CacheRepository : ICacheRepository
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<CacheRepository> _logger;
    private static readonly Object _lock = new Object();

    public CacheRepository(IDistributedCache cache, ILogger<CacheRepository> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public void AddToCache(string key, string value, string Category)
    {
        var cacheExpiryOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = DateTime.Now.AddHours(1),
            SlidingExpiration = TimeSpan.FromMinutes(10)
        };
        
        lock (_lock)
        {
            _logger.LogInformation("Saving value for {key} under Category {Category}", key, Category);
            _cache.SetString(key, value, cacheExpiryOptions);
        }
    }

    public Task<string> GetFromCache(string key)
    {
        var value = _cache.GetStringAsync(key);
        return value ?? Task.FromResult<string>(null);
    }
}
```

**Benefits**:
- Controls access to distributed cache
- Provides logging and monitoring
- Implements thread-safe cache operations
- Manages cache expiration policies transparently
- Can control and track all cache operations

---

## BEHAVIORAL PATTERNS

### Observer Pattern

**Purpose**: Defines a one-to-many dependency between objects so that when one object changes state, all dependents are notified automatically.

**Implementation in Solution**:
The event-driven architecture uses the Observer pattern extensively with Kafka topics and multiple observer classes:

**File**: `RequestRouting.Observer/BaseObserver.cs`

```csharp
public class BaseObserver
{
    private readonly IConsumer<string, string> _consumer;
    private readonly ILogger<BaseObserver> _logger;
    private readonly string _topicName;

    protected BaseObserver(ILogger<BaseObserver> logger, IKafkaConsumer consumer, string topicName)
    {
        _logger = logger;
        _consumer = consumer.GetConsumer();
        _topicName = topicName;
    }

    public void StartConsumer(CancellationToken stoppingToken)
    {
        Task.Run(async () => await ConsumeEvents(stoppingToken), stoppingToken);
    }

    private async Task ConsumeEvents(CancellationToken stoppingToken)
    {
        try
        {
            _consumer.Subscribe(_topicName);  // Subscribe to topic
            while (!stoppingToken.IsCancellationRequested)
            {
                var consumerResult = _consumer.Consume(stoppingToken);
                if (consumerResult.IsPartitionEOF)
                    continue;

                try
                {
                    // Notify - handle the event
                    await HandleEventAsync(consumerResult);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, ex.Message);
                }
            }
        }
        finally
        {
            _consumer.Close();
        }
    }

    public virtual Task HandleEventAsync(ConsumeResult<string, string> consumerResult)
    {
        throw new NotImplementedException();
    }
}
```

**Observer Implementations** (Each is a specialized observer):

**File**: `RequestRouting.Observer/Observers/` - Contains 25+ observer implementations

```csharp
// Example: RoutableDeterminationObserver
public class RoutableDeterminationObserver : BaseObserver
{
    private readonly IRoutableDeterminationHandler _handler;
    private readonly IOrchestrator _orchestrator;
    private readonly IOptions<AppSettings> _options;

    public RoutableDeterminationObserver(
        ILogger<RoutableDeterminationObserver> logger,
        IKafkaRoutableDeterminationConsumer consumer,
        IOrchestrator orchestrator,
        IOptions<AppSettings> options,
        string topicName,
        IRoutableDeterminationHandler handler) 
        : base(logger, consumer, topicName)
    {
        _handler = handler;
        _orchestrator = orchestrator;
        _options = options;
    }

    public override async Task HandleEventAsync(ConsumeResult<string, string> consumerResult)
    {
        if (_options.Value.ReplayRoutableDetermination)
        {
            var deserialized = JsonSerializer.Deserialize<RoutableDeterminationEvent>(
                consumerResult.Message.Value);
            await _handler.HandleAsync(deserialized, _orchestrator);
        }
    }
}
```

**Other Observers**:
- `AuthorizationCanceledObserver`
- `AuthorizationDismissedObserver`
- `DequeueRoutingRequestObserver`
- `DueDateCalculatedObserver`
- `DueDateMissedObserver`
- `FileAttachedObserver`
- `JurisdictionStateUpdatedObserver`
- `MedicalDisciplineCorrectedObserver`
- `MemberIneligibilitySubmittedObserver`
- `MemberInfoUpdatedForRequestObserver`
- `PeerToPeerActionSubmittedObserver`
- `PhysicianDecisionMadeObserver`
- `RequestForServiceSubmittedObserver`
- `RequestLineOfBusinessUpdatedObserver`
- `RequestSpecialtyUpdatedObserver`
- `RequestUrgencyUpdatedObserver`
- `RoutingRequestAssignedObserver`
- `StateConfigurationAddedObserver`
- `StatusChangeObserver`
- `UserExcludedFromRequestObserver`
- `UserProfileConfigurationUpdatedObserver`
- And more...

**Benefits**:
- Loose coupling between event producers and consumers
- Dynamic subscription/unsubscription
- Multiple observers can react to the same event
- Easy to add new observers without modifying existing code
- Kafka provides the pub-sub infrastructure

---

### Command Pattern

**Purpose**: Encapsulates a request as an object, allowing parametrization of clients with different requests.

**Implementation in Solution**:
Command handlers process different types of events as commands:

**File**: `RequestRouting.Application/Commands/Interfaces/`

```csharp
// Command handler interface
public interface IRequestHandler
{
    public string EntityTypeName { get; }
    public Task<RequestAggregate> HandleAsync(RequestAggregate requestState, UCXEvent currentEvent);
}
```

**File**: `RequestRouting.Application/Commands/Handlers/` - Multiple command handler implementations

```csharp
// Example: DequeueRoutingRequestHandler
public class DequeueRoutingRequestHandler : IRequestHandler
{
    private readonly ILogger<DequeueRoutingRequestHandler> _logger;

    public DequeueRoutingRequestHandler(ILogger<DequeueRoutingRequestHandler> logger)
    {
        _logger = logger;
    }

    public string EntityTypeName => "UCXDequeueRoutingRequest";
    public string Version => "1.0.0";

    public Task<RequestAggregate> HandleAsync(RequestAggregate requestState, UCXEvent currentEvent)
    {
        _logger.LogInformation("DequeueRoutingRequest:HandleAsync started");

        if (currentEvent is null)
            throw new ArgumentNullException(nameof(currentEvent));

        var @event = currentEvent as DequeueRoutingRequestEvent;
        if (@event is null)
            return Task.FromResult(requestState);

        requestState.UpdateDequeueRequestRouting();
        return Task.FromResult(requestState);
    }
}
```

**Command Handlers** (25+ handlers):
- `AuthorizationCanceledHandler`
- `AuthorizationDismissedHandler`
- `AuthorizationWithdrawnHandler`
- `DequeueRoutingRequestHandler`
- `DueDateCalculatedHandler`
- `DueDateMissedHandler`
- `FileAttachedHandler`
- `JurisdictionStateUpdatedHandler`
- `MedicalDisciplineCorrectedHandler`
- `MemberIneligibilitySubmittedHandler`
- `MemberInfoUpdatedForRequestHandler`
- `PeerToPeerActionSubmittedHandler`
- `PhysicianDecisionMadeHandler`
- `RequestForServiceSubmittedHandler`
- `RequestLineOfBusinessUpdatedHandler`
- `RequestSpecialtyUpdatedHandler`
- `RequestUrgencyUpdatedHandler`
- `RoutableDeterminationHandler`
- `RoutableDeterminationRequestForServiceSavedHandler`
- `RoutingRequestAssignedHandler`
- `StateConfigurationAddedHandler`
- `StatusChangeHandler`
- `UserExcludedFromRequestHandler`
- `UserProfileConfigurationUpdatedHandler`

**Benefits**:
- Encapsulates different request types as command objects
- Allows undo/redo capabilities (with event sourcing)
- Decouples request sender from request handler
- Supports queuing, scheduling, and remote execution of commands
- Easy to add new command types

---

### Strategy Pattern

**Purpose**: Defines a family of algorithms, encapsulates each one, and makes them interchangeable.

**Implementation in Solution**:
Polly policies provide interchangeable retry and resilience strategies:

**File**: `RequestRouting.Application/Policy/RoutingRetryPolicy.cs`

```csharp
public static class Policies
{
    public static void InitiatePolicyRegistry(this IServiceCollection services)
    {
        var registry = services.AddPolicyRegistry();

        // Strategy 1: Retry with exponential backoff for cosmos operations
        var cosmosRetryPolicy = Polly.Policy
            .Handle<Exception>()
            .OrResult<bool>(x => x == false)
            .WaitAndRetryAsync(
                retryCount: 5,
                sleepDurationProvider: retryCount => TimeSpan.FromMilliseconds(50 * retryCount),
                onRetry: (exception, timeSpan, retryCount, context) =>
                {
                    // Logging and context management
                });

        // Strategy 2: Simple retry for general operations
        var generalRetryPolicy = Polly.Policy
            .Handle<Exception>()
            .RetryAsync(
                retryCount: 3,
                onRetry: (exception, retryCount, context) =>
                {
                    // Logging and context management
                });

        registry.Add(PolicyConstants.RetryPolicy, cosmosRetryPolicy);
        registry.Add(PolicyConstants.GeneralRetryPolicy, generalRetryPolicy);
    }
}
```

**Usage in Orchestrator**:

```csharp
public class Orchestrator : IOrchestrator
{
    private readonly AsyncRetryPolicy<bool> _cosmosPolicy;
    
    public async Task OrchestrateAsync(UCXEvent evt, CancellationToken cancellationToken)
    {
        // Uses the strategy pattern - can swap policies without changing code
        var result = await _cosmosPolicy.ExecuteAndCaptureAsync(
            ctx => OrchestrateInternal_HandleConcurrency(evt), 
            context);
    }
}
```

**Benefits**:
- Different retry strategies for different scenarios
- Easy to switch between strategies at runtime
- Supports composition of multiple policies
- Centralizes resilience logic
- Makes code more testable

---

### State Pattern

**Purpose**: Allows an object to alter its behavior when its internal state changes.

**Implementation in Solution**:
The `RequestAggregate` uses various state enums and status-based methods:

**File**: `RequestRouting.Domain/Aggregates/RequestAggregate.cs`

```csharp
public class RequestAggregate
{
    public StatusEnum Status { get; private set; }
    public StatusEnum PriorPeerStatus { get; private set; }
    public SslRequirementStatusEnum SslRequirementStatus { get; private set; }
    public PriorityEnum Priority { get; private set; }

    // State-based behavior methods
    public bool IsRoutable()
    {
        var filterDate = new DateTime(2023, 03, 01).ToUniversalTime();
        if (StartDateUtc == null || DateTime.MinValue.Equals(StartDateUtc) || StartDateUtc < filterDate)
            return false;

        // State determines behavior
        return (Status is StatusEnum.NeedsMdReview or 
                StatusEnum.NeedsSslSignoff && DueDateUtc is not null && 
                !DateTime.MinValue.Equals(DueDateUtc)) || IsPostDecisionRequestRoutable();
    }

    public bool IsPostDecisionRequestRoutable()
    {
        // Different state = different behavior
        return ((Status is StatusEnum.Appeal or 
                 StatusEnum.AppealRecommended or 
                 StatusEnum.Reconsideration) && 
                (AssignmentExpirationUtc != null && AssignmentExpirationUtc >= DateTime.UtcNow) && 
                DueDateUtc is not null && !DateTime.MinValue.Equals(DueDateUtc));
    }

    public bool IsDeletable()
    {
        // State determines if deletion is allowed
        return Status is StatusEnum.Dismissed or
            StatusEnum.Canceled or
            StatusEnum.MemberIneligible or
            StatusEnum.NeedsP2PReview or
            StatusEnum.Withdrawn or
            // ... many other states
            StatusEnum.InEligibilityReview or
            StatusEnum.SiteOfCareOutreach or
            StatusEnum.OOPPendingHPApproval ||
            IsReserved || IsPostDecisionRequestDeletable();
    }

    public bool IsRemoveableSpecialStatesFromSecondReview(
        List<string> commercialStatesToBeRemoved,
        List<string> commercialMedicaidStatesToBeRemoved)
    {
        // Complex state-based logic
        if(IsPermanentAssignment == false && 
           Status == StatusEnum.NeedsSslSignoff && 
           MedicalDiscipline == DisciplineEnum.Radiology &&
           !string.IsNullOrEmpty(JurisdictionState) &&
           LineOfBusiness == LineOfBusinessEnum.Commercial &&
           commercialStatesToBeRemoved != null &&
           commercialStatesToBeRemoved.Any(item => item.Contains(JurisdictionState?.ToLower())))
        {
            return true;
        }
        return false;
    }

    // State transitions
    public void UpdateDequeueRequestRouting()
    {
        Status = StatusEnum.Dequeue;
    }

    public void UpdateAssignment(string userEmail, int assignmentExpirationMinutes)
    {
        AssignedToId = userEmail;
        AssignmentExpirationUtc = DateTime.UtcNow.AddMinutes(assignmentExpirationMinutes);
    }
}
```

**Benefits**:
- Encapsulates state-specific behavior
- Easy to add new states
- Reduces complex conditional logic
- Makes state transitions explicit
- Improves code readability and maintainability

---

### Chain of Responsibility Pattern

**Purpose**: Passes a request along a chain of handlers where each handler decides either to process the request or pass it along the chain.

**Implementation in Solution**:
The handler queue and event processing mechanism implements a chain of responsibility:

**File**: `RequestRouting.Application/Interface/IHandlerQueue.cs`

```csharp
// The handler queue acts as a chain
public interface IHandlerQueue
{
    Task<IRequestHandler> GetNextHandler(string entityTypeName);
    void RegisterHandler(IRequestHandler handler);
}
```

**Event Processing Chain**:
Events flow through a chain of handlers:
1. Event is deserialized
2. Orchestrator processes the event
3. Appropriate handler is selected based on event type
4. Handler processes the event and updates state
5. State is persisted back to repositories

```csharp
// Handlers are registered for different event types
public void RegisterHandlers(IServiceCollection services)
{
    services.RegisterHandlers(
        typeof(DequeueRoutingRequestHandler),
        typeof(AuthorizationCanceledHandler),
        typeof(AuthorizationDismissedHandler),
        typeof(AuthorizationWithdrawnHandler),
        // ... and many more
    );
}
```

**Benefits**:
- Decouples senders from receivers
- Allows handlers to be added/removed dynamically
- Handler doesn't need to know about others in the chain
- Supports complex workflows

---

### Template Method Pattern

**Purpose**: Defines the skeleton of an algorithm in a method, deferring some steps to subclasses.

**Implementation in Solution**:
The `BaseObserver` class defines the template for event consumption:

**File**: `RequestRouting.Observer/BaseObserver.cs`

```csharp
public class BaseObserver
{
    private readonly IConsumer<string, string> _consumer;
    private readonly ILogger<BaseObserver> _logger;
    private readonly string _topicName;

    public void StartConsumer(CancellationToken stoppingToken)
    {
        // Template method - defines the skeleton
        Task.Run(async () => await ConsumeEvents(stoppingToken), stoppingToken);
    }

    private async Task ConsumeEvents(CancellationToken stoppingToken)
    {
        try
        {
            _consumer.Subscribe(_topicName);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var consumerResult = _consumer.Consume(stoppingToken);
                    if (consumerResult.IsPartitionEOF)
                        continue;

                    using (_logger.BeginScope(new Dictionary<string, object>
                    {
                        { "Topic", consumerResult.Topic },
                        { "Partition", consumerResult.Partition },
                        { "Offset", consumerResult.Offset },
                        { "Key", consumerResult.Message.Key }
                    }))
                    {
                        try
                        {
                            // Template method hook - subclasses override this
                            await HandleEventAsync(consumerResult);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, ex.Message);
                        }
                    }
                }
                catch (ConsumeException e)
                {
                    if (!e.Error.IsFatal)
                        continue;
                    throw;
                }
            }
        }
        finally
        {
            _consumer.Close();
        }
    }

    // Template method hook - overridden by subclasses
    public virtual Task HandleEventAsync(ConsumeResult<string, string> consumerResult)
    {
        throw new NotImplementedException();
    }
}
```

**Subclass Implementation**:

```csharp
public class RoutableDeterminationObserver : BaseObserver
{
    // Implements the template method hook
    public override async Task HandleEventAsync(ConsumeResult<string, string> consumerResult)
    {
        if (_options.Value.ReplayRoutableDetermination)
        {
            if (!consumerResult.Message.Value.Contains($"Version\":\"{RoutableDeterminationEvent.EventVersion}"))
                return;

            _logger.LogInformation("RoutableDeterminationObserver started");
            var deserialized = JsonSerializer.Deserialize<RoutableDeterminationEvent>(
                consumerResult.Message.Value);
            await _handler.HandleAsync(deserialized, _orchestrator);
            _logger.LogInformation("RoutableDeterminationObserver ended");
        }
    }
}
```

**Benefits**:
- Standardizes event consumption across all observers
- Consistent logging, error handling, and scoping
- Subclasses only need to implement specific event handling
- Code reuse and consistency

---

## ARCHITECTURAL PATTERNS

### Domain-Driven Design (DDD)

**Purpose**: Focuses on modeling the core business domain and using that model as the basis for software design.

**Implementation in Solution**:
The solution is structured around domain concepts:

**Domain Layer** (`RequestRouting.Domain/`):
- **Aggregates**: `RequestAggregate`, `UserAggregate`, `UserProfileAggregate`, `StateConfigurationAggregate`, `UserProfileConfigurationAggregate`
- **Entities**: Domain entities representing core business concepts
- **Value Objects**: `ValueObjects/` folder - immutable objects representing domain values
- **Events**: Domain events representing important business occurrences
- **Factory**: `RequestFactory` - creates aggregates according to domain rules
- **Constants & Enums**: `StatusEnum`, `DisciplineEnum`, `PriorityEnum`, `SslRequirementStatusEnum`

**Application Layer** (`RequestRouting.Application/`):
- **Commands**: Encapsulates requests to change domain state
- **Query**: Encapsulates requests to read domain data
- **Events**: Application events raised by domain operations
- **Services**: Domain services coordinating multiple aggregates
- **Orchestrator**: Orchestrates complex workflows

**Infrastructure Layer** (`RequestRouting.Infrastructure/`):
- **Repositories**: Implementations for persisting aggregates
- **Services**: Infrastructure services (Kafka, Search, etc.)
- **Database Objects**: DTO mappings for persistence

**Example: RequestAggregate Core Domain Model**:

```csharp
public class RequestAggregate
{
    // Domain state
    public Guid AuthorizationKey { get; private set; }
    public Guid RequestForServiceKey { get; private set; }
    public StatusEnum Status { get; private set; }
    public string AssignedToId { get; private set; }
    public List<string> ExcludedUsernames { get; private set; } = new();

    // Domain logic - business rules
    public bool IsRoutable() { /* ... */ }
    public bool IsDeletable() { /* ... */ }
    public bool IsRemoveableSpecialStatesFromSecondReview(...) { /* ... */ }
    
    // Domain operations - state changes
    public void UpdateAssignment(string userEmail, int expirationMinutes) { /* ... */ }
    public void UpdateDequeueRequestRouting() { /* ... */ }
    public void UpdateSuspendExpiration(...) { /* ... */ }
}
```

**Benefits**:
- Clear separation of concerns
- Domain logic is testable and independent of frameworks
- Easy to understand business requirements from code structure
- Supports complex business logic without framework dependencies

---

### Event Sourcing

**Purpose**: Stores the state of an entity as a sequence of state-changing events rather than storing just the current state.

**Implementation in Solution**:
The solution uses event sourcing extensively:

**Event Definitions** (`RequestRouting.Domain/Events/` and `RequestRouting.Application/Events/`):

```csharp
// Base event type
public abstract class UCXEvent
{
    public string Key { get; set; }
    public DateTime CreatedDateTimeUtc { get; set; }
}

// Domain events
public class DequeueRoutingRequestEvent : UCXEvent
{
    public const string EventVersion = "1.0.0";
    public const string EventName = "UCXDequeueRoutingRequest";
}

public class RoutableDeterminationEvent : UCXEvent
{
    public const string EventVersion = "1.0.0";
    public const string EventName = "UCXRoutableDeterminationRequest";
}

public class RoutingRequestAssignedEvent : UCXEvent
{
    public const string EventVersion = "1.0.0";
    public const string EventName = "UCXRoutingRequestAssigned";
}

public class UserExcludedFromRequestEvent : UCXEvent
{
    public const string EventVersion = "1.0.0";
    public const string EventName = "UCXUserExcludedFromRequest";
}
```

**Event History Storage** (`RequestRouting.Infrastructure/Repositories/EventHistoryRepository.cs`):

```csharp
public class EventHistoryRepository : IEventHistoryRepository
{
    // Stores complete history of events for a request
    public async Task<RequestEventHistoryRequest> GetRequestEventHistory(string requestForServiceKey)
    {
        // Retrieves all events for a request
        var response = await _container.ReadItemAsync<RequestEventHistoryDto>(
            requestForServiceKey, new PartitionKey(requestForServiceKey));
        
        return new RequestEventHistoryRequest
        {
            Id = response.Resource.Id,
            RequestForServiceKey = response.Resource.RequestForServiceKey,
            History = response.Resource.History  // List of all events
        };
    }

    public async Task UpdateEventHistoryAsync(RequestEventHistoryRequest eventHistory)
    {
        // Appends new events to history
        await _container.UpsertItemAsync(eventHistoryDto);
    }
}
```

**Event Replay** (`RequestRouting.Application/Replay/`):
```csharp
// Replay service reconstructs entity state from events
public interface IReplayService
{
    Task<RequestAggregate> ReplayAsync(string requestForServiceKey, CancellationToken token);
}
```

**Orchestrator Integration**:

```csharp
public async Task OrchestrateAsync(UCXEvent evt, CancellationToken cancellationToken)
{
    // 1. Get event history
    var history = await _historyRepository.GetRequestEventHistory(evt.Key);
    
    // 2. Replay events to get current state
    var requestState = await _replayService.ReplayAsync(evt.Key, cancellationToken);
    
    // 3. Apply new event
    var handler = await _handlerQueue.GetNextHandler(evt.EntityTypeName);
    requestState = await handler.HandleAsync(requestState, evt);
    
    // 4. Store updated state and new event
    history.History.Add(evt);
    await _historyRepository.UpdateEventHistoryAsync(history);
}
```

**Benefits**:
- Complete audit trail of all changes
- Can replay events to recover state at any point in time
- Supports temporal queries ("what was the state at time X?")
- Enables complex event-driven workflows
- Decouples write and read models (CQRS pattern compatible)
- Resilient to application failures

---

## PATTERN USAGE SUMMARY TABLE

| Pattern | Type | Usage | Key File |
|---------|------|-------|----------|
| Factory | Creational | Creating RequestAggregates | `RequestFactory.cs` |
| Singleton | Creational | Services, Configuration, Repositories | `Startup.cs` |
| Builder | Creational | Building Polly policies | `RoutingRetryPolicy.cs` |
| Repository | Structural | Data access abstraction | `EventHistoryRepository.cs` |
| Adapter | Structural | Kafka consumer abstraction | `BaseObserver.cs` |
| Facade | Structural | Orchestrating complex operations | `Orchestrator.cs` |
| Proxy | Structural | Cache access control | `CacheRepository.cs` |
| Observer | Behavioral | Event-driven request processing | `BaseObserver.cs` |
| Command | Behavioral | Encapsulating event handlers | `IRequestHandler.cs` |
| Strategy | Behavioral | Retry and resilience policies | `RoutingRetryPolicy.cs` |
| State | Behavioral | Request status-based behavior | `RequestAggregate.cs` |
| Chain of Responsibility | Behavioral | Event handler chain | `IHandlerQueue.cs` |
| Template Method | Behavioral | Event consumption algorithm | `BaseObserver.cs` |
| Domain-Driven Design | Architectural | Domain modeling and structure | Domain layer |
| Event Sourcing | Architectural | Storing state changes as events | `EventHistoryRepository.cs` |

---

## DESIGN PATTERN INTERACTION FLOW

The following diagram illustrates how these patterns work together:

```
HTTP Request / Kafka Event
    ↓
Observer Pattern (BaseObserver)
    ↓
Adapter Pattern (IKafkaConsumer)
    ↓
Command Pattern (IRequestHandler)
    ↓
Facade Pattern (Orchestrator)
    ├→ Strategy Pattern (Polly Policies)
    ├→ Repository Pattern (EventHistoryRepository)
    ├→ Repository Pattern (RequestIndexingRepository)
    ├→ Proxy Pattern (CacheRepository)
    └→ Singleton Pattern (Services)
    ↓
State Pattern (RequestAggregate)
    ├→ Domain-Driven Design (Domain Logic)
    └→ Event Sourcing (Event History)
    ↓
Result/Persistence
```

---

## BEST PRACTICES DEMONSTRATED

1. **Dependency Injection**: Heavy use of DI for loose coupling and testability
2. **SOLID Principles**: 
   - Single Responsibility: Each handler, repository, and service has one job
   - Open/Closed: Easy to add new handlers and repositories
   - Liskov Substitution: Services are interchangeable through interfaces
   - Interface Segregation: Small, focused interfaces
   - Dependency Inversion: Depends on abstractions, not implementations

3. **Async/Await**: Asynchronous operations throughout for scalability
4. **Error Handling**: Comprehensive logging and exception handling
5. **Resiliency**: Polly policies for transient failure handling
6. **Testing**: Patterns support unit testing through mocking and isolation
7. **Configuration**: Externalized configuration through AppSettings
8. **Scalability**: Event-driven, asynchronous architecture supports high throughput

---

## CONCLUSION

The RequestRouting solution demonstrates a sophisticated application of Gang of Four design patterns combined with modern architectural patterns. The patterns work together to create a:

- **Maintainable** codebase with clear separation of concerns
- **Scalable** system handling complex request routing logic
- **Testable** architecture supporting unit and integration testing
- **Resilient** design with comprehensive error handling and retry policies
- **Extensible** framework making it easy to add new event handlers and repositories

The combination of these patterns creates a robust, enterprise-grade solution for managing complex medical request routing workflows.
