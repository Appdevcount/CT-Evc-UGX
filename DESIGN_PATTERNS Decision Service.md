# Gang of Four (GoF) Design Patterns in Ucx.DecisionService Solution

## Overview

This document provides a comprehensive analysis of Gang of Four (GoF) design patterns implemented in the Ucx.DecisionService solution. The solution demonstrates a well-structured microservice architecture with multiple design patterns that promote scalability, maintainability, and loose coupling.

---

## 1. **Strategy Pattern**

### Description
The Strategy pattern defines a family of algorithms, encapsulates each one, and makes them interchangeable. Strategy lets the algorithm vary independently from clients that use it.

### Implementation in Solution

Multiple implementations of `ISendIfNeeded<RequestState>` represent different strategies for sending different types of messages based on specific conditions:

#### Code References:

**Interface Definition:**
```csharp
// File: Ucx.DecisionService.Domain/Services/ISendIfNeeded.cs
public interface ISendIfNeeded<TState> where TState : BaseState
{
    Task SendIfNeeded(TState state, EventSourceMessage eventSourceMessage);
}
```

**Concrete Strategy Implementations:**

1. **SendApprovalScriptIfNeeded.cs**
```csharp
public class SendApprovalScriptIfNeeded: ISendIfNeeded<RequestState>
{
    private readonly ICosmosDbService<AuditLog> _auditLogRepo;
    private readonly ILogger<SendApprovalScriptIfNeeded> _logger;
    private readonly IMessageSenderService<Guid, UCXApprovalScriptCreated> _messageSenderService;

    public SendApprovalScriptIfNeeded(ILogger<SendApprovalScriptIfNeeded> logger, 
        IMessageSenderService<Guid, UCXApprovalScriptCreated> messageSenderService,
        ICosmosDbService<AuditLog> auditLogRepo)
    {
        _logger = logger;
        _messageSenderService = messageSenderService;
        _auditLogRepo = auditLogRepo;
    }

    public async Task SendIfNeeded(RequestState state, EventSourceMessage eventSourceMessage)
    {
        if (state.RequestForServiceKey == Guid.Empty || !state.IsApprovalScriptDirty || 
            state.ReviewType == null! || eventSourceMessage.PayloadType == nameof(UCXRequestForServiceSubmitted))
            return;

        var toSend = new UCXApprovalScriptCreated { /* ... */ };
        state.IsApprovalScriptDirty = false;
        await SendMessage(toSend, eventSourceMessage);
    }
}
```

2. **SendAutomatedDecisionMadeIfNeeded.cs**
```csharp
public class SendAutomatedDecisionMadeIfNeeded : ISendIfNeeded<RequestState>
{
    private readonly IMessageSenderService<Guid, UcxAutomatedDecisionMade> _automatedDecisionMadeSender;
    private readonly ILogger<SendAutomatedDecisionMadeIfNeeded> _logger;

    public async Task SendIfNeeded(RequestState state, EventSourceMessage eventSourceMessage)
    {
        if (state.RequestForServiceKey == Guid.Empty || !state.IsAutomatedDecisionDirty || 
            state.ReviewType == null!)
            return;

        var toSend = new UcxAutomatedDecisionMade { /* ... */ };
        await SendMessage(toSend, eventSourceMessage);
        state.IsAutomatedDecisionDirty = false;
    }
}
```

3. **SendPhysicianDecisionMadeIfNeeded.cs, SendNurseDecisionMadeIfNeeded.cs, SendStructuredClinicalDecisionMadeIfNeeded.cs, SendPostDecisionWebDecisionMadeIfNeeded.cs, SendProcedureUpdatedIfNeeded.cs, SendUcxNoteCreatedIfNeeded.cs**

All follow the same pattern with different message types.

**Registration in DI Container:**
```csharp
// File: Ucx.DecisionService.Api/Configuration/ApiServiceConfiguration.cs
builder.Services.RegisterImplementingClasses<ISendIfNeeded<RequestState>>();
```

### Benefits
- Easy to add new message sending strategies without modifying existing code
- Each strategy encapsulates its own logic and decision criteria
- Runtime selection of strategy based on state conditions
- Promotes the Open/Closed Principle

---

## 2. **Adapter Pattern**

### Description
The Adapter pattern converts the interface of a class into another interface clients expect. Adapter lets classes work together that could not otherwise because of incompatible interfaces.

### Implementation in Solution

The **TranslationService** acts as an adapter between different message formats and domain models:

**File: Ucx.DecisionService.Domain/Services/TranslationService.cs**

