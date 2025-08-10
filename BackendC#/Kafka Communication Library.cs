using System.Collections.Generic;
using evicore.gravity.common.Interface;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace evicore.gravity.common.controllers;

[ApiController]
[Route("api/DeadLetterReplay/all")]
public class DeadLetterReplayController : ControllerBase
{
    private readonly ILogger<DeadLetterReplayController> _logger;
    private readonly IDeadLetterReplayService _deadLetterReplayService;
    private readonly IBackgroundWorkerQueue _backgroundWorkerQueue;

    public DeadLetterReplayController(ILogger<DeadLetterReplayController> logger, IDeadLetterReplayService deadLetterReplayService,
        IBackgroundWorkerQueue backgroundWorkerQueue)
    {
        _logger = logger;
        _deadLetterReplayService = deadLetterReplayService;
        _backgroundWorkerQueue = backgroundWorkerQueue;
    }

    [HttpPost]
    public ActionResult ReplayDeadLetterAll(int batchSize = 500, int delayMs = 0)
    {
        var scope = new Dictionary<string, object>
        {
            { "Class", nameof(DeadLetterReplayController) },
            { "ApiPath", "api/deadletterreplay/all" },
            { "Method", "ActionResult ReplayDeadLetterAll()" }
        };
        using (_logger.BeginScope(scope))
        {
            _logger.LogInformation("Start all records for replay");
            _backgroundWorkerQueue.QueueBackgroundWorkItem(async token =>
            {
                await _deadLetterReplayService.Reprocess(batchSize, delayMs);
                _logger.LogInformation("records replay completed");
            });

            return Accepted();
        }
    }
}
using Confluent.Kafka;
using evicore.gravity.common.Service;
using Microsoft.Extensions.DependencyInjection;
using ucx.messages.@base;

namespace evicore.gravity.common.ExtensionMethods;

public static class MessageSenderServiceExtensions
{
    public static void AddMessageSender<TKey, TValue>(this IServiceCollection services, ClientConfig clientConfig,
        string topic) where TValue : Message, new()
    {
        MessageSenderService<TKey, TValue>.AddMessageSender(services, clientConfig, topic);
    }
}
using System;
using evicore.gravity.common.Configuration;
using evicore.gravity.common.Service;
using Microsoft.Extensions.DependencyInjection;

namespace evicore.gravity.common.ExtensionMethods;

public static class MessageConsumerServiceExtension
{
    public static void AddMessageConsumer<T>(this IServiceCollection services, MessageConsumerServiceConfig config) where T : new()
    {
        MessageConsumerService<T>.AddMessageConsumer(services, config);
    }

    public static void AddMessageConsumer<T>(this IServiceCollection services, Action<MessageConsumerServiceConfig> messageConsumerServiceConfig) where T : new()
    {
        MessageConsumerService<T>.AddMessageConsumer(services, messageConsumerServiceConfig);
    }
}

using Ardalis.GuardClauses;
using evicore.gravity.common.Interface;
using evicore.gravity.common.Service;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using ucx.messages.@base.Dtos;

namespace evicore.gravity.common.ExtensionMethods;

public static class AddKafkaDeadLetterExtension
{
    private const string FrameworkName = "evicore.gravity.common";

    /// <summary>
    /// Register CosmosClient for Dead letter
    /// Register IDeadLetterServices
    /// </summary>
    /// <param name="services"></param>
    /// <param name="config">CosmosSettings</param>
    /// <returns></returns>
    public static IServiceCollection AddKafkaDeadLetter(this IServiceCollection services, DeadLetterCosmosSettings config) =>
        services.AddKafkaDeadLetter(new DeadLetterSettings() { CosmosSettings = config });

    /// <summary>
    /// Register CosmosClient for DeadLetter
    /// Register DeadLetter Replay
    /// </summary>
    /// <param name="services"></param>
    /// <param name="config">DeadLetterSettings</param>
    /// <returns></returns>
    public static IServiceCollection AddKafkaDeadLetter(this IServiceCollection services, DeadLetterSettings config)
    {
        //Validate CosmosEndpoints
        ValidateConfigRequiredFields(config.CosmosSettings);
        //Add CosmosClient
        services.AddTransient(s => DeadLetterService.InitializeCosmosDbService<DeadLetter>(config.CosmosSettings, s));
        //DeadLetter Service
        services.AddTransient<IDeadLetterService, DeadLetterService>();
        //Optionally load DeadLetterReplayService
        if (config.EnableDeadLetterReplay)
        {
            LongRunningService.RegisterDependency(services);
            BackgroundWorkerQueue.RegisterDependency(services);
            services.AddTransient<IDeadLetterReplayService, DeadLetterReplayService>();
            services.EnableDeadLetterReplayEndpoints(true);
        }
        else
            services.EnableDeadLetterReplayEndpoints();

        return services;
    }

    private static void EnableDeadLetterReplayEndpoints(this IServiceCollection serviceCollection, bool isEndpointEnabled = false)
    {
        serviceCollection.AddMvcCore().ConfigureApplicationPartManager(options =>
        {
            var appPart = options.ApplicationParts.SingleOrDefault(s => s.Name == FrameworkName);
            if (appPart != null && !isEndpointEnabled)
                options.ApplicationParts.Remove(appPart);
        });
    }

