Program.cs
using Microsoft.ApplicationInsights.Extensibility;
using RequestRouting.Observer.Configuration;
using Serilog;
using Serilog.Exceptions;

namespace RequestRouting.Observer;

public static class Program
{
    public static int Main(string[] args)
    {
        var loggingResult = ConfigureLogging(args);
        if (loggingResult > 0) return loggingResult;

        CreateHostBuilder(args).Build().Run();

        return 0;
    }

    private static int ConfigureLogging(string[] args)
    {
        var config = ConfigurationHelper.GetConfiguration();
        var loggerConfiguration = new LoggerConfiguration().ReadFrom.Configuration(config);
        var telemetryConfiguration = TelemetryConfiguration.CreateDefault();
        telemetryConfiguration.ConnectionString = config.GetValue<string>("ApplicationInsights:ConnectionString");
        loggerConfiguration.WriteTo.ApplicationInsights(telemetryConfiguration, TelemetryConverter.Traces);

        Log.Logger = loggerConfiguration
            .Enrich.FromLogContext()
            .Enrich.WithExceptionDetails() //NOSONAR
            .Enrich.WithDynamicProperty("Application", () => "RequestRouting.Observer")
            .WriteTo.Async(l =>
            {
                l.ApplicationInsights(new TelemetryConfiguration
                    {
                        ConnectionString = config.GetValue<string>("ApplicationInsights:ConnectionString")
                    },
                    TelemetryConverter.Traces);
            })
            .WriteTo.Console()
            .CreateLogger();

        try
        {
            Log.Debug("Starting web host");
            CreateHostBuilder(args).Build().Run();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Host terminated unexpectedly");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    public static IHostBuilder CreateHostBuilder(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .UseSerilog()
            .ConfigureWebHostDefaults(webBuilder => { webBuilder.UseStartup<Startup>(); })
            .ConfigureLogging(logging => logging.AddAzureWebAppDiagnostics());
    }
}

Startup.ts
using Azure;
using Azure.Search.Documents.Indexes;
using Confluent.Kafka;
using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using RequestRouting.Application.Commands.Handlers;
using RequestRouting.Application.Commands.Interfaces;
using RequestRouting.Application.Configurations;
using RequestRouting.Application.Deserialization;
using RequestRouting.Application.Interface;
using RequestRouting.Application.Orchestrator;
using RequestRouting.Application.Policy;
using RequestRouting.Application.Query.Interfaces;
using RequestRouting.Application.Replay;
using RequestRouting.Application.Services;
using RequestRouting.Application.Validator;
using RequestRouting.Infrastructure.Configs;
using RequestRouting.Infrastructure.HealthChecks;
using RequestRouting.Infrastructure.Kafka;
using RequestRouting.Infrastructure.Repositories;
using RequestRouting.Observer.Configuration;
using Serilog;
using ucx.locking.@base.models;
using Ucx.Locking.extensions;

namespace RequestRouting.Observer;

public class Startup
{
    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    private IConfiguration Configuration { get; }

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddControllers();
        var options = new ApplicationInsightsServiceOptions
        { ConnectionString = Configuration.GetValue<string>("ApplicationInsights:ConnectionString") };
        services.AddApplicationInsightsTelemetry(options);
        services.AddSearchStorageDependencies(Configuration);
        services.RegisterProducer(Configuration);

        AddHealthChecks(services);

        services.AddSingleton(Configuration);

        services.AddOptions();
        services.Configure<AppSettings>(Configuration);
        services.Configure<KafkaSettings>(Configuration.GetSection("KafkaSettings"));

        // Initialize AD Graph API
        services.AddTransient<IAzureGraphApiClientConfiguration, AzureADGraphAPIClientConfiguration>();
        services.AddTransient<IAzureGraphClientEndpoint, AzureGraphApiClient>();
        services.AddSingleton<IUserProfileCacheService, UserProfileCacheService>();
        services.AddSingleton<IUserGroup, UserGroupConfiguration>();

        services.Configure<LockOptions>(Configuration.GetSection("Locking"));
        services.AddLocking(Configuration.GetValue<string>("RedisConnectionString"));

        services.InitiatePolicyRegistry();

        var documentDbSection = Configuration.GetSection("DocumentDB");
        services.AddSingleton(c => InitializeCosmosClient(documentDbSection));
        services.AddSingleton(c => InitializeEventHistoryRepository(c, documentDbSection));
        services.AddSingleton(c => InitializeStateConfigurationRepository(c, documentDbSection));
        services.AddSingleton(c => InitializeUserConfigurationRepository(c, documentDbSection));
        services.AddSingleton(c => InitializeUserProfileRepository(c, documentDbSection));

        services.AddSingleton<IMessageValidator, MessageValidator>();
        services.AddSingleton<IKafkaConsumer, KafkaConsumer>();
        services.AddSingleton<IKafkaRoutableDeterminationConsumer, KafkaRoutableDeterminationConsumer>();
        services.AddScoped<IOrchestrator, Orchestrator>();
        services.AddSingleton<IHandlerQueue, HandlerQueue.HandlerQueue>();

        services.AddSingleton<IStateConfigurationAddedHandler, StateConfigurationAddedHandler>();
        services.AddSingleton<IUserProfileConfigurationUpdatedHandler, UserProfileConfigurationUpdatedHandler>();

        services.AddSingleton<IRequestHandler, AuthorizationCanceledHandler>();
        services.AddSingleton<IRequestHandler, AuthorizationDismissedHandler>();
        services.AddSingleton<IRequestHandler, AuthorizationWithdrawnHandler>();
        services.AddSingleton<IRequestHandler, DequeueRoutingRequestHandler>();
        services.AddSingleton<IRequestHandler, DueDateCalculatedHandler>();
        services.AddSingleton<IRequestHandler, DueDateMissedHandler>();
        services.AddSingleton<IRequestHandler, FileAttachedHandler>();
        services.AddSingleton<IRequestHandler, JurisdictionStateUpdatedHandler>();
        services.AddSingleton<IRequestHandler, MemberIneligibilitySubmittedHandler>();
        services.AddSingleton<IRequestHandler, MemberInfoUpdatedForRequestHandler>();
        services.AddSingleton<IRequestHandler, PeerToPeerActionSubmittedHandler>();
        services.AddSingleton<IRequestHandler, PhysicianDecisionMadeHandler>();
        services.AddSingleton<IRequestHandler, RequestForServiceSubmittedHandler>();
        services.AddSingleton<IRequestHandler, RequestSpecialtyUpdatedHandler>();
        services.AddSingleton<IRequestHandler, RequestUrgencyUpdatedHandler>();
        services.AddSingleton<IRequestHandler, RequestLineOfBusinessUpdatedHandler>();
        services.AddSingleton<IRequestHandler, RoutingRequestAssignedHandler>();
        services.AddSingleton<IRequestHandler, StatusChangeHandler>();
        services.AddSingleton<IRequestHandler, UserExcludedFromRequestHandler>();
        services.AddSingleton<IRoutableDeterminationHandler, RoutableDeterminationHandler>();
        services.AddSingleton<IRoutableDeterminiationRequestForServiceSavedHandler, RoutableDeterminationRequestForServiceSavedHandler>();
        services.AddSingleton<IRequestHandler, MedicalDisciplineCorrectedHandler>();
        services.AddSingleton<IReplayService, ReplayService>();
        services.AddSingleton<IRequestIndexingRepository, RequestIndexingRepository>();
        services.AddSingleton<IUserProfileConfigurationUpdatedHandler, UserProfileConfigurationUpdatedHandler>();
        services.AddSingleton<IStateConfigurationCacheService, StateConfigurationCacheService>();

        services.AddSingleton<IRequestSearchRepository, RequestSearchRepository>();

        // Initialize and Start Observers
        services.AddObservers(Configuration);
        services.StartObservers(services.BuildServiceProvider());
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseSerilogRequestLogging();
        app.UseHttpsRedirection();
        app.UseRouting();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
            endpoints.MapHealthChecks(
                "/observer/health",
                new HealthCheckOptions
                {
                    Predicate = _ => true
                });
        });
    }

    private static CosmosClient InitializeCosmosClient(IConfigurationSection configurationSection)
    {
        var accountEndpoint = configurationSection.GetSection("Endpoint").Value;
        var key = configurationSection.GetSection("Key").Value;

        var serializerSettings = new JsonSerializerSettings
        {
            Converters =
            {
                new RoutingJsonConverter(),
                new StringEnumConverter()
            }
        };

        var options = new CosmosClientOptions
        { ConnectionMode = ConnectionMode.Gateway, Serializer = new JsonCosmosSerializer(serializerSettings) };
        return new CosmosClient(accountEndpoint, key, options);
    }

    private static IEventHistoryRepository InitializeEventHistoryRepository(IServiceProvider c,
        IConfigurationSection configurationSection)
    {
        var databaseName = configurationSection.GetSection("DatabaseName").Value;
        var containerName = configurationSection.GetSection("EventHistoryContainer").Value;
        var client = c.GetService<CosmosClient>();
        var logger = c.GetService<ILogger<EventHistoryRepository>>();
        return new EventHistoryRepository(client, databaseName, containerName, logger);
    }

    private static IStateConfigurationRepository InitializeStateConfigurationRepository(
        IServiceProvider serviceProvider, IConfigurationSection configurationSection)
    {
        var databaseName = configurationSection.GetSection("DatabaseName").Value;
        var containerName = configurationSection.GetSection("StateConfigurationContainer").Value;
        var client = serviceProvider.GetService<CosmosClient>();
        var logger = serviceProvider.GetService<ILogger<StateConfigurationRepository>>();
        return new StateConfigurationRepository(client, databaseName, containerName, logger);
    }

    private static IUserProfileConfigurationRepository InitializeUserConfigurationRepository(IServiceProvider c,
        IConfigurationSection configurationSection)
    {
        var databaseName = configurationSection.GetSection("DatabaseName").Value;
        var containerName = configurationSection.GetSection("UserConfigurationContainer").Value;
        var client = c.GetService<CosmosClient>();
        var logger = c.GetService<ILogger<UserProfileConfigurationRepository>>();
        return new UserProfileConfigurationRepository(client, databaseName, containerName, logger);
    }

    private static IUserProfileRepository InitializeUserProfileRepository(IServiceProvider c,
        IConfigurationSection configurationSection)
    {
        var databaseName = configurationSection.GetSection("DatabaseName").Value;
        var containerName = configurationSection.GetSection("UserProfileContainer").Value;
        var client = c.GetService<CosmosClient>();
        var logger = c.GetService<ILogger<UserProfileRepository>>();
        return new UserProfileRepository(client, databaseName, containerName, logger);
    }

    private void AddHealthChecks(IServiceCollection services)
    {
        var kafkaSettings = Configuration.GetSection("KafkaSettings").Get<KafkaSettings>();
        var dbConnectionString =
            $"AccountEndpoint={Configuration.GetValue<string>("DocumentDB:Endpoint")};" +
            $"AccountKey={Configuration.GetValue<string>("DocumentDB:Key")};";
        services.AddHealthChecks().AddKafka(options =>
                {
                    options.BootstrapServers = kafkaSettings.BootstrapServers;
                    options.SecurityProtocol = Enum.Parse<SecurityProtocol>(kafkaSettings.SecurityProtocol);
                    options.SslCaLocation = kafkaSettings.SslCaLocation;
                    options.SaslMechanism = SaslMechanism.ScramSha512;
                    options.SaslUsername = kafkaSettings.SaslUsername;
                    options.SaslPassword = kafkaSettings.SaslPassword;
                    options.SocketNagleDisable = true;
                },
                "UCX.RequestRoutingHealthCheck",
                "Observer:Kafka",
                null,
                null,
                TimeSpan.FromSeconds(30)
            ).AddCosmosDb(
                dbConnectionString,
                null,
                "Observer:CosmosDB",
                null,
                null,
                TimeSpan.FromSeconds(30))
            .AddAzureSearchHealthCheck(new[]
                {
                    Configuration.GetValue<string>("SearchDB:RoutingIndexName")
                },
                new SearchIndexClient(new Uri(Configuration.GetValue<string>("SearchDB:Endpoint")),
                    new AzureKeyCredential(Configuration.GetValue<string>("SearchDB:Key"))))
            .AddApplicationInsightsPublisher(Configuration.GetValue<string>("ApplicationInsights:InstrumentationKey"));
    }

}
BaseObserver.cs
using Confluent.Kafka;
using RequestRouting.Infrastructure.Kafka;

namespace RequestRouting.Observer;

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

    protected BaseObserver(ILogger<BaseObserver> logger, IKafkaRoutableDeterminationConsumer consumer, string topicName)
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
            _consumer.Subscribe(_topicName);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var consumerResult = _consumer.Consume(stoppingToken);
                    if (consumerResult.IsPartitionEOF)
                    {
                        continue;
                    }

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
                            _logger.LogDebug("New message consumption started for {topicName} and message {messageName}", consumerResult.Topic, consumerResult.Message);
                            
                            await HandleEventAsync(consumerResult);
                            
                            _logger.LogDebug("New message consumption ended for {topicName} and message {messageName}", consumerResult.Topic, consumerResult.Message);
                        }

                        catch (Exception ex)
                        {
                            _logger.LogError(ex, ex.Message);
                        }

                    }
                }
                catch (ConsumeException e)
                {
                    _logger.LogWarning(e.Error.Reason);

                    if (!e.Error.IsFatal)
                    {
                        continue;
                    }

                    _logger.LogError(e, "FATAL ERROR - " + e.Message);
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, ex.Message);
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

RoutingRequestAssignmentObserver.cs
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using RequestRouting.Application.Configurations;
using RequestRouting.Application.Events;
using RequestRouting.Application.Interface;
using RequestRouting.Infrastructure.Kafka;
using System.Text.Json;

namespace RequestRouting.Observer.Observers;

public class RoutingRequestAssignedObserver : BaseObserver
{
    private readonly ILogger<RoutingRequestAssignedObserver> _logger;
    private readonly IOrchestrator _orchestrator;
    private readonly IMessageValidator _validator;
    private readonly int _eventReplayOffset;
    private readonly string _version;

    public RoutingRequestAssignedObserver(ILogger<RoutingRequestAssignedObserver> logger,
        IKafkaConsumer consumer,
        IMessageValidator validator,
        IOrchestrator orchestrator,
        IOptions<AppSettings> options,
        string topicName) : base(logger, consumer, topicName)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _eventReplayOffset = options.Value.EventReplayOffset["UCXRoutingRequestAssigned"];
        _version = options.Value.EventConfigs.Find(x => x.Name == "UCXRoutingRequestAssigned").Version;
    }

    public override async Task HandleEventAsync(ConsumeResult<string, string> consumerResult)
    {
        if (!consumerResult.Message.Value.Contains($"Version\":\"{_version}")) return;

        if (_eventReplayOffset != -1 && consumerResult.Offset.Value > _eventReplayOffset)
        {
            _logger.LogDebug("UCXRoutingRequestAssigned finished at offset: " + consumerResult.Offset.Value);
            return;
        }

        _logger.LogInformation("RoutingRequestAssignedObserver started");

        var deserialized = JsonSerializer.Deserialize<RoutingRequestAssignedEvent>(consumerResult.Message.Value);

        if (_validator.ValidateRequest(deserialized, _logger))
        {
            await _orchestrator.OrchestrateAsync(deserialized, new CancellationToken());
        }

        _logger.LogInformation("RoutingRequestAssignedObserver ended");
    }
}