```csharp
public class TranslationService : ITranslateService
{
    private const string EpNurseClinicalRoleType = "NURSE";
    private const string EpMdClinicalRoleType = "MD";
    private const string IoClientSystem = "CareCore National";
    private const string EpClientSystem = "eP";

    public UcxNonClinicalDetermination ToNonClinicalMedicalDetermination(
        UCXRequestForServiceSubmitted ucxRequestForServiceSubmitted, 
        XplUpadsReviewCompleted xplUpadsReview)
    {
        var determinations = new Dictionary<string, List<string>>();
        foreach (var determination in xplUpadsReview.ReviewSummary.Determinations)
        {
            if (!determinations.ContainsKey(determination.Key))
                determinations.Add(determination.Key, new List<string>());
            determinations[determination.Key].Add(determination.Value);
        }

        switch (xplUpadsReview.ReviewSummary.ClientSystem)
        {
            case IoClientSystem:
            {
                GetIOStartandExpirationDate(determinations, out var authorizationStartDate,
                    out var requestExpirationDate, ucxRequestForServiceSubmitted);
                var procedure = determinations.ContainsKey(UpdateProcedure)
                    ? GetProcedures(determinations, ucxRequestForServiceSubmitted, UpdateProcedure,
                        authorizationStartDate, requestExpirationDate)
                    : null;

                return new UcxNonClinicalDetermination
                {
                    RequestForServiceKey = ucxRequestForServiceSubmitted.RequestForServiceKey,
                    AuthorizationKey = ucxRequestForServiceSubmitted.AuthorizationKey,
                    RequestForServiceId = xplUpadsReview.ReviewSummary.ContextKey,
                    ReviewType = ReviewType.NonClinicalInitialReview,
                    Procedures = procedure,
                    AuthorizationStartDate = authorizationStartDate,
                    RequestExpirationDate = requestExpirationDate,
                    OriginSystem = ClientSystem.IO.ToString()
                };
            }
            case EpClientSystem:
            {
                GetEPStartandExpirationDate(determinations, out var authorizationStartDate,
                    out var requestExpirationDate, ucxRequestForServiceSubmitted);
                var procedure = determinations.ContainsKey("Procedure")
                    ? GetProcedures(determinations, ucxRequestForServiceSubmitted, "Procedure",
                        authorizationStartDate, requestExpirationDate)
                    : null;

                return new UcxNonClinicalDetermination
                {
                    RequestForServiceKey = ucxRequestForServiceSubmitted.RequestForServiceKey,
                    AuthorizationKey = ucxRequestForServiceSubmitted.AuthorizationKey,
                    RequestForServiceId = xplUpadsReview.ReviewSummary.ContextKey,
                    ReviewType = ReviewType.NonClinicalInitialReview,
                    Procedures = procedure,
                    AuthorizationStartDate = authorizationStartDate,
                    RequestExpirationDate = requestExpirationDate,
                    OriginSystem = ClientSystem.eP.ToString()
                };
            }
        }
    }

    public UcxClinicalDetermination ToClinicalMedicalDetermination(
        UCXRequestForServiceSubmitted uCXRequestForServiceSubmitted,
        XplUpadsReviewCompleted xplUpadsReview)
    {
        // Similar adaptation logic for clinical determinations
    }
}
```

### Benefits
- Bridges incompatible message formats (IO vs eP systems)
- Centralizes transformation logic
- Makes client code independent of source data format
- Easy to extend for new client systems

---

## 3. **Repository Pattern**

### Description
The Repository pattern mediates between the domain and the data mapping layers, acting like an in-memory collection of domain objects.

### Implementation in Solution

**Repository Interface:**
```csharp
// File: Ucx.DecisionService.Domain/Repository/Interface/IDecisionRepository.cs
public interface IDecisionRepository
{
    /// <summary>
    /// Get Determination record by Request For Service Id
    /// </summary>
    Task<DecisionModel> GetByRequestForServiceIdAsync(string requestForServiceId);

    /// <summary>
    /// Insert or Replace existing Record
    /// Request For Service ID is being used because RequestForServiceKey Maybe missing from the event
    /// </summary>
    Task<DecisionModel> UpsertAsync(Func<DecisionModel, DecisionModel> upsertFunc,
        string requestForServiceId);

    Task<DecisionModel> UpdateAsync(Func<DecisionModel, DecisionModel> updateFunc,
        string requestForServiceId);

    Task<DecisionModel> UpdateStatusOfSentMessages(DecisionModel messagesSentModel);
    Task<DecisionModel> Insert(DecisionModel model, string requestForServiceId);
}
```