    /// <summary>
    /// Validate CosmosSettings All fields are required
    /// </summary>
    /// <param name="cosmosSettings"></param>
    private static void ValidateConfigRequiredFields(DeadLetterCosmosSettings cosmosSettings)
    {
        //Kafka Required Fields
        Guard.Against.NullOrWhiteSpace(cosmosSettings.Endpoint, nameof(cosmosSettings.Endpoint), $"AddKafkaDeadLetter: {nameof(cosmosSettings.Endpoint)} is null");
        Guard.Against.NullOrWhiteSpace(cosmosSettings.Key, nameof(cosmosSettings.Key), $"AddKafkaDeadLetter: {nameof(cosmosSettings.Key)} is null");
        Guard.Against.NullOrWhiteSpace(cosmosSettings.DeadLetterContainerName, nameof(cosmosSettings.DeadLetterContainerName),
            $"AddKafkaDeadLetter: {nameof(cosmosSettings.DeadLetterContainerName)} is null");
        Guard.Against.NullOrWhiteSpace(cosmosSettings.DatabaseName, nameof(cosmosSettings.DatabaseName), $"AddKafkaDeadLetter: {nameof(cosmosSettings.DatabaseName)} is null");
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using evicore.gravity.common.Dtos;
using evicore.gravity.common.Interface;
using evicore.gravity.common.Serializers;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using ucx.messages.@base.Dtos;

namespace evicore.gravity.common.Service;

public class DeadLetterService : IDeadLetterService
{
    private readonly ICosmosDbService<DeadLetter> _deadLetterCosmosDbService;
    private readonly ILogger _logger;
    private readonly Dictionary<string, object> _scope = new();

    public DeadLetterService(ICosmosDbService<DeadLetter> deadLetterCosmosDbService, ILogger<DeadLetterService> logger)
    {
        _scope["Class"] = nameof(DeadLetterService);
        _deadLetterCosmosDbService = deadLetterCosmosDbService;
        _logger = logger;
    }

    public async Task Handle(DeadLetter deadLetter)
    {
        var scope = new Dictionary<string, object>(_scope)
        {
            { "Method", "Handle(DeadLetter deadLetter)" }
        };
        using (_logger.BeginScope(scope))
        {
            scope["DeadLetterId"] = deadLetter.Id;
            _logger.LogWarning("A dead letter has occurred");
            await _deadLetterCosmosDbService.CreateItemAsync(deadLetter, deadLetter.Id.ToString());
        }
    }

    public async Task Handle<T>(T message, string description, KafkaMetadata kafkaMetadata, string messageName, string messageVersion) where T : new()
    {
        await Handle(new DeadLetter
        {
            Id = Guid.NewGuid(),
            EventVersion = messageVersion,
            EventName = messageName,
            TopicName = kafkaMetadata?.Topic,
            Message = message is null ? null : JsonConvert.SerializeObject(message),
            Offset = kafkaMetadata?.Offset,
            Partition = kafkaMetadata?.Partition,
            Description = description
        });
    }

    /// <summary>
    ///     Register DeadLetter CosmosClient
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="cosmosSettings">Cosmos Settings where dead letter will be stored</param>
    /// <param name="serviceProvider"></param>
    /// <returns></returns>
    public static ICosmosDbService<T> InitializeCosmosDbService<T>(DeadLetterCosmosSettings cosmosSettings, IServiceProvider serviceProvider) where T : class
    {
        return new CosmosDbService<T>(
            new CosmosClient(cosmosSettings.Endpoint, cosmosSettings.Key,
                    new CosmosClientOptions
                    {
                        ConnectionMode = ConnectionMode.Gateway,
                        Serializer = new CosmosJsonDotNetSerializer(new JsonSerializerSettings
                        {
                            Converters = new JsonConverter[]
                            {
                                new StringEnumConverter()
                            }
                        })
                    })
                .GetContainer(cosmosSettings.DatabaseName, cosmosSettings.DeadLetterContainerName),
            serviceProvider.GetService<ILogger<CosmosDbService<T>>>());
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using evicore.gravity.common.Dtos;
using evicore.gravity.common.Interface;
using Microsoft.Extensions.Logging;
using ucx.messages.@base.Dtos;

namespace evicore.gravity.common.Service;

public class DeadLetterReplayService : IDeadLetterReplayService
{
    private readonly ILogger<DeadLetterReplayService> _logger;
    private readonly IReprocessDeadLetter _reprocessDeadLetter;
    private readonly ICosmosDbService<DeadLetter> _cosmosDbService;
    private readonly IDictionary<string, object> _scope;

    public DeadLetterReplayService(ILogger<DeadLetterReplayService> logger, ICosmosDbService<DeadLetter> cosmosDbService,
        IReprocessDeadLetter reprocessDeadLetter = null)
    {
        _logger = logger;
        _reprocessDeadLetter = reprocessDeadLetter ?? new NoOpReprocessDeadLetter();
        _cosmosDbService = cosmosDbService;
        _scope = new Dictionary<string, object>
        {
            { "Class", nameof(DeadLetterReplayService) }
        };
    }

    public async Task Reprocess(int batchSize = 500, int delayMs = 0)
    {
        using (_logger.BeginScope(_scope))
        {
            _logger.LogInformation("DeadLetter Reprocess Started");
            using var feed = _cosmosDbService.ReadItems<IdOnlyModel>("SELECT c.id FROM c");
            var ids = new List<string>();
            while (feed.HasMoreResults)
            {
                var response = await feed.ReadNextAsync();
                _logger.LogDebug("DeadLetter RU Cost {RequestCharge}", response.RequestCharge);
                ids.AddRange(response.Select(item => item.id));
            }

            _logger.LogInformation("DeadLetter Reprocess select all ids Completed with {IdsCount} records", ids.Count);

            var count = 0;
            while (count < ids.Count)
            {
                var tasks = new List<Task>();
                for (var i = 0; i < batchSize; i++)
                {
                    if (count >= ids.Count)
                        break;
                    tasks.Add(ProcessDeadLetter(ids[count]));
                    count++;
                }

                await Task.WhenAll(tasks);
                if (count < ids.Count)
                    await Task.Delay(TimeSpan.FromMilliseconds(delayMs));
            }

            _logger.LogInformation("DeadLetter Reprocess Completed");
        }
    }

    private async Task ProcessDeadLetter(string id)
    {
        var scope = new Dictionary<string, object>(_scope)
        {
            ["DeadLetterId"] = id
        };
        using (_logger.BeginScope(scope))
        {
            var deadLetter = await _cosmosDbService.ReadItemAsync(id, id);
            scope["EventName"] = deadLetter.EventName;
            scope["TopicName"] = deadLetter.TopicName;
            scope["Offset"] = deadLetter.Offset;
            scope["Partition"] = deadLetter.Partition;
            try
            {
                await _reprocessDeadLetter.Handle(deadLetter);
                await _cosmosDbService.DeleteItemAsync(id, id);
                _logger.LogInformation("DeadLetter Reprocess deleted {Id}", id);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "DeadLetter Reprocess Failed to Process Record {Id}", id);
                await _cosmosDbService.UpdateItemAsync(record =>
                {
                    record.NumberOfRetries++;
                    return record;
                }, id, id);
            }
        }
    }
}

using System.Threading.Tasks;
using ucx.messages.@base;

namespace evicore.gravity.common.Interface;

public interface IMessageSender
{
    /// <summary>
    ///     send command message
    /// </summary>
    /// <typeparam name="TMessage"></typeparam>
    /// <param name="message"></param>
    /// <returns></returns>
    Task SendAsync<TMessage>(TMessage message) where TMessage : Message, new();
}

public interface IMessageSender<TKey, TMessage>
{
    /// <summary>
    ///     send command message
    /// </summary>
    /// <typeparam name="TMessage"></typeparam>
    /// <param name="message"></param>
    /// <returns></returns>
    Task SendAsync(TMessage message);
}

using System;
using System.Threading.Tasks;
using ucx.messages.@base;

// ReSharper disable UnusedMemberInSuper.Global
// ReSharper disable UnusedMember.Global

namespace evicore.gravity.common.Interface;

public interface IMessageConsumer<T> where T : new()
{
    Task Consume<TMessage>(string messageName, string version);
    Task ConsumeAll<TMessage>();
    Task ConsumeGravityCommonMessage<TMessage>(string messageName, string version);
    Task ConsumeAllNoMessagePayload<TMessage>();
    void Cancel();
    event EventHandler<MessagePayload<T>> ValidMessageReceived;
}

using System.Threading.Tasks;
using evicore.gravity.common.Dtos;

namespace evicore.gravity.common.Interface;

public interface IMessageHandler<in T>
{
    Task Handle(T message, KafkaMetadata kafkaMetadata = null);
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using evicore.gravity.common.Interface;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ucx.messages.@base;

namespace evicore.gravity.common.Service;

/// <summary>
///     This will allow you to consume kafka messages.
/// </summary>
public class KafkaMessageConsumer<T> : IMessageConsumer<T> where T : new()
{
    private readonly CancellationTokenSource _cancellationToken;
    private readonly IConsumer<string, string> _consumer;
    private readonly ConsumerConfig _consumerConfig;
    private readonly TimeSpan _consumerDelay;
    private readonly string _consumerGroupId;
    private readonly string _executingAssemblyName = "evicore.gravity.common";
    private readonly long? _finalOffset;
    private readonly ILogger _logger;

    protected readonly Dictionary<string, object> _scope = new();
    private readonly TelemetryClient _telemetryClient;
    private readonly string _topic;
    private readonly TimeSpan _lagCheckTimeDefault = TimeSpan.FromMinutes(30);

    public KafkaMessageConsumer(ILogger<KafkaMessageConsumer<T>> logger, TelemetryClient telemetryClient,
        IConsumer<string, string> consumer, string topic, TimeSpan? lagCheckTime = null, long? finalOffset = null, TimeSpan? consumerDelay = null)
    {
        _scope.Add("Class", nameof(KafkaMessageConsumer<T>));
        _logger = logger;
        _topic = topic;
        _consumer = consumer;
        _telemetryClient = telemetryClient;
        _finalOffset = finalOffset;

        _cancellationToken = new CancellationTokenSource();

        _executingAssemblyName = Assembly.GetExecutingAssembly().FullName;

        _logger.LogDebug("Constructing KafkaMessageConsumer. Details: Topic: {Topic}, ", topic);

        _consumerDelay = TimeSpan.FromMilliseconds(0);
        if (consumerDelay.HasValue)
            _consumerDelay = consumerDelay.Value;
        if (_consumerConfig != null)
        {
            RegisterCumulativeLagsMetricsSender(_consumerConfig, lagCheckTime ?? _lagCheckTimeDefault);
        }
    }

    /// <summary>
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="topic"></param>
    /// <param name="groupId">This will be modified to add on the class name inside the constructor</param>
    /// <param name="brokers">This can also be referred to as Bootstrap Servers</param>
    /// <param name="autoOffset"></param>
    /// <param name="sslCaLocation"></param>
    /// <param name="allowTopicAutoCreate"></param>
    /// <param name="securityProtocol"></param>
    /// <param name="saslUsername"></param>
    /// <param name="saslPassword"></param>
    /// <param name="saslMechanism"></param>
    // ReSharper disable once UnusedMember.Global
    public KafkaMessageConsumer(ILogger<KafkaMessageConsumer<T>> logger, TelemetryClient telemetryClient, string topic,
        string groupId, IEnumerable<string> brokers, AutoOffsetReset autoOffset,
        bool allowTopicAutoCreate, TimeSpan? lagCheckTime = null, SecurityProtocol securityProtocol = SecurityProtocol.SaslSsl,
        string sslCaLocation = "",
        SaslMechanism saslMechanism = SaslMechanism.ScramSha256, string saslUsername = "", string saslPassword = "",
        string sslKeystoreLocation = "",
        string sslKeystorePassword = "", TimeSpan? consumerDelay = null)
        : this(logger, telemetryClient, new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            GroupId = groupId,
            BootstrapServers = brokers.Aggregate((x, y) => x + ", " + y),
            AutoOffsetReset = autoOffset,
            AllowAutoCreateTopics = allowTopicAutoCreate,
            SecurityProtocol = securityProtocol,
            SslCaLocation = sslCaLocation,
            SaslMechanism = saslMechanism,
            SslKeystoreLocation = sslKeystoreLocation,
            SslKeystorePassword = sslKeystorePassword,
            SaslUsername = saslUsername,
            SaslPassword = saslPassword
        }).Build(), topic, lagCheckTime, consumerDelay: consumerDelay)
    {
        _consumerGroupId = groupId;
        _consumerConfig = new ConsumerConfig
        {
            GroupId = groupId,
            BootstrapServers = brokers.Aggregate((x, y) => x + ", " + y),
            AutoOffsetReset = autoOffset,
            AllowAutoCreateTopics = allowTopicAutoCreate,
            SecurityProtocol = securityProtocol,
            SslCaLocation = sslCaLocation,
            SaslMechanism = saslMechanism,
            SslKeystoreLocation = sslKeystoreLocation,
            SslKeystorePassword = sslKeystorePassword,
            SaslUsername = saslUsername,
            SaslPassword = saslPassword
        };
    }

    /// <summary>
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="topic"></param>
    /// <param name="groupId">This will be modified to add on the class name inside the constructor</param>
    /// <param name="bootstrapServers"></param>
    /// <param name="autoOffset"></param>
    /// <param name="sslCertificateLocation"></param>
    /// <param name="sslCaLocation"></param>
    /// <param name="sslKeyLocation"></param>
    /// <param name="sslKeyPassword"></param>
    /// <param name="securityProtocol"></param>
    // ReSharper disable once UnusedMember.Global
    public KafkaMessageConsumer(ILogger<KafkaMessageConsumer<T>> logger, TelemetryClient telemetryClient, string topic,
        string groupId, IEnumerable<string> bootstrapServers, AutoOffsetReset autoOffset, TimeSpan? lagCheckTime,
        string sslCertificateLocation = "", string sslCaLocation = "", string sslKeyLocation = "",
        string sslKeyPassword = "",
        SecurityProtocol securityProtocol = SecurityProtocol.Plaintext, TimeSpan? consumerDelay = null)
        : this(logger, telemetryClient, new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            GroupId = groupId,
            BootstrapServers = bootstrapServers.Aggregate((x, y) => x + ", " + y),
            AutoOffsetReset = autoOffset,
            SslCertificateLocation = sslCertificateLocation,
            SslCaLocation = sslCaLocation,
            SslKeyLocation = sslKeyLocation,
            SslKeyPassword = sslKeyPassword,
            SecurityProtocol = securityProtocol
        }).Build(), topic, lagCheckTime, consumerDelay: consumerDelay)
    {
        _consumerGroupId = groupId;
        _consumerConfig = new ConsumerConfig
        {
            GroupId = groupId,
            BootstrapServers = bootstrapServers.Aggregate((x, y) => x + ", " + y),
            AutoOffsetReset = autoOffset,
            SslCertificateLocation = sslCertificateLocation,
            SslCaLocation = sslCaLocation,
            SslKeyLocation = sslKeyLocation,
            SslKeyPassword = sslKeyPassword,
            SecurityProtocol = securityProtocol
        };
    }