{
  "ApplicationInsights": {
    "ConnectionString": "InstrumentationKey=00000000-0000-0000-0000-000000000000;",
    "InstrumentationKey": "test123"
  },
  "SearchDB": {
    "Key": "",
    "RoutingIndexName": "routing-index-green",
    "Endpoint": "https://eheu2-dev-requestrouting-acs.search.windows.net",
    "ScoringProfiles": [
      {
        "scoringProfileName": "SleepMdSp",
        "weights": {
          "Priority": 6000,
          "JurisdictionState": 20,
          "Status": 2,
          "SslRequirementStatus": 1,
          "AssignedToId": 10
        },
        "freshnessFunctions": [
          {
            "fieldName": "DueDateUtc",
            "boostValue": 5,
            "boostingDuration": "0:2:0:0",
            "isFutureDated": true,
            "Interpolation": "Linear"
          },
          {
            "fieldName": "DueDateUtc",
            "boostValue": 10,
            "boostingDuration": "10:0:0",
            "isFutureDated": false,
            "Interpolation": "Linear"
          }
        ]
      }
    ]
  },
  "ObserverType": "Blue",
  "RedisConnectionString": "",
  "Locking": {
    "AcquireLockTimeout": "00:00:09",
    "ExpireLockTimeout": "00:00:08"
  },
  "DocumentDB": {
    "Endpoint": "https://eheu2devcdbucxrequestrouting.documents.azure.com:443/",
    "Key": "",
    "DatabaseName": "devrequestrouting",
    "EventHistoryContainer": "RequestHistoryGreen",
    "StateConfigurationContainer": "StateConfiguration",
    "UserConfigurationContainer": "UserConfiguration",
    "UserProfileContainer": "UserProfile"
  },
  "KafkaSettings": {
    "BootstrapServers": "eheu2dv1cpkaf01.innovate.lan:9093,eheu2dv1cpkaf02.innovate.lan:9093,eheu2dv1cpkaf03.innovate.lan:9093,eheu2dv1cpkaf04.innovate.lan:9093,eheu2dv1cpkaf05.innovate.lan:9093",
    "GroupId": "RequestRouting",
    "SslCaLocation": "certs/dev/DevCARoot.crt",
    "SecurityProtocol": "SaslSsl",
    "SaslPassword": "",
    "SaslUsername": "ksvcRequestRouting",
    "RoutableDeterminationGroupId": "RequestRoutableDetermination",
    "Topic": {
      "ucxAuthorizationCanceledTopic":"UCX.AuthorizationCanceled",
      "ucxAuthorizationDismissedTopic":"UCX.AuthorizationDismissed",
      "ucxAuthorizationWithdrawnTopic":"UCX.AuthorizationWithdrawn",
      "ucxDueDateCalculatedTopic":"UCXDueDateCalculatedTopicV2",
      "ucxDueDateMissedTopic": "UCX.DueDateMissed",
      "ucxDequeueRoutingRequestTopic":"UCX.DequeueRoutingRequest",
      "ucxFileAttachedTopic":"UCX.FileAttachedV2",
      "ucxJurisdictionStateUpdatedTopic":"Ucx.JurisdictionStateUpdated",
      "ucxMemberIneligibilitySubmittedTopic": "UCX.MemberIneligibilitySubmittedEvent",
      "ucxMemberInfoUpdatedForRequestTopic": "UCX.MemberInfoUpdatedForRequestEvent",
      "ucxPhysicianDecisionMadeTopic": "Ucx.PhysicianDecisionMadeV2",
      "ucxPeerToPeerActionSubmittedTopic": "UCX.PeerToPeerActionSubmitted",
      "ucxRequestForServiceSubmittedTopic": "UCX.RequestForServiceSubmittedV2",
      "ucxRequestSpecialtyUpdatedTopic": "UCX.RequestSpecialtyUpdated",
      "ucxRequestUrgencyUpdatedTopic": "UCX.RequestUrgencyUpdated",
      "ucxRoutingRequestAssignedTopic": "UCX.RoutingRequestAssigned",
      "ucxRequestLineOfBusinessUpdatedTopic": "Ucx.RequestLineOfBusinessUpdated",
      "ucxRoutableDeterminationTopic": "Ucx.RoutableDetermination",
      "ucxStateConfigurationAddedTopic": "UCX.StateConfiguration",
      "ucxStatusChangeTopic": "Ucx.ExternalSystemStatusChangeV2",
      "ucxUserConfigurationEventsTopic": "UCX.UserConfiguration.Events",
      "ucxUserProfileConfigurationTopic": "UCX.UserProfileConfiguration",
      "ucxUserExcludedFromRequestTopic": "UCX.UserExcludedFromRequest",
      "ucxMedicalDisciplineCorrectedTopic":"Ucx.MedicalDisciplineCorrected"
    }
  },

  "EventConfigs": [
    {
      "Name": "UCXAuthorizationCanceled",
      "HandlerName": "AuthorizationCanceledHandler",
      "Version": "1.0.0.1"     
    },
    {
      "Name": "UCXAuthorizationDismissed",
      "HandlerName": "AuthorizationDismissedHandler",
      "Version": "1.0.0.1"
    },
    {
      "Name": "UCXAuthorizationWithdrawn",
      "HandlerName": "AuthorizationWithdrawnHandler",
      "Version": "1.0.0.1"
    },
    {
      "Name": "UCXDueDateCalculated",
      "HandlerName": "DueDateCalculatedHandler",
      "Version": "1.1.0"
    },
    {
      "Name": "UcxDueDateMissed",
      "HandlerName": "DueDateMissedHandler",
      "Version": "1.1.0"
    },
    {
      "Name": "UCXDequeueRoutingRequest",
      "HandlerName": "DequeueRoutingRequestHandler",
      "Version": "1.0.0"
    },
    {
      "Name": "UCXFileAttached",
      "HandlerName": "FileAttachedHandler",
      "Version": "1.1.0"
    },
    {
      "Name": "UcxJurisdictionStateUpdated",
      "HandlerName": "JurisdictionStateUpdatedHandler",
      "Version": "1.2.0"
    },
    {
      "Name": "UcxMemberIneligibilitySubmitted",
      "HandlerName": "MemberIneligibilitySubmittedHandler",
      "Version": "1.0.0.0"
    },
    {
      "Name": "UCXMemberInfoUpdatedForRequest",
      "HandlerName": "MemberInfoUpdatedForRequestHandler",
      "Version": "1.0.0.0"
    },
    {
      "Name": "UcxPeerToPeerActionSubmitted",
      "HandlerName": "PeerToPeerActionSubmittedHandler",
      "Version": "1.0.0.0"
    },
    {
      "Name": "UcxPhysicianDecisionMade",
      "HandlerName": "PhysicianDecisionMadeHandler",
      "Version": "1.4.0"
    },
    {
      "Name": "UCXRequestForServiceSubmitted",
      "HandlerName": "RequestForServiceSubmittedHandler",
      "Version": "1.4.0"
    },
    {
      "Name": "UCXRequestSpecialtyUpdated",
      "HandlerName": "RequestSpecialtyUpdatedHandler",
      "Version": "1.0.0"
    },
    {
      "Name": "UcxRequestUrgencyUpdated",
      "HandlerName": "RequestUrgencyUpdatedHandler",
      "Version": "1.1.0"
    },
    {
      "Name": "UcxRequestLineOfBusinessUpdated",
      "HandlerName": "RequestLineOfBusinessUpdatedHandler",
      "Version": "1.2.0"
    },
    {
      "Name": "StateConfigurationAdded",
      "HandlerName": "StateConfigurationAddedHandler",
      "Version": "1.0.0.0"
    },
    {
      "Name": "UcxExternalSystemStatusChange",
      "HandlerName": "StatusChangeHandler",
      "Version": "1.0.0"
    },
    {
      "Name": "UserProfileConfigurationUpdated",
      "HandlerName": "UserProfileConfigurationUpdatedHandler",
      "Version": "2.0.0.0"
    },
    {
      "Name": "UCXUserExcludedFromRequest",
      "HandlerName": "UserExcludedFromRequestHandler",
      "Version": "1.0.0"
    },
    {
      "Name": "UCXRoutingRequestAssigned",
      "HandlerName": "RoutingRequestAssignedHandler",
      "Version": "1.1.0"
    },
    {
      "Name": "UcxMedicalDisciplineCorrected",
      "HandlerName": "MedicalDisciplineCorrectedHandler",
      "Version": "1.0.0"
    }
  ],
  "EventReplayOffset": {
    "UCXAuthorizationCanceled": -1,
    "UCXAuthorizationDismissed": -1,
    "UCXAuthorizationWithdrawn": -1,
    "UCXDueDateCalculated": -1,
    "UcxDueDateMissed": -1,
    "UCXDequeueRoutingRequest": -1,
    "UCXFileAttached": -1,
    "UcxJurisdictionStateUpdated": -1,
    "UcxMemberIneligibilitySubmitted": -1,
    "UCXMemberInfoUpdatedForRequest": -1,
    "UcxPeerToPeerActionSubmitted": -1,
    "UcxPhysicianDecisionMade": -1,
    "UCXRequestForServiceSubmitted": -1,
    "UcxRequestUrgencyUpdated": -1,
    "UcxRequestLineOfBusinessUpdated": -1,
    "UCXRoutingRequestAssigned": -1,
    "UCXRequestSpecialtyUpdated": -1,
    "UCXStateConfiguration": -1,
    "UcxExternalSystemStatusChange": -1,
    "UCXUserExcludedFromRequest": -1,
    "UCXUserProfileConfigurationUpdated": -1,
    "UcxMedicalDisciplineCorrected": -1
  },
  "MaxEventsAllowedPerRequest": 200,
  "AzureADGraphAPIClientSettings": {
    "ClientSecret": "",
    "ClientId": "59854417-14d1-444c-b2e8-d07025ffd724",
    "Tenant": "evicore.com"
  },
  "UserGroups": [
    {
      "Name": "dv1_EpSleepMedicalDirector",
      "UserType": "MD"
    },
    {
      "Name": "dv1_ucx_md",
      "UserType": "MD"
    },
    {
      "Name": "dv1_EPSleepNurse",
      "UserType": "Nurse"
    },
    {
      "Name": "dv1_EpPacMedicalDirector",
      "UserType": "MD"
    },
    {
      "Name": "dv1_EpPacpNurse",
      "UserType": "Nurse"
    },
    {
      "Name": "dv1_EpPacClinicalNurseSupervisor",
      "UserType": "Nurse"
    },
    {
      "Name": "dv1_EpPacNurseCCR",
      "UserType": "Nurse"
    }
  ],
  "AssignmentExpirationMinutes": 20,
  "ReplayVersion": false,
  "Replay": false,
  "ReplayRoutableDetermination": false,
  "CaptureRfssForRoutableDetermination": false
}
launchsettings
{
  "profiles": {
    "RequestRouting.Observer": {
      "commandName": "Project",
      "launchBrowser": true,
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      },
      "dotnetRunMessages": true,
      "applicationUrl": "https://localhost:7230;http://localhost:5114"
    },
    "IIS Express": {
      "commandName": "IISExpress",
      "launchBrowser": true,
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    },
    "Docker": {
      "commandName": "Docker",
      "launchBrowser": true,
      "launchUrl": "{Scheme}://{ServiceHost}:{ServicePort}",
      "publishAllPorts": true,
      "useSSL": true
    }
  },
  "iisSettings": {
    "windowsAuthentication": false,
    "anonymousAuthentication": true,
    "iisExpress": {
      "applicationUrl": "http://localhost:46437",
      "sslPort": 44322
    }
  }
}
HandlerQueue.cs
using System.Collections.Concurrent;
using System.Configuration;
using Microsoft.Extensions.Options;
using RequestRouting.Application.Configurations;
using RequestRouting.Application.Interface;

namespace RequestRouting.Observer.HandlerQueue;

public class HandlerQueue : IHandlerQueue
{
    private readonly ConcurrentDictionary<string, IRequestHandler> _handlers = new();
    private readonly ILogger<HandlerQueue> _logger;

    public HandlerQueue(IServiceProvider serviceProvider, IOptions<AppSettings> options, ILogger<HandlerQueue> logger)
    {
        _logger = logger;
        var handlers = serviceProvider.GetServices<IRequestHandler>();
        if (!handlers.Any())
        {
            throw new Exception("No implementations found for IRequestHandler, please ensure they are registered.");
        }

        foreach (var handler in handlers)
        {
            var handlerName = handler.GetType().Name;
            var handlerConfigs = options.Value.EventConfigs.FindAll(x => x.HandlerName == handlerName);
            if (handlerConfigs.Count == 0)
            {
                throw new ConfigurationErrorsException($"No config found for handler: {handlerName}");
            }

            handlerConfigs.ForEach(handlerConfig =>
            {
                if (string.IsNullOrWhiteSpace(handlerConfig.Version))
                {
                    throw new ConfigurationErrorsException($"Event configuration cannot have a null/empty version, EventName = {handlerConfig.Name}, HandlerName = {handlerConfig.HandlerName}");
                }
                
                var key = handler.EntityTypeName + handlerConfig.Version;
                var success = _handlers.TryAdd(key, handler);
                if (!success)
                {
                    throw new InvalidOperationException(
                        $"Unable to initialize handler cache for type {handler.EntityTypeName} version {handlerConfig.Version}");
                }

                _logger.LogInformation(
                    "HandlerQueue successfully added handler (HandlerName:{HandlerName}, EventName:{EventName}, EventVersion:{EventVersion}, Key:{Key})",
                    handlerName, handler.EntityTypeName, handlerConfig.Version, key);
            });
        }

        _logger.LogInformation("HandlerQueue loaded {HandlerCount} handlers", _handlers.Count);
    }

    public IRequestHandler GetRequestHandler(string entityTypeName, string version)
    {
        _handlers.TryGetValue(entityTypeName + version, out var handler);

        if (handler is null)
        {
            throw new InvalidOperationException($"Handler not found for {entityTypeName} version {version}");
        }

        return handler;
    }

    public bool GetRequestCurrentVersion(string entityTypeName, string version)
    {
        _handlers.TryGetValue(entityTypeName + version, out var handler);

        if (handler is null) return false;

        return true;
    }
}
ObserversRegistration.cs
using Microsoft.Extensions.Options;
using Polly.Registry;
using RequestRouting.Application.Commands.Interfaces;
using RequestRouting.Application.Configurations;
using RequestRouting.Application.Interface;
using RequestRouting.Application.Orchestrator;
using RequestRouting.Application.Query.Interfaces;
using RequestRouting.Domain.Constants;
using RequestRouting.Infrastructure.Kafka;
using RequestRouting.Observer.Observers;
using StackExchange.Redis;
using ucx.locking.@base.interfaces;
using ucx.locking.@base.models;
using Ucx.Locking.extensions;

namespace RequestRouting.Observer.Configuration;

public static class ObserversRegistration
{
    public static void AddObservers(this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton(c => InitializeAuthorizationCanceledObserver(c, config.GetValue<string>("KafkaSettings:Topic:ucxAuthorizationCanceledTopic")));
        services.AddSingleton(c => InitializeAuthorizationDismissedObserver(c, TopicNames.AuthorizationDismissed));
        services.AddSingleton(c => InitializeAuthorizationWithdrawnObserver(c, TopicNames.AuthorizationWithdrawn));
        services.AddSingleton(c => InitializeDueDateCalculatedObserver(c, TopicNames.DueDateCalculated));
        services.AddSingleton(c => InitializeDueDateMissedObserver(c, TopicNames.DueDateMissed));
        services.AddSingleton(c => InitializeDequeueRoutingRequestObserver(c, TopicNames.DequeueRoutingRequest));
        services.AddSingleton(c => InitializeFileAttachedObserver(c, TopicNames.FileAttached));
        services.AddSingleton(c => InitializeJurisdictionStateUpdatedObserver(c, TopicNames.JurisdictionStateUpdated));
        services.AddSingleton(c => InitializeMemberIneligibilitySubmittedObserver(c, TopicNames.MemberIneligibilitySubmitted));
        services.AddSingleton(c => InitializeMemberInfoUpdatedForRequestObserver(c, TopicNames.MemberInfoUpdatedForRequest));
        services.AddSingleton(c => InitializePeerToPeerActionSubmittedObserver(c, TopicNames.PeerToPeerActionSubmitted));
        services.AddSingleton(c => InitializePhysicianDecisionMadeObserver(c, TopicNames.PhysicianDecisionMade));
        services.AddSingleton(c => InitializeRequestForServiceSubmittedObserver(c, TopicNames.RequestForServiceSubmitted));
        services.AddSingleton(c => InitializeRequestUrgencyUpdatedObserver(c, TopicNames.RequestUrgencyUpdated));
        services.AddSingleton(c => InitializeRequestSpecialtyUpdatedObserver(c, TopicNames.RequestSpecialtyUpdated));
        services.AddSingleton(c => InitializeRequestLineOfBusinessUpdatedObserver(c, TopicNames.RequestLineOfBusinessUpdated));
        services.AddSingleton(c => InitializeRoutingRequestAssignedObserver(c, TopicNames.RoutingRequestAssigned));
        services.AddSingleton(c => InitializeStateConfigurationAddedObserver(c, TopicNames.StateConfigurationAdded));
        services.AddSingleton(c => InitializeStatusChangeObserver(c, TopicNames.StatusChange));
        services.AddSingleton(c => InitializeUserConfigurationUpdatedObserver(c, TopicNames.UserProfileConfiguration));
        services.AddSingleton(c => InitializeUserExcludedFromRequestObserver(c, TopicNames.UserExcludedFromRequest));
        services.AddSingleton(c => InitializeRoutableDeterminationObserver(c, TopicNames.RoutableDetermination));
        services.AddSingleton(c => InitializeRoutableDeterminationRequestForServiceSavedObserver(c, TopicNames.RequestForServiceSubmitted));
        services.AddSingleton(c=> InitializeMedicalDisciplineCorrectedObserver(c,TopicNames.MedicalDisciplineCorrected));
    }

    public static void StartObservers(this IServiceCollection servicesCollection, IServiceProvider services)
    {
        services.GetRequiredService<AuthorizationCanceledObserver>().StartConsumer(new CancellationToken());
        services.GetRequiredService<AuthorizationDismissedObserver>().StartConsumer(new CancellationToken());
        services.GetRequiredService<AuthorizationWithdrawnObserver>().StartConsumer(new CancellationToken());
        services.GetRequiredService<DueDateCalculatedObserver>().StartConsumer(new CancellationToken());
        services.GetRequiredService<DueDateMissedObserver>().StartConsumer(new CancellationToken());
        services.GetRequiredService<DequeueRoutingRequestObserver>().StartConsumer(new CancellationToken());
        services.GetRequiredService<FileAttachedObserver>().StartConsumer(new CancellationToken());
        services.GetRequiredService<JurisdictionStateUpdatedObserver>().StartConsumer(new CancellationToken());
        services.GetRequiredService<MemberIneligibilitySubmittedObserver>().StartConsumer(new CancellationToken());
        services.GetRequiredService<MemberInfoUpdatedForRequestObserver>().StartConsumer(new CancellationToken());
        services.GetRequiredService<PeerToPeerActionSubmittedObserver>().StartConsumer(new CancellationToken());
        services.GetRequiredService<PhysicianDecisionMadeObserver>().StartConsumer(new CancellationToken());
        services.GetRequiredService<RequestForServiceSubmittedObserver>().StartConsumer(new CancellationToken());
        services.GetRequiredService<RequestUrgencyUpdatedObserver>().StartConsumer(new CancellationToken());
        services.GetRequiredService<RequestSpecialtyUpdatedObserver>().StartConsumer(new CancellationToken());
        services.GetRequiredService<RequestLineOfBusinessUpdatedObserver>().StartConsumer(new CancellationToken());
        services.GetRequiredService<RoutingRequestAssignedObserver>().StartConsumer(new CancellationToken());
        services.GetRequiredService<StateConfigurationAddedObserver>().StartConsumer(new CancellationToken());
        services.GetRequiredService<StatusChangeObserver>().StartConsumer(new CancellationToken());
        services.GetRequiredService<UserExcludedFromRequestObserver>().StartConsumer(new CancellationToken());
        services.GetRequiredService<UserProfileConfigurationUpdatedObserver>().StartConsumer(new CancellationToken());
        services.GetRequiredService<RoutableDeterminationObserver>().StartConsumer(new CancellationToken());
        services.GetRequiredService<RoutableDeterminationRequestForServiceSavedObserver>().StartConsumer(new CancellationToken());
        services.GetRequiredService<MedicalDisciplineCorrectedObserver>().StartConsumer(new CancellationToken());
    }

    public static IServiceCollection AddLocking(this IServiceCollection services, string redisConnectionString = null,
                IConnectionMultiplexer connectionMultiplexer = null)
    {
        services.AddRedisLocking(o =>
        {
            o.ConnectionString = redisConnectionString;
            o.CustomConnectionMultiplexer = connectionMultiplexer;
        });
        return services;
    }
    private static AuthorizationCanceledObserver InitializeAuthorizationCanceledObserver(IServiceProvider c,
        string topicName)
    {
        var logger = c.GetService<ILogger<AuthorizationCanceledObserver>>();
        var kafkaConsumer = c.GetService<IKafkaConsumer>();
        var validator = c.GetService<IMessageValidator>();
        var orchestrator = CreateOrchestrator(c);
        var options = c.GetService<IOptions<AppSettings>>();

        return new AuthorizationCanceledObserver(logger, kafkaConsumer, validator, orchestrator, options, topicName);
    }

    private static AuthorizationDismissedObserver InitializeAuthorizationDismissedObserver(IServiceProvider c,
        string topicName)
    {
        var logger = c.GetService<ILogger<AuthorizationDismissedObserver>>();
        var kafkaConsumer = c.GetService<IKafkaConsumer>();
        var validator = c.GetService<IMessageValidator>();
        var orchestrator = CreateOrchestrator(c);
        var options = c.GetService<IOptions<AppSettings>>();

        return new AuthorizationDismissedObserver(logger, kafkaConsumer, validator, orchestrator, options, topicName);
    }

    private static AuthorizationWithdrawnObserver InitializeAuthorizationWithdrawnObserver(IServiceProvider c,
        string topicName)
    {
        var logger = c.GetService<ILogger<AuthorizationWithdrawnObserver>>();
        var kafkaConsumer = c.GetService<IKafkaConsumer>();
        var validator = c.GetService<IMessageValidator>();
        var orchestrator = CreateOrchestrator(c);
        var options = c.GetService<IOptions<AppSettings>>();

        return new AuthorizationWithdrawnObserver(logger, kafkaConsumer, validator, orchestrator, options, topicName);
    }

    private static DueDateCalculatedObserver InitializeDueDateCalculatedObserver(IServiceProvider c, string topicName)
    {
        var logger = c.GetService<ILogger<DueDateCalculatedObserver>>();
        var kafkaConsumer = c.GetService<IKafkaConsumer>();
        var validator = c.GetService<IMessageValidator>();
        var orchestrator = CreateOrchestrator(c);
        var options = c.GetService<IOptions<AppSettings>>();

        return new DueDateCalculatedObserver(logger, kafkaConsumer, validator, orchestrator, options, topicName);
    }

    private static DueDateMissedObserver InitializeDueDateMissedObserver(IServiceProvider c, string topicName)
    {
        var logger = c.GetService<ILogger<DueDateMissedObserver>>();
        var kafkaConsumer = c.GetService<IKafkaConsumer>();
        var validator = c.GetService<IMessageValidator>();
        var orchestrator = CreateOrchestrator(c);
        var options = c.GetService<IOptions<AppSettings>>();

        return new DueDateMissedObserver(logger, kafkaConsumer, validator, orchestrator, options, topicName);
    }