**Repository Implementation:**
```csharp
// File: Ucx.DecisionService.Domain/Repository/Repo/DecisionRepository.cs
public class DecisionRepository : IDecisionRepository
{
    private readonly ICosmosDbService<DecisionModel> _cosmosDbService;
    private readonly ILogger<DecisionRepository> _logger;

    public DecisionRepository(ICosmosDbService<DecisionModel> cosmosDbService,
        ILogger<DecisionRepository> logger)
    {
        _cosmosDbService = cosmosDbService;
        _logger = logger;
    }

    public async Task<DecisionModel> GetByRequestForServiceIdAsync(string requestForServiceId)
    {
        return await _cosmosDbService.ReadItemAsync(requestForServiceId, requestForServiceId);
    }

    public async Task<DecisionModel> UpsertAsync(Func<DecisionModel, DecisionModel> upsertFunc,
        string requestForServiceId)
    {
        return await _cosmosDbService.UpsertItemAsync(upsertFunc, requestForServiceId, requestForServiceId);
    }

    public async Task<DecisionModel> UpdateAsync(Func<DecisionModel, DecisionModel> updateFunc,
        string requestForServiceId)
    {
        return await _cosmosDbService.UpdateItemAsync(updateFunc, requestForServiceId, requestForServiceId);
    }

    public async Task<DecisionModel> UpdateStatusOfSentMessages(DecisionModel messagesSentModel)
    {
        var statusDictionary = messagesSentModel.XplUpadsReviewCompleteds
            .ToDictionary(x => x.MessageKey, x => x.XplUpadsReviewCompletedStatus);
        
        return await UpdateAsync(record =>
        {
            record.XplUpadsReviewCompleteds
                .Where(xpl => statusDictionary.ContainsKey(xpl.MessageKey))
                .ToList()
                .ForEach(xpl => xpl.XplUpadsReviewCompletedStatus = statusDictionary[xpl.MessageKey]);
            return record;
        }, messagesSentModel.RequestForServiceId);
    }

    public async Task<DecisionModel> Insert(DecisionModel model, string requestForServiceId)
    {
        return await _cosmosDbService.CreateItemAsync(model, requestForServiceId);
    }
}
```

### Benefits
- Abstracts data access logic from business logic
- Centralizes CRUD operations
- Enables easier testing with mock repositories
- Decouples domain models from persistence mechanism
- Supports functional updates with lambda expressions

---

## 4. **Factory Pattern**

### Description
The Factory pattern provides an interface for creating objects without specifying the exact classes to instantiate.

### Implementation in Solution

**Implicit Factory in Configuration:**
```csharp
// File: Ucx.DecisionService.Api/Configuration/ApiServiceConfiguration.cs
public static class ApiServiceConfiguration
{
    public static WebApplicationBuilder RegisterApplicationServices(this WebApplicationBuilder builder)
    {
        // Factory-like registration: Services are created based on type
        builder.Services.RegisterImplementingClasses<ISendIfNeeded<RequestState>>();
        
        CosmosConfiguration.RegisterDependency(builder.Configuration, builder.Services);
        builder.AddMessageHandlers().AddProducers().AddEventSourceRehydrate().AddKafkaDeadLetter();
        ReprocessDeadLetter.RegisterDependencies(builder.Services);
        builder.AddLocking(builder.Configuration.GetValue<string>("RedisConnectionString")!);

        return builder;
    }

    private static WebApplicationBuilder AddMessageHandlers(this WebApplicationBuilder builder)
    {
        builder.Services.AddTransient<IMessageHandler<UCXRequestForServiceSubmitted>, MessageHandler>();
        builder.Services.AddTransient<IMessageHandler<XplUpadsReviewCompleted>, MessageHandler>();
        builder.Services.AddTransient<IMessageHandler<XplUpadsReviewCompletedV2>, MessageHandler>();
        builder.Services.AddTransient<IMessageHandler<XplUpadsReviewCompletedOutageEvent>, MessageHandler>();
        return builder;
    }

    private static WebApplicationBuilder AddProducers(this WebApplicationBuilder builder)
    {
        var clientConfig = GetClientConfig(builder);
        builder.Services.AddMessageSender<Guid, UcxProcedureUpdated>(clientConfig, 
            builder.Configuration.GetValue<string>("Kafka:UcxProcedureUpdatedTopic"));
        builder.Services.AddMessageSender<Guid, UcxAutomatedDecisionMade>(clientConfig, 
            builder.Configuration.GetValue<string>("Kafka:UcxAutomatedDecisionMadeTopic"));
        builder.Services.AddMessageSender<Guid, UcxPhysicianDecisionMade>(clientConfig, 
            builder.Configuration.GetValue<string>("Kafka:UcxPhysicianDecisionMadeTopic"));
        // ... more message senders
        return builder;
    }

    private static ClientConfig GetClientConfig(WebApplicationBuilder builder)
    {
        var clientConfig = builder.Configuration.GetSection("Kafka").Get<ClientConfig>()!;
        if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "local")
        {
            clientConfig.SecurityProtocol = SecurityProtocol.Plaintext;
            return clientConfig;
        }
        clientConfig.SecurityProtocol = SecurityProtocol.SaslSsl;
        clientConfig.ClientId = Dns.GetHostName();
        clientConfig.SaslMechanism = SaslMechanism.ScramSha512;
        return clientConfig;
    }
}
```