    public KafkaMessageConsumer(ILogger<KafkaMessageConsumer<T>> logger, TelemetryClient telemetryClient,
        ConsumerConfig config, string topic, TimeSpan? lagCheckTime = null, long? finalOffset = null, bool createGroupWithMessageName = true, TimeSpan? consumerDelay = null)
    {
        _logger = logger;
        _topic = topic;
        config.GroupId = createGroupWithMessageName ? $"{config.GroupId}-{typeof(T).Name}" : $"{config.GroupId}";
        _consumerConfig = config;
        _consumer = new ConsumerBuilder<string, string>(config).Build();
        _finalOffset = finalOffset;

        _cancellationToken = new CancellationTokenSource();

        _consumerGroupId = config.GroupId;
        _telemetryClient = telemetryClient;
        _consumerDelay = TimeSpan.FromMilliseconds(0);
        if (consumerDelay.HasValue)
            _consumerDelay = consumerDelay.Value;
        if (_consumerConfig != null)
        {
            RegisterCumulativeLagsMetricsSender(_consumerConfig, lagCheckTime ?? _lagCheckTimeDefault);
        }
    }

    public event EventHandler<MessagePayload<T>> ValidMessageReceived;

    /// <summary>
    ///     This is for consuming a message of any type not expecting a message payload envelope, BUT, it must have a Name and
    ///     a Version
    /// </summary>
    /// <typeparam name="TMessage"></typeparam>
    /// <param name="messageName"></param>
    /// <param name="version"></param>
    /// <returns></returns>
    public async Task ConsumeGravityCommonMessage<TMessage>(string messageName, string version)
    {
        await Task.Run(() =>
        {
            var scope = new Dictionary<string, object>(_scope)
            {
                ["Method"] = "ConsumeGravityCommonMessage<TMessage>",
                ["EventName"] = messageName,
                ["EventVersion"] = version,
                ["Topic"] = _topic
            };
            _consumer.Subscribe(_topic);
            _logger.LogDebug("Consumer subscribed to topic: {Topic}", _topic);
            var messageString = "";

            var offset = 0L;
            do
            {
                try
                {
                    // This allows us to set an artificial delay if we need to.
                    Thread.Sleep(_consumerDelay);
                    var consumeResult = _consumer.Consume(_cancellationToken.Token);
                    messageString = consumeResult.Message.Value;
                    var kafkaKey = consumeResult.Message.Key;
                    offset = consumeResult.Offset.Value;
                    var partition = consumeResult.Partition.Value;
                    dynamic message;
                    var lag = SendLagMetric(consumeResult);
                    scope["Offset"] = offset;
                    scope["Lag"] = lag;
                    scope["Partition"] = partition;
                    using (_logger.BeginScope(scope))
                    {
                        try
                        {
                            message = JsonConvert.DeserializeObject<T>(consumeResult.Message.Value);
                        }
                        catch (Exception ex)
                        {
                            LogError(ex, offset, partition);
                            continue;
                        }

                        if (!string.Equals(message?.Name, messageName, StringComparison.InvariantCultureIgnoreCase)
                            || !string.Equals(message?.Version, version, StringComparison.InvariantCultureIgnoreCase))
                            continue;

                        _logger.LogDebug(
                            "Message on topic {Topic} received from {Offset}", _topic, consumeResult.TopicPartitionOffset);
                        // wrapping this in a message payload so we don't have to have 2 different event handlers.
                        var payload = new MessagePayload<T>
                        {
                            Version = message.Version,
                            Payload = JsonConvert.SerializeObject(message),
                            KafkaKey = kafkaKey,
                            Name = message.Name,
                            Lag = lag,
                            Offset = offset,
                            Partition = partition,
                            TopicName = _topic
                        };
                        payload.Sent = ProcessSent(payload, consumeResult);
                        ValidMessageReceived?.Invoke(this, payload);
                    }
                }
                catch (Exception ex)
                {
                    LogError(ex, offset);
                }
            } while (_finalOffset == null || offset <= _finalOffset.Value);

            _consumer.Close();
        }, _cancellationToken.Token);
    }