    private static DequeueRoutingRequestObserver InitializeDequeueRoutingRequestObserver(IServiceProvider c, string topicName)
    {
        var logger = c.GetService<ILogger<DequeueRoutingRequestObserver>>();
        var kafkaConsumer = c.GetService<IKafkaConsumer>();
        var validator = c.GetService<IMessageValidator>();
        var orchestrator = CreateOrchestrator(c);
        var options = c.GetService<IOptions<AppSettings>>();

        return new DequeueRoutingRequestObserver(logger, kafkaConsumer, validator, orchestrator, options, topicName);
    }

    private static FileAttachedObserver InitializeFileAttachedObserver(IServiceProvider c, string topicName)
    {
        var logger = c.GetService<ILogger<FileAttachedObserver>>();
        var kafkaConsumer = c.GetService<IKafkaConsumer>();
        var validator = c.GetService<IMessageValidator>();
        var orchestrator = CreateOrchestrator(c);
        var options = c.GetService<IOptions<AppSettings>>();

        return new FileAttachedObserver(logger, kafkaConsumer, validator, orchestrator, options, topicName);
    }

    private static JurisdictionStateUpdatedObserver InitializeJurisdictionStateUpdatedObserver(IServiceProvider c, string topicName)
    {
        var logger = c.GetService<ILogger<JurisdictionStateUpdatedObserver>>();
        var kafkaConsumer = c.GetService<IKafkaConsumer>();
        var validator = c.GetService<IMessageValidator>();
        var orchestrator = CreateOrchestrator(c);
        var options = c.GetService<IOptions<AppSettings>>();

        return new JurisdictionStateUpdatedObserver(logger, kafkaConsumer, validator, orchestrator, options, topicName);
    }

    private static MemberIneligibilitySubmittedObserver InitializeMemberIneligibilitySubmittedObserver(
        IServiceProvider c, string topicName)
    {
        var logger = c.GetService<ILogger<MemberIneligibilitySubmittedObserver>>();
        var kafkaConsumer = c.GetService<IKafkaConsumer>();
        var validator = c.GetService<IMessageValidator>();
        var orchestrator = CreateOrchestrator(c);
        var options = c.GetService<IOptions<AppSettings>>();

        return new MemberIneligibilitySubmittedObserver(logger, kafkaConsumer, validator, orchestrator, options, topicName);
    }

    private static MemberInfoUpdatedForRequestObserver InitializeMemberInfoUpdatedForRequestObserver(IServiceProvider c,
        string topicName)
    {
        var logger = c.GetService<ILogger<MemberInfoUpdatedForRequestObserver>>();
        var kafkaConsumer = c.GetService<IKafkaConsumer>();
        var validator = c.GetService<IMessageValidator>();
        var orchestrator = CreateOrchestrator(c);
        var options = c.GetService<IOptions<AppSettings>>();

        return new MemberInfoUpdatedForRequestObserver(logger, kafkaConsumer, validator, orchestrator, options, topicName);
    }

    private static Orchestrator CreateOrchestrator(IServiceProvider c)
    {
        var logger = c.GetService<ILogger<Orchestrator>>();
        var eventHistory = c.GetService<IEventHistoryRepository>();
        var replayService = c.GetService<IReplayService>();
        var policyRegistry = c.GetService<IReadOnlyPolicyRegistry<string>>();
        var requestIndexingRepo = c.GetService<IRequestIndexingRepository>();
        var requestSearchRepo = c.GetService<IRequestSearchRepository>();
        var userProfileCacheService = c.GetService<IUserProfileCacheService>();
        var options = c.GetService<IOptions<AppSettings>>();
        var handlerQueue = c.GetService<IHandlerQueue>();
        var lockingService= c.GetService<ILockingService>(); 
        var loptions= c.GetService<IOptions<LockOptions>>();

        return new Orchestrator(eventHistory, replayService, logger, policyRegistry, requestIndexingRepo, options, userProfileCacheService, handlerQueue, requestSearchRepo,lockingService,loptions);
    }

    private static PeerToPeerActionSubmittedObserver InitializePeerToPeerActionSubmittedObserver(IServiceProvider c,
        string topicName)
    {
        var logger = c.GetService<ILogger<PeerToPeerActionSubmittedObserver>>();
        var kafkaConsumer = c.GetService<IKafkaConsumer>();
        var validator = c.GetService<IMessageValidator>();
        var orchestrator = CreateOrchestrator(c);
        var options = c.GetService<IOptions<AppSettings>>();

        return new PeerToPeerActionSubmittedObserver(logger, kafkaConsumer, validator, orchestrator, options, topicName);
    }

    private static PhysicianDecisionMadeObserver InitializePhysicianDecisionMadeObserver(IServiceProvider c,
        string topicName)
    {
        var logger = c.GetService<ILogger<PhysicianDecisionMadeObserver>>();
        var kafkaConsumer = c.GetService<IKafkaConsumer>();
        var validator = c.GetService<IMessageValidator>();
        var orchestrator = CreateOrchestrator(c);
        var options = c.GetService<IOptions<AppSettings>>();

        return new PhysicianDecisionMadeObserver(logger, kafkaConsumer, validator, orchestrator, options, topicName);
    }

    private static RequestForServiceSubmittedObserver InitializeRequestForServiceSubmittedObserver(IServiceProvider c,
        string topicName)
    {
        var logger = c.GetService<ILogger<RequestForServiceSubmittedObserver>>();
        var kafkaConsumer = c.GetService<IKafkaConsumer>();
        var validator = c.GetService<IMessageValidator>();
        var orchestrator = CreateOrchestrator(c);
        var options = c.GetService<IOptions<AppSettings>>();

        return new RequestForServiceSubmittedObserver(logger, kafkaConsumer, validator, orchestrator, options, topicName);
    }

    private static RequestUrgencyUpdatedObserver InitializeRequestUrgencyUpdatedObserver(IServiceProvider c,
        string topicName)
    {
        var logger = c.GetService<ILogger<RequestUrgencyUpdatedObserver>>();
        var kafkaConsumer = c.GetService<IKafkaConsumer>();
        var validator = c.GetService<IMessageValidator>();
        var orchestrator = CreateOrchestrator(c);
        var options = c.GetService<IOptions<AppSettings>>();

        return new RequestUrgencyUpdatedObserver(logger, kafkaConsumer, validator, orchestrator, options, topicName);
    }
    private static RequestSpecialtyUpdatedObserver InitializeRequestSpecialtyUpdatedObserver(IServiceProvider c,
    string topicName)
    {
        var logger = c.GetService<ILogger<RequestSpecialtyUpdatedObserver>>();
        var kafkaConsumer = c.GetService<IKafkaConsumer>();
        var validator = c.GetService<IMessageValidator>();
        var orchestrator = CreateOrchestrator(c);
        var options = c.GetService<IOptions<AppSettings>>();

        return new RequestSpecialtyUpdatedObserver(logger, kafkaConsumer, validator, orchestrator, options, topicName);
    }

    private static RoutingRequestAssignedObserver InitializeRoutingRequestAssignedObserver(IServiceProvider c,
        string topicName)
    {
        var logger = c.GetService<ILogger<RoutingRequestAssignedObserver>>();
        var kafkaConsumer = c.GetService<IKafkaConsumer>();
        var validator = c.GetService<IMessageValidator>();
        var orchestrator = CreateOrchestrator(c);
        var options = c.GetService<IOptions<AppSettings>>();

        return new RoutingRequestAssignedObserver(logger, kafkaConsumer, validator, orchestrator, options, topicName);
    }

    private static RequestLineOfBusinessUpdatedObserver InitializeRequestLineOfBusinessUpdatedObserver(IServiceProvider c,
        string topicName)
    {
        var logger = c.GetService<ILogger<RequestLineOfBusinessUpdatedObserver>>();
        var kafkaConsumer = c.GetService<IKafkaConsumer>();
        var validator = c.GetService<IMessageValidator>();
        var orchestrator = CreateOrchestrator(c);
        var options = c.GetService<IOptions<AppSettings>>();

        return new RequestLineOfBusinessUpdatedObserver(logger, kafkaConsumer, validator, orchestrator, options, topicName);
    }

    private static StateConfigurationAddedObserver InitializeStateConfigurationAddedObserver(IServiceProvider c,
        string topicName)
    {
        var logger = c.GetService<ILogger<StateConfigurationAddedObserver>>();
        var handler = c.GetService<IStateConfigurationAddedHandler>();
        var kafkaConsumer = c.GetService<IKafkaConsumer>();
        var options = c.GetService<IOptions<AppSettings>>();

        return new StateConfigurationAddedObserver(logger, handler, kafkaConsumer, options, topicName);
    }

    private static StatusChangeObserver InitializeStatusChangeObserver(IServiceProvider c,
        string topicName)
    {
        var logger = c.GetService<ILogger<StatusChangeObserver>>();
        var kafkaConsumer = c.GetService<IKafkaConsumer>();
        var validator = c.GetService<IMessageValidator>();
        var orchestrator = CreateOrchestrator(c);
        var options = c.GetService<IOptions<AppSettings>>();

        return new StatusChangeObserver(logger, kafkaConsumer, validator, orchestrator, options, topicName);
    }
    private static UserProfileConfigurationUpdatedObserver InitializeUserConfigurationUpdatedObserver(IServiceProvider c,
        string topicName)
    {
        var logger = c.GetService<ILogger<UserProfileConfigurationUpdatedObserver>>();
        var kafkaConsumer = c.GetService<IKafkaConsumer>();
        var handler = c.GetService<IUserProfileConfigurationUpdatedHandler>();
        var options = c.GetService<IOptions<AppSettings>>();
        var validator = c.GetService<IMessageValidator>();

        return new UserProfileConfigurationUpdatedObserver(logger, kafkaConsumer, handler, options, validator, topicName);
    }

    private static UserExcludedFromRequestObserver InitializeUserExcludedFromRequestObserver(IServiceProvider c,
    string topicName)
    {
        var logger = c.GetService<ILogger<UserExcludedFromRequestObserver>>();
        var kafkaConsumer = c.GetService<IKafkaConsumer>();
        var validator = c.GetService<IMessageValidator>();
        var orchestrator = CreateOrchestrator(c);
        var options = c.GetService<IOptions<AppSettings>>();

        return new UserExcludedFromRequestObserver(logger, kafkaConsumer, validator, orchestrator, options, topicName);
    }

    private static RoutableDeterminationObserver InitializeRoutableDeterminationObserver(IServiceProvider c,
        string topicName)
    {
        var logger = c.GetService<ILogger<RoutableDeterminationObserver>>();
        var kafkaConsumer = c.GetService<IKafkaRoutableDeterminationConsumer>();
        var options = c.GetService<IOptions<AppSettings>>();
        var handler = c.GetService<IRoutableDeterminationHandler>();
        var orchestrator = CreateOrchestrator(c);
        return new RoutableDeterminationObserver(logger, kafkaConsumer, orchestrator, options, topicName, handler);
    }

    private static RoutableDeterminationRequestForServiceSavedObserver InitializeRoutableDeterminationRequestForServiceSavedObserver(IServiceProvider c,
       string topicName)
    {
        var logger = c.GetService<ILogger<RoutableDeterminationRequestForServiceSavedObserver>>();
        var kafkaConsumer = c.GetService<IKafkaRoutableDeterminationConsumer>();
        var options = c.GetService<IOptions<AppSettings>>();
        var handler = c.GetService<IRoutableDeterminiationRequestForServiceSavedHandler>();
        return new RoutableDeterminationRequestForServiceSavedObserver(logger, kafkaConsumer, options, topicName, handler);
    }

    private static MedicalDisciplineCorrectedObserver InitializeMedicalDisciplineCorrectedObserver(IServiceProvider c,
    string topicName)
    {
        var logger = c.GetService<ILogger<MedicalDisciplineCorrectedObserver>>();
        var kafkaConsumer = c.GetService<IKafkaConsumer>();
        var validator = c.GetService<IMessageValidator>();
        var orchestrator = CreateOrchestrator(c);
        var options = c.GetService<IOptions<AppSettings>>();

        return new MedicalDisciplineCorrectedObserver(logger, kafkaConsumer, validator, orchestrator, options, topicName);
    }
}
using RequestRouting.Application.Services;

namespace RequestRouting.Observer.Configuration
{
    public class AzureADGraphAPIClientConfiguration : AzureGraphApiClientConfigurationBase
    {
        private readonly IConfiguration _configuration;

        public AzureADGraphAPIClientConfiguration(IConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            ClientId = _configuration["AzureADGraphAPIClientSettings:ClientId"];
            ClientSecret = _configuration["AzureADGraphAPIClientSettings:ClientSecret"];
            Tenant = _configuration["AzureADGraphAPIClientSettings:Tenant"];
        }

        public override string ClientId { get; set; }
        public override string ClientSecret { get; set; }
        public override string Tenant { get; set; }
    }
}

using RequestRouting.Application.Query.Requests;
using RequestRouting.Domain.Enums;

namespace RequestRouting.Infrastructure.Utils;

public static class AzureSearchFilterBuilder
{
    public static string BuildFilterString(RequestSearchRequest searchRequest)
    {
        var combinedFiltersWithAND = new List<string>
        {
            $"{FilterFieldNamesEnum.ExcludedUsernames.ToString()}/all(x: not search.in(x, '{string.Join(",", searchRequest.UserEmail)}', ','))",
            $"search.in({FilterFieldNamesEnum.Status.ToString()}, '{string.Join(",", searchRequest.RoutableStatuses)}', ',')",
            $"not({FilterFieldNamesEnum.AssignedToId.ToString()} ne '{searchRequest.UserEmail}' and {FilterFieldNamesEnum.AssignedToId.ToString()} ne null and {FilterFieldNamesEnum.AssignmentExpirationUtc.ToString()} gt {DateTime.UtcNow:O})",
            $"(((search.in({FilterFieldNamesEnum.Status.ToString()}, '{string.Join(",", searchRequest.RoutableSslStatuses)}'))  and search.in({FilterFieldNamesEnum.JurisdictionState.ToString()}, '{string.Join(",", searchRequest.UserStateLicenses??new List<string>())}', ',') and (search.in({FilterFieldNamesEnum.Specialty.ToString()}, '{string.Join(",", searchRequest.Specialties)}', ',')))"
           

        };
        var andFilters = combinedFiltersWithAND.Any() ? string.Join(" and ", combinedFiltersWithAND) : "";

        var combinedFilters = new List<string>
        {
            andFilters
        };

        if (searchRequest.IsNonDisciplineSslRouting)
        {
            combinedFilters.Add($"(((search.in({FilterFieldNamesEnum.Status.ToString()}, '{string.Join(",", searchRequest.RoutableNonSslStatuses)}')) and (search.in({FilterFieldNamesEnum.Specialty.ToString()}, '{string.Join(",", searchRequest.Specialties)}', ','))) and  ({FilterFieldNamesEnum.SuspendExpirationUtc.ToString()} lt {DateTime.UtcNow:O} or {FilterFieldNamesEnum.SuspendExpirationUtc.ToString()} eq null)))");

        }
        else
        {
            combinedFilters.Add($"(((search.in({FilterFieldNamesEnum.Specialty.ToString()}, '{string.Join(",", searchRequest.Specialties)}', ','))) and  ({FilterFieldNamesEnum.SuspendExpirationUtc.ToString()} lt {DateTime.UtcNow:O} or {FilterFieldNamesEnum.SuspendExpirationUtc.ToString()} eq null)))");
        }

        //Handling Post Decsion cases in GetNext
        //combinedFilters.Add( $"(search.in({FilterFieldNamesEnum.Status.ToString()}, '{string.Join(",", new List<string>{StatusEnum.Reconsideration.ToString(),StatusEnum.Appeal.ToString(),StatusEnum.AppealRecommended.ToString() })}', ',')  and search.in({FilterFieldNamesEnum.Specialty.ToString()}, '{string.Join(",", searchRequest.Specialties)}', ',') and {FilterFieldNamesEnum.AssignedToId.ToString()} ne null and {FilterFieldNamesEnum.AssignedToId.ToString()} eq '{searchRequest.UserEmail}' and {FilterFieldNamesEnum.AssignmentExpirationUtc.ToString()} gt {DateTime.UtcNow:O} and ({FilterFieldNamesEnum.SuspendExpirationUtc.ToString()} lt {DateTime.UtcNow:O} or {FilterFieldNamesEnum.SuspendExpirationUtc.ToString()} eq null))");

        //Handling Post Decsion and all manually assigned Ione cases irrespective of speciality
        combinedFilters.Add($"({FilterFieldNamesEnum.OriginSystem.ToString()} eq '{OriginSystemTypeEnum.IOne.ToString()}'" +
          $" and {FilterFieldNamesEnum.AssignedToId.ToString()} ne null and {FilterFieldNamesEnum.AssignedToId.ToString()} eq '{searchRequest.UserEmail}' and {FilterFieldNamesEnum.AssignmentExpirationUtc.ToString()} gt {DateTime.UtcNow.AddMinutes(searchRequest.AssignmentExpirationMinutes):O}" +
          $" and ({FilterFieldNamesEnum.SuspendExpirationUtc.ToString()} lt {DateTime.UtcNow:O} or {FilterFieldNamesEnum.SuspendExpirationUtc.ToString()} eq null))");

        return combinedFilters.Any() ? string.Join(" or ", combinedFilters) : "";
    }
}
using RequestRouting.Application.Query.Requests;
using RequestRouting.Infrastructure.AzureSearch;

namespace RequestRouting.Infrastructure.Utils;

public static class AzureSearchQueryBuilder
{
    public static string BuildSearchQuery(RequestSearchRequest searchRequest)
    {
        var searchQuery = new List<string>();
        if (searchRequest.Specialties != null && searchRequest.Specialties.Any())
        {
            var specialties = new List<string>();
            searchRequest.Specialties.ForEach(specialty =>
                specialties.Add($"\"{specialty}\""));
            searchQuery.Add($"{SearchFields.Specialty}:({string.Join(" ", specialties)})");
        }

        if (!string.IsNullOrWhiteSpace(searchRequest.Priority))
        {
            searchQuery.Add($"{SearchFields.Priority}:\"{searchRequest.Priority}\"");
        }

        if (!string.IsNullOrWhiteSpace(searchRequest.Status))
        {
            searchQuery.Add($"{SearchFields.Status}:\"{searchRequest.Status}\"");
        }

        if (searchRequest.UserStateLicenses != null && searchRequest.UserStateLicenses.Any())
        {
            var jurisdictionStates = new List<string>();
            searchRequest.UserStateLicenses.ForEach(jurisdictionState =>
                jurisdictionStates.Add($"\"{jurisdictionState}\""));
            searchQuery.Add($"{SearchFields.JurisdictionState}:({string.Join(" ", jurisdictionStates)})");
        }

        if (!string.IsNullOrWhiteSpace(searchRequest.SslRequirementStatus))
        {
            searchQuery.Add($"{SearchFields.SslRequirementStatus}:\"{searchRequest.SslRequirementStatus}\"");
        }

        if (!string.IsNullOrWhiteSpace(searchRequest.UserEmail))
        {
            searchQuery.Add($"{SearchFields.AssignedToId}:\"{searchRequest.UserEmail}\"");
        }

        return searchQuery.Any() ? string.Join(" | ", searchQuery) : "";
    }
}

using Azure.Search.Documents.Indexes.Models;

namespace RequestRouting.Infrastructure.Utils;