**Cosmos Configuration Factory:**
```csharp
// File: Ucx.DecisionService.Domain/Configuration/CosmosConfiguration.cs
public class CosmosConfiguration
{
    public static CosmosConfiguration RegisterDependency(IConfiguration configuration, IServiceCollection services)
    {
        var cosmosConfig = configuration.GetSection(Cosmos).Get<CosmosConfiguration>();
        
        services.AddSingleton(s => 
            InitializeCosmosDbService<DecisionModel>(cosmosConfig, s, cosmosConfig.DeterminationsContainerName));
        services.AddSingleton(s => 
            InitializeCosmosDbServiceAuditLog<AuditLog>(cosmosConfig, s, cosmosConfig.AuditLog.ContainerName));

        return cosmosConfig;
    }

    public static ICosmosDbService<T> InitializeCosmosDbService<T>(
        CosmosConfiguration cosmosConfiguration, IServiceProvider serviceProvider, string containerName) 
        where T : class
    {
        return new CosmosDbService<T>(
            new CosmosClient(cosmosConfiguration.Endpoint, cosmosConfiguration.Key,
                    new CosmosClientOptions
                    {
                        ConnectionMode = ConnectionMode.Gateway,
                        Serializer = new CosmosJsonDotNetSerializer(new JsonSerializerSettings()
                        {
                            Converters = new JsonConverter[] { new StringEnumConverter() }
                        })
                    })
                .GetContainer(cosmosConfiguration.DatabaseName, containerName),
            serviceProvider.GetService<ILogger<CosmosDbService<T>>>());
    }
}
```

### Benefits
- Centralizes object creation logic
- Makes it easy to create different implementations based on configuration
- Supports environment-specific configurations
- Reduces coupling between classes
- Simplifies testing with different factory behaviors

---

## 5. **Template Method Pattern**

### Description
The Template Method pattern defines the skeleton of an algorithm in a method, deferring some steps to subclasses.

### Implementation in Solution

The **MessageHandler** class demonstrates the Template Method pattern by handling different message types with common processing logic:

```csharp
// File: Ucx.DecisionService.Domain/Handlers/MessageHandler.cs
public class MessageHandler : IMessageHandler<UCXRequestForServiceSubmitted>, 
    IMessageHandler<XplUpadsReviewCompleted>, 
    IMessageHandler<XplUpadsReviewCompletedV2>,
    IMessageHandler<XplUpadsReviewCompletedOutageEvent>,
    IMessageHandler<XplUpadsReviewCompletedV2Replay>
{
    private readonly IEventSourceService<RequestState> _eventSourceService;
    private readonly ILogger<MessageHandler> _logger;
    private readonly IStateRepository<ProcedureState> _procedureStateRepository;
    private readonly ILockingService _lockingService;

    public async Task Handle(UCXRequestForServiceSubmitted message, KafkaMetadata kafkaMetadata = null)
    {
        var scope = new Dictionary<string, object>
        {
            {"Class", nameof(MessageHandler)},
            {"Method", "Task Handle(UCXRequestForServiceSubmitted message, KafkaMetadata kafkaMetadata)" }
        };
        using (_logger.BeginScope(scope))
        {
            var stateIds = new StateIds(message.RequestForServiceId, message.RequestForServiceId);
            await using (await _lockingService.LockAsync<RequestState>(message.RequestForServiceId, 
                _options.Value.AcquireLockTimeout, _options.Value.ExpireLockTimeout, default))
            {
                await _eventSourceService.HandleAsync(EventSourceMessageHelper.ConvertToEventSourceMessage(message,
                    message.Sent, message.Version, message.Key.ToString(), stateIds));
            }
        }
    }

    public async Task Handle(XplUpadsReviewCompletedV2 message, KafkaMetadata kafkaMetadata = null)
    {
        // Template Method Structure:
        // 1. Pre-validation
        if (!PreValidate(message))
            return;

        // 2. Payload cleaning
        var cleanedMessage = CleanPayload(message);

        // 3. Date extraction (subclass variation)
        cleanedMessage.AuthorizationStartDate = GetAuthStartDate(cleanedMessage);
        cleanedMessage.AuthorizationEndDate = GetAuthEndDate(cleanedMessage);
        
        // 4. Post-validation
        if (PostCleanValidate(cleanedMessage))
        {
            cleanedMessage.Procedures = await GetProcedures(cleanedMessage);
        }

        // 5. Common processing with locking
        var contextKey = cleanedMessage.ContextKey();
        if (string.IsNullOrWhiteSpace(contextKey)) return;

        var stateIds = new StateIds(contextKey, contextKey);
        await using (await _lockingService.LockAsync<RequestState>(stateIds.Id, 
            _options.Value.AcquireLockTimeout, _options.Value.ExpireLockTimeout, default))
        {
            await _eventSourceService.HandleAsync(EventSourceMessageHelper.ConvertToEventSourceMessage(cleanedMessage,
                kafkaMetadata!.TimeStamp.DateTime, null, cleanedMessage.MessageKey.ToString(), stateIds));
        }
    }

    // Helper methods providing extension points
    private bool PreValidate(object message) { /* ... */ }
    private T CleanPayload<T>(T message) { /* ... */ }
    private bool PostCleanValidate(object message) { /* ... */ }
    private async Task<List<Procedure>> GetProcedures(object message) { /* ... */ }
}
```

### Benefits
- Eliminates code duplication across message handlers
- Defines common algorithm structure while allowing variations
- Makes it easy to add new message types
- Ensures consistent error handling and logging

---

## 6. **Decorator Pattern**

### Description
The Decorator pattern attaches additional responsibilities to an object dynamically, providing a flexible alternative to subclassing.

### Implementation in Solution

Extension methods and configuration decorators add behavior to existing services:

```csharp
// File: Ucx.DecisionService.Domain/Configuration/EventSourceConfiguration.cs
public static class EventSourceConfiguration
{
    public static WebApplicationBuilder AddEventSource(this WebApplicationBuilder builder)
    {
        // Decorating WebApplicationBuilder with additional configuration
        var cosmosConfig = builder.Configuration.GetSection("Cosmos").Get<CosmosSettings>();

        builder.Services.AddEventSource<ProcedureState>();
        builder.Services.AddEventSourceRepositoryCosmos<ProcedureState>(new CosmosSettings
        {
            Endpoint = cosmosConfig.Endpoint,
            Key = cosmosConfig.Key,
            DatabaseName = cosmosConfig.DatabaseName,
            RetryLockLimit = cosmosConfig.RetryLockLimit,
            CosmosClientOptions = cosmosConfig.CosmosClientOptions
        }, new CosmosContainerNames
        {
            State = builder.Configuration.GetValue<string>("Cosmos:ProcedureState:ContainerName")!,
            EventSourceMessages = builder.Configuration.GetValue<string>("Cosmos:ProcedureMessages:ContainerName")!
        });

        builder.Services.AddEventSource<RequestState>();
        builder.Services.AddEventSourceRepositoryCosmos<RequestState>(new CosmosSettings
        {
            Endpoint = cosmosConfig.Endpoint,
            Key = cosmosConfig.Key,
            DatabaseName = cosmosConfig.DatabaseName,
            RetryLockLimit = cosmosConfig.RetryLockLimit,
            CosmosClientOptions = cosmosConfig.CosmosClientOptions
        }, new CosmosContainerNames
        {
            State = builder.Configuration.GetValue<string>("Cosmos:RequestState:ContainerName")!,
            EventSourceMessages = builder.Configuration.GetValue<string>("Cosmos:RequestMessages:ContainerName")!
        });

        // Adding post-processors as decorators
        builder.Services.AddTransient<IPostApplyProcessor<RequestState>, RequestStatePostApplyProcessor>();
        builder.Services.AddTransient<ISortEventSourceMessages, SortEventSourceMessages>();
        builder.Services.AddTransient<IDetermineRehydrateRequired<RequestState>, DetermineRehydrateRequired>();
        
        return builder;
    }
}
```