    public async Task Consume<TMessage>(string messageName, string version)
    {
        await Task.Run(() =>
        {
            var scope = new Dictionary<string, object>(_scope)
            {
                ["Method"] = "Consume<TMessage>",
                ["Topic"] = _topic
            };

            using (_logger.BeginScope(scope))
            {
                _consumer.Subscribe(_topic);
                _logger.LogDebug("Consumer subscribed to topic: {Topic}", _topic);
                var messageString = "";

                var offset = 0L;
                do
                {
                    try
                    {
                        // This allows us to set an artificial delay if we need to.
                        Thread.Sleep(_consumerDelay);
                        var consumeResult = _consumer.Consume(_cancellationToken.Token);
                        var lag = SendLagMetric(consumeResult);
                        messageString = consumeResult.Message.Value;
                        var kafkaKey = consumeResult.Message.Key;
                        offset = consumeResult.Offset.Value;
                        var partition = consumeResult.Partition.Value;
                        MessagePayload<T> message;
                        scope["Offset"] = offset;
                        scope["Lag"] = lag;
                        scope["Partition"] = partition;
                        using (_logger.BeginScope(scope))
                        {
                            try
                            {
                                message = JsonConvert.DeserializeObject<MessagePayload<T>>(consumeResult.Message.Value);
                                message.Offset = offset;
                                message.KafkaKey = kafkaKey;
                                message.Payload ??= consumeResult.Message.Value;
                                message.Lag = lag;
                                message.Partition = partition;
                                message.TopicName = _topic;
                                message.Sent = ProcessSent(message, consumeResult);
                            }
                            catch (Exception ex)
                            {
                                LogError(ex, offset, partition);
                                continue;
                            }

                            if (!string.Equals(message?.Name, messageName, StringComparison.InvariantCultureIgnoreCase)
                                || !string.Equals(message?.Version, version, StringComparison.InvariantCultureIgnoreCase))
                                continue;

                            _logger.LogDebug(
                                "Message on topic {Topic} received from {Offset}", _topic, consumeResult.TopicPartitionOffset);
                            ValidMessageReceived?.Invoke(this, message);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogError(ex, offset);
                    }
                } while (_finalOffset == null || offset <= _finalOffset.Value);
            }

            _consumer.Close();
        }, _cancellationToken.Token);
    }

    public async Task ConsumeAll<TMessage>()
    {
        await Task.Run(() =>
        {
            var scope = new Dictionary<string, object>(_scope)
            {
                ["Method"] = "ConsumeAll<TMessage>",
                ["Topic"] = _topic
            };

            _consumer.Subscribe(_topic);
            _logger.LogDebug("Consumer subscribed to topic: {Topic}", _topic);
            var messageString = "";

            var offset = 0L;
            do
            {
                try
                {
                    // This allows us to set an artificial delay if we need to.
                    Thread.Sleep(_consumerDelay);
                    var consumeResult = _consumer.Consume(_cancellationToken.Token);
                    var lag = SendLagMetric(consumeResult);
                    messageString = consumeResult.Message.Value;
                    var kafkaKey = consumeResult.Message.Key;
                    offset = consumeResult.Offset.Value;
                    var partition = consumeResult.Partition.Value;
                    MessagePayload<T> message;
                    scope["Offset"] = offset;
                    scope["Lag"] = lag;
                    scope["Partition"] = partition;
                    using (_logger.BeginScope(scope))
                    {
                        try
                        {
                            message = JsonConvert.DeserializeObject<MessagePayload<T>>(consumeResult.Message.Value);
                            message.Offset = offset;
                            message.KafkaKey = kafkaKey;
                            message.Payload ??= consumeResult.Message.Value;
                            message.Lag = lag;
                            message.Partition = partition;
                            message.TopicName = _topic;
                            message.Sent = ProcessSent(message, consumeResult);
                        }
                        catch (Exception ex)
                        {
                            LogError(ex, offset, partition);
                            continue;
                        }

                        _logger.LogDebug(
                            "Message on topic {Topic} received from {Offset}", _topic, consumeResult.TopicPartitionOffset);
                        ValidMessageReceived?.Invoke(this, message);
                    }
                }
                catch (Exception ex)
                {
                    LogError(ex, offset);
                }
            } while (_finalOffset == null || offset <= _finalOffset.Value);

            _consumer.Close();
        }, _cancellationToken.Token);
    }

    public void Cancel()
    {
        _cancellationToken.Cancel();
    }

    public async Task ConsumeAllNoMessagePayload<TMessage>()
    {
        await Task.Run(() =>
        {
            var scope = new Dictionary<string, object>(_scope)
            {
                ["Method"] = "ConsumeAllNoMessagePayload<TMessage>",
                ["Topic"] = _topic
            };

            _consumer.Subscribe(_topic);
            _logger.LogDebug("Consumer subscribed to topic: {Topic}", _topic);
            var messageString = "";

            var offset = 0L;
            do
            {
                try
                {
                    // This allows us to set an artificial delay if we need to.
                    Thread.Sleep(_consumerDelay);
                    var consumeResult = _consumer.Consume(_cancellationToken.Token);
                    var lag = SendLagMetric(consumeResult);
                    messageString = consumeResult.Message.Value;
                    var kafkaKey = consumeResult.Message.Key;
                    offset = consumeResult.Offset.Value;
                    var partition = consumeResult.Partition.Value;
                    scope["Offset"] = offset;
                    scope["Lag"] = lag;
                    scope["Partition"] = partition;
                    using (_logger.BeginScope(scope))
                    {
                        var message = new MessagePayload<T>();
                        try
                        {
                            message.Offset = offset;
                            message.KafkaKey = kafkaKey;
                            message.Payload ??= consumeResult.Message.Value;
                            message.Lag = lag;
                            message.Partition = partition;
                            message.TopicName = _topic;
                            message.Sent = ProcessSent(message, consumeResult);
                        }
                        catch (Exception ex)
                        {
                            LogError(ex, offset, partition);
                            continue;
                        }

                        _logger.LogDebug(
                            "Message on topic {Topic} received from {Offset}", _topic, consumeResult.TopicPartitionOffset);
                        ValidMessageReceived?.Invoke(this, message);
                    }
                }
                catch (Exception ex)
                {
                    LogError(ex, offset);
                }
            } while (_finalOffset == null || offset <= _finalOffset.Value);

            _consumer.Close();
        }, _cancellationToken.Token);
    }

    private void LogError(Exception ex, long offset, int? partition = null)
    {
        var scope = new Dictionary<string, object>(_scope)
        {
            ["Exception"] = ex.ToString(),
            ["Offset"] = offset,
            ["Topic"] = _topic
        };
        if (partition != null)
        {
            scope.Add("Partition", partition);
        }

        using (_logger.BeginScope(scope))
        {
            _logger.LogError(ex, "An error has occured");
        }
    }

    public void RegisterCumulativeLagsMetricsSender(ConsumerConfig consumerConfig, TimeSpan lagCheckTime)
    {
        Task.Run(async () => { await SendCumulativeLagMetrics(consumerConfig, lagCheckTime); }, _cancellationToken.Token);
    }

    private async Task SendCumulativeLagMetrics(ConsumerConfig consumerConfig, TimeSpan lagCheckTime)
    {
        while (!_cancellationToken.IsCancellationRequested)
        {
            using var adminClient = new AdminClientBuilder(consumerConfig).Build();
            using var consumer = new ConsumerBuilder<Ignore, string>(consumerConfig).Build();

            IEnumerable<TopicPartition> topics;

            try
            {
                var result = await adminClient.DescribeConsumerGroupsAsync(new[] { consumerConfig.GroupId }, new DescribeConsumerGroupsOptions());
                topics = result.ConsumerGroupDescriptions.SelectMany(desc =>
                        desc.Members.Select(member => member.Assignment.TopicPartitions).SelectMany(partitions => partitions)).Distinct()
                    .ToList();
            }
            catch
            {
                _logger.LogError("Could not retrieve consumer groups");
                return;
            }

            consumer.Assign(topics);

            List<TopicPartitionOffset> topicPartitionOffsets;
            try
            {
                topicPartitionOffsets = consumer.Committed(topics, TimeSpan.FromMinutes(5));
            }
            catch (Exception e)
            {
                _logger.LogError("Could not retrieve topic partition offsets");
                return;
            }

            long lag = 0;
            foreach (var topicPartitionOffset in topicPartitionOffsets)
            {
                try
                {
                    var w = consumer.QueryWatermarkOffsets(topicPartitionOffset.TopicPartition, TimeSpan.FromSeconds(40));
                    var committed = topicPartitionOffset.Offset.Value;
                    var logEndOffset = w.High.Value;
                    _logger.LogDebug(
                        "ConsumerGroupId: {ConsumerGroupId}, Topic: {Topic}, Partition: {Partition}, Assembly: {ExecutingAssemblyName}, Offset:{Offset}, Lag:{Lag}",
                        _consumerGroupId, topicPartitionOffset.Topic, topicPartitionOffset.Partition.Value, _executingAssemblyName,
                        logEndOffset, logEndOffset - committed);
                    lag += logEndOffset - committed;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error retrieving lag for {Topic} on partition {Partition}", topicPartitionOffset.Topic, topicPartitionOffset.Partition.Value);
                }
            }

            _logger.LogDebug(
                "Total lag for ConsumerGroupId: {ConsumerGroupId}, Lag:{Lag}",
                _consumerGroupId, lag);
            _telemetryClient
                ?.GetMetric($"CumulativeLag:{_consumerGroupId}")
                .TrackValue(lag);
            await Task.Delay(lagCheckTime, _cancellationToken.Token);
        }
    }

    private long SendLagMetric(ConsumeResult<string, string> consumeResult)
    {
        var watermarks = _consumer.GetWatermarkOffsets(consumeResult.TopicPartition);

        if (watermarks == null)
            return 0;

        var lag = watermarks.High.Value - consumeResult.TopicPartitionOffset.Offset.Value - 1;
        _logger.LogDebug(
            "ConsumerGroupId: {ConsumerGroupId}, Topic: {Topic}, Partition: {Partition}, Assembly: {ExecutingAssemblyName}, Offset:{Offset}, Lag:{lag}",
            _consumerGroupId, consumeResult.Topic, consumeResult.TopicPartition.Partition.Value, _executingAssemblyName,
            consumeResult.Offset.Value, lag);
        _telemetryClient
            ?.GetMetric(
                $"Lag:{_consumerGroupId}:{consumeResult.Topic}:{consumeResult.TopicPartition.Partition.Value}:{_executingAssemblyName}")
            .TrackValue(lag);
        return lag;
    }

    private string ProcessSent(MessagePayload<T> message, ConsumeResult<string, string> consumeResult)
    {
        const string dateTimeFormat = @"yyyy'-'MM'-'dd'T'HH':'mm':'ss.FFFFFFFK";
        var sent = DateTimeOffset.Parse(consumeResult.Message.Timestamp.UtcDateTime.ToString(dateTimeFormat)).ToString(dateTimeFormat);

        if (message.Sent == null)
            return sent;

        // Has to be converted properly to UTC so it has the correct timezone settings.
        var parsed = DateTimeOffset.Parse(message.Sent);
        return new DateTime(parsed.Year, parsed.Month, parsed.Day, parsed.Hour, parsed.Minute, parsed.Second, parsed.Millisecond,
            DateTimeKind.Utc).ToString(dateTimeFormat);
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Confluent.Kafka;
using evicore.gravity.common.Interface;
using Microsoft.Extensions.Logging;
using ucx.messages.@base;

namespace evicore.gravity.common.Service;

public class KafkaMessageSenderService<TKey, TMessage> : IMessageSender<TKey, TMessage>
    where TMessage : Message, new()
{
    private readonly ILogger _logger;
    private readonly IProducer<TKey, TMessage> _producer;
    protected readonly Dictionary<string, object> _scope = new();
    private readonly string _topic;

    public KafkaMessageSenderService(ILogger logger, IProducer<TKey, TMessage> producer, string topic)
    {
        _scope.Add("Class", nameof(KafkaMessageSenderService<TKey, TMessage>));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _producer = producer ?? throw new ArgumentNullException(nameof(producer));
        _topic = topic ?? throw new ArgumentNullException(nameof(topic));
    }

    public async Task SendAsync(TMessage message)
    {
        var scope = new Dictionary<string, object>(_scope)
        {
            ["Method"] = "ConsumeGravityCommonMessage<TMessage>",
            ["EventName"] = message.Name,
            ["EventVersion"] = message.Version,
            ["Topic"] = _topic
        };

        using (_logger.BeginScope(scope))
        {
            if (string.IsNullOrWhiteSpace(message.Name)) message.Name = typeof(TMessage).Name;

            var kafkaMessage = new Message<TKey, TMessage>
            {
                Value = message
            };

            _logger.LogDebug("KafkaMessageSenderService: Sending message with no key");

            var result = await _producer.ProduceAsync(_topic, kafkaMessage);

            _logger.LogDebug("KafkaMessageSenderService: Status of message sending was {@Status}", result?.Status);
        }
    }
}

using System.Net;
using System.Reflection;
using Confluent.Kafka;
using evicore.gravity.common.ExtensionMethods;
using evicore.gravity.common.Interface;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ucx.AclService.Erss.Domain.Models;
using Ucx.AclService.Erss.Dto.Event.Erss;
using Ucx.AclService.Erss.Dto.Event.XplErssCaseEnrichedIone;
using Ucx.AclService.Erss.Services.Handlers;
using ucx.messages.@base.Dtos;
using ucx.messages.events;

namespace Ucx.AclService.Erss.Services.Configuration
{
    public static class KafkaConfiguration
    {
        private const string consumerGroup = "Ucx.AclServiceErssV2";
        private const string ioPreAuthThindboDiagnosisTopic = "IOPreAuthThindboDiagnosis";
        private const string xplErssCaseEnrichedloneEventTopic = "XPLErssCaseEnrichedIone";
        private const string xplErssCaseEnrichedloneHistoryEventTopic = "XPLErssCaseEnrichedIoneHistory";
        private const string xplErssFinalTopic = "XPLErssFinal";
        private const string ucxImageOneQualifiersUpdatedTopic = "Ucx.ImageOneQualifiersUpdated";
        private const string ucxXPLAmsFileCreatedTopic = "XPLAmsFileCreated";
        private const string ucxUcxWebClinicalUploadedTopic = "Ucx.WebClinicalUploaded";
        private const string UcxJurisdictionStateUpdatedTopic = "Ucx.JurisdictionStateUpdated";
        private const string UcxExternalSystemStatusChangeTopic = "Ucx.ExternalSystemStatusChange";
        private const string ucxRequestForServiceSubmittedTopic = "UCX.RequestForServiceSubmitted";
        private const string ucxHistoricalRequestForServiceSubmittedTopic = "UCX.HistoricalRequestForServiceSubmitted";
        private const string ucxDueDateCalculatedTopic = "UCXDueDateCalculatedTopic";
        private const string ucxRequestUrgencyUpdatedTopic = "UCX.RequestUrgencyUpdated";
        private const string ucxDueDateMissedTopic = "UCX.DueDateMissed";
        private const string ucxRequestLineOfBusinessUpdatedTopic = "Ucx.RequestLineOfBusinessUpdated";
        private const string ucxProcedureUpdated = "Ucx.ProcedureUpdated";
        private const string ucxRequestSpecialtyUpdated = "UCX.RequestSpecialtyUpdated";
        private const string ucxDiagnosesUpdatedTopic = "Ucx.DiagnosesUpdated";
        private const string xplErssFinalHistoryTopic = "XPLErssFinalHistory";
        private const string ucxHistoricalProcedureUpdatedTopic = "UCX.HistoricalProcedureUpdated";
        private const string ucxHistoricalImageOneQualifiersUpdatedTopic = "Ucx.HistoricalImageOneQualifiersUpdated";
        private const string ucxRoutingRequestAssigned = "UCX.RoutingRequestAssigned";
        private const string ucxMemberInfoUpdatedForRequestEvent = "UCX.MemberInfoUpdatedForRequestEvent";
        private const string UcxMedicalDisciplineCorrectedTopic = "Ucx.MedicalDisciplineCorrected";

        public static WebApplicationBuilder AddConsumers(this WebApplicationBuilder builder)
        {
            builder.Services.AddMessageConsumer<XplErssCaseEnrichedIone>(options =>
            {
                options.AutoOffsetReset = AutoOffsetReset.Earliest;
                options.ClientConfig = GetClientConfig(builder);
                options.DelayMs = builder.Configuration.GetValue<int>("Observers:ErssCaseEnrichedIoneMs");
                options.Topic = xplErssCaseEnrichedloneEventTopic;
                options.GroupId = consumerGroup;
                options.HandlerAssembly = Assembly.GetAssembly(typeof(MessageHandler));
            });
            builder.Services.AddMessageConsumer<XplAmsFileCreated>(options =>
            {
                options.AutoOffsetReset = AutoOffsetReset.Earliest;
                options.ClientConfig = GetClientConfig(builder);
                options.DelayMs = builder.Configuration.GetValue<int>("Observers:XplAmsFileCreatedThrottleMs");
                options.Topic = ucxXPLAmsFileCreatedTopic;
                options.GroupId = consumerGroup;
                options.HandlerAssembly = Assembly.GetAssembly(typeof(XplAmsFileCreatedHandler));
            });
            builder.Services.AddMessageConsumer<XplErssFinalEventEventSource<ServiceRequest>>(options =>
            {
                options.AutoOffsetReset = AutoOffsetReset.Earliest;
                options.ClientConfig = GetClientConfig(builder);
                options.DelayMs = builder.Configuration.GetValue<int>("Observers:ErssThrottleMs");
                options.Topic = xplErssFinalTopic;
                options.GroupId = consumerGroup;
                options.HandlerAssembly = Assembly.GetAssembly(typeof(MessageHandler));
            });
            builder.Services.AddMessageConsumer<DiagnosisMessage>(options =>
            {
                options.AutoOffsetReset = AutoOffsetReset.Earliest;
                options.ClientConfig = GetClientConfig(builder);
                options.DelayMs = builder.Configuration.GetValue<int>("Observers:DiagnosisThrottleMs");
                options.Topic = ioPreAuthThindboDiagnosisTopic;
                options.GroupId = consumerGroup;
                options.HandlerAssembly = Assembly.GetAssembly(typeof(MessageHandler));
            });
            builder.Services.AddMessageConsumer<UcxProcedureUpdated>(options =>
            {
                options.AutoOffsetReset = AutoOffsetReset.Earliest;
                options.ClientConfig = GetClientConfig(builder);
                options.DelayMs = builder.Configuration.GetValue<int>("Observers:UcxProcedureUpdatedThrottleMs");
                options.Topic = ucxProcedureUpdated;
                options.GroupId = consumerGroup;
                options.HandlerAssembly = Assembly.GetAssembly(typeof(ProcedureUpdatedHandler));
            });

            builder.Services.AddMessageConsumer<XplErssFinalHistoryEvent<ServiceRequest>>(options =>
            {
                options.AutoOffsetReset = AutoOffsetReset.Earliest;
                options.ClientConfig = GetClientConfig(builder);
                options.DelayMs = builder.Configuration.GetValue<int>("Observers:ErssFInalHistoryThrottleMs");
                options.Topic = xplErssFinalHistoryTopic;
                options.GroupId = consumerGroup;
                options.HandlerAssembly = Assembly.GetAssembly(typeof(MessageHandler));
            });
            builder.Services.AddMessageConsumer<XplErssCaseEnrichedIoneHistory>(options =>
            {
                options.AutoOffsetReset = AutoOffsetReset.Earliest;
                options.ClientConfig = GetClientConfig(builder);
                options.DelayMs = builder.Configuration.GetValue<int>("Observers:ErssCaseEnrichedIoneHistoryMs");
                options.Topic = xplErssCaseEnrichedloneHistoryEventTopic;
                options.GroupId = consumerGroup;
                options.HandlerAssembly = Assembly.GetAssembly(typeof(MessageHandler));
            });
            builder.Services.AddMessageConsumer<UcxMedicalDisciplineCorrected>(options =>
            {
                options.AutoOffsetReset = AutoOffsetReset.Earliest;
                options.ClientConfig = GetClientConfig(builder);
                options.DelayMs = builder.Configuration.GetValue<int>("Observers:UcxMedicalDisciplineCorrectedThrottleMs");
                options.Topic = UcxMedicalDisciplineCorrectedTopic;
                options.GroupId = consumerGroup;
                options.HandlerAssembly = Assembly.GetAssembly(typeof(MessageHandler))!;
            });

            return builder;
        }

        /// <summary>
        ///     Start the Consumers/Observer after they have been registered
        /// </summary>
        /// <param name="app">WebApplication</param>
        /// <returns>WebApplication</returns>
        public static WebApplication StartConsumers(this WebApplication app)
        {
            app.Services.GetRequiredService<IMessageConsumerService<XplErssCaseEnrichedIone>>()
                .StartConsumingAllNoMessagePayload();
            app.Services.GetRequiredService<IMessageConsumerService<XplAmsFileCreated>>().StartConsumingAllNoMessagePayload();
            app.Services.GetRequiredService<IMessageConsumerService<XplErssFinalEventEventSource<ServiceRequest>>>().StartConsumingAllNoMessagePayload();
            app.Services.GetRequiredService<IMessageConsumerService<XplErssFinalHistoryEvent<ServiceRequest>>>().StartConsumingAllNoMessagePayload();
            app.Services.GetRequiredService<IMessageConsumerService<XplErssCaseEnrichedIoneHistory>>().StartConsumingAllNoMessagePayload();
            app.Services.GetRequiredService<IMessageConsumerService<DiagnosisMessage>>().StartConsumingAllNoMessagePayload();
            app.Services.GetRequiredService<IMessageConsumerService<UcxProcedureUpdated>>().StartConsuming(nameof(UcxProcedureUpdated), "1.2.1");
            app.Services.GetRequiredService<IMessageConsumerService<UcxMedicalDisciplineCorrected>>().StartConsuming(nameof(UcxMedicalDisciplineCorrected), "1.0.0");
            return app;
        }

        /// <summary>
        ///     Register Kafka Producers
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        public static WebApplicationBuilder AddProducers(this WebApplicationBuilder builder)
        {
            builder.Services.AddMessageSender<Guid, UcxImageOneQualifiersUpdated>(GetClientConfig(builder),
                ucxImageOneQualifiersUpdatedTopic);
            builder.Services.AddMessageSender<Guid, UcxWebClinicalUploaded>(GetClientConfig(builder),
                ucxUcxWebClinicalUploadedTopic);
            builder.Services.AddMessageSender<Guid, UcxJurisdictionStateUpdated>(GetClientConfig(builder),
                UcxJurisdictionStateUpdatedTopic);

            builder.Services.AddMessageSender<Guid, UcxExternalSystemStatusChange>(GetClientConfig(builder),
                UcxExternalSystemStatusChangeTopic);

            // TODO: Remove when we start consuming Jedi's generic RequestForServiceSubmitted.
            builder.Services.AddMessageSender<Guid, UCXRequestForServiceSubmitted>(GetClientConfig(builder),
                ucxRequestForServiceSubmittedTopic);

            builder.Services.AddMessageSender<Guid, UCXDueDateCalculated>(GetClientConfig(builder),
                ucxDueDateCalculatedTopic);
            builder.Services.AddMessageSender<Guid, UcxRequestUrgencyUpdated>(GetClientConfig(builder),
                ucxRequestUrgencyUpdatedTopic);
            builder.Services.AddMessageSender<Guid, UcxDueDateMissed>(GetClientConfig(builder),
                ucxDueDateMissedTopic);
            builder.Services.AddMessageSender<Guid, UcxRequestLineOfBusinessUpdated>(GetClientConfig(builder),
                ucxRequestLineOfBusinessUpdatedTopic);
            builder.Services.AddMessageSender<Guid, UcxProcedureUpdated>(GetClientConfig(builder),
                ucxProcedureUpdated);
            builder.Services.AddMessageSender<Guid, UCXRequestSpecialtyUpdated>(GetClientConfig(builder),
                ucxRequestSpecialtyUpdated);
            builder.Services.AddMessageSender<Guid, UcxDiagnosesUpdated>(GetClientConfig(builder),
                ucxDiagnosesUpdatedTopic);
            builder.Services.AddMessageSender<Guid, UcxHistoricalRequestForServiceSubmitted>(GetClientConfig(builder),
                ucxHistoricalRequestForServiceSubmittedTopic);
            builder.Services.AddMessageSender<Guid, UcxHistoricalImageOneQualifiersUpdated>(GetClientConfig(builder),
                ucxHistoricalImageOneQualifiersUpdatedTopic);
            builder.Services.AddMessageSender<Guid, UcxHistoricalProcedureUpdated>(GetClientConfig(builder),
                ucxHistoricalProcedureUpdatedTopic);
            builder.Services.AddMessageSender<Guid, UCXRoutingRequestAssigned>(GetClientConfig(builder),
                ucxRoutingRequestAssigned);

            builder.Services.AddMessageSender<Guid, UCXMemberInfoUpdatedForRequest>(GetClientConfig(builder),
                ucxMemberInfoUpdatedForRequestEvent);
            builder.Services.AddMessageSender<Guid, UcxMedicalDisciplineCorrected>(GetClientConfig(builder), UcxMedicalDisciplineCorrectedTopic);

            return builder;
        }

        /// <summary>
        /// Register Dead Letter For Kafka
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        public static WebApplicationBuilder AddKafkaDeadLetter(this WebApplicationBuilder builder)
        {
            builder.Services.AddKafkaDeadLetter(builder.Configuration.GetSection("Cosmos")
                .Get<DeadLetterCosmosSettings>());
            return builder;
        }

        /// <summary>
        ///     Get Kafka Settings from AppSettings.json
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        private static ClientConfig GetClientConfig(WebApplicationBuilder builder)
        {
            var clientConfig = builder.Configuration.GetSection("Kafka").Get<ClientConfig>();
            if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "local")
            {
                clientConfig.SecurityProtocol = SecurityProtocol.Plaintext;
                clientConfig.SaslMechanism = SaslMechanism.Plain;
                return clientConfig;
            }

            clientConfig.SecurityProtocol = SecurityProtocol.SaslSsl;
            clientConfig.ClientId = Dns.GetHostName();
            clientConfig.SaslMechanism = SaslMechanism.ScramSha512;

            return clientConfig;
        }
    }
}



Thanks and Regards
Siraj