public static class AzureSearchScoringProfileFunctions
{
    /// <summary>
    ///     Azure Search uses negative timespans as related to the future dated fields.  It will boost more if it is less than
    ///     now.
    ///     This function is used by the azure search profile to boost based on current time and timespan in the future
    ///     entered.
    /// </summary>
    /// <param name="fieldName">Datetime field to use for the boost</param>
    /// <param name="boostScoreBy">Double that you want to boost the score by</param>
    /// <param name="timeSpan">
    ///     Timespan on which you want to boost the scores in the future. Please provide a positive time
    ///     span.
    /// </param>
    /// <param name="interpolation"></param>
    /// <returns>The freshness scoring function to use within the azure search scoring profile</returns>
    public static FreshnessScoringFunction AddFutureDateTimeBoostFunction(string fieldName, double boostScoreBy,
        TimeSpan timeSpan, ScoringFunctionInterpolation interpolation)
    {
        return new FreshnessScoringFunction(fieldName, boostScoreBy, new FreshnessScoringParameters(-timeSpan))
        {
            Interpolation = interpolation
        };
    }

    /// <summary>
    ///     Azure Search uses positive as related to the searching the latest created items.
    ///     This function is used by the azure search profile to boost based on current time and timespan entered.
    /// </summary>
    /// <param name="fieldName">Datetime field to use for the boost</param>
    /// <param name="boostScoreBy">Double that you want to boost the score by</param>
    /// <param name="timeSpan">
    ///     Timespan on which you want to boost the scores closest to now. Please provide a positive time
    ///     span.
    /// </param>
    /// <param name="interpolation"></param>
    /// <returns>The freshness scoring function to use within the azure search scoring profile</returns>
    public static FreshnessScoringFunction AddPresentDateTimeBoostFunction(string fieldName, double boostScoreBy,
        TimeSpan timeSpan, ScoringFunctionInterpolation interpolation)
    {
        return new FreshnessScoringFunction(fieldName, boostScoreBy, new FreshnessScoringParameters(timeSpan))
        {
            Interpolation = interpolation
        };
    }
}
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Microsoft.Extensions.Logging;
using RequestRouting.Application.Interface;
using RequestRouting.Application.Requests;
using RequestRouting.Infrastructure.Utils;

namespace RequestRouting.Infrastructure.Services;

public class ScoringProfileService : IScoringProfileService
{
    private readonly ILogger<ScoringProfileService> _logger;
    private readonly SearchIndexClient _searchIndexClient;

    public ScoringProfileService(SearchIndexClient searchIndexClient, ILogger<ScoringProfileService> logger)
    {
        _searchIndexClient = searchIndexClient;
        _logger = logger;
    }

    public async Task AddAsync(string indexName, AddScoringProfileRequest addScoringProfileRequest)
    {
        try
        {
            _logger.LogInformation("ScoringProfileService:AddAsync started");
            var index = await _searchIndexClient.GetIndexAsync(indexName);

            var existingScoringProfile =
                index.Value.ScoringProfiles.FirstOrDefault(x => x.Name == addScoringProfileRequest.ScoringProfileName);
            if (existingScoringProfile != null)
            {
                _logger.LogInformation(
                    "Found scoring profile {ScoringProfileName}, and it will be removed to be updated",
                    addScoringProfileRequest.ScoringProfileName);
                index.Value.ScoringProfiles.Remove(existingScoringProfile);
            }

            var newScoringProfile = new ScoringProfile(addScoringProfileRequest.ScoringProfileName)
            {
                TextWeights = new TextWeights(addScoringProfileRequest.Weights)
            };

            if (addScoringProfileRequest.FreshnessFunctions is { Count: > 0 })
            {
                foreach (var freshnessFunctionParam in addScoringProfileRequest.FreshnessFunctions)
                {
                    var interpolation = (ScoringFunctionInterpolation)Enum.Parse(typeof(ScoringFunctionInterpolation),
                        freshnessFunctionParam.Interpolation);
                    if (freshnessFunctionParam.IsFutureDated)
                    {
                        newScoringProfile.Functions.Add(
                            AzureSearchScoringProfileFunctions.AddFutureDateTimeBoostFunction(
                                freshnessFunctionParam.FieldName, freshnessFunctionParam.BoostValue,
                                TimeSpan.Parse(freshnessFunctionParam.BoostingDuration), interpolation));
                    }
                    else
                    {
                        newScoringProfile.Functions.Add(
                            AzureSearchScoringProfileFunctions.AddPresentDateTimeBoostFunction(
                                freshnessFunctionParam.FieldName, freshnessFunctionParam.BoostValue,
                                TimeSpan.Parse(freshnessFunctionParam.BoostingDuration), interpolation));
                    }
                }
            }

            index.Value.ScoringProfiles.Add(newScoringProfile);

            await _searchIndexClient.CreateOrUpdateIndexAsync(index);
            _logger.LogInformation("ScoringProfileService:AddAsync ended");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to add scoring profile for index {IndexName}", indexName);
            throw;
        }
    }

    public async Task DeleteAsync(string indexName, string scoringProfileName)
    {
        try
        {
            _logger.LogInformation("ScoringProfileService:DeleteAsync started");
            var index = await _searchIndexClient.GetIndexAsync(indexName);
            var existingScoringProfile =
                index.Value.ScoringProfiles.FirstOrDefault(x => x.Name == scoringProfileName);
            if (existingScoringProfile == null)
            {
                _logger.LogInformation("Scoring Profile {ScoringProfileName} does not exist in index {IndexName}",
                    scoringProfileName, indexName);
                return;
            }

            index.Value.ScoringProfiles.Remove(existingScoringProfile);
            await _searchIndexClient.CreateOrUpdateIndexAsync(index);
            _logger.LogInformation("ScoringProfileService:DeleteAsync ended");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to delete scoring profile for index {IndexName}", indexName);
            throw;
        }
    }
}

using System.Diagnostics;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Logging;
using RequestRouting.Application.Query.Interfaces;
using RequestRouting.Application.Query.Requests;
using RequestRouting.Application.Query.Responses;
using RequestRouting.Infrastructure.Utils;

namespace RequestRouting.Infrastructure.Repositories;

public class RequestSearchRepository : IRequestSearchRepository
{
    private readonly ILogger<RequestSearchRepository> _logger;
    private readonly SearchClient _searchClient;

    public RequestSearchRepository(ILogger<RequestSearchRepository> logger, SearchClient searchClient)
    {
        _logger = logger;
        _searchClient = searchClient;
    }

    public async Task<List<RequestSearchResponse>> SearchAsync(RequestSearchRequest searchRequest)
    {
        var requestSearchResponseList = new List<RequestSearchResponse>();
        try
        {
            var stopWatch = new Stopwatch();
            stopWatch.Start();
            _logger.LogInformation("RequestSearchRepository:SearchAsync started");

            var filterString = AzureSearchFilterBuilder.BuildFilterString(searchRequest);
            _logger.LogDebug("RequestSearchRepository:SearchAsync Filter string value {FilterString}", filterString);
            var searchOptions = new SearchOptions
            {
                SearchMode = SearchMode.Any,
                QueryType = SearchQueryType.Full,
                IncludeTotalCount = true,
                Size = 50,
                ScoringStatistics = ScoringStatistics.Global,
                ScoringProfile = searchRequest.ScoringProfile,
                Filter = filterString
            };

            var searchQuery = AzureSearchQueryBuilder.BuildSearchQuery(searchRequest);
            _logger.LogDebug("RequestSearchRepository:SearchAsync search query value {SearchQuery}", searchQuery);
            var searchResults =
                await _searchClient.SearchAsync<RequestResponse>(searchQuery, searchOptions);
            await foreach (var response in searchResults.Value.GetResultsAsync())
            {
                var requestSearchResponse = new RequestSearchResponse
                {
                    Score = response.Score,
                    RequestResponse = response.Document
                };
                requestSearchResponseList.Add(requestSearchResponse);
            }

            var searchId = string.Empty;
            if (searchResults.GetRawResponse().Headers.TryGetValues("x-ms-azs-searchid", out var headerValues))
            {
                searchId = headerValues.FirstOrDefault();
            }

            var properties = new Dictionary<string, object>
            {
                { "SearchServiceName", "RequestRouting" },
                { "SearchId", searchId },
                { "QueryTerms", searchQuery },
                { "ResultCount", searchResults.Value.TotalCount.ToString() },
                { "ScoringProfile", searchRequest.ScoringProfile }
            };

            stopWatch.Stop();
            var timeInMilliseconds = stopWatch.ElapsedMilliseconds;
            using (_logger.BeginScope(properties))
            {
                _logger.LogInformation(
                    "RequestSearchRepository:SearchAsync retrieved {Count} requests in {TimeInMilliseconds} milliseconds",
                    requestSearchResponseList.Count, timeInMilliseconds);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Request search failed");
            throw;
        }

        _logger.LogInformation("RequestSearchRepository:SearchAsync ended");

        return requestSearchResponseList;
    }

    public async Task<RequestSearchResponse> SearchByIdAsync(Guid requestForServiceKey)
    {
        var requestSearchResponseList = new List<RequestSearchResponse>();
        try
        {
            var stopWatch = new Stopwatch();
            stopWatch.Start();
            _logger.LogInformation("RequestSearchRepository:SearchByIdAsync started");

            var searchOptions = new SearchOptions
            {
                SearchMode = SearchMode.All,
                QueryType = SearchQueryType.Full,
                IncludeTotalCount = true,
            };

            var searchResults = await _searchClient.SearchAsync<RequestResponse>($"'{requestForServiceKey}'", searchOptions);
            await foreach (var response in searchResults.Value.GetResultsAsync())
            {
                var requestSearchResponse = new RequestSearchResponse
                {
                    Score = response.Score,
                    RequestResponse = response.Document
                };
                requestSearchResponseList.Add(requestSearchResponse);
            }

            stopWatch.Stop();
            var timeInMilliseconds = stopWatch.ElapsedMilliseconds;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Request search failed");
            throw;
        }

        _logger.LogInformation("RequestSearchRepository:SearchByIdAsync ended");

        return requestSearchResponseList.FirstOrDefault();
    }
}

using Azure;
using Azure.Search.Documents;
using Microsoft.Extensions.Logging;
using RequestRouting.Application.Interface;
using RequestRouting.Application.Query.Responses;
using RequestRouting.Domain.Aggregates;
using RequestRouting.Infrastructure.AzureSearch;

namespace RequestRouting.Infrastructure.Repositories;

public class RequestIndexingRepository : IRequestIndexingRepository
{
    private readonly ILogger<RequestIndexingRepository> _logger;
    private readonly SearchClient _searchClient;

    public RequestIndexingRepository(ILogger<RequestIndexingRepository> logger, SearchClient searchClient)
    {
        _logger = logger;
        _searchClient = searchClient;
    }

    public async Task<RequestResponse> GetAsync(Guid requestForServiceKey)
    {
        var customFields = new Dictionary<string, object>
        {
            { "RequestForServiceKey", requestForServiceKey },
        };

        using (_logger.BeginScope(customFields))
        {
            try
            {
                _logger.LogInformation("RequestIndexingRepository:GetAsync started");
                var document = await _searchClient.GetDocumentAsync<RequestResponse>(requestForServiceKey.ToString());
                _logger.LogInformation("RequestIndexingRepository:GetAsync ended");
                return document;
            }
            catch (RequestFailedException ex)
            {
                if (ex.Status == 404)
                {
                    _logger.LogInformation(ex, "RequestIndexingRepository:GetAsync Request Not Found In Index");
                    return null;
                }

                _logger.LogError(ex, "RequestIndexingRepository:GetAsync Failed");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RequestIndexingRepository:GetAsync Failed");
                throw;
            }
        }
    }

    public async Task DeleteAsync(RequestAggregate request, DateTime createDate)
    {
        var customFields = new Dictionary<string, object>
        {
            { "RequestForServiceKey", request.RequestForServiceKey },
            { "Specialty", request.Specialty }
        };

        using (_logger.BeginScope(customFields))
        {
            try
            {
                _logger.LogInformation("RequestIndexingRepository:DeleteAsync Deleting Request from index started");
                await _searchClient.DeleteDocumentsAsync(new[] { RequestIndex.FromDomain(request, createDate) });
                _logger.LogInformation("RequestIndexingRepository:DeleteAsync Deleting Request from index ended");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RequestIndexingRepository:DeleteAsync Failed");
                throw;
            }
        }
    }

    public async Task UpsertWithAssignmentAsync(RequestAggregate request, DateTime createDate)
    {
        var customFields = new Dictionary<string, object>
        {
            { "RequestForServiceKey", request.RequestForServiceKey },
            { "Specialty", request.Specialty }
        };

        using (_logger.BeginScope(customFields))
        {
            _logger.LogInformation("RequestIndexingRepository:UpsertWithAssignmentAsync started");

            var searchItem = RequestUpsertDto.FromDomain(request, createDate);

            await UpsertAsync(request, new[] { searchItem });

            _logger.LogInformation("RequestIndexingRepository:UpsertWithAssignmentAsync ended");
        }
    }

    public async Task UpsertWithoutAssignmentAsync(RequestAggregate request, DateTime createDat)
    {
        var customFields = new Dictionary<string, object>
        {
            { "RequestForServiceKey", request.RequestForServiceKey },
            { "Specialty", request.Specialty }
        };

        using (_logger.BeginScope(customFields))
        {
            _logger.LogInformation("RequestIndexingRepository:UpsertWithoutAssignmentAsync started");

            var searchItem = RequestUpsertWithoutAssignmentDto.FromDomain(request, createDat);
            await UpsertAsync(request, new[] { searchItem });

            _logger.LogInformation("RequestIndexingRepository:UpsertWithoutAssignmentAsync ended");
        }
    }

    private async Task UpsertAsync(RequestAggregate request, IEnumerable<object> documents)
    {
        var customFields = new Dictionary<string, object>
        {
            { "RequestForServiceKey", request.RequestForServiceKey },
            { "Specialty", request.Specialty }
        };

        using (_logger.BeginScope(customFields))
        {
            try
            {
                await _searchClient.MergeOrUploadDocumentsAsync(documents);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RequestIndexingRepository:UpsertAsync Failed");
                throw;
            }
        }
    }
}

using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RequestRouting.Application.Configurations;

namespace RequestRouting.Infrastructure.Kafka;

public static class KafkaProducer
{
    private static IProducer<string, string> BuildProducer(this IConfiguration configuration)
    {
        var options = configuration.GetSection("KafkaSettings").Get<KafkaSettings>();
        var config = new ProducerConfig
        {
            BootstrapServers = options.BootstrapServers,
            SecurityProtocol = Enum.Parse<SecurityProtocol>(options.SecurityProtocol),
            SslCaLocation = options.SslCaLocation,
            SaslMechanism = SaslMechanism.ScramSha512,
            SaslPassword = options.SaslPassword,
            SaslUsername = options.SaslUsername,
            SocketNagleDisable = true
        };

        return new ProducerBuilder<string, string>(config).Build();
    }

    public static void RegisterProducer(this IServiceCollection services, IConfiguration configuration)
    {
        if (configuration == null)
        {
            services.AddSingleton<IProducer<string, string>>(_ => null!);
        }

        services.AddSingleton(_ => BuildProducer(configuration));
    }
}
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using RequestRouting.Application.Configurations;

namespace RequestRouting.Infrastructure.Kafka;

public class KafkaConsumer : IKafkaConsumer
{
    private readonly IOptions<KafkaSettings> _options;

    public KafkaConsumer(IOptions<KafkaSettings> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public IConsumer<string, string> GetConsumer()
    {
        var kafkaConfig = new ConsumerConfig
        {
            BootstrapServers = _options.Value.BootstrapServers,
            GroupId = _options.Value.GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnablePartitionEof = true,
            SecurityProtocol = Enum.Parse<SecurityProtocol>(_options.Value.SecurityProtocol),
            SslCaLocation = _options.Value.SslCaLocation,
            SaslMechanism = SaslMechanism.ScramSha512,
            SaslPassword = _options.Value.SaslPassword,
            SaslUsername = _options.Value.SaslUsername
        };

        return new ConsumerBuilder<string, string>(kafkaConfig).Build();
    }
}

using Azure.Search.Documents.Indexes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace RequestRouting.Infrastructure.HealthChecks;

public static class HealthCheckExtensions
{
    public static IHealthChecksBuilder AddAzureSearchHealthCheck(
        this IHealthChecksBuilder builder,
        string[] indexNames, SearchIndexClient searchIndexClient,
        HealthStatus? failureStatus = default,
        IEnumerable<string> tags = default,
        TimeSpan? timeout = default)
    {
        var healthcheckRegistration = new HealthCheckRegistration("AzureSearch",
            new AzureSearchHealthCheck(indexNames, searchIndexClient),
            failureStatus,
            tags,
            timeout);
        return builder.Add(healthcheckRegistration);
    }
}

using Azure.Search.Documents.Indexes;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace RequestRouting.Infrastructure.HealthChecks;

public class AzureSearchHealthCheck : IHealthCheck
{
    private readonly string[] _indexNames;
    private readonly SearchIndexClient _searchIndexClient;

    public AzureSearchHealthCheck(string[] indexNames, SearchIndexClient searchIndexClient)
    {
        _indexNames = indexNames;
        _searchIndexClient = searchIndexClient;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = new())
    {
        try
        {
            var allIndexes = _searchIndexClient.GetIndexes().Select(x => x.Name).ToList();
            foreach (var indexName in _indexNames)
            {
                if (!allIndexes.Contains(indexName))
                {
                    return HealthCheckResult.Unhealthy($"Azure search index {indexName} does not exist.");
                }
            }

            return HealthCheckResult.Healthy("All azure search indexes are available.");
        }
        catch (Exception e)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, exception: e);
        }
    }
}
using Azure;
using Azure.Core;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using RequestRouting.Application.Configurations;
using RequestRouting.Application.Requests;
using RequestRouting.Infrastructure.AzureSearch;
using RequestRouting.Infrastructure.Utils;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RequestRouting.Infrastructure.Configs;

public static class AzureCognitiveSearchConfigs
{
    public static void AddSearchStorageDependencies(this IServiceCollection services,
        IConfiguration configuration)
    {
        var uri = new Uri(configuration.GetValue<string>("SearchDB:Endpoint"));
        var key = new AzureKeyCredential(configuration.GetValue<string>("SearchDB:Key"));
        var scoringProfiles = configuration.GetSection("SearchDB:ScoringProfiles").Get<List<RequestScoringProfile>>();

        SearchIndexClient clientIndex = new(uri, key);

        // Get all existing indexes
        var currentIndexes = clientIndex.GetIndexes();

        // Create Index
        CreateSearchIndex(configuration.GetValue<string>("SearchDB:RoutingIndexName"), clientIndex, currentIndexes, scoringProfiles);

        // Routing Index SearchClient
        services.AddSingleton(c =>
            CreateSearchClient(configuration.GetValue<string>("SearchDB:RoutingIndexName"), uri, key));

        // Add SearchIndexClient to update scoring profiles via api calls
        services.AddSingleton(c => new SearchIndexClient(uri, key));
    }

    private static SearchClient CreateSearchClient(string indexName, Uri uri, AzureKeyCredential key)
    {
        var clientOptions = new SearchClientOptions();
        clientOptions.AddPolicy(new SearchIdPipelinePolicy(), HttpPipelinePosition.PerCall);
        return new SearchClient(uri, indexName, key, clientOptions);
    }

    private static void CreateSearchIndex(string indexName, SearchIndexClient clientIndex,
        Pageable<SearchIndex> currentIndexes, List<RequestScoringProfile> scoringProfiles)
    {
        var index = currentIndexes.FirstOrDefault(i => i.Name == indexName);        
        var fieldBuilder = new FieldBuilder();
        var searchFields = fieldBuilder.Build(typeof(RequestIndex));
        var requestsIndex = new SearchIndex(indexName, searchFields);

        foreach (var requestScoringProfile in scoringProfiles)
        {
            var scoringProfile = CreateScoringProfile(requestScoringProfile);
            requestsIndex.ScoringProfiles.Add(scoringProfile);
        }

        clientIndex.CreateOrUpdateIndex(requestsIndex);
    }