**Extension Method as Decorator:**
```csharp
// File: Ucx.DecisionService.Api/Configuration/ApiServiceConfiguration.cs
public static WebApplicationBuilder AddKafkaDeadLetter(this WebApplicationBuilder builder)
{
    // Decorates the builder with dead letter functionality
    builder.Services.AddKafkaDeadLetter(new DeadLetterSettings
    {
        CosmosSettings = builder.Configuration.GetSection("Cosmos").Get<DeadLetterCosmosSettings>(),
        EnableDeadLetterReplay = true
    });
    return builder;
}
```

### Benefits
- Adds functionality without modifying original classes
- Enables fluent configuration API
- Supports layering of concerns
- Makes code more readable and maintainable

---

## 7. **Observer Pattern**

### Description
The Observer pattern defines a one-to-many dependency between objects such that when one object changes state, all its dependents are notified automatically.

### Implementation in Solution

The **EventSource** framework with **ISortEventSourceMessages** and **IDetermineRehydrateRequired** implementations:

```csharp
// File: Ucx.DecisionService.Domain/EventSource/SortEventSourceMessages.cs
public class SortEventSourceMessages : ISortEventSourceMessages
{
    private readonly ILogger<SortEventSourceMessages> _logger;

    public SortEventSourceMessages(ILogger<SortEventSourceMessages> logger)
    {
        _logger = logger;
    }

    public async Task<IEnumerable<EventSourceMessage>> SortAsync(
        IEnumerable<EventSourceMessage> messages, 
        CancellationToken cancellationToken = new CancellationToken())
    {
        var eventSourceMessages = messages.ToList();
        _logger.LogDebug("Sorting Messages Based on Type then date sent");
        
        // Observer behavior: react to message events
        var sortedEventSourceMessages = eventSourceMessages
            .OrderBy(x => x.PayloadType != nameof(UCXRequestForServiceSubmitted))
            .ThenBy(y => y.Sent);
        
        return await Task.FromResult(sortedEventSourceMessages);
    }
}
```

```csharp
// File: Ucx.DecisionService.Domain/EventSource/DetermineRehydrateRequired.cs
public class DetermineRehydrateRequired : IDetermineRehydrateRequired<RequestState>
{
    private readonly ILogger<DetermineRehydrateRequired> _logger;

    public async Task<bool> GetAsync(RequestState state, EventSourceMessage message, 
        CancellationToken cancellationToken = new CancellationToken())
    {
        _logger.LogDebug("Determining If Rehydrate is required");
        
        // Observer pattern: respond to state changes
        if (message.PayloadType is nameof(UCXRequestForServiceSubmitted) || 
            message.Sent < state.LastMessageReceivedDate ||
            string.IsNullOrWhiteSpace(state.MedicalDiscipline))
            return await Task.FromResult(true);

        return await Task.FromResult(false);
    }
}
```

**Event-driven Processing Architecture:**
```csharp
// File: Ucx.DecisionService.Domain/Configuration/EventSourceConfiguration.cs
// Multiple observers react to RequestState changes
builder.Services.AddTransient<IPostApplyProcessor<RequestState>, RequestStatePostApplyProcessor>();
builder.Services.AddTransient<ISortEventSourceMessages, SortEventSourceMessages>();
builder.Services.AddTransient<IDetermineRehydrateRequired<RequestState>, DetermineRehydrateRequired>();
```

### Benefits
- Decouples event producers from consumers
- Multiple observers can react to the same state change
- Dynamic subscription/unsubscription of handlers
- Promotes event-driven architecture

---

## 8. **Chain of Responsibility Pattern**

### Description
The Chain of Responsibility pattern avoids coupling the sender of a request to its receiver by giving more than one object a chance to handle the request.

### Implementation in Solution

The message handling pipeline creates a chain of responsibility:

```csharp
// File: Ucx.DecisionService.Domain/Handlers/MessageHandler.cs
public class MessageHandler : 
    IMessageHandler<UCXRequestForServiceSubmitted>, 
    IMessageHandler<XplUpadsReviewCompleted>, 
    IMessageHandler<XplUpadsReviewCompletedV2>,
    IMessageHandler<XplUpadsReviewCompletedOutageEvent>,
    IMessageHandler<XplUpadsReviewCompletedV2Replay>
{
    // Each handler method in the chain passes the request down the line
    public async Task Handle(UCXRequestForServiceSubmitted message, KafkaMetadata kafkaMetadata = null)
    {
        // Step 1: Lock acquisition
        await using (await _lockingService.LockAsync<RequestState>(message.RequestForServiceId, ...))
        {
            // Step 2: Event source handling
            await _eventSourceService.HandleAsync(EventSourceMessageHelper.ConvertToEventSourceMessage(...));
        }
    }

    public async Task Handle(XplUpadsReviewCompletedV2 message, KafkaMetadata kafkaMetadata = null)
    {
        // Step 1: Pre-validation
        if (!PreValidate(message)) return;

        // Step 2: Payload cleaning
        var cleanedMessage = CleanPayload(message);

        // Step 3: Data extraction
        cleanedMessage.AuthorizationStartDate = GetAuthStartDate(cleanedMessage);
        cleanedMessage.AuthorizationEndDate = GetAuthEndDate(cleanedMessage);

        // Step 4: Post-validation
        if (PostCleanValidate(cleanedMessage))
        {
            cleanedMessage.Procedures = await GetProcedures(cleanedMessage);
        }

        // Step 5: Message conversion and handling
        var stateIds = new StateIds(contextKey, contextKey);
        await using (await _lockingService.LockAsync<RequestState>(stateIds.Id, ...))
        {
            await _eventSourceService.HandleAsync(EventSourceMessageHelper.ConvertToEventSourceMessage(...));
        }
    }
}
```

**Kafka Producer Chain:**
```csharp
// File: Ucx.DecisionService.Api/Configuration/ApiServiceConfiguration.cs
// Chain of message producers creating a pipeline
private static WebApplicationBuilder AddProducers(this WebApplicationBuilder builder)
{
    var clientConfig = GetClientConfig(builder);
    
    // Each producer represents a link in the chain
    builder.Services.AddMessageSender<Guid, UcxProcedureUpdated>(clientConfig, topic1);
    builder.Services.AddMessageSender<Guid, UcxAutomatedDecisionMade>(clientConfig, topic2);
    builder.Services.AddMessageSender<Guid, UcxPhysicianDecisionMade>(clientConfig, topic3);
    builder.Services.AddMessageSender<Guid, UcxNurseDecisionMade>(clientConfig, topic4);
    builder.Services.AddMessageSender<Guid, UcxStructuredClinicalReviewDecisionMade>(clientConfig, topic5);
    builder.Services.AddMessageSender<Guid, UcxPostDecisionWebDecisionMade>(clientConfig, topic6);
    builder.Services.AddMessageSender<Guid, UcxNoteCreated>(clientConfig, topic7);
    builder.Services.AddMessageSender<Guid, UCXApprovalScriptCreated>(clientConfig, topic8);
    
    return builder;
}
```

### Benefits
- Allows dynamic composition of handlers
- Separates request sender from receiver
- Enables conditional processing at each step
- Supports middleware-like architecture

---

## 9. **Dependency Injection Pattern**

### Description
Dependency Injection is a pattern that deals with how components get hold of their dependencies. The injected dependencies are made available to a class via constructor injection, property injection, or method injection.

### Implementation in Solution

The solution extensively uses dependency injection throughout:

```csharp
// File: Ucx.DecisionService.Api/Configuration/ApiServiceConfiguration.cs
public static class ApiServiceConfiguration
{
    public static WebApplicationBuilder RegisterApplicationServices(this WebApplicationBuilder builder)
    {
        // DI Registration Examples:
        
        // 1. Service discovery and registration
        builder.Services.RegisterImplementingClasses<ISendIfNeeded<RequestState>>();

        // 2. Cosmos DB Services
        CosmosConfiguration.RegisterDependency(builder.Configuration, builder.Services);

        // 3. Message Handlers
        builder.Services.AddTransient<IMessageHandler<UCXRequestForServiceSubmitted>, MessageHandler>();
        builder.Services.AddTransient<IMessageHandler<XplUpadsReviewCompleted>, MessageHandler>();

        // 4. Event Source processors
        builder.Services.AddTransient<IPostApplyProcessor<RequestState>, RequestStatePostApplyProcessor>();
        builder.Services.AddTransient<ISortEventSourceMessages, SortEventSourceMessages>();

        return builder;
    }
}
```

**Constructor Injection in Services:**
```csharp
// File: Ucx.DecisionService.Domain/Services/SendApprovalScriptIfNeeded.cs
public class SendApprovalScriptIfNeeded: ISendIfNeeded<RequestState>
{
    private readonly ICosmosDbService<AuditLog> _auditLogRepo;
    private readonly ILogger<SendApprovalScriptIfNeeded> _logger;
    private readonly IMessageSenderService<Guid, UCXApprovalScriptCreated> _messageSenderService;

    // Dependencies injected through constructor
    public SendApprovalScriptIfNeeded(
        ILogger<SendApprovalScriptIfNeeded> logger, 
        IMessageSenderService<Guid, UCXApprovalScriptCreated> messageSenderService,
        ICosmosDbService<AuditLog> auditLogRepo)
    {
        _logger = logger;
        _messageSenderService = messageSenderService;
        _auditLogRepo = auditLogRepo;
    }
}
```