    private static ScoringProfile CreateScoringProfile(RequestScoringProfile addScoringProfile)
    {

        var newScoringProfile = new ScoringProfile(addScoringProfile.ScoringProfileName)
        {
            TextWeights = new TextWeights(addScoringProfile.Weights)
        };

        if (addScoringProfile.FreshnessFunctions is { Count: > 0 })
        {
            foreach (var freshnessFunctionParam in addScoringProfile.FreshnessFunctions)
            {
                var interpolation = (ScoringFunctionInterpolation)Enum.Parse(typeof(ScoringFunctionInterpolation),
                    freshnessFunctionParam.Interpolation);
                if (freshnessFunctionParam.IsFutureDated)
                {
                    newScoringProfile.Functions.Add(
                        AzureSearchScoringProfileFunctions.AddFutureDateTimeBoostFunction(
                            freshnessFunctionParam.FieldName, freshnessFunctionParam.BoostValue,
                            TimeSpan.Parse(freshnessFunctionParam.BoostingDuration), interpolation));
                }
                else
                {
                    newScoringProfile.Functions.Add(
                        AzureSearchScoringProfileFunctions.AddPresentDateTimeBoostFunction(
                            freshnessFunctionParam.FieldName, freshnessFunctionParam.BoostValue,
                            TimeSpan.Parse(freshnessFunctionParam.BoostingDuration), interpolation));
                }
            }
        }

        return newScoringProfile;

    }
}


using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Newtonsoft.Json;
using RequestRouting.Domain.Aggregates;

namespace RequestRouting.Infrastructure.AzureSearch;

public class RequestIndex
{
    [JsonProperty("RequestForServiceKey")]
    [SearchableField(IsKey = true, IsFilterable = true)]
    public string RequestForServiceKey { get; set; }

    [JsonProperty("AuthorizationKey")]
    [SimpleField]
    public string AuthorizationKey { get; set; }

    [JsonProperty("RequestForServiceId")]
    [SearchableField]
    public string RequestForServiceId { get; set; }

    [JsonProperty("MedicalDiscipline")]
    [SearchableField(IsFilterable = true, AnalyzerName = LexicalAnalyzerName.Values.EnLucene)]
    public string MedicalDiscipline { get; set; }

    [JsonProperty("Specialty")]
    [SearchableField(IsFilterable = true, AnalyzerName = LexicalAnalyzerName.Values.EnLucene)]
    public string Specialty { get; set; }

    [JsonProperty("Priority")]
    [SearchableField(IsFilterable = true, AnalyzerName = LexicalAnalyzerName.Values.EnLucene)]
    public string Priority { get; set; }

    [JsonProperty("Status")]
    [SearchableField(IsFilterable = true, AnalyzerName = LexicalAnalyzerName.Values.EnLucene)]
    public string Status { get; set; }

    [JsonProperty("JurisdictionState")]
    [SearchableField(IsFilterable = true, AnalyzerName = LexicalAnalyzerName.Values.EnLucene)]
    public string JurisdictionState { get; set; }

    [JsonProperty("SslRequirementStatus")]
    [SearchableField(IsFilterable = true, AnalyzerName = LexicalAnalyzerName.Values.EnLucene)]
    public string SslRequirementStatus { get; set; }

    [JsonProperty("DueDateUtc")]
    [SimpleField(IsFilterable = true)]
    public DateTime? DueDateUtc { get; set; }

    [JsonProperty("ExcludedUsernames")]
    [SearchableField(IsFilterable = true, AnalyzerName = LexicalAnalyzerName.Values.EnLucene)]
    public List<string> ExcludedUsernames { get; set; }

    [JsonProperty("AssignedToId")]
    [SearchableField(IsFilterable = true, AnalyzerName = LexicalAnalyzerName.Values.EnLucene)]
    public string AssignedToId { get; set; }

    [JsonProperty("AssignmentExpirationUtc")]
    [SimpleField(IsFilterable = true)]
    public DateTime? AssignmentExpirationUtc { get; set; }

    [JsonProperty("OriginSystem")]
    [SearchableField(IsFilterable = true)]
    public string OriginSystem { get; set; }

    [JsonProperty("SuspendExpirationUtc")]
    [SimpleField(IsFilterable = true)]
    public DateTime? SuspendExpirationUtc { get; set; }

    [JsonProperty("CreateDate")]
    [SearchableField]
    public string CreateDate { get; set; }

    public static RequestIndex FromDomain(RequestAggregate request, DateTime createDate)
    {
        return new RequestIndex
        {
            RequestForServiceKey = request.RequestForServiceKey.ToString(),
            AuthorizationKey = request.AuthorizationKey.ToString(),
            RequestForServiceId = request.RequestForServiceId,
            MedicalDiscipline = request.MedicalDiscipline.ToString(),
            Specialty = request.Specialty.ToString(),
            Priority = request.Priority.ToString(),
            Status = request.Status.ToString(),
            JurisdictionState = request.JurisdictionState,
            SslRequirementStatus = request.SslRequirementStatus.ToString(),
            DueDateUtc = request.DueDateUtc,
            ExcludedUsernames = request.ExcludedUsernames,
            AssignedToId = request.AssignedToId,
            AssignmentExpirationUtc = request.AssignmentExpirationUtc,
            SuspendExpirationUtc = request.SuspendExpirationUtc,
            CreateDate = createDate.ToString()
        };
    }
}
using RequestRouting.Domain.Enums;
using RequestRouting.Domain.ValueObjects;

namespace RequestRouting.Domain.Aggregates;

public class RequestAggregate
{
    public Guid AuthorizationKey { get; private set; }

    public Guid RequestForServiceKey { get; private set; }

    public string RequestForServiceId { get; private set; }

    public SpecialtyEnum Specialty { get; private set; }

    public DisciplineEnum MedicalDiscipline { get; private set; }

    public Guid InsuranceCarrierKey { get; private set; }

    public LineOfBusinessEnum LineOfBusiness { get; private set; }

    public PriorityEnum Priority { get; private set; }

    public StatusEnum Status { get; private set; }

    public StatusEnum PriorPeerStatus { get; private set; }

    public string JurisdictionState { get; private set; }

    public DateTime? StartDateUtc { get; set; }

    public DateTime? DueDateUtc { get; private set; }

    public string AssignedToId { get; private set; }

    public DateTime? AssignmentExpirationUtc { get; private set; }

    public List<string> ExcludedUsernames { get; private set; } = new();

    public SslRequirementStatusEnum SslRequirementStatus { get; private set; }

    public string OriginSystem { get; private set; }

    public DateTime? SuspendExpirationUtc { get; private set; }

    public bool IsPermanentAssignment { get; private set; }

    public bool IsPostDecisionRequestRoutable()
    {
       return ((Status is StatusEnum.Appeal or StatusEnum.AppealRecommended or StatusEnum.Reconsideration) && (AssignmentExpirationUtc != null && AssignmentExpirationUtc >= DateTime.UtcNow)) && DueDateUtc is not null && !DateTime.MinValue.Equals(DueDateUtc);
    }
    public bool IsRoutable()
    {
        var filterDate = new DateTime(2023, 03, 01).ToUniversalTime();
        if (StartDateUtc == null || DateTime.MinValue.Equals(StartDateUtc) || StartDateUtc < filterDate)
        {
            return false;
        }

        return (Status is StatusEnum.NeedsMdReview or
            StatusEnum.NeedsSslSignoff && DueDateUtc is not null && !DateTime.MinValue.Equals(DueDateUtc)) || IsPostDecisionRequestRoutable();
    }
    public bool IsPostDecisionRequestDeletable()
    {
        return ((Status is StatusEnum.Appeal or StatusEnum.AppealRecommended or StatusEnum.Reconsideration) && (AssignmentExpirationUtc == null || AssignmentExpirationUtc < DateTime.UtcNow));
    }

    public bool IsRemoveableSpecialStatesFromSecondReview(List<string> commercialStatesToBeRemoved,
       List<string> commercialMedicaidStatesToBeRemoved)
    {
        if(IsPermanentAssignment == false && Status == StatusEnum.NeedsSslSignoff && MedicalDiscipline == DisciplineEnum.Radiology  && !string.IsNullOrEmpty(JurisdictionState) 
            && LineOfBusiness == LineOfBusinessEnum.Commercial && commercialStatesToBeRemoved != null 
            && commercialStatesToBeRemoved.Any(item => item.Contains(JurisdictionState?.ToLower()))

        ||(IsPermanentAssignment == false && Status == StatusEnum.NeedsSslSignoff && MedicalDiscipline == DisciplineEnum.Radiology && !string.IsNullOrEmpty(JurisdictionState) 
            && (LineOfBusiness == LineOfBusinessEnum.Commercial || LineOfBusiness == LineOfBusinessEnum.Medicaid) && commercialMedicaidStatesToBeRemoved != null 
            && commercialMedicaidStatesToBeRemoved.Any(item => item.Contains(JurisdictionState?.ToLower())))){

            return true;
        }
        return false;
    }

   
    public bool IsDeletable()
    {
        return Status is StatusEnum.Dismissed or
            StatusEnum.Canceled or
            StatusEnum.MemberIneligible or
            StatusEnum.NeedsP2PReview or
            StatusEnum.Withdrawn or
            StatusEnum.Hold or
            StatusEnum.RecommendedAppeal or
            StatusEnum.Approved or
            StatusEnum.Denied or
            StatusEnum.Expired or
            StatusEnum.PartialApproval or
            StatusEnum.DenialRecommended or
            StatusEnum.Dequeue or
            StatusEnum.OONRequestPendingPlanApproval or
            StatusEnum.NeedsNurseReview or
            StatusEnum.InEligibilityReview or
            StatusEnum.SiteOfCareOutreach or
            StatusEnum.PendingClaimOrAppealReviewByHealthPlan || IsPostDecisionRequestDeletable();
    }

    // create / updates ---------------------------------------------------------------------------------------

    public void UpdateAssignment(string userEmail, int assignmentExpirationMinutes)
    {
        AssignedToId = userEmail;
        AssignmentExpirationUtc = DateTime.UtcNow.AddMinutes(assignmentExpirationMinutes);
    }

    public void UpdateSuspendExpiration(DateTimeOffset? suspendExpirationTime, DateTimeOffset? suspendedCaseAssignmentExpirationTime)
    {
        SuspendExpirationUtc = suspendExpirationTime.HasValue ? suspendExpirationTime.Value.UtcDateTime : null;
        if (AssignmentExpirationUtc.HasValue && suspendedCaseAssignmentExpirationTime.HasValue && AssignmentExpirationUtc <= suspendedCaseAssignmentExpirationTime.Value.UtcDateTime)
        {
            //UCX Internally routed and suspended cases will have Assignment expiration of less than or equal to 4 hrs 
            AssignmentExpirationUtc = suspendedCaseAssignmentExpirationTime.Value.UtcDateTime;
        }
    }

    public void UpdateAssignment(string assignedToId, DateTime assignmentExpirationUtc)
    {
        if (string.IsNullOrWhiteSpace(assignedToId))
        {
            throw new ArgumentException("assignedToId is invalid");
        }

        if (DateTime.MinValue.Equals(assignmentExpirationUtc))
        {
            throw new ArgumentException("assignmentExpirationUtc is invalid");
        }
        if(assignedToId !=AssignedToId && SuspendExpirationUtc!=null )
        {
            SuspendExpirationUtc = null;//Note:The previous suspension is not valid when the Assignment changes. This will be handled in planned replay .Replay only will normalize this change
        }

        AssignedToId = assignedToId;
        AssignmentExpirationUtc = assignmentExpirationUtc;
    }

    public void UpdateDequeueRequestRouting()
    {
        Status = StatusEnum.Dequeue;
    }

    public void UpdateDueDate(DateTime dueDateUtc)
    {
        DueDateUtc = dueDateUtc;

        //If DueDate recalculated for already suspended request
        if (SuspendExpirationUtc != null && SuspendExpirationUtc >= DueDateUtc)
        {
            SuspendExpirationUtc = null;
        }
    }

    public void UpdateDueDateCalculated(DueDateCalculatedObject dueDateCalculatedObject)
    {
        RequestForServiceKey = dueDateCalculatedObject.RequestForServiceKey;
        UpdateDueDate(dueDateCalculatedObject.DueDateUtc);
    }

    public void UpdateDueDateMissed()
    {
        if (StatusEnum.Hold.Equals(Status))
        {
            UpdateStatus(StatusEnum.NeedsMdReview);
        }
    }

    public void UpdateFileAttached(string attachmentType)
    {
        if (attachmentType.ToLower() == "clinicalinformationreceived" && Status == StatusEnum.Hold)
        {
            Status = StatusEnum.NeedsNurseReview;
        }
    }

    public void UpdateInsuranceCarrierKey(Guid insuranceCarrierKey, StateConfigurationAggregate stateConfiguration)
    {
        InsuranceCarrierKey = insuranceCarrierKey;
        UpdateSslDetails(stateConfiguration);
    }

    public void UpdateJurisdictionState(StateConfigurationAggregate stateConfiguration)
    {
        UpdateSslDetails(stateConfiguration);
    }

    public void UpdateLineOfBusiness(LineOfBusinessEnum lineOfBusiness)
    {
        LineOfBusiness = lineOfBusiness;
    }

    public void UpdateLineOfBusinessSslDetails(LineOfBusinessEnum lineOfBusiness, StateConfigurationAggregate stateConfiguration)
    {
        LineOfBusiness = lineOfBusiness;
        UpdateSslDetails(stateConfiguration);
    }

    public void UpdateSpecialty(SpecialtyEnum specialty)
    {
        Specialty = specialty;
    }
    public void UpdateMedicalDiscipline(DisciplineEnum discipline)
    {
        MedicalDiscipline = discipline;
    }
    public void UpdateMemberInfoUpdatedForRequest(StateConfigurationAggregate stateConfiguration)
    {
        if (Status == StatusEnum.NeedsEligibilityReview)
        {
            var status = Specialty != SpecialtyEnum.Sleep || Priority == PriorityEnum.Urgent
                ? StatusEnum.NeedsNurseReview
                : StatusEnum.Hold;
            Status = status;
        }

        UpdateJurisdictionState(stateConfiguration);
    }

    public void UpdatePeerToPeerStatus(PeerToPeerActionSubmittedObject peerToPeerActionSubmittedDto)
    {
        if (peerToPeerActionSubmittedDto.PeerToPeerActionType == PeerToPeerActionTypeEnum.Withdrawn)
        {
            UpdateStatus(PriorPeerStatus);
        }
        else if (peerToPeerActionSubmittedDto.PeerToPeerActionType == PeerToPeerActionTypeEnum.Scheduled)
        {
            UpdatePriorPeerStatus(Status);
            UpdateStatus(StatusEnum.NeedsP2PReview);
        }

        UpdateMedicalDiscipline(peerToPeerActionSubmittedDto.MedicalDiscipline);
    }

    public void UpdatePriorPeerStatus(StatusEnum priorPeerStatus)
    {
        PriorPeerStatus = priorPeerStatus;
    }

    public void UpdateRequestUrgencyUpdated(RequestUrgencyUpdatedObject requestUrgencyUpdatedDto)
    {
        Priority = requestUrgencyUpdatedDto.Priority;
    }

    public void UpdateStatus(StatusEnum status)
    {
        if (!StatusEnum.StatusUnknown.Equals(status) && !StatusEnum.NeedsP2PReview.Equals(Status))
        {
            Status = status;
        }

        if (CanClearAssignment())  
        {
            ClearAssignment();
        }
    }

    public bool CanClearAssignment()
    {
        return (Status is StatusEnum.Approved or StatusEnum.Denied or StatusEnum.PartialApproval or StatusEnum.NeedsSslSignoff
            or StatusEnum.Reconsideration or StatusEnum.Appeal or StatusEnum.AppealRecommended);
    }

    public void UpdateStartDate(DateTime startDate)
    {
        StartDateUtc = startDate;
    }

    public void UpdatePermanentAssignment(bool isPermanentAssignment)
    {
        IsPermanentAssignment = isPermanentAssignment;
    }
    public static RequestAggregate Reconstitute(Guid authorizationKey,
        Guid requestForServiceKey,
        string requestForServiceId,
        SpecialtyEnum? specialty,
        Guid insuranceCarrierKey,
        LineOfBusinessEnum lineOfBusiness,
        PriorityEnum? priority,
        StatusEnum status,
        string jurisdictionState,
        SslRequirementStatusEnum sslRequirementStatus,
        string assignedToId,
        DateTime? assignmentExpirationUtc,
        List<string> excludedUsernames,
        DateTime? dueDate,
        string originSystem)
    {
        return new RequestAggregate
        {
            AuthorizationKey = authorizationKey,
            RequestForServiceKey = requestForServiceKey,
            RequestForServiceId = requestForServiceId,
            Specialty = specialty ?? SpecialtyEnum.Unknown,
            InsuranceCarrierKey = insuranceCarrierKey,
            LineOfBusiness = lineOfBusiness,
            Priority = priority?? PriorityEnum.PriorityUnknown,
            Status = status,
            JurisdictionState = jurisdictionState,
            SslRequirementStatus = sslRequirementStatus,
            AssignedToId = assignedToId,
            AssignmentExpirationUtc = assignmentExpirationUtc,
            ExcludedUsernames = excludedUsernames,
            DueDateUtc = dueDate,
            OriginSystem = originSystem
        };
    }

    public static bool IsRequestAssignedToDifferentUser(string assignedToId,
        string currentUserEmail,
        DateTime? assignmentExpiration)
    {
        return assignedToId != null && !assignedToId.Equals(currentUserEmail) && assignmentExpiration > DateTime.UtcNow;
    }

    public void ClearAssignmentForSkips(string skippedByUser)
    {
        if (!skippedByUser.Equals(AssignedToId, StringComparison.CurrentCultureIgnoreCase))
        {
            return;
        }

        AssignedToId = null;
        AssignmentExpirationUtc = null;
        SuspendExpirationUtc = null;
    }
    public void ClearAssignment()
    {
        AssignedToId = null;
        AssignmentExpirationUtc = null;
        SuspendExpirationUtc = null;
        IsPermanentAssignment = false ;
    }

    public void AddExclusion(string userId)
    {
        if (!string.IsNullOrWhiteSpace(userId) && !ExcludedUsernames.Contains(userId))
        {
            ExcludedUsernames.Add(userId);
        }
    }

    public void PopulateInitialRequestData(RequestForServiceSubmittedObject request, StateConfigurationAggregate stateConfiguration)
    {
        AuthorizationKey = request.AuthorizationKey;
        RequestForServiceKey = request.RequestForServiceKey;
        RequestForServiceId = request.RequestForServiceId;
        Priority = request.Priority;
        LineOfBusiness = request.LineOfBusiness;
        InsuranceCarrierKey = request.InsuranceCarrierKey;
        StartDateUtc = request.StartDateUtc;
        UpdateSpecialty(request.Specialty);
        UpdateMedicalDiscipline(request.MedicalDiscipline);
        UpdateStatus(request.Status);
        UpdateJurisdictionState(stateConfiguration);
        OriginSystem = request.OriginSystem;
    }

    #region "private"

    private void UpdateSslDetails(StateConfigurationAggregate stateConfiguration)
    {
        if (stateConfiguration == null || !stateConfiguration.IsSslRequired(LineOfBusiness, InsuranceCarrierKey))
        {
            SslRequirementStatus = SslRequirementStatusEnum.SslNotRequired;
            JurisdictionState = stateConfiguration?.Code;
            return;
        }
        //Do not consider State SSL Required as of now.
        SslRequirementStatus = SslRequirementStatusEnum.SslNotRequired;
        JurisdictionState = stateConfiguration.Code;
    }

    #endregion "private"
}
using System.ComponentModel;

namespace RequestRouting.Domain.Enums;

[DefaultValue(StatusUnknown)]
public enum DecisionOutcomesEnum
{
    StatusUnknown = 0,
    Approved = 1,
    Denied = 2,
    Withdrawn = 3,
    Expired = 4,
    PendingNurseReview = 5,
    PendingMDReview = 6,
    WaitingOnClinicalInfo = 7,
    Appeal = 8,
    PartialApproval = 9,
    RecommendedDenial = 11,
    Reconsideration = 12,
    NoChange = 13,
    Canceled = 14,
    PendingEligibilityReview = 15,
    RecommendedPartiallyApproved = 16,
    RecommendedUnableToExtend = 17,
    RecommendedAppeal = 18,
    NewRequest = 19,
    AppealRecommended = 20,
    OONRequestPendingPlanApproval = 21,
    NoDecisionMade = 22,
    InEligibilityReview = 23,
    SiteOfCareOutreach = 24,
    PendingClaimOrAppealReviewByHealthPlan = 25
}

using System.ComponentModel;

namespace RequestRouting.Domain.Enums;

[DefaultValue(Unknown)]
public enum DisciplineEnum
{
    [Description("UNKNOWN DISCIPLINE")]
    Unknown = 0,
    [Description("SLEEP")]
    Sleep = 1,
    [Description("POST-ACUTE CARE")]
    PostAcuteCare = 2,
    [Description("FERTILITY")]
    Fertility = 3,
    [Description("HOME HEALTH")]
    HomeHealth = 4,
    [Description("RADIOLOGY")]
    Radiology = 5,
    [Description("CARDIOLOGY")]
    Cardiology = 6,
    [Description("GASTRO")]
    Gastro = 7,
    [Description("DME")]
    DME = 8,
    [Description("MSK")]
    MSK = 9
}

using System.ComponentModel;

namespace RequestRouting.Domain.Enums;

[DefaultValue(Unknown)]
public enum SpecialtyEnum
{
    [Description("UNKNOWN MEDICAL SPECIALTY")]
    Unknown = 0,
    [Description("SLEEP")]
    Sleep = 1,
    [Description("POST-ACUTE CARE")]
    PostAcuteCare = 2,
    [Description("FERTILITY")]
    Fertility = 3,
    [Description("HOME HEALTH")]
    HomeHealth = 4,
    [Description("RADIOLOGY")]
    Radiology = 5,
    [Description("CARDIOLOGY")]
    Cardiology = 6,
    [Description("GASTRO")]
    Gastro = 7,
    [Description("DME")]
    DME = 8,
    [Description("INTENSITY-MODULATED RADIATION THERAPY")]
    IMRT = 9,
    [Description("SPECIALTY DRUGS")]
    SpecialtyDrugs = 10,
    [Description("RADIATION THERAPY")]
    RadiationTherapy = 11,
    [Description("MUSCULOSKELETAL MANAGEMENT")]
    MSK = 12,
    [Description("LAB")]
    Lab = 13,
    [Description("ALL")]
    All = 14,

    // radiology -----------------------------------

    [Description("MEDICAL ONCOLOGY")]
    MedicalOncology = 15,
    [Description("MSK SPINE")]
    MSKSpine = 16,
    [Description("MSK ST")]
    MSKST = 17,
    [Description("MSK PTOT")]
    MSKPTOT = 18,
    [Description("MSK PT")]
    MSKPT = 19,
    [Description("MSK PT - Therapist")]
    MSKPTTherapist = 20,
    [Description("MSK OT")]
    MSKOT = 21,
    [Description("MSK MT")]
    MSKMT = 22,
    [Description("MSK JOINT")]
    MSKJOINT = 23,
    [Description("MSK CHIRO")]
    MSKChiro = 24,
    [Description("MSK ACUMT")]
    MSKACUMT = 25,
    [Description("MSK ACU")]
    MSKACU = 26,
    [Description("MSK OT - Therapist")]
    MSKAOTTherapist = 27, 
   
    [Description("HEAD NEURO")]
    HeadNeuro = 28,
    [Description("MED ONC - PED")]
    MedOncPed = 29,
    [Description("ONC - ONC")]
    MedOncOnc = 30,
    [Description("MED ONC-COC")]
    MedOncCoC = 31,
    [Description("ONC - RT")]
    MedOncRT = 32,
    [Description("MED ONC")]
    MedOnc = 33,
    [Description("MED SURG")]
    MedSurg = 34,
    [Description("LOW TECH")]
    LowTech = 35,
    [Description("NUCLEAR MEDICINE")]
    NuclearMedicine = 36,
    [Description("NUCLEAR MEDICINE_CARDIOLOGY")]
    NuclearMedicineCardiology = 37,
    [Description("NUCLEAR MEDICINE_MED SURG")]
    NuclearMedicineMedSurg = 38,
    [Description("NUCLEAR MEDICINE_ONCOLOGY")]
    NuclearMedicineOncology = 39,
    [Description("NUCLEAR MEDICINE_SPINE ORTHO")]
    NuclearMedicineSpineOrtho = 40,
    [Description("OB ULTRASOUND")]
    OBUltrasound = 41,
    [Description("ONCOLOGY")]
    Oncology = 42,
    [Description("SPINE ORTHO")]
    SpineOrtho = 43,
    [Description("WOMANS IMAGING")]
    WomansImaging = 44,
    [Description("VASC")]
    VASC = 45,
    [Description("PAIN MANAGEMENT")]
    PainManagement = 46,

    // cardio ---------------------------------------

    [Description("CARD CATH")]
    CardCath = 47,
    [Description("CARD COMPLETE")]
    CardComplete = 48,
    [Description("CARD DEVICE")]
    CardDevice = 49,
    [Description("CARD ECHO")]
    CardEcho = 50,
    [Description("CARDIAC IMPLANTABLE")]
    CardiacImplantable = 51,

    // pac (post acute care) ----------------------

    [Description("LONG TERM ACUTE CARE")]
    LTAC = 52,
    [Description("INPATIENT REHABILITATION FACILITY")]
    IRF = 53,
    [Description("SKILLED NURSING FACILITY")]
    SNF = 54,

    // dme ----------------------------------------

    [Description("EDME")]
    eDME = 55
}

using System.ComponentModel;

namespace RequestRouting.Domain.Enums;

[DefaultValue(Unknown)]
public enum LineOfBusinessEnum
{
    LineOfBusinessUnknown = 0,
    Commercial = 1,
    Medicare = 2,
    Medicaid = 3,
    Unknown =4
}

namespace RequestRouting.Domain.Events;

public class RequestForServiceSubmittedEvent : UCXEvent
{
    public string RequestGeneratingAgent { get; set; }
    public string AuthorizationKey { get; set; }
    public string RequestSpecialty { get; set; }
    public string MedicalDiscipline { get; set; }
    public DateTime DateOfService { get; set; }
    public DateTime StartDate { get; set; }
    public bool IsUrgent { get; set; }
    public MemberPolicyEvent MemberPolicy { get; set; }
    public List<ProviderEvent> Providers { get; set; } = new();
    public List<SubmittedProcedureCodeEvent> SubmittedProcedureCodes { get; set; } = new();
    public List<DiagnosisEvent> Diagnoses { get; set; }
    public RequestedServicesEvent RequestedServices { get; set; }
    public string Status { get; set; }
    public string OriginStatus { get; set; }
}

using System.ComponentModel;

namespace RequestRouting.Domain.Enums;

[DefaultValue(StatusUnknown)]
public enum StatusEnum
{
    StatusUnknown = 0,
    NeedsNurseReview = 1,
    NeedsMdReview = 2,
    NeedsSslSignoff = 3,
    NeedsSpecialtySignoff = 4,
    NeedsP2PReview = 5,
    DenialRecommended = 6,
    Hold = 7,
    Incomplete = 8,
    NeedsEligibilityReview = 9,
    Skipped = 10,
    Dismissed = 11,
    Withdrawn = 12,
    Canceled = 13, 
    MemberIneligible = 14,
    Approved = 15,
    Denied = 16,
    Expired = 17,
    Appeal = 18,
    PartialApproval = 19,
    Dequeue = 20,
    RecommendedAppeal = 21,
    AppealRecommended = 22,
    OONRequestPendingPlanApproval = 23,
    NoDecisionMade = 24,
    AppealOrAppealRecommendedWithIOneAssignment = 25,
    InEligibilityReview = 26,
    SiteOfCareOutreach = 27,
    PendingClaimOrAppealReviewByHealthPlan = 28,
    Reconsideration = 29
}

using Microsoft.Extensions.Logging;
using RequestRouting.Application.Helpers;
using RequestRouting.Application.Interface;
using RequestRouting.Domain.Aggregates;
using RequestRouting.Domain.Enums;
using RequestRouting.Domain.Events;
using RequestRouting.Domain.ValueObjects;

namespace RequestRouting.Application.Commands.Handlers;

public class RequestForServiceSubmittedHandler : IRequestHandler
{
    private readonly ILogger<RequestForServiceSubmittedHandler> _logger;
    private readonly IStateConfigurationCacheService _stateConfigurationCacheService;

    public string EntityTypeName => "UCXRequestForServiceSubmitted";

    public RequestForServiceSubmittedHandler(IStateConfigurationCacheService stateConfigurationCacheService,
        ILogger<RequestForServiceSubmittedHandler> logger)
    {
        _stateConfigurationCacheService = stateConfigurationCacheService;
        _logger = logger;
    }

    public async Task<RequestAggregate> HandleAsync(RequestAggregate requestState, UCXEvent currentEvent)
    {
        _logger.LogDebug("{className} started", nameof(RequestForServiceSubmittedHandler));

        if (currentEvent is null) throw new ArgumentNullException(nameof(currentEvent));

        var request = currentEvent as RequestForServiceSubmittedEvent;

        if (request == null) return requestState;

        var specialty = SpecialtyEnum.Unknown;
        if (request.RequestSpecialty != null)
        {
            specialty = EnumDescriptionConverter.GetValueFromDescription<SpecialtyEnum>(request.RequestSpecialty.ToUpper());
        }

        var discipline = EnumDescriptionConverter.GetValueFromDescription<DisciplineEnum>(request.MedicalDiscipline.ToUpper());

        var authKeyParseSuccess = Guid.TryParse(request.AuthorizationKey, out var authKey);
        var insuranceCarrierParseSuccess = Guid.TryParse(request.MemberPolicy.InsuranceCarrierKey, out var carrierKey);
        var lineOfBusiness = !string.IsNullOrEmpty(request.MemberPolicy.LineofBusiness)
            ? Enum.Parse<LineOfBusinessEnum>(request.MemberPolicy.LineofBusiness, true)
            : LineOfBusinessEnum.LineOfBusinessUnknown;

        var statusEnum = StatusMappers.MapRequestForServiceSubmittedStatus(request.OriginStatus);

        var requestForServiceSubmitted = new RequestForServiceSubmittedObject
        {
            AuthorizationKey = authKeyParseSuccess ? authKey : Guid.Empty,
            RequestForServiceKey = Guid.Parse(request.RequestForServiceKey),
            RequestForServiceId = request.RequestForServiceId,
            Specialty = specialty,
            MedicalDiscipline = discipline,
            InsuranceCarrierKey = insuranceCarrierParseSuccess ? carrierKey : Guid.Empty,
            LineOfBusiness = lineOfBusiness,
            Priority = request.IsUrgent ? PriorityEnum.Urgent : PriorityEnum.Standard,
            Status = statusEnum,
            OriginSystem = request.OriginSystem,
            StartDateUtc = request.StartDate
        };

        var stateConfiguration = GetStateConfiguration(request);

        requestState.PopulateInitialRequestData(requestForServiceSubmitted, stateConfiguration);

        _logger.LogDebug("{className} ended", nameof(RequestForServiceSubmittedHandler));

        return requestState;

    }  

    private StateConfigurationAggregate GetStateConfiguration(RequestForServiceSubmittedEvent request)
    {
        var eventStateCode = request.MemberPolicy.StateCode;
        if (string.IsNullOrWhiteSpace(eventStateCode))
        {
            _logger.LogInformation("RequestForServiceSubmittedHandler:HandleAsync event does not contain request state");
            return null;
        }

        var stateConfiguration = _stateConfigurationCacheService.GetStateConfigurationByCode(eventStateCode);
        if (stateConfiguration == null)
        {
            _logger.LogError("RequestForServiceSubmittedHandler:HandleAsync state configuration not found for {eventStateCode}", eventStateCode);
        }

        return stateConfiguration;
    }
}

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RequestRouting.Application.Configurations;
using RequestRouting.Application.Helpers;
using RequestRouting.Application.Interface;
using RequestRouting.Domain.Aggregates;
using RequestRouting.Domain.Enums;
using RequestRouting.Domain.Events;

namespace RequestRouting.Application.Commands.Handlers;

public class PhysicianDecisionMadeHandler : IRequestHandler
{
    private readonly ILogger<PhysicianDecisionMadeHandler> _logger;
    private readonly IOptions<AppSettings> _options;

    public PhysicianDecisionMadeHandler(ILogger<PhysicianDecisionMadeHandler> logger, IOptions<AppSettings> options)
    {
        _logger = logger;
        _options = options;
    }

    public string EntityTypeName => "UcxPhysicianDecisionMade";

    public Task<RequestAggregate> HandleAsync(RequestAggregate requestState, UCXEvent currentEvent)
    {
        _logger.LogDebug("{className} started", nameof(PhysicianDecisionMadeHandler));

        if (currentEvent is null)
        {
            throw new ArgumentNullException($"Current event is null for {nameof(PhysicianDecisionMadeEvent)}");
        }

        if (currentEvent is not PhysicianDecisionMadeEvent request)
        {
            return Task.FromResult(requestState);
        }

        if (!Enum.TryParse(request.Outcome, true, out DecisionOutcomesEnum decisionStatus))
        {
            decisionStatus = DecisionOutcomesEnum.StatusUnknown;
        }

        _logger.LogInformation("{className} request status = {Status}", nameof(PhysicianDecisionMadeHandler), requestState.Status);

        //NoDecisionMade | Suspend 
        if (request.IsPendingInQueue == true)
        {
            requestState.UpdateSuspendExpiration(request.SuspendExpirationTime, request.SuspendedCaseAssignmentExpirationTime);

            _logger.LogInformation("{className} request status = {Status}, adding Suspend Expiration datetime {SuspendExpirationUtc} and setting Assignment Expiration time {AssignmentExpirationUtc}",
           nameof(PhysicianDecisionMadeHandler), requestState.Status.ToString(), requestState.SuspendExpirationUtc.ToString(), requestState.AssignmentExpirationUtc.ToString());
        }
        else
        {
            // Only run following if it is MD's who reviewing and putting into NeedsMdReview

            var jurisdictionStatesCommercial = _options.Value.JurisdictionStatesCommercial.Split(",", StringSplitOptions.None).ToList<string>();
            var jurisdictionStatesCommercialMedicaid = _options.Value.JurisdictionStatesCommercialMedicaid.Split(",", StringSplitOptions.None).ToList<string>();

            var status = StatusMappers.MapDecisionStatus(requestState.Status,
                decisionStatus,
                true,
                false);

            _logger.LogInformation("{className} decisionStatus = {DecisionStatus}, mapped status = {Status}",
                nameof(PhysicianDecisionMadeHandler), decisionStatus.ToString(), status.ToString());
            if (status == StatusEnum.NeedsSslSignoff)
            {
                _logger.LogDebug("{className} set status to {status}", nameof(PhysicianDecisionMadeHandler), status.ToString());
                requestState.UpdateStatus(status);
            }

            _logger.LogDebug("{className} ended", nameof(PhysicianDecisionMadeHandler));
        }

        _logger.LogDebug("{className} ended", nameof(PhysicianDecisionMadeHandler));

        return Task.FromResult(requestState);
    }
}

namespace RequestRouting.Domain.Events;

public abstract class UCXEvent
{
    public string RequestForServiceKey { get; set; }
    public string RequestForServiceId { get; set; }
    public string Name { get; set; }
    public string Version { get; set; }
    public string Key { get; set; }
    public string OriginSystem { get; set; }
    public DateTime Sent { get; set; }
}
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RequestRouting.Application.Events;
using RequestRouting.Domain.Events;

namespace RequestRouting.Application.Deserialization;

public class RoutingJsonConverter : JsonConverter
{
    // This converter handles only deserialization, not serialization.
    public override bool CanRead => true;

    public override bool CanWrite => false;

    public override bool CanConvert(Type objectType)
    {
        // Only if the target type is the abstract base class
        return objectType == typeof(UCXEvent);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        // First, just read the JSON as a JObject
        var obj = JObject.Load(reader);

        // Then look at the $type property:
        var typeName = obj["Name"]?.Value<string>();

        switch (typeName)
        {
            case "UCXAuthorizationCanceled":
                return obj.ToObject<AuthorizationCanceledEvent>(serializer);
            case "UCXAuthorizationDismissed":
                return obj.ToObject<AuthorizationDismissedEvent>(serializer);
            case "UCXAuthorizationWithdrawn":
                return obj.ToObject<AuthorizationWithdrawnEvent>(serializer);
            case "UCXDueDateCalculated":
                return obj.ToObject<DueDateCalculatedEvent>(serializer);
            case "UcxDueDateMissed":
                return obj.ToObject<DueDateMissedEvent>(serializer);
            case "UCXDequeueRoutingRequest":
                return obj.ToObject<DequeueRoutingRequestEvent>(serializer);
            case "UCXFileAttached":
                return obj.ToObject<FileAttachedEvent>(serializer);
            case "UcxJurisdictionStateUpdated":
                return obj.ToObject<JurisdictionStateUpdatedEvent>(serializer);
            case "UcxMemberIneligibilitySubmitted":
                return obj.ToObject<MemberIneligibilitySubmittedEvent>(serializer);
            case "UCXMemberInfoUpdatedForRequest":
                return obj.ToObject<MemberInfoUpdatedForRequestEvent>(serializer);
            case "UcxPeerToPeerActionSubmitted":
                return obj.ToObject<PeerToPeerActionSubmittedEvent>(serializer);               
            case "UcxPhysicianDecisionMade":
                return obj.ToObject<PhysicianDecisionMadeEvent>(serializer);
            case "UCXRequestForServiceSubmitted":
                return obj.ToObject<RequestForServiceSubmittedEvent>(serializer);
            case "UcxRequestLineOfBusinessUpdated":
                return obj.ToObject<RequestLineOfBusinessUpdatedEvent>(serializer);
            case "UCXRequestSpecialtyUpdated":
                return obj.ToObject<RequestSpecialtyUpdatedEvent>(serializer);
            case "UcxRequestUrgencyUpdated":
                return obj.ToObject<RequestUrgencyUpdatedEvent>(serializer);
            case "UCXRoutingRequestAssigned":
                return obj.ToObject<RoutingRequestAssignedEvent>(serializer);
            case "UcxExternalSystemStatusChange":
                return obj.ToObject<StatusChangeEvent>(serializer);
            case "UCXUserExcludedFromRequest":
                return obj.ToObject<UserExcludedFromRequestEvent>(serializer);
            case "UcxMedicalDisciplineCorrected":
                return obj.ToObject<MedicaDisciplineCorrectedEvent>(serializer);
            default:
                throw new InvalidOperationException($"Unknown type with type name: '{typeName}'");
        }
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        throw new NotSupportedException("This converter handles only deserialization, not serialization.");
    }
}