**Handler with Multiple Dependencies:**
```csharp
// File: Ucx.DecisionService.Domain/Handlers/MessageHandler.cs
public class MessageHandler : IMessageHandler<UCXRequestForServiceSubmitted>, ...
{
    private readonly IEventSourceService<RequestState> _eventSourceService;
    private readonly ILogger<MessageHandler> _logger;
    private readonly IStateRepository<ProcedureState> _procedureStateRepository;
    private readonly ILockingService _lockingService;
    private readonly IOptions<LockOptions> _options;

    // All dependencies injected
    public MessageHandler(ILogger<MessageHandler> logger, 
        IEventSourceService<RequestState> eventSourceService,
        IStateRepository<ProcedureState> procedureStateRepository, 
        ILockingService lockingService,
        IOptions<LockOptions> options)
    {
        _logger = logger;
        _eventSourceService = eventSourceService;
        _procedureStateRepository = procedureStateRepository;
        _lockingService = lockingService;
        _options = options;
    }
}
```

### Benefits
- Loose coupling between classes
- Easy testing with mock dependencies
- Centralized dependency management
- Supports SOLID principles
- Enables runtime configuration of implementations

---

## Summary Table

| Pattern | Purpose | Implementation | Files |
|---------|---------|-----------------|-------|
| **Strategy** | Encapsulate interchangeable algorithms | Multiple `ISendIfNeeded<RequestState>` implementations | Services/*.cs |
| **Adapter** | Convert incompatible interfaces | TranslationService (IO vs eP format conversion) | TranslationService.cs |
| **Repository** | Mediate between domain and data layers | IDecisionRepository & DecisionRepository | Repository/*.cs |
| **Factory** | Create objects without specifying classes | ApiServiceConfiguration, CosmosConfiguration | Configuration/*.cs |
| **Template Method** | Define algorithm skeleton, defer steps | MessageHandler with various Handle methods | MessageHandler.cs |
| **Decorator** | Add responsibilities dynamically | Extension methods & IPostApplyProcessor | Configuration/*.cs, EventSource/*.cs |
| **Observer** | Notify multiple objects of state changes | ISortEventSourceMessages, IDetermineRehydrateRequired | EventSource/*.cs |
| **Chain of Responsibility** | Pass requests through handler chain | Message handling pipeline | MessageHandler.cs, Handlers/*.cs |
| **Dependency Injection** | Inject dependencies at runtime | Constructor injection throughout | All services |

---

## Architecture Highlights

### Event-Driven Architecture
The solution implements an event-driven microservice pattern with:
- **Kafka** for asynchronous message processing
- **Cosmos DB** for event sourcing and state management
- **Request/Response Pattern** between multiple event systems

### Key Design Principles Applied
1. **Single Responsibility Principle** - Each class has one reason to change
2. **Open/Closed Principle** - Open for extension, closed for modification
3. **Liskov Substitution Principle** - Implementations can be used interchangeably
4. **Interface Segregation Principle** - Specific interfaces for specific needs
5. **Dependency Inversion Principle** - Depend on abstractions, not concretions

### Message Flow
```
Kafka Producer
    ↓
MessageHandler (Chain of Responsibility)
    ↓
EventSourceService (Observer Pattern)
    ├→ SortEventSourceMessages
    ├→ DetermineRehydrateRequired
    └→ IPostApplyProcessor
    ↓
ISendIfNeeded Strategies (Strategy Pattern)
    ├→ SendApprovalScriptIfNeeded
    ├→ SendAutomatedDecisionMadeIfNeeded
    ├→ SendPhysicianDecisionMadeIfNeeded
    └→ (6 more sending strategies)
    ↓
TranslationService (Adapter Pattern)
    ↓
DecisionRepository (Repository Pattern)
    ↓
Cosmos DB
```

---

## Conclusion

The Ucx.DecisionService solution demonstrates a sophisticated application of Gang of Four design patterns. The combination of these patterns creates a flexible, maintainable, and scalable microservice architecture that effectively handles complex business logic for medical decision processing. The careful use of interfaces, dependency injection, and composition over inheritance follows SOLID principles and enables the system to evolve with changing business requirements.