public static StatusEnum MapRequestForServiceSubmittedStatus(string eventStatus)
    {
        return eventStatus switch
        {
            "WaitingOnClinicalInfo" or "Waiting On Clinical Info" => StatusEnum.Hold,
            "PendingClinicalReview" => StatusEnum.NeedsNurseReview,
            "InMdReview" or "SendToMD" => StatusEnum.NeedsMdReview,
            "Reconsideration" => StatusEnum.NeedsNurseReview,
            "PartialApproval" => StatusEnum.PartialApproval,
            "PendingMDReview" => StatusEnum.NeedsMdReview,
            "RecommendedDenial" => StatusEnum.DenialRecommended,
            "Approved" => StatusEnum.Approved,
            "Canceled" => StatusEnum.Canceled,
            "Denied" => StatusEnum.Denied,
            "Appeal" => StatusEnum.Appeal,
            "PendingNurseReview" => StatusEnum.NeedsNurseReview,
            "Withdrawn" => StatusEnum.Withdrawn,
            "PendingEligibilityReview" => StatusEnum.NeedsEligibilityReview,
            "AppealRecommended" => StatusEnum.AppealRecommended,
            "OONRequestPendingPlanApproval" => StatusEnum.OONRequestPendingPlanApproval,
            _ => StatusEnum.StatusUnknown
        };
    }


using Microsoft.Extensions.Logging;
using RequestRouting.Application.Interface;
using RequestRouting.Domain.Aggregates;
using RequestRouting.Domain.Events;

namespace RequestRouting.Application.Replay;

public class ReplayService : IReplayService
{
    private readonly IHandlerQueue _handlerQueue;
    private readonly ILogger<ReplayService> _logger;

    public ReplayService(IHandlerQueue handlerQueue, ILogger<ReplayService> logger)
    {
        _handlerQueue = handlerQueue;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<RequestAggregate> ReplayEvents(List<UCXEvent> eventHistory)
    {
        var requestState = new RequestAggregate();

        foreach (var ucxEvent in eventHistory)
        {   
            if (ucxEvent.Name is null) throw new ArgumentNullException(nameof(ucxEvent.Name));
            if (ucxEvent.Version is null) throw new ArgumentNullException(nameof(ucxEvent.Version));
            
            var handler = _handlerQueue.GetRequestHandler(ucxEvent.Name, ucxEvent.Version);

            if (handler == null) throw new ArgumentNullException($"{ucxEvent.Name} does not have version: {ucxEvent.Version}");

            requestState = await handler.HandleAsync(requestState, ucxEvent);
            LogRequestState(requestState);
        }

        return requestState;
    }

    private void LogRequestState(RequestAggregate request)
    {
        if (request is null) return;

        var currentDateTime = DateTime.UtcNow;

        using (_logger.BeginScope(new Dictionary<string, string>
               {
                   { "StateAsOf", currentDateTime.ToShortTimeString()},
                   { "AuthorizationKey", request.AuthorizationKey.ToString() },
                   { "RequestForServiceKey", request.RequestForServiceKey.ToString() },
                   { "RequestForServiceId", request.RequestForServiceId },
                   { "Specialty", request.Specialty.ToString() },
                   { "InsuranceCarrierKey", request.InsuranceCarrierKey.ToString() },
                   { "LineOfBusiness", request.LineOfBusiness.ToString() },
                   { "Priority", request.Priority.ToString() },
                   { "RequestStatus", request.Status.ToString() },
                   { "PriorPeerStatus", request.PriorPeerStatus.ToString() },
                   { "JurisdictionState", request.JurisdictionState },
                   { "DueDateUtc", request.DueDateUtc.ToString() },
                   { "SslRequirementStatus", request.SslRequirementStatus.ToString() }
               }))
        {
            _logger.LogDebug("Replay current request state: {RequestForServiceKey} and Request Status {RequestStatus} as of {currentTime}", request.RequestForServiceKey, request.Status, currentDateTime);
        }
    }
}
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using RequestRouting.Application.Interface;

namespace RequestRouting.Application.Services
{
    public class AzureGraphApiClient : IAzureGraphClientEndpoint
    {
        private readonly string _azureGraphUri;
        private readonly string _tenant;
        private readonly GraphServiceClient _graphServiceClient;
        private readonly ILogger<AzureGraphApiClient> _logger;

        /// <summary>
        /// set up graph api client
        /// </summary>
        /// <param name="azureGraphApiClientConfiguration"></param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public AzureGraphApiClient(IAzureGraphApiClientConfiguration azureGraphApiClientConfiguration, ILogger<AzureGraphApiClient> logger)
        {
            _logger = logger;
            if (azureGraphApiClientConfiguration == null)
                throw new ArgumentNullException(nameof(azureGraphApiClientConfiguration));

            #region Gaurd
            if (string.IsNullOrWhiteSpace(azureGraphApiClientConfiguration.ClientId)
                || string.IsNullOrWhiteSpace(azureGraphApiClientConfiguration.ClientSecret)
                || string.IsNullOrWhiteSpace(azureGraphApiClientConfiguration.Tenant))
            {
                throw new ArgumentException(nameof(AzureGraphApiClient));
            }
            #endregion

            var options = new AuthorizationCodeCredentialOptions
            {
                AuthorityHost = AzureAuthorityHosts.AzurePublicCloud,
            };
            var clientSecretCredential = new ClientSecretCredential(
                azureGraphApiClientConfiguration.Tenant,
                azureGraphApiClientConfiguration.ClientId,
                azureGraphApiClientConfiguration.ClientSecret, options);
            var scopes = new[] { "https://graph.microsoft.com/.default" };

            _graphServiceClient = new GraphServiceClient(clientSecretCredential, scopes);
        }

        #region Private
        /// <summary>
        /// AD lookup by user email
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="graphClient"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        private async Task<User> GetAdUser(string email, GraphServiceClient graphClient)
        {
            if (graphClient != null)
            {
                try {
                    var result = await graphClient.Users[email].GetAsync((requestConfiguration) =>
                    {
                        requestConfiguration.QueryParameters.Select = new string[] { "id", "displayName", "userPrincipalName", "memberOf" };
                    });
                    
                   
                    return result;
                }
                catch(Exception ex) {
                    
                    _logger.LogInformation($"Could not find {email} in AD lookup.");
                    _logger.LogInformation(ex.Message);
                    return new User()
                        {
                            Id = Guid.NewGuid().ToString(),
                            DisplayName = email,
                            UserPrincipalName = email
                    };
                    
                    
                }
               
            }
            throw new ArgumentException(nameof(graphClient));
        }

        /// <summary>
        /// AD lookup by user id
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="graphClient"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        private async Task<User> GetAdUserByUserId(string userId, GraphServiceClient graphClient)
        {
            if (graphClient != null)
            {
                try
                {
                    var result = await graphClient.Users.GetAsync((requestConfiguration) =>
                    {
                        requestConfiguration.QueryParameters.Filter = $"startsWith(userPrincipalName, '{userId}')";
                        requestConfiguration.QueryParameters.Select = new string[] { "id", "displayName", "userPrincipalName", "memberOf" };
                    });

                    if (result != null && !result.Value.Any())
                    {
                        _logger.LogInformation($"Could not find {userId} in AD lookup.");
                        return new User()
                        {
                            Id = Guid.NewGuid().ToString(),
                            DisplayName = userId,
                            UserPrincipalName = userId
                        };
                    }
                    return result.Value.FirstOrDefault();
                }
                catch (Exception ex)
                {
                    _logger.LogInformation(ex.Message);
                }
               
                
            }
            throw new ArgumentException(nameof(graphClient));
        }

        /// <summary>
        /// Get AD groups by email
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="graphClient"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        private async Task<List<Group>> GetGroups(string userId, GraphServiceClient graphClient)
        {
            List<Group> groups = new List<Group>();
            if (graphClient != null)
            {
                try
                {
                    var response = await graphClient.Users[userId].MemberOf.GetAsync();
                    foreach (Group item in response?.Value)
                    {
                        groups.Add(item);
                    }
                    return groups;
                }
                catch(Exception ex)
                {
                    _logger.LogInformation($"Could not find {userId} in AD lookup.");
                    _logger.LogInformation(ex.Message);
                    return new List<Group>();
                }
                
            }
            throw new ArgumentException(nameof(graphClient));
        }

        #endregion

        /// <summary>
        /// Get AD groups belong to an user
        /// </summary>
        /// <param name="userid"> userid or user email or displayName</param>
        /// <returns></returns>
        public async Task<Domain.Aggregates.UserAggregate> GetSecurityGroupsByUserId(string userid)
        {
            Domain.Aggregates.UserAggregate adUser = null;
            List<string> adGroups = new List<string>();
            using (_graphServiceClient)
            {
                User result = userid.Contains('@')?await GetAdUser(userid, _graphServiceClient):await GetAdUserByUserId(userid, _graphServiceClient);

                if (result != null)
                {
                    var groups = await GetGroups(result.UserPrincipalName, _graphServiceClient);
                    foreach (Group group in groups)
                    {
                        adGroups.Add(group.DisplayName);
                    }
                    adUser = Domain.Aggregates.UserAggregate.Create(result.Id, result.UserPrincipalName, result.DisplayName, adGroups);
                }
                return adUser;
            }

        }

    }
}


using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph.Models.TermStore;
using Polly;
using Polly.Registry;
using Polly.Retry;
using RequestRouting.Application.Configurations;
using RequestRouting.Application.Constants;
using RequestRouting.Application.Events;
using RequestRouting.Application.Interface;
using RequestRouting.Application.Query.Interfaces;
using RequestRouting.Application.Requests;
using RequestRouting.Domain.Aggregates;
using RequestRouting.Domain.Enums;
using RequestRouting.Domain.Events;
using System.Globalization;
using System.Linq.Expressions;
using System.Xml.Linq;
using ucx.locking.@base.interfaces;
using ucx.locking.@base.models;

namespace RequestRouting.Application.Orchestrator;

public class Orchestrator : IOrchestrator
{
    private readonly AsyncRetryPolicy<bool> _cosmosPolicy;
    private readonly string[] _discipline;
    private readonly IEventHistoryRepository _historyRepository;
    private readonly ILogger<Orchestrator> _logger;
    private readonly int _maxEventsAllowed;
    private readonly IReplayService _replayService;
    private readonly IRequestIndexingRepository _requestIndexingRepository;
    private readonly IUserProfileCacheService _userProfileCacheService;
    private readonly IHandlerQueue _handlerQueue;
    private readonly IOptions<AppSettings> _options;
    private readonly IRequestSearchRepository _requestSearchRepository;
    private readonly ILockingService _lockingService;
    private readonly IOptions<LockOptions> _loptions;
    private readonly TimeSpan _acquireLockTimeout;
    private readonly TimeSpan _expireLockTimeout;

    public Orchestrator(IEventHistoryRepository historyRepository,
        IReplayService replayService,
        ILogger<Orchestrator> logger,
        IReadOnlyPolicyRegistry<string> policyRegistry,
        IRequestIndexingRepository requestIndexingRepository,
        IOptions<AppSettings> options,
        IUserProfileCacheService userProfileCacheService,
        IHandlerQueue handlerQueue,
        IRequestSearchRepository requestSearchRepository,
        ILockingService lockingService,
        IOptions<LockOptions> loptions
        )
    {
        _historyRepository = historyRepository ?? throw new ArgumentNullException(nameof(historyRepository));
        _replayService = replayService ?? throw new ArgumentNullException(nameof(replayService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _requestIndexingRepository = requestIndexingRepository ?? throw new ArgumentNullException(nameof(requestIndexingRepository));
        _cosmosPolicy = policyRegistry.Get<AsyncRetryPolicy<bool>>(PolicyConstants.RetryPolicy);
        _discipline = options.Value.Discipline.ToLower().Split(',');
        var maxEventsAllowedPerRequest = options.Value.MaxEventsAllowedPerRequest;
        _maxEventsAllowed = maxEventsAllowedPerRequest > 0 ? maxEventsAllowedPerRequest : 500;
        _userProfileCacheService = userProfileCacheService;
        _handlerQueue = handlerQueue ?? throw new ArgumentNullException(nameof(handlerQueue));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _requestSearchRepository = requestSearchRepository ?? throw new ArgumentNullException(nameof(requestSearchRepository));
        this._lockingService = lockingService;
        this._loptions = loptions;
        _acquireLockTimeout = loptions.Value.AcquireLockTimeout;
        _expireLockTimeout = loptions.Value.ExpireLockTimeout;
    }

    public async Task OrchestrateAsync(UCXEvent evt, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var context = new Context { { PolicyConstants.RetryCount, 0 }, { PolicyConstants.Logger, _logger } };

            var result = await _cosmosPolicy.ExecuteAndCaptureAsync(ctx => OrchestrateInternal_HandleConcurrency(evt), context);

            if (result.Outcome == OutcomeType.Failure) _logger.LogError("Routing Message Fault: exceeded retry attempts for {MessageKey}", evt.Key);

            return;
        }
    }
    private async Task<bool> OrchestrateInternal_HandleConcurrency(UCXEvent requestEvent)
    {
        _logger.LogInformation("Starting To Acquire Lock for {Name}  {RequestForServiceKey} ,for MessageKey= {Key} ", requestEvent.Name, requestEvent.RequestForServiceKey,requestEvent.Key);
        var LockId = _options.Value.ObserverType + "_" + requestEvent.RequestForServiceKey;
        bool isCompleted = false;
        try
        {
            await using (await _lockingService.LockAsync<string>(LockId, _acquireLockTimeout, _expireLockTimeout, default))
            {
                isCompleted = await OrchestrateInternal(requestEvent);
            }
            _logger.LogInformation("Lock Released by  {Name} : {RequestForServiceKey}  ,for MessageKey= {Key} ", requestEvent.Name, requestEvent.RequestForServiceKey, requestEvent.Key);
        }
        catch(Exception e)
        {
            if (e.Message== "Timeout exceeded when trying to acquire the lock")
            {
                _logger.LogInformation(e.Message+ " for  {Name} : {RequestForServiceKey}  ,for MessageKey= {Key} ", requestEvent.Name, requestEvent.RequestForServiceKey, requestEvent.Key);
                isCompleted = await OrchestrateInternal(requestEvent);
                _logger.LogInformation("Lock Released by  {Name} : {RequestForServiceKey}  ,for MessageKey= {Key} ", requestEvent.Name, requestEvent.RequestForServiceKey, requestEvent.Key);
            }
            _logger.LogInformation("Exception in handling  {Name} : {RequestForServiceKey}  ,for MessageKey= {Key} - {Message}", requestEvent.Name, requestEvent.RequestForServiceKey, requestEvent.Key, e.Message);
        }
        return isCompleted;
    }

    private async Task<bool> OrchestrateInternal(UCXEvent requestEvent)
    {
        try
        {
            if (requestEvent.GetType() == typeof(StatusChangeEvent))
            {
                var currentEvent = requestEvent as StatusChangeEvent;
                if (string.IsNullOrEmpty(currentEvent.UserType))
                {
                    var userProfile = (!string.IsNullOrEmpty(currentEvent.UserId) ? _userProfileCacheService.GetUserProfileByUserIdentifier(currentEvent.UserId) : null);
                    currentEvent.UserType = userProfile != null ? (userProfile.Result.UserType == UserTypeEnum.MD ? "MD" : "Nurse") : "Nurse";
                    requestEvent = currentEvent;
                }                
            }
            // look for this request in history
            var history = await _historyRepository.GetRequestEventHistory(requestEvent.RequestForServiceKey);

            // when there is no history for the request and status change event captured
            if (history?.History == null)
            {                
                    history = new RequestEventHistoryRequest
                    {
                        History = new List<UCXEvent> { requestEvent },
                        Id = requestEvent.RequestForServiceKey,
                        RequestForServiceKey = requestEvent.RequestForServiceKey
                    };                

                await _historyRepository.CreateRequestEventHistory(history);
                return true;
            }

            var eventHistory = history.History;

            if (eventHistory.Count >= _maxEventsAllowed)
            {
                _logger.LogInformation(new InvalidOperationException(), $"Request reached max events allowed per single request, RequestForServiceKey = {requestEvent.RequestForServiceKey}, MaxEventsAllowed = {_maxEventsAllowed}");
                return true;
            }

            AssessEventInHistory(ref eventHistory, ref requestEvent);
            eventHistory.Add(requestEvent);

            // sort by timestamp, now everything we know about is in order
            eventHistory.Sort((x, y) => x.Sent.CompareTo(y.Sent));

            // ensure RequestForServiceSubmitted appears first
            eventHistory = eventHistory.OrderByDescending(x => x.Name == "UCXRequestForServiceSubmitted").ToList();

            // save history
            history.History = eventHistory;
            _logger.LogInformation("Saving request history for request {requestForServiceKey} for event name {eventName}", requestEvent.RequestForServiceKey, requestEvent.Name);

            await _historyRepository.SaveRequestEventHistory(history);

            var determination = await RoutingDetermination(requestEvent, eventHistory);
            return determination;

        }
        catch (Exception e)
        {
            _logger.LogInformation("Got exception while orchestrating message {Key} : {Exception}", requestEvent.Key, e.Message);
            return false;
        }
    }

    public async Task<bool> RoutingDetermination(UCXEvent requestEvent, List<UCXEvent> eventHistory)
    {
        // fire replay logic - the result here is the most up to date state of the request
        var result = await _replayService.ReplayEvents(eventHistory);

        _logger.LogInformation("Saving request history for request {requestForServiceKey} for event name {eventName}", requestEvent.RequestForServiceKey, requestEvent.Name);

        if (!_discipline.Contains(result.MedicalDiscipline.ToString().ToLower()))
        {
            _logger.LogInformation("MedicalDiscipline is not currently actionable so not routable");
            return true;
        }

        // log final request state
        LogFinalRequestState(result);

        // save the current state into search
        var searchItemExists = await _requestSearchRepository.SearchByIdAsync(result.RequestForServiceKey);
        var jurisdictionStatesCommercial = _options.Value.JurisdictionStatesCommercial?.Split(",", StringSplitOptions.None).ToList<string>();
        var jurisdictionStatesCommercialMedicaid = _options.Value.JurisdictionStatesCommercialMedicaid?.Split(",", StringSplitOptions.None).ToList<string>();

        if (result.IsRoutable() & !result.IsRemoveableSpecialStatesFromSecondReview(jurisdictionStatesCommercial, jurisdictionStatesCommercialMedicaid))
        {
            _logger.LogInformation("Request {RequestForServiceKey} is routable, inserting/updating search index no search item", result.RequestForServiceKey);

            var createDate = DateTime.UtcNow;
            await _requestIndexingRepository.UpsertWithAssignmentAsync(result, createDate);

            _logger.LogInformation("Request {RequestForServiceKey} added", result.RequestForServiceKey);
        }       

        if (searchItemExists is not null && (result.IsDeletable() || result.IsRemoveableSpecialStatesFromSecondReview(jurisdictionStatesCommercial, jurisdictionStatesCommercialMedicaid)))
        {
            _logger.LogInformation("Request {RequestForServiceKey} is not routable, deleting from search index", result.RequestForServiceKey);
            await _requestIndexingRepository.DeleteAsync(result, DateTime.UtcNow);
            _logger.LogInformation("Request {RequestForServiceKey} removed", result.RequestForServiceKey);
        }

        _logger.LogInformation("Completed orchestration of event {Key}", requestEvent.Key);
        return true;
    }

    private void AssessEventInHistory(ref List<UCXEvent> requestEventHistory, ref UCXEvent requestEvent)
    {
        var replayVersion = _options.Value.ReplayVersion;
        var requestEventKey = requestEvent.Key;

        // replace existing event if the same kafka key
        if (requestEventHistory.Any(x => x.Key == requestEventKey)) requestEventHistory.RemoveAll(x => x.Key == requestEventKey);

        // remove all events if replaying different version
        if (replayVersion == true)
        {
            var removeList = new List<UCXEvent>();
            foreach (var item in requestEventHistory)
            {
                var currentVersion = _handlerQueue.GetRequestCurrentVersion(item.Name, item.Version);

                if (currentVersion == false) removeList.Add(item);
            }

            foreach (var item in removeList) requestEventHistory.Remove(item);
        }

        // check if exact micro-second sent date status change outcome
        foreach (var item in requestEventHistory)
        {
            if (item.Sent == requestEvent.Sent && (requestEvent.Name == "UcxExternalSystemStatusChange" || item.Name == "UcxExternalSystemStatusChange"))
            {
                if (requestEvent.GetType() == typeof(StatusChangeEvent))
                {
                    item.Sent = item.Sent.AddMilliseconds(-1);
                    break;
                }
                else if(item.GetType() == typeof(StatusChangeEvent))
                {
                    requestEvent.Sent = requestEvent.Sent.AddMilliseconds(-1);
                    break;
                }
            }
        }
    }

    private void LogFinalRequestState(RequestAggregate result)
    {
        using (_logger.BeginScope(new Dictionary<string, object>
               {
                   { "AuthorizationKey", result.AuthorizationKey },
                   { "RequestForServiceKey", result.RequestForServiceKey },
                   { "RequestForServiceId", result.RequestForServiceId },
                   { "MedicalDiscipline", result.Specialty },
                   { "InsuranceCarrierKey", result.InsuranceCarrierKey },
                   { "LineOfBusiness", result.LineOfBusiness },
                   { "Priority", result.Priority },
                   { "Status", result.Status },
                   { "PriorPeerStatus", result.PriorPeerStatus },
                   { "JurisdictionState", result.JurisdictionState },
                   { "DueDateUtc", result.DueDateUtc },
                   { "SslRequirementStatus", result.SslRequirementStatus }
               }))
        {
            _logger.LogInformation("Replay completed for request {RequestForServiceKey}", result.RequestForServiceKey);
        }
    }
    public async Task<bool> InternalRoutingReplayAync(RoutableDeterminationEvent replayRequest)
    {
        _logger.LogInformation("Internal Replay started for Request {RequestForServiceKey}", replayRequest.RequestForServiceKey);
        var history = await _historyRepository.GetRequestEventHistory(replayRequest.RequestForServiceKey);
        var eventHistory = history?.History;
        if (eventHistory != null)
        {
            // sort by timestamp, now everything we know about is in order
            eventHistory.Sort((x, y) => x.Sent.CompareTo(y.Sent));
            // ensure RequestForServiceSubmitted appears first
            eventHistory = eventHistory.OrderByDescending(x => x.Name == "UCXRequestForServiceSubmitted").ToList();

            var determination = await RoutingDetermination(replayRequest, eventHistory);
            _logger.LogInformation("Internal Replay completed for Request {RequestForServiceKey}", replayRequest.RequestForServiceKey);
            return determination;
        }
        return true;
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using RequestRouting.Application.Constants;
using System.Text.Json;

namespace RequestRouting.Application.Policy;

public static class Policies
{
    public static void InitiatePolicyRegistry(this IServiceCollection services)
    {
        var registry = services.AddPolicyRegistry();

        var policy = Polly.Policy
            .Handle<Exception>()
            .OrResult<bool>(x => x == false)
            .WaitAndRetryAsync(5,
                retryCount => TimeSpan.FromMilliseconds(50),
                (exception, timeSpan, retryCount, context) =>
                {
                    if (!context.TryGetLogger(out var logger)) return;

                    context[PolicyConstants.RetryCount] = retryCount;

                    var exceptionSerialized = JsonSerializer.Serialize(exception);
                    logger.LogInformation("Error causing Retry: {exception}", exceptionSerialized);
                    logger.LogInformation("Got exception : {Exception}, retry attempt number {RetryCount}", exception?.Exception?.Message ?? "exception missing", retryCount);
                });

        registry.Add(PolicyConstants.RetryPolicy, policy);
    }
}
using Azure;
using Azure.Search.Documents.Indexes;
using Confluent.Kafka;
using FluentValidation;
using FluentValidation.AspNetCore;
using HealthChecks.UI.Client;
using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Azure.Cosmos;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using RequestRouting.Api.Validators;
using RequestRouting.Application.Configurations;
using RequestRouting.Application.Deserialization;
using RequestRouting.Application.Interface;
using RequestRouting.Application.Query.Handlers;
using RequestRouting.Application.Query.Interfaces;
using RequestRouting.Application.Requests;
using RequestRouting.Infrastructure.Configs;
using RequestRouting.Infrastructure.HealthChecks;
using RequestRouting.Infrastructure.Kafka;
using RequestRouting.Infrastructure.Repositories;
using RequestRouting.Infrastructure.Services;
using Serilog;

namespace RequestRouting.Api;

public class Startup
{
    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public IConfiguration Configuration { get; }

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddCors(allowsites =>
            allowsites.AddPolicy("AllowOrigin",
                options => options
                    .AllowAnyOrigin()
                    .AllowAnyHeader()
                    .AllowAnyMethod()));
        services.AddMvc();
        services.AddControllers();
        services.AddSearchStorageDependencies(Configuration);
        var options = new ApplicationInsightsServiceOptions
            { ConnectionString = Configuration.GetValue<string>("ApplicationInsights:ConnectionString") };
        services.AddApplicationInsightsTelemetry(options);
        services.RegisterProducer(Configuration);

        AddHealthChecks(services);

        ConfigureAuthentication(services);
        ConfigureSwagger(services);

        services.AddSingleton(c => InitializeCosmosClient(Configuration.GetSection("DocumentDB")));
        services.AddSingleton(c => InitializeUserConfigurationRepository(c, Configuration.GetSection("DocumentDB")));
        services.AddSingleton(c => InitializeStateConfigurationRepository(c, Configuration.GetSection("DocumentDB")));
        services.AddSingleton(c => InitializeEventHistoryRepository(c, Configuration.GetSection("DocumentDB")));

        services.AddSingleton<IRequestIndexingRepository, RequestIndexingRepository>();
        services.AddSingleton<IRequestSearchRepository, RequestSearchRepository>();
        services.AddSingleton<IGetNextHandler, GetNextRequestHandler>();
        services.AddSingleton<ISkipRequestHandler, SkipRequestHandler>();
        services.AddSingleton<ICreateDequeueRoutingRequestEventHandler, CreateDequeueRoutingRequestEventHandler>();
        services.AddSingleton<IGetRequestHistoryByIdHandler, GetRequestHistoryByIdHandler>();

        services.AddSingleton<IAddScoringProfileHandler, AddScoringProfileHandler>();
        services.AddSingleton<IDeleteScoringProfileHandler, DeleteScoringProfileHandler>();
        services.AddSingleton<IScoringProfileService, ScoringProfileService>();

        services.AddFluentValidation();
        services.AddTransient<IValidator<SkipRequestRequest>, SkipRequestRequestValidator>();
    }

    private void AddHealthChecks(IServiceCollection services)
    {
        var kafkaSettings = Configuration.GetSection("KafkaSettings").Get<KafkaSettings>();
        var dbConnectionString =
            $"AccountEndpoint={Configuration.GetValue<string>("DocumentDB:Endpoint")};" +
            $"AccountKey={Configuration.GetValue<string>("DocumentDB:Key")};";
        services.AddHealthChecks()
            .AddKafka(options =>
                {
                    options.BootstrapServers = kafkaSettings.BootstrapServers;
                    options.SecurityProtocol = Enum.Parse<SecurityProtocol>(kafkaSettings.SecurityProtocol);
                    options.SslCaLocation = kafkaSettings.SslCaLocation;
                    options.SaslMechanism = SaslMechanism.ScramSha512;
                    options.SaslUsername = kafkaSettings.SaslUsername;
                    options.SaslPassword = kafkaSettings.SaslPassword;
                    options.SocketNagleDisable = true;
                },
                "UCX.RequestRoutingHealthCheck",
                "API:Kafka",
                null,
                null,
                TimeSpan.FromSeconds(30)
            ).AddCosmosDb(
                dbConnectionString,
                null,
                "API:CosmosDB",
                null,
                null,
                TimeSpan.FromSeconds(30))
            .AddAzureSearchHealthCheck(new[]
                {
                    Configuration.GetValue<string>("SearchDB:RoutingIndexName")
                },
                new SearchIndexClient(new Uri(Configuration.GetValue<string>("SearchDB:Endpoint")),
                    new AzureKeyCredential(Configuration.GetValue<string>("SearchDB:Key"))))
            .AddApplicationInsightsPublisher(Configuration.GetValue<string>("ApplicationInsights:InstrumentationKey"));
    }

    private void ConfigureAuthentication(IServiceCollection services)
    {
        services.AddAuthentication(options => { options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme; })
            .AddJwtBearer(jwtOptions =>
            {
                jwtOptions.Authority =
                    $"https://{Configuration["AzureAdB2C:Tenant"]}.b2clogin.com/tfp/{Configuration["AzureAdB2C:Tenant"]}.onmicrosoft.com/{Configuration["AzureAdB2C:Policy"]}/v2.0/";
                jwtOptions.Audience = Configuration["AzureAdB2C:ClientId"];
                jwtOptions.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidAudiences = new List<string>
                    {
                        Configuration["AzureAdB2C:ClientId"],
                        Configuration["AzureAdB2C:UcxClientId"],
                        Configuration["AzureAdB2C:UcxApiClientId"]
                    }
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("UCXMdGroup",
                policy => policy
                    .RequireAuthenticatedUser()
                    .RequireClaim("groups", Configuration["AzureAdB2C:Groups:UCX_MD"].Split(",").ToArray()));
            options.AddPolicy("DeveloperGroup",
                policy => policy
                    .RequireAuthenticatedUser()
                    .RequireClaim("groups", Configuration["AzureAdB2C:Groups:Developer"].Split(",").ToArray()));
        });
    }

    private void ConfigureSwagger(IServiceCollection services)
    {
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "RequestRouting.Api", Version = "v1" });

            var tenant = Configuration["AzureAdB2C:Tenant"];
            c.AddSecurityDefinition("oauth2", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                Flows = new OpenApiOAuthFlows
                {
                    Implicit = new OpenApiOAuthFlow
                    {
                        AuthorizationUrl =
                            new Uri(
                                $"https://{tenant}.b2clogin.com/{tenant}.onmicrosoft.com/{Configuration["AzureAdB2C:Policy"]}/oauth2/v2.0/authorize"),
                        TokenUrl = new Uri(
                            $"https://{tenant}.b2clogin.com/{tenant}.onmicrosoft.com/{Configuration["AzureAdB2C:Policy"]}/oauth2/v2.0/token"),
                        Scopes = new Dictionary<string, string>
                        {
                            {
                                $"https://{tenant}.onmicrosoft.com/{Configuration["AzureAdB2C:ClientId"]}/RequestRouting.rw",
                                "RequestRouting Api"
                            }
                        }
                    }
                }
            });

            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "oauth2"
                        },
                        Scheme = "oauth2",
                        Name = "oauth2",
                        In = ParameterLocation.Header
                    },
                    new List<string>()
                }
            });
        });
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseSerilogRequestLogging();

        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "RequestRouting API");
            c.RoutePrefix = string.Empty;
            c.OAuthClientId(Configuration["AzureAdB2C:ClientId"]);
            c.OAuthUseBasicAuthenticationWithAccessCodeGrant();
        });

        if (!"ApiAutomatedTest".Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")))
        {
            app.UseHttpsRedirection();
        }

        app.UseRouting();
        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
            endpoints.MapHealthChecks(
                "/api/health",
                new HealthCheckOptions
                {
                    Predicate = _ => true,
                    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
                });
            endpoints.MapHealthChecksUI();
        });
    }

    private static CosmosClient InitializeCosmosClient(IConfigurationSection configurationSection)
    {
        var accountEndpoint = configurationSection.GetSection("Endpoint").Value;
        var key = configurationSection.GetSection("Key").Value;

        var serializerSettings = new JsonSerializerSettings
        {
            Converters =
            {
                new RoutingJsonConverter(),
                new StringEnumConverter()
            }
        };

        var options = new CosmosClientOptions
            { ConnectionMode = ConnectionMode.Gateway, Serializer = new JsonCosmosSerializer(serializerSettings) };
        return new CosmosClient(accountEndpoint, key, options);
    }

    private static IUserProfileConfigurationRepository InitializeUserConfigurationRepository(IServiceProvider c,
        IConfigurationSection configurationSection)
    {
        var databaseName = configurationSection.GetSection("DatabaseName").Value;
        var containerName = configurationSection.GetSection("UserConfigurationContainer").Value;
        var client = c.GetService<CosmosClient>();
        var logger = c.GetService<ILogger<UserProfileConfigurationRepository>>();
        return new UserProfileConfigurationRepository(client, databaseName, containerName, logger);
    }

    private static IStateConfigurationRepository InitializeStateConfigurationRepository(
        IServiceProvider serviceProvider, IConfigurationSection configurationSection)
    {
        var databaseName = configurationSection.GetSection("DatabaseName").Value;
        var containerName = configurationSection.GetSection("StateConfigurationContainer").Value;
        var client = serviceProvider.GetService<CosmosClient>();
        var logger = serviceProvider.GetService<ILogger<StateConfigurationRepository>>();
        return new StateConfigurationRepository(client, databaseName, containerName, logger);
    }
    
    private static IEventHistoryRepository InitializeEventHistoryRepository(IServiceProvider c, IConfigurationSection configurationSection)
    {
        var databaseName = configurationSection.GetSection("DatabaseName").Value;
        var containerName = configurationSection.GetSection("EventHistoryContainer").Value;
        var client = c.GetService<CosmosClient>();
        var logger = c.GetService<ILogger<EventHistoryRepository>>();
        return new EventHistoryRepository(client, databaseName, containerName, logger);
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using RequestRouting.Api.Responses;
using RequestRouting.Application.Query.Interfaces;
using RequestRouting.Application.Requests;
using RequestRouting.Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using RequestRouting.Domain.Enums;

namespace RequestRouting.Api.Controllers;

[EnableCors("AllowOrigin")]
[ApiController]
[Route("api/[controller]")]
public class RequestRoutingController : ControllerBase
{
    private readonly IGetNextHandler _getNextRequestHandler;
    private readonly ILogger<RequestRoutingController> _logger;
    private readonly ISkipRequestHandler _skipRequestHandler;

    public RequestRoutingController(ILogger<RequestRoutingController> logger,
        IGetNextHandler getNextRequestHandler,
        ISkipRequestHandler skipRequestHandler)
    {
        _logger = logger;
        _getNextRequestHandler = getNextRequestHandler;
        _skipRequestHandler = skipRequestHandler;
    }

    private string LoggedInUserEmail => HttpContext.User.FindFirst("emails")?.Value;

    [Authorize(Policy = "UCXMdGroup")]
    [HttpGet("Requests/Next")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [ProducesDefaultResponseType]
    public async Task<IActionResult> GetNextRequest()
    {
        try
        {
            using (_logger.BeginScope(new Dictionary<string, object> { { "LoggedInUserEmail", LoggedInUserEmail } }))
            {
                _logger.LogInformation("RequestRoutingController GetNextRequest: process started");
                var nextRequest = await _getNextRequestHandler.HandleAsync(LoggedInUserEmail);

                if (!nextRequest.Success)
                {
                    switch (nextRequest.GetNextNoRequestsReason)
                    {
                        case GetNextNoRequestsReasonEnum.NoneAvailable:
                            _logger.LogInformation("GetNextRequest no requests available for user");
                            break;
                        case GetNextNoRequestsReasonEnum.NoneSuitable:
                            _logger.LogInformation("GetNextRequest no request found for user");
                            break;
                        case GetNextNoRequestsReasonEnum.NoConfigAvailable:
                            _logger.LogInformation("GetNextRequest no config found for user");
                            break;
                    }

                    return Ok(new SimpleResponseBooleanModel
                    {
                        Success = false,
                        Message = nextRequest.ErrorMessage
                    });
                }

                var getNextResponse = new GetNextRequestResponse
                {
                    AuthorizationKey = nextRequest.AuthorizationKey,
                    RequestForServiceId = nextRequest.RequestForServiceId,
                    RequestForServiceKey = nextRequest.RequestForServiceKey,
                    WasAssigned = true,
                    AssignmentExpireTimeUtc = nextRequest.AssignmentExpirationUtc
                };
                _logger.LogInformation("RequestRoutingController GetNextRequest: process ended");

                return Ok(getNextResponse);
            }
        }
       
        catch (Exception e)
        {
            _logger.LogError(e, "GetNextRequest operation failed");
            return StatusCode(StatusCodes.Status500InternalServerError, e.Message);
        }
    }

    [Authorize(Policy = "UCXMdGroup")]
    [HttpPost("Requests/Skip")]
    public async Task<IActionResult> PostSkipRequest(SkipRequestRequest skipRequestRequest)
    {
        var scope = new Dictionary<string, object>
        {
            { "LoggedInUserEmail", LoggedInUserEmail },
            { "RequestForServiceKey", skipRequestRequest.RequestForServiceKey },
            { "UserId", skipRequestRequest.UserId },
            { "SkipReasonId", skipRequestRequest.SkipReasonId },
            { "SkipExplanation", skipRequestRequest.SkipExplanation }
        };
        using (_logger.BeginScope(scope))
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                _logger.LogInformation("RequestRoutingController:PostSkipRequest started");
                skipRequestRequest.UserId = LoggedInUserEmail;
                var response = await _skipRequestHandler.HandleAsync(skipRequestRequest);

                if (response.Item1)
                {
                    return Ok(new SimpleResponseBooleanModel
                    {
                        Success = true,
                        Message = response.Item2
                    });
                }

                _logger.LogInformation("RequestRoutingController:PostSkipRequest ended");
                return Ok(new SimpleResponseBooleanModel
                {
                    Success = false,
                    Message = response.Item2
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RequestRoutingController:PostSkipRequest failed");
                return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
            }
        }
    }
}
FROM globalcrep.azurecr.io/evicore-aspnet:7.0-kafka-alpine AS base

WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src

ARG PAT
RUN wget -qO- https://raw.githubusercontent.com/Microsoft/artifacts-credprovider/master/helpers/installcredprovider.sh | bash
ENV NUGET_CREDENTIALPROVIDER_SESSIONTOKENCACHE_ENABLED true
ENV VSS_NUGET_EXTERNAL_FEED_ENDPOINTS "{\"endpointCredentials\": [{\"endpoint\":\"https://pkgs.dev.azure.com/eviCoreDev/_packaging/eviCoreVSTSNugetFeed/nuget/v3/index.json\", \"password\":\"${PAT}\"}]}"

COPY ["RequestRouting.Api/RequestRouting.Api.csproj", "RequestRouting.Api/"]
RUN dotnet restore -s "https://api.nuget.org/v3/index.json" -s "https://pkgs.dev.azure.com/eviCoreDev/_packaging/eviCoreVSTSNugetFeed/nuget/v3/index.json" "RequestRouting.Api/RequestRouting.Api.csproj"

COPY . .
WORKDIR "RequestRouting.Api"
RUN dotnet build "RequestRouting.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "RequestRouting.Api.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "RequestRouting.Api.dll"]



Thanks and Regards
Siraj

