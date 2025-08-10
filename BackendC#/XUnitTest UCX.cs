using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoFixture;
using Divergic.Logging.Xunit;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using RequestRouting.Api.Controllers;
using RequestRouting.Api.Responses;
using RequestRouting.Application.Query.Handlers;
using RequestRouting.Application.Query.Interfaces;
using RequestRouting.Application.Query.Responses;
using RequestRouting.Application.Requests;
using RequestRouting.Domain.Enums;
using RequestRouting.Domain.Exceptions;
using Xunit;
using Xunit.Abstractions;

namespace RequestRouting.UnitTests.Api.Controllers;

public class RequestRoutingControllerTests
{
    private const string TestUserEmail = "test@test.com";
    private readonly ICacheLogger _cacheLogger;
    private readonly Mock<HttpContext> _context;
    private readonly RequestRoutingController _controller;
    private readonly Fixture _fixture;
    private readonly Mock<IGetNextHandler> _getNextHandler;
    private readonly Mock<ISkipRequestHandler> _skipRequestHandler;
    private readonly Mock<IGetRequestHistoryByIdHandler> _getRequestHistoryByIdHandler;

    public RequestRoutingControllerTests(ITestOutputHelper outputHelper)
    {
        _cacheLogger = outputHelper.BuildLoggerFor<RequestRoutingController>();
        _getNextHandler = new Mock<IGetNextHandler>();
        _skipRequestHandler = new Mock<ISkipRequestHandler>();
        //_getRequestHistoryByIdHandler = new Mock<IGetRequestHistoryByIdHandler>();
        _controller =
            new RequestRoutingController((ILogger<RequestRoutingController>)_cacheLogger,
            _getNextHandler.Object,
            _skipRequestHandler.Object//,
                                      //_getRequestHistoryByIdHandler.Object
            );

        var user = new ClaimsPrincipal(
            new ClaimsIdentity(new List<Claim> { new("emails", TestUserEmail), new("name", "Test Name") }));
        _context = new Mock<HttpContext>();
        _context.Setup(x => x.User).Returns(user);
        _controller.ControllerContext.HttpContext = _context.Object;
        _fixture = new Fixture();
    }

    [Fact]
    public async Task GetNextSleepRequest_LogsUserName_WhenCalled()
    {
        await _controller.GetNextRequest();

        _cacheLogger.Count.Should().Be(2);
        var logEntry = _cacheLogger.Entries.FirstOrDefault(v =>
            v.Message.Contains("RequestRoutingController GetNextRequest: process started"));
        var scopeData = (Dictionary<string, object>)logEntry.Scopes.FirstOrDefault();
        scopeData["LoggedInUserEmail"].Should().Be(TestUserEmail);
    }

    [Fact]
    public async Task GetNextSleepRequest_GetsGetNextSleepRequestResponse_WhenCalled()
    {
        var request = _fixture.Create<RequestResponse>();
        _getNextHandler.Setup(x => x.HandleAsync(It.IsAny<string>())).ReturnsAsync(request);

        var controllerResponse = (OkObjectResult)await _controller.GetNextRequest();

        var response = (GetNextRequestResponse)controllerResponse.Value;
        response.AuthorizationKey.Should().Be(request.AuthorizationKey);
        response.RequestForServiceId.Should().Be(request.RequestForServiceId);
        response.RequestForServiceKey.Should().Be(request.RequestForServiceKey);
        response.AssignmentExpireTimeUtc.Should().Be(request.AssignmentExpirationUtc);
        response.WasAssigned.Should().BeTrue();
    }

    [Fact]
    public async Task GetNextSleepRequest_CallsHandlerOnce_WhenCalled()
    {
        var request = _fixture.Create<RequestResponse>();
        _getNextHandler.Setup(x => x.HandleAsync(It.IsAny<string>())).ReturnsAsync(request);

        await _controller.GetNextRequest();

        _getNextHandler.Verify(x => x.HandleAsync(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task GetNextSleepRequest_ReturnsSuccess_WhenRequestIsFound()
    {
        var request = _fixture.Create<RequestResponse>();
        _getNextHandler.Setup(x => x.HandleAsync(It.IsAny<string>())).ReturnsAsync(request);

        var response = (OkObjectResult)await _controller.GetNextRequest();

        response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task GetNextSleepRequest_Returns500_WhenHandlerErrorsOut()
    {
        _getNextHandler.Setup(x => x.HandleAsync(It.IsAny<string>())).ThrowsAsync(new Exception());

        var response = (ObjectResult)await _controller.GetNextRequest();

        response.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task GetNextSleepRequest_ReturnsAndLogsNotFound_WhenDataSourceIsEmpty()
    {
        _getNextHandler.Setup(x => x.HandleAsync(It.IsAny<string>()))
            .ReturnsAsync(new RequestResponse { Success = false, ErrorMessage = "No requests available for user", GetNextNoRequestsReason = GetNextNoRequestsReasonEnum.NoneAvailable});

        var result = (OkObjectResult)await _controller.GetNextRequest();
        var response = (SimpleResponseBooleanModel)result.Value;
        var logEntry = _cacheLogger.Entries.FirstOrDefault(v =>
            v.Message.Contains("GetNextRequest no requests available for user"));
        
        logEntry.Should().NotBeNull();
        
        result.StatusCode.Should().Be(200);
        
        response.Success.Should().BeFalse();
        response.Message.Should().Be("No requests available for user");
    }

    [Fact]
    public async Task GetNextSleepRequest_ReturnsAndLogsNotFound_WhenNoRequestIsFoundForUser()
    {
        _getNextHandler.Setup(x => x.HandleAsync(It.IsAny<string>()))
            .ReturnsAsync(new RequestResponse { Success = false, ErrorMessage = "No suitable requests found for user", GetNextNoRequestsReason = GetNextNoRequestsReasonEnum.NoneSuitable});

        var result = (OkObjectResult)await _controller.GetNextRequest();
        var response = (SimpleResponseBooleanModel)result.Value;

        var logEntry = _cacheLogger.Entries.FirstOrDefault(v =>
            v.Message.Contains("GetNextRequest no request found for user"));
        
        logEntry.Should().NotBeNull();
        
        result.StatusCode.Should().Be(200);

        response.Success.Should().BeFalse();
        response.Message.Should().Be("No suitable requests found for user");
    }

    [Fact]
    public async Task GetNextSleepRequest_ReturnsAndLogsNotFound_WhenNoConfigIsFoundForUser()
    {
        _getNextHandler.Setup(x => x.HandleAsync(It.IsAny<string>()))
            .ReturnsAsync(new RequestResponse { Success = false, ErrorMessage = $"No configuration found for this user {TestUserEmail}", GetNextNoRequestsReason = GetNextNoRequestsReasonEnum.NoConfigAvailable });

        var result = (OkObjectResult)await _controller.GetNextRequest();
        var response = result.Value as SimpleResponseBooleanModel;

        var logEntry = _cacheLogger.Entries.FirstOrDefault(v =>
            v.Message.Contains("GetNextRequest no config found for user"));

        logEntry.Should().NotBeNull();

        result.StatusCode.Should().Be(200);

        response.Success.Should().BeFalse();
        response.Message.Should().Be($"No configuration found for this user {TestUserEmail}");
    }

    [Fact]
    public async Task PostSkipRequest_ReturnsBadRequest_WhenModelStateIsInvalid()
    {
        // Arrange
        _controller.ModelState.AddModelError("error", "test error");
        var skipRequestRequest = new SkipRequestRequest();

        // Act
        var result = (BadRequestObjectResult)await _controller.PostSkipRequest(skipRequestRequest);
        var response = (SerializableError)result.Value;

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
        result.StatusCode.Should().Be(400);
        response.Values.FirstOrDefault().Should().BeEquivalentTo(new[] { "test error" });
    }

    [Fact]
    public async Task PostSkipRequest_ReturnsOk_WhenSkipRequestHandlerReturnsSuccess()
    {
        // Arrange
        _skipRequestHandler.Setup(x => x.HandleAsync(It.IsAny<SkipRequestRequest>()))
            .ReturnsAsync((true, "Exclusion added"));
        var skipRequestRequest = new SkipRequestRequest();

        // Act
        var result = (OkObjectResult)await _controller.PostSkipRequest(skipRequestRequest);
        var response = (SimpleResponseBooleanModel)result.Value;

        // Assert
        result.StatusCode.Should().Be(200);
        response.Message.Should().Be("Exclusion added");
        response.Success.Should().BeTrue();
    }

    [Fact]
    public async Task PostSkipRequest_ReturnsNotFound_WhenSkipRequestHandlerReturnsFailure()
    {
        // Arrange
        _skipRequestHandler.Setup(x => x.HandleAsync(It.IsAny<SkipRequestRequest>()))
            .ReturnsAsync((false, "Request not found"));
        var skipRequestRequest = new SkipRequestRequest();

        // Act
        var result = (OkObjectResult)await _controller.PostSkipRequest(skipRequestRequest);
        var response = (SimpleResponseBooleanModel)result.Value;

        // Assert
        result.StatusCode.Should().Be(200);
        response.Message.Should().Be("Request not found");
        response.Success.Should().BeFalse();
    }

    [Fact]
    public async Task PostSkipRequest_ReturnsInternalServerError_WhenExceptionIsThrown()
    {
        // Arrange
        _skipRequestHandler.Setup(x => x.HandleAsync(It.IsAny<SkipRequestRequest>()))
            .ThrowsAsync(new Exception("Test handler exception"));
        var skipRequestRequest = new SkipRequestRequest();

        // Act
        var result = (ObjectResult)await _controller.PostSkipRequest(skipRequestRequest);
        var response = (string)result.Value;

        // Assert
        result.StatusCode.Should().Be(500);
        response.Should().BeEquivalentTo("Test handler exception");
    }
}

using Divergic.Logging.Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RequestRouting.Application.Commands.Handlers;
using RequestRouting.Application.Configurations;
using RequestRouting.Domain.Aggregates;
using RequestRouting.Domain.Enums;
using RequestRouting.Domain.Events;
using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace RequestRouting.UnitTests.Application.Commands.Handlers;

public class PhysicianDecisionMadeHandlerTests
{
    private readonly PhysicianDecisionMadeHandler _physicianDecisionMadeHandler;
    private readonly ICacheLogger _logger;

    private readonly IOptions<AppSettings> _options;

    private readonly int _assignmentExpirationMinutes;

    public PhysicianDecisionMadeHandlerTests(ITestOutputHelper outputHelper)
    {

        _options = Options.Create(new AppSettings
        {
            JurisdictionStatesCommercial = "ak,mo,ct",
            JurisdictionStatesCommercialMedicaid = "mn",
            AssignmentExpirationMinutes = "20"
        });

        _logger = outputHelper.BuildLoggerFor<PhysicianDecisionMadeHandler>();

        _physicianDecisionMadeHandler = new PhysicianDecisionMadeHandler((ILogger<PhysicianDecisionMadeHandler>)_logger, _options);

    }

    [Fact]
    public async Task PhysicianDecisionMadeHandler_ThrowsExceptionOnNullEvent()
    {
        // arrange
        var request = new RequestAggregate();
        // act
        // assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => _physicianDecisionMadeHandler.HandleAsync(request, null));
    }

    //[Fact]
    //public async Task PhysicianDecisionMadeHandlerTests_Suspend_NoDecisionMade()
    //{
    //    // arrange
    //    var decision = new PhysicianDecisionMadeEvent { PendInQueue = true };

    //    var requestState = new RequestAggregate();
    //    requestState.UpdateDueDate(DateTime.Now.AddDays(10));
    //    requestState.UpdateSpecialty(SpecialtyEnum.Sleep);
    //    requestState.UpdateStatus(StatusEnum.NeedsMdReview);
    //    requestState.UpdateAssignment("test", DateTime.UtcNow.AddMinutes(20));

    //    // act
    //    await _physicianDecisionMadeHandler.HandleAsync(requestState, decision);
    //    // assert
    //    Assert.NotNull(requestState.SuspendExpirationUtc);
    //    Assert.NotNull(requestState.AssignmentExpirationUtc);
    //    Assert.NotNull(requestState.DueDateUtc);
    //    Assert.True(requestState.DueDateUtc.Value > requestState.SuspendExpirationUtc.Value);
    //    Assert.Equal(requestState.AssignmentExpirationUtc.Value.Subtract(requestState.SuspendExpirationUtc.Value).Minutes, _assignmentExpirationMinutes);

    //}

    //[Fact]
    //public async Task PhysicianDecisionMadeHandlerTests_Suspend__Null_DueDate()
    //{
    //    // arrange
    //    var decision = new PhysicianDecisionMadeEvent { PendInQueue = true };

    //    var requestState = new RequestAggregate();

    //    requestState.UpdateSpecialty(SpecialtyEnum.Sleep);
    //    requestState.UpdateStatus(StatusEnum.NeedsMdReview);
    //    requestState.UpdateAssignment("test", DateTime.UtcNow.AddMinutes(20));
    //    // act
    //    await _physicianDecisionMadeHandler.HandleAsync(requestState, decision);
    //    // assert
    //    Assert.NotNull(requestState.SuspendExpirationUtc);
    //    Assert.NotNull(requestState.AssignmentExpirationUtc);
    //    Assert.Null(requestState.DueDateUtc);        
    //    Assert.Equal(requestState.AssignmentExpirationUtc.Value.Subtract(requestState.SuspendExpirationUtc.Value).Minutes, _assignmentExpirationMinutes);

    //}

    [Fact]
    public async Task PhysicianDecisionMadeHandlerTests_DoNot_Suspend_No_Assignments()
    {
        // arrange
        var decision = new PhysicianDecisionMadeEvent { Outcome = StatusEnum.StatusUnknown.ToString() };

        var requestState = new RequestAggregate();

        requestState.UpdateSpecialty(SpecialtyEnum.Sleep);
        requestState.UpdateStatus(StatusEnum.NeedsMdReview);

        // act
        await _physicianDecisionMadeHandler.HandleAsync(requestState, decision);
        // assert
        Assert.Null(requestState.SuspendExpirationUtc);
        Assert.Null(requestState.DueDateUtc);

    }

    [Fact]
    public async Task PhysicianDecisionMadeHandlerTests_DoNot_Suspend_Not_NeedsMdReview()
    {
        // arrange
        var decision = new PhysicianDecisionMadeEvent { Outcome = StatusEnum.StatusUnknown.ToString() };

        var requestState = new RequestAggregate();

        requestState.UpdateSpecialty(SpecialtyEnum.Sleep);
        requestState.UpdateStatus(StatusEnum.NeedsNurseReview);
        requestState.UpdateAssignment("test", DateTime.UtcNow.AddMinutes(20));

        // act
        await _physicianDecisionMadeHandler.HandleAsync(requestState, decision);
        // assert
        Assert.Null(requestState.SuspendExpirationUtc);
        Assert.Null(requestState.DueDateUtc);

    }

    [Fact]
    public async Task PhysicianDecisionMadeHandlerTests_DoNot_Suspend_DueDate_Smaller()
    {
        // arrange
        var decision = new PhysicianDecisionMadeEvent { Outcome = StatusEnum.StatusUnknown.ToString() };

        var requestState = new RequestAggregate();

        requestState.UpdateSpecialty(SpecialtyEnum.Sleep);
        requestState.UpdateDueDate(DateTime.UtcNow.AddMinutes(-10));
        requestState.UpdateStatus(StatusEnum.NeedsMdReview);
        requestState.UpdateAssignment("test", DateTime.UtcNow.AddMinutes(20));

        // act
        await _physicianDecisionMadeHandler.HandleAsync(requestState, decision);
        // assert
        Assert.Null(requestState.SuspendExpirationUtc);
        Assert.NotNull(requestState.DueDateUtc);

    }

    [Fact]
    public async Task PhysicianDecisionMadeHandlerTests_DoNot_Suspend_IsPendingInQueue_IsFalse()
    {
        // arrange
        var decision = new PhysicianDecisionMadeEvent { IsPendingInQueue = false };

        var requestState = new RequestAggregate();

        requestState.UpdateSpecialty(SpecialtyEnum.Sleep);
        requestState.UpdateStatus(StatusEnum.NeedsMdReview);
        requestState.UpdateAssignment("test", DateTime.UtcNow.AddMinutes(20));

        // act
        await _physicianDecisionMadeHandler.HandleAsync(requestState, decision);
        // assert
        Assert.Null(requestState.SuspendExpirationUtc);

    }
    [Fact]
    public async Task PhysicianDecisionMadeHandlerTests_Suspend_IsPendingInQueue_IsTrue()
    {
        // arrange
        DateTimeOffset? suspendExpirationTime = DateTimeOffset.Now.AddMinutes(60);
        DateTimeOffset? suspendAssignmentExpirationTime = DateTimeOffset.Now.AddMinutes(240);
        var decision = new PhysicianDecisionMadeEvent { IsPendingInQueue = true, SuspendExpirationTime = suspendExpirationTime, SuspendedCaseAssignmentExpirationTime = suspendAssignmentExpirationTime };

        var requestState = new RequestAggregate();

        requestState.UpdateSpecialty(SpecialtyEnum.Sleep);
        requestState.UpdateStatus(StatusEnum.NeedsMdReview);
        requestState.UpdateAssignment("test", DateTime.UtcNow.AddMinutes(20));

        // act
        await _physicianDecisionMadeHandler.HandleAsync(requestState, decision);
        // assert
        Assert.NotNull(requestState.SuspendExpirationUtc);
        Assert.True(requestState.SuspendExpirationUtc == decision.SuspendExpirationTime.Value.UtcDateTime);
        Assert.True(requestState.AssignmentExpirationUtc == decision.SuspendedCaseAssignmentExpirationTime.Value.UtcDateTime);

    }
    [Fact]
    public async Task PhysicianDecisionMadeHandlerTests_SetSuspendedCaseAssisgnmentExpirationTo4hrs_WhenItsNotManualAssignment()
    {
        // arrange
        DateTimeOffset? suspendExpirationTime = DateTimeOffset.Now.AddMinutes(60);
        DateTimeOffset? suspendAssignmentExpirationTime = DateTimeOffset.Now.AddMinutes(240);
        var decision = new PhysicianDecisionMadeEvent { IsPendingInQueue = true, SuspendExpirationTime = suspendExpirationTime, SuspendedCaseAssignmentExpirationTime = suspendAssignmentExpirationTime };

        var requestState = new RequestAggregate();

        requestState.UpdateSpecialty(SpecialtyEnum.Sleep);
        requestState.UpdateStatus(StatusEnum.NeedsMdReview);
        requestState.UpdateAssignment("test", DateTime.UtcNow.AddMinutes(20));

        // act
        await _physicianDecisionMadeHandler.HandleAsync(requestState, decision);
        // assert
        Assert.NotNull(requestState.SuspendExpirationUtc);
        Assert.True(requestState.SuspendExpirationUtc == decision.SuspendExpirationTime);
        Assert.True(requestState.AssignmentExpirationUtc == decision.SuspendedCaseAssignmentExpirationTime.Value.UtcDateTime);

    }
    [Fact]
    public async Task PhysicianDecisionMadeHandlerTests_RetainSuspendCaseExistingAssisgnmentExpiration_WhenItsManualAssignment()
    {
        // arrange
        DateTimeOffset? suspendExpirationTime = DateTimeOffset.Now.AddMinutes(60);
        DateTimeOffset? suspendAssignmentExpirationTime = DateTimeOffset.Now.AddMinutes(240);
        var decision = new PhysicianDecisionMadeEvent { IsPendingInQueue = true, SuspendExpirationTime = suspendExpirationTime, SuspendedCaseAssignmentExpirationTime = suspendAssignmentExpirationTime };

        var requestState = new RequestAggregate();

        requestState.UpdateSpecialty(SpecialtyEnum.Sleep);
        requestState.UpdateStatus(StatusEnum.NeedsMdReview);
        DateTime assignmentExpirationTimeUtc = DateTime.UtcNow.AddDays(999);
        requestState.UpdateAssignment("test", assignmentExpirationTimeUtc);

        // act
        await _physicianDecisionMadeHandler.HandleAsync(requestState, decision);
        // assert
        Assert.NotNull(requestState.SuspendExpirationUtc);
        Assert.True(requestState.SuspendExpirationUtc == decision.SuspendExpirationTime);
        Assert.True(requestState.AssignmentExpirationUtc == assignmentExpirationTimeUtc);

    }

    [Fact]
    public async Task PhysicianDecisionMadeHandlerTests_Status_NeedsSSLSignOff_Update_Status()
    {
        // arrange
        var decision = new PhysicianDecisionMadeEvent { IsPendingInQueue = false, Outcome = DecisionOutcomesEnum.PendingMDReview.ToString() };
        var requestState = new RequestAggregate();
        requestState.UpdateSpecialty(SpecialtyEnum.Sleep);
        //Case in MD Review status
        requestState.UpdateStatus(StatusEnum.NeedsMdReview);
        // act
        await _physicianDecisionMadeHandler.HandleAsync(requestState, decision);
        // assert
        //Doctor reviewed a case that was is MD Review and Outcome is still PendingMDReview - needs SSL
        Assert.Equal(requestState.Status, StatusEnum.NeedsSslSignoff);

    }

    [Fact]
    public async Task PhysicianDecisionMadeHandlerTests_Status_Approved_DoNot_Update_Status()
    {
        // arrange
        var decision = new PhysicianDecisionMadeEvent { IsPendingInQueue = false, Outcome = DecisionOutcomesEnum.PendingMDReview.ToString() };
        var requestState = new RequestAggregate();
        requestState.UpdateSpecialty(SpecialtyEnum.Sleep);
        //make case already Approved - negative scenario
        requestState.UpdateStatus(StatusEnum.Approved);
        // act
        await _physicianDecisionMadeHandler.HandleAsync(requestState, decision);
        // assert
        Assert.NotEqual(requestState.Status, StatusEnum.NeedsSslSignoff);
        //Already approved case. Do not change the status
        Assert.Equal(requestState.Status, StatusEnum.Approved);

    }

    [Fact]
    public async Task PhysicianDecisionMadeHandlerTests_OutCome_Denied_DoNot_Update_Status()
    {
        // arrange
        var decision = new PhysicianDecisionMadeEvent { IsPendingInQueue = false, Outcome = DecisionOutcomesEnum.Denied.ToString() };
        var requestState = new RequestAggregate();
        requestState.UpdateSpecialty(SpecialtyEnum.Sleep);
        requestState.UpdateStatus(StatusEnum.NeedsMdReview);
        // act
        await _physicianDecisionMadeHandler.HandleAsync(requestState, decision);
        // assert
        Assert.NotEqual(requestState.Status, StatusEnum.NeedsSslSignoff);

        //Do not change requestState status from Physician Decision Made event for Denied outcome - negative scenario
        Assert.Equal(requestState.Status, StatusEnum.NeedsMdReview);

    }
}


using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph.Models.TermStore;
using Moq;
using Polly;
using Polly.Registry;
using Polly.Retry;
using RequestRouting.Application.Configurations;
using RequestRouting.Application.Constants;
using RequestRouting.Application.Events;
using RequestRouting.Application.Interface;
using RequestRouting.Application.Policy;
using RequestRouting.Application.Query.Interfaces;
using RequestRouting.Application.Query.Responses;
using RequestRouting.Application.Requests;
using RequestRouting.Domain.Aggregates;
using RequestRouting.Domain.Enums;
using RequestRouting.Domain.Events;
using RequestRouting.Domain.ValueObjects;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ucx.locking.@base.interfaces;
using ucx.locking.@base.models;
using Xunit;


namespace RequestRouting.UnitTests.Application.Orchestrator;

public class OrchestratorTests
{
    private readonly Mock<IEventHistoryRepository> _eventHistoryRepository;
    private readonly Mock<ILogger<RequestRouting.Application.Orchestrator.Orchestrator>> _logger;
    private readonly IOptions<AppSettings> _options;
    private readonly RequestRouting.Application.Orchestrator.Orchestrator _orchestrator;
    private readonly Mock<IReplayService> _replayService;
    private readonly Mock<IRequestIndexingRepository> _requestIndexingRepository;
    private readonly Mock<IRequestSearchRepository> _requestSearchRepository;
    private readonly AsyncRetryPolicy<bool> _retryPolicy;
    private readonly Mock<IUserProfileCacheService> _userProfileCacheService;
    private readonly Mock<IHandlerQueue> _handlerQueue;
    private readonly Guid _requestForServiceKey;
    private readonly Mock<IOptions<LockOptions>> _lockOptions;


    private async Task<RequestResponse> CreateRequestResponse()
    {
        var requestResponse = new RequestResponse
        {
            CreateDate = DateTime.UtcNow.AddDays(-1),
        };

        return requestResponse;
    }

    private async Task<RequestResponse> CreateRequestResponseNow()
    {
        var requestResponse = new RequestResponse
        {
            CreateDate = DateTime.UtcNow,
        };

        return requestResponse;
    }

    private async Task<RequestSearchResponse> CreateRequestSearchResponse()
    {
        var requestResponse = new RequestResponse
        {
            RequestForServiceKey = _requestForServiceKey.ToString(),
            CreateDate = DateTime.UtcNow,
        };

        var response = new RequestSearchResponse();
        response.RequestResponse = requestResponse;

        return response;
    }

    public OrchestratorTests()
    {
        _retryPolicy = Policy
            .Handle<Exception>()
            .OrResult<bool>(x => x == false)
            .WaitAndRetryAsync(2,
                retryCount => TimeSpan.FromSeconds(Math.Pow(2, retryCount)),
                (exception, timeSpan, retryCount, context) =>
                {
                    if (!context.TryGetLogger(out var logger))
                    {
                        return;
                    }

                    context[PolicyConstants.RetryCount] = retryCount;

                    logger.LogInformation("Got exception : {Exception}, retry attempt number {RetryCount}",
                        exception, retryCount);
                });

        var _handlers = new Mock<IRequestHandler>();
        _handlerQueue = new Mock<IHandlerQueue>();
        _handlerQueue.Setup(x => x.GetRequestCurrentVersion(It.IsAny<string>(), It.IsAny<string>())).Returns(true);

        _options = Options.Create(new AppSettings 
        { Discipline = "Sleep,Gastro,DME,Radiology", 
            JurisdictionStatesCommercial="ak,mo,ct,az" , 
            JurisdictionStatesCommercialMedicaid ="az,mn",
            ObserverType = "Blue"
        });
        _lockOptions = new Mock<IOptions<LockOptions>>();
        _lockOptions.Setup(x => x.Value).Returns(new LockOptions());

       _eventHistoryRepository = new Mock<IEventHistoryRepository>();

        _requestIndexingRepository = new Mock<IRequestIndexingRepository>();
        _requestIndexingRepository.Setup(x => x.GetAsync(It.IsAny<Guid>())).Returns(CreateRequestResponse());

        _requestForServiceKey = Guid.NewGuid();
        _requestSearchRepository = new Mock<IRequestSearchRepository>();
        _requestSearchRepository.Setup(x => x.SearchByIdAsync(_requestForServiceKey)).Returns(CreateRequestSearchResponse());

        _logger = new Mock<ILogger<RequestRouting.Application.Orchestrator.Orchestrator>>();
        _replayService = new Mock<IReplayService>();
        _userProfileCacheService = new Mock<IUserProfileCacheService>();

        var policyMock = new PolicyRegistry { { PolicyConstants.RetryPolicy, _retryPolicy } };
        _orchestrator = new RequestRouting.Application.Orchestrator.Orchestrator(_eventHistoryRepository.Object,
            _replayService.Object,
            _logger.Object,
            policyMock,
            _requestIndexingRepository.Object,
            _options,
            _userProfileCacheService.Object,
        _handlerQueue.Object,
        _requestSearchRepository.Object,
            new Mock<ILockingService>().Object, _lockOptions.Object);
    }

    [Fact]
    public async Task Orchestrator_UpsertsIntoSearchIndex_NoMedicalDiscipline()
    {
        // arrange
        var requestEvents = new List<UCXEvent>();

        var history = new RequestEventHistoryRequest
        {
            History = requestEvents
        };
        _eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);

        var dueDateCalculatedObject = new DueDateCalculatedObject
        {
            DueDateUtc = DateTime.Today.AddDays(14),
            RequestForServiceKey = Guid.NewGuid()
        };

        var replayResult = new RequestAggregate();
        replayResult.UpdateStatus(StatusEnum.NeedsMdReview);
        replayResult.UpdateDueDateCalculated(dueDateCalculatedObject);
        replayResult.UpdateStartDate(DateTime.Now);

        _replayService.Setup(x => x.ReplayEvents(It.IsAny<List<UCXEvent>>())).ReturnsAsync(replayResult);

        var newEvent = new RequestForServiceSubmittedEvent
        {
            RequestForServiceKey = "RFS123",
            Name = "RequestForServiceSubmitted",
            Version = "1.0.0.0",
            Sent = DateTime.Now,
            MedicalDiscipline = DisciplineEnum.Sleep.ToString()
        };

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        _requestIndexingRepository.Verify(x => x.UpsertWithAssignmentAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task Orchestrator_DeletesFromSearchIndex()
    {
        // arrange
        var newEvent = new RequestForServiceSubmittedEvent
        {
            RequestForServiceKey = _requestForServiceKey.ToString(),
            Name = "RequestForServiceSubmitted",
            Version = "1.0.0.0",
            Sent = DateTime.Now
        };

        var requestEvents = new List<UCXEvent> { newEvent };

        var history = new RequestEventHistoryRequest
        {
            History = requestEvents
        };
        _eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);

        var dueDateCalculatedObject = new DueDateCalculatedObject
        {
            DueDateUtc = DateTime.Today.AddDays(14),
            RequestForServiceKey = _requestForServiceKey
        };

        var replayResult = new RequestAggregate();
        replayResult.UpdateDueDateCalculated(dueDateCalculatedObject);
        replayResult.UpdateMedicalDiscipline(DisciplineEnum.Sleep);
        replayResult.UpdateStatus(StatusEnum.Dismissed);

        _replayService.Setup(x => x.ReplayEvents(It.IsAny<List<UCXEvent>>())).ReturnsAsync(replayResult);

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        _requestIndexingRepository.Verify(x => x.DeleteAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task Orchestrator_Logs3Times_WhenUpsertingToSearchIndex()
    {
        // arrange
        var requestEvents = new List<UCXEvent>();
        var history = new RequestEventHistoryRequest
        {
            History = requestEvents
        };
        _eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);

        var dueDateCalculatedObject = new DueDateCalculatedObject
        {
            DueDateUtc = DateTime.Today.AddDays(14),
            RequestForServiceKey = Guid.NewGuid()
        };

        var replayResult = new RequestAggregate();
        replayResult.UpdateStatus(StatusEnum.NeedsMdReview);
        replayResult.UpdateDueDateCalculated(dueDateCalculatedObject);
        replayResult.UpdateStartDate(DateTime.Now);

        _replayService.Setup(x => x.ReplayEvents(It.IsAny<List<UCXEvent>>())).ReturnsAsync(replayResult);

        var newEvent = new RequestForServiceSubmittedEvent
        {
            RequestForServiceKey = "RFS123",
            Name = "RequestForServiceSubmitted",
            Version = "1.0.0.0",
            Sent = DateTime.Now
        };

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        _logger.Verify(x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
            Times.Exactly(5));
    }

    [Fact]
    public async Task Orchestrator_Logs3Times_WhenDeletingFromSearchIndex()
    {
        // arrange
        var requestEvents = new List<UCXEvent>();
        var history = new RequestEventHistoryRequest
        {
            History = requestEvents
        };
        _eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);

        var dueDateCalculatedObject = new DueDateCalculatedObject
        {
            DueDateUtc = DateTime.Today.AddDays(14),
            RequestForServiceKey = Guid.NewGuid()
        };

        var replayResult = new RequestAggregate();
        replayResult.UpdateStatus(StatusEnum.NeedsMdReview);
        replayResult.UpdateDueDateCalculated(dueDateCalculatedObject);
        replayResult.UpdateStartDate(DateTime.Now);

        _replayService.Setup(x => x.ReplayEvents(It.IsAny<List<UCXEvent>>())).ReturnsAsync(replayResult);

        var newEvent = new RequestForServiceSubmittedEvent
        {
            RequestForServiceKey = "RFS123",
            Name = "RequestForServiceSubmitted",
            Version = "1.0.0.0",
            Sent = DateTime.Now
        };

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        _logger.Verify(x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
            Times.Exactly(5));
    }

    [Fact]
    public async Task Orchestrator_Actionable_NotYetRoutable()
    {
        // arrange
        var requestEvents = new List<UCXEvent>();
        var history = new RequestEventHistoryRequest
        {
            History = requestEvents
        };
        _eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);

        var replayResult = new RequestAggregate();
        replayResult.UpdateSpecialty(SpecialtyEnum.Sleep);

        _replayService.Setup(x => x.ReplayEvents(It.IsAny<List<UCXEvent>>())).ReturnsAsync(replayResult);

        var newEvent = new DueDateCalculatedEvent
        {
            Name = "UCXDueDateCalculated",
            Version = "1.0.0.0",
            DueDateByDateTimeUtc = DateTime.Today.AddDays(14)
        };

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        _eventHistoryRepository.Verify(x => x.GetRequestEventHistory(It.IsAny<string>()), Times.Once);
        _eventHistoryRepository.Verify(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()),
            Times.Once);
        _requestIndexingRepository.Verify(x => x.DeleteAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task Orchestrator_Actionable_NotRoutableDueDateNoDiscipline()
    {
        // arrange
        var requestEvents = new List<UCXEvent>
        {
            new DueDateCalculatedEvent
            {
                Name = "UCXDueDateCalculated",
                Version = "1.0.0.0",
                DueDateByDateTimeUtc = DateTime.Today.AddDays(14),
                Sent = DateTime.Now.AddDays(1)
            }
        };

        var dueDateCalculatedObject = new DueDateCalculatedObject
        {
            DueDateUtc = DateTime.Today.AddDays(14),
            RequestForServiceKey = Guid.NewGuid()
        };

        var history = new RequestEventHistoryRequest
        {
            History = requestEvents
        };

        _eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);

        var replayResult = new RequestAggregate();
        replayResult.UpdateStatus(StatusEnum.NeedsMdReview);
        replayResult.UpdateDueDateCalculated(dueDateCalculatedObject);
        replayResult.UpdateStartDate(DateTime.Now);

        _replayService.Setup(x => x.ReplayEvents(It.IsAny<List<UCXEvent>>())).ReturnsAsync(replayResult);

        var newEvent = new RequestForServiceSubmittedEvent
        {
            RequestForServiceKey = "RFS123",
            Name = "RequestForServiceSubmitted",
            Version = "1.0.0.0",
            Sent = DateTime.Now,
            MedicalDiscipline = DisciplineEnum.Sleep.ToString()
        };

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        _eventHistoryRepository.Verify(x => x.GetRequestEventHistory(It.IsAny<string>()), Times.Once);
        _eventHistoryRepository.Verify(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()), Times.Once);
        _requestIndexingRepository.Verify(x => x.UpsertWithAssignmentAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
        _requestIndexingRepository.Verify(x => x.DeleteAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task Orchestrator_ReplaySameEvent()
    {
        // arrange
        var requestEvents = new List<UCXEvent>();
        var firstEvent = new RequestForServiceSubmittedEvent
        {
            RequestForServiceKey = "RFS123",
            Name = "RequestForServiceSubmitted",
            AuthorizationKey = "KEY",
            Version = "1.0.0.0",
            Sent = DateTime.Now
        };
        requestEvents.Add(firstEvent);

        var history = new RequestEventHistoryRequest
        {
            History = requestEvents,
            RequestForServiceKey = firstEvent.RequestForServiceKey
        };
        _eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);

        var replayResult = new RequestAggregate();
        replayResult.UpdateSpecialty(SpecialtyEnum.Sleep);

        _replayService.Setup(x => x.ReplayEvents(It.IsAny<List<UCXEvent>>())).ReturnsAsync(replayResult);

        // act
        await _orchestrator.OrchestrateAsync(firstEvent, CancellationToken.None);

        // assert
        _eventHistoryRepository.Verify(x => x.GetRequestEventHistory(It.IsAny<string>()), Times.Once);
        _eventHistoryRepository.Verify(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()),
            Times.Once);
        _requestIndexingRepository.Verify(x => x.UpsertWithAssignmentAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
        _requestIndexingRepository.Verify(x => x.DeleteAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task Orchestrator_ExceptionRetries()
    {
        // arrange
        var requestEvents = new List<UCXEvent>();
        var firstEvent = new RequestForServiceSubmittedEvent
        {
            RequestForServiceKey = "RFS123",
            Name = "RequestForServiceSubmitted",
            AuthorizationKey = "KEY",
            Version = "1.0.0.0",
            Sent = DateTime.Now
        };
        requestEvents.Add(firstEvent);
        var history = new RequestEventHistoryRequest
        {
            History = requestEvents,
            RequestForServiceKey = firstEvent.RequestForServiceKey
        };
        _eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);
        _eventHistoryRepository
            .SetupSequence(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()))
            .Throws(new Exception())
            .Returns(Task.CompletedTask);

        var replayResult = new RequestAggregate();
        replayResult.UpdateSpecialty(SpecialtyEnum.Sleep);

        _replayService.Setup(x => x.ReplayEvents(It.IsAny<List<UCXEvent>>())).ReturnsAsync(replayResult);

        // act
        await _orchestrator.OrchestrateAsync(firstEvent, CancellationToken.None);

        // assert
        _eventHistoryRepository.Verify(x => x.GetRequestEventHistory(It.IsAny<string>()), Times.Exactly(2));
        _eventHistoryRepository.Verify(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()),
            Times.Exactly(2));
        _requestIndexingRepository.Verify(x => x.DeleteAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task Orchestrator_CosmosExceptionRetries()
    {
        // arrange
        var requestEvents = new List<UCXEvent>();
        var firstEvent = new RequestForServiceSubmittedEvent
        {
            RequestForServiceKey = "RFS123",
            Name = "RequestForServiceSubmitted",
            AuthorizationKey = "KEY",
            Version = "1.0.0.0",
            Sent = DateTime.Now
        };
        requestEvents.Add(firstEvent);

        var history = new RequestEventHistoryRequest
        {
            History = requestEvents,
            RequestForServiceKey = firstEvent.RequestForServiceKey
        };

        _eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);
        _eventHistoryRepository
            .SetupSequence(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()))
           .Throws(new CosmosException("Exception", HttpStatusCode.PreconditionFailed, 1, "activity",
                double.MinValue))
            .Returns(Task.CompletedTask);
        ;

        var replayResult = new RequestAggregate();
        replayResult.UpdateSpecialty(SpecialtyEnum.Sleep);

        _replayService.Setup(x => x.ReplayEvents(It.IsAny<List<UCXEvent>>())).ReturnsAsync(replayResult);

        // act
        await _orchestrator.OrchestrateAsync(firstEvent, CancellationToken.None);

        // assert
        _eventHistoryRepository.Verify(x => x.GetRequestEventHistory(It.IsAny<string>()), Times.Exactly(2));
        _eventHistoryRepository.Verify(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()),
            Times.Exactly(2));
        _requestIndexingRepository.Verify(x => x.DeleteAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task Orchestrator_RetryPolicy_LogsOnEachRetry()
    {
        // arrange
        var requestEvents = new List<UCXEvent>();
        var firstEvent = new RequestForServiceSubmittedEvent
        {
            RequestForServiceKey = "RFS123",
            Name = "RequestForServiceSubmitted",
            AuthorizationKey = "KEY",
            Version = "1.0.0.0",
            Sent = DateTime.Now
        };
        requestEvents.Add(firstEvent);

        var history = new RequestEventHistoryRequest
        {
            History = requestEvents,
            RequestForServiceKey = firstEvent.RequestForServiceKey
        };

        _eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);
        _eventHistoryRepository
            .SetupSequence(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()))
            .Throws(new CosmosException("Exception", HttpStatusCode.PreconditionFailed, 1, "activity", double.MinValue))
            .Throws(new CosmosException("Exception", HttpStatusCode.PreconditionFailed, 1, "activity", double.MinValue))
            .Throws(new CosmosException("Exception", HttpStatusCode.PreconditionFailed, 1, "activity", double.MinValue))
            .Throws(new CosmosException("Exception", HttpStatusCode.PreconditionFailed, 1, "activity",
                double.MinValue));

        var replayResult = new RequestAggregate();
        replayResult.UpdateSpecialty(SpecialtyEnum.Sleep);

        _replayService.Setup(x => x.ReplayEvents(It.IsAny<List<UCXEvent>>())).ReturnsAsync(replayResult);

        // act
        await _orchestrator.OrchestrateAsync(firstEvent, CancellationToken.None);

        // assert
        _logger.Verify(logger => logger.Log(
                It.Is<LogLevel>(logLevel => logLevel == LogLevel.Information),
                It.Is<EventId>(eventId => eventId.Id == 0),
                It.Is<It.IsAnyType>((@object, type) => @object.ToString().Contains("retry attempt number")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Orchestrator_WillCallSaveToRequestRepository_WhenNoDisciplineAndOutOfOrderEvents()
    {
        // arrange
        var authorizationKey = Guid.NewGuid();
        var requestForServiceKey = Guid.NewGuid();
        var requestForServiceId = $"r{requestForServiceKey.ToString().Substring(0, 5)}";
        var sentDate = DateTime.UtcNow;
        var requestEvents = new List<UCXEvent>();

        var history = new RequestEventHistoryRequest
        {
            History = requestEvents
        };

        _eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);

        var replayResult = new RequestAggregate();

        _replayService.Setup(x => x.ReplayEvents(It.IsAny<List<UCXEvent>>())).ReturnsAsync(replayResult);

        var newEvent = new StatusChangeEvent
        {
            AuthorizationKey = authorizationKey,
            Key = Guid.NewGuid().ToString(),
            Name = "UCXStatusChange",
            OriginSystem = "eP",
            RequestForServiceId = requestForServiceId,
            RequestForServiceKey = requestForServiceKey.ToString(),
            Sent = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(5)),
            Version = "1.3.0",
            Outcome = DecisionOutcomesEnum.PendingNurseReview.ToString(),
            Status = DecisionOutcomesEnum.PendingNurseReview.ToString()
        };

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        _eventHistoryRepository.Verify(x => x.GetRequestEventHistory(It.IsAny<string>()), Times.Once);
        _eventHistoryRepository.Verify(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()),
            Times.Once);
        _requestIndexingRepository.Verify(x => x.DeleteAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
        _requestIndexingRepository.Verify(x => x.UpsertWithAssignmentAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task Orchestrator_WillBeRoutable_WhenDisciplineReceivedAndOutOfOrderEvents()
    {
        // arrange
        var authorizationKey = Guid.NewGuid();
        var requestForServiceKey = Guid.NewGuid();
        var requestForServiceId = $"r{requestForServiceKey.ToString().Substring(0, 5)}";
        var sentDate = DateTime.UtcNow;
        var requestEvents = new List<UCXEvent>
        {
        new StatusChangeEvent
        {
            AuthorizationKey = authorizationKey,
            Key = Guid.NewGuid().ToString(),
            Name = "UCXStatusChange",
            OriginSystem = "eP",
            RequestForServiceId = requestForServiceId,
            RequestForServiceKey = requestForServiceKey.ToString(),
            Sent = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(5)),
            Version = "1.3.0",
            Outcome = DecisionOutcomesEnum.PendingMDReview.ToString(),
            Status = DecisionOutcomesEnum.PendingMDReview.ToString()
        },
        new DueDateCalculatedEvent
            {
                Name = "UCXDueDateCalculated",
                Version = "1.0.0.0",
                DueDateByDateTimeUtc = DateTime.Today.AddDays(5),
                Sent = sentDate.AddMinutes(5),
                Key = Guid.NewGuid().ToString()
            }
        };

        var history = new RequestEventHistoryRequest
        {
            History = requestEvents
        };

        _eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);

        var replayResult = new RequestAggregate();
        replayResult.UpdateStatus(StatusEnum.NeedsMdReview);
        replayResult.UpdateDueDateCalculated(new DueDateCalculatedObject
        { DueDateUtc = sentDate.AddMinutes(5), RequestForServiceKey = requestForServiceKey });
        replayResult.UpdateStartDate(sentDate);
        replayResult.UpdateMedicalDiscipline(DisciplineEnum.Sleep);
        replayResult.UpdateRequestUrgencyUpdated(new RequestUrgencyUpdatedObject { Priority = PriorityEnum.Standard });

        _replayService.Setup(x => x.ReplayEvents(It.IsAny<List<UCXEvent>>())).ReturnsAsync(replayResult);

        var newEvent = new RequestForServiceSubmittedEvent
        {
            RequestForServiceKey = requestForServiceKey.ToString(),
            RequestForServiceId = requestForServiceId,
            AuthorizationKey = authorizationKey.ToString(),
            MedicalDiscipline = DisciplineEnum.Sleep.ToString(),
            IsUrgent = false,
            DateOfService = sentDate.AddDays(6),
            Name = "RequestForServiceSubmitted",
            Version = "1.0.0.0",
            Sent = sentDate.AddMinutes(45),
            Key = Guid.NewGuid().ToString()
        };

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        _eventHistoryRepository.Verify(x => x.GetRequestEventHistory(It.IsAny<string>()), Times.Once);
        _eventHistoryRepository.Verify(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()), Times.Once);
        _requestIndexingRepository.Verify(x => x.DeleteAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
        _requestIndexingRepository.Verify(x => x.UpsertWithAssignmentAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Once);
    }


    [Fact]
    public async Task Orchestrator_CallsCreateRequestEventHistoryOnce_WhenEventHistoryDoesNotExist()
    {
        // arrange
        var requestEvents = new List<UCXEvent>();
        var history = new RequestEventHistoryRequest
        {
            History = null
        };
        _eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);

        var newEvent = new DueDateCalculatedEvent
        {
            Name = "UCXRequestFoService",
            Version = "1.0.0.0",
            DueDateByDateTimeUtc = DateTime.Today.AddDays(14)
        };

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        _eventHistoryRepository.Verify(x => x.CreateRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()),
            Times.Once);
    }

    [Fact]
    public async Task Orchestrator_DoesNotCallCreateRequestEventHistoryOnce_WhenEventHistoryIsNotNull()
    {
        // arrange
        var requestEvents = new List<UCXEvent>();
        var history = new RequestEventHistoryRequest
        {
            History = new List<UCXEvent>()
        };

        _eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);

        var newEvent = new DueDateCalculatedEvent
        {
            Name = "UCXRequestFoService",
            Version = "1.0.0.0",
            DueDateByDateTimeUtc = DateTime.Today.AddDays(14)
        };

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        _eventHistoryRepository.Verify(x => x.CreateRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()),
            Times.Never);
    }

    [Fact]
    public async Task Orchestrator_DoesNotSaveHistory_WhenNumberOfEventsOnARequestMaxesOut()
    {
        // arrange
        var requestEvents = new List<UCXEvent>();
        var key = Guid.NewGuid().ToString();
        for (var i = 0; i < 1000; i++)
        {
            var firstEvent = new RequestForServiceSubmittedEvent
            {
                RequestForServiceKey = key,
                Name = "RequestForServiceSubmitted",
                AuthorizationKey = "KEY",
                Version = "1.0.0.0",
                Sent = DateTime.Now
            };

            requestEvents.Add(firstEvent);
        }


        var history = new RequestEventHistoryRequest
        {
            History = requestEvents,
            RequestForServiceKey = key
        };

        _eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);

        var newEvent = new DueDateCalculatedEvent
        {
            Name = "UCXRequestFoService",
            Version = "1.0.0.0",
            DueDateByDateTimeUtc = DateTime.Today.AddDays(14)
        };

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        _eventHistoryRepository.Verify(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()), Times.Never);

        _logger.Verify(logger => logger.Log(
                It.Is<LogLevel>(logLevel => logLevel == LogLevel.Information),
                It.Is<EventId>(eventId => eventId.Id == 0),
                It.Is<It.IsAnyType>((@object, type) =>
                    @object.ToString()
                        .Contains(
                            "Request reached max events allowed per single request")),
                It.IsAny<Exception>(),
               It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Exactly(1));
    }

    [Fact]
    public async Task Orchestrator_DoesNotDeleteDuplicateRequest_WhenSentDateIsAfterDataFix()
    {
        // arrange
        var duplicateRequestForServiceKey = Guid.NewGuid();
        var requestForServiceKey = Guid.NewGuid();
        var requestForServiceId = "RFS123Id";
        var requestEvents = new List<UCXEvent>
        {
            new RequestForServiceSubmittedEvent { RequestForServiceKey = duplicateRequestForServiceKey.ToString() }
        };

        var history = new RequestEventHistoryRequest
        {
            History = new List<UCXEvent>()
        };

        var duplicateHistory = new RequestEventHistoryRequest
        {
            RequestForServiceKey = duplicateRequestForServiceKey.ToString(),
            History = requestEvents
        };

        _eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);
        _eventHistoryRepository.Setup(x => x.GetDuplicateRequestEventHistory(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(duplicateHistory);

        var dueDateCalculatedObject = new DueDateCalculatedObject
        {
            DueDateUtc = DateTime.Today.AddDays(14),
            RequestForServiceKey = requestForServiceKey
        };

        var replayResult = new RequestAggregate();
        replayResult.UpdateStatus(StatusEnum.NeedsMdReview);
        replayResult.UpdateDueDateCalculated(dueDateCalculatedObject);
        replayResult.UpdateStartDate(DateTime.Now);

        _replayService.Setup(x => x.ReplayEvents(It.IsAny<List<UCXEvent>>())).ReturnsAsync(replayResult);

        var newEvent = new RequestForServiceSubmittedEvent
        {
            RequestForServiceKey = requestForServiceKey.ToString(),
            RequestForServiceId = requestForServiceId,
            Name = "UCXRequestForServiceSubmitted",
            Version = "1.0.0.0",
            Sent = new DateTime(2023, 06, 24).ToUniversalTime(),
            StartDate = new DateTime(2023, 03, 02).ToUniversalTime(),
            OriginSystem = "IOne",
            MedicalDiscipline = "Sleep"
        };

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert

        _eventHistoryRepository.Verify(x => x.GetDuplicateRequestEventHistory(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);

        _logger.Verify(logger => logger.Log(
                It.Is<LogLevel>(logLevel => logLevel == LogLevel.Information),
                It.Is<EventId>(eventId => eventId.Id == 0),
                It.Is<It.IsAnyType>((@object, type) =>
                    @object.ToString()
                        .Contains(
                            $"Request {requestForServiceKey} removed due to duplication")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task Orchestrator_DoesNotDeleteDuplicateRequest_WhenStartDateEqualsMinDate()
    {
        // arrange
        var duplicateRequestForServiceKey = Guid.NewGuid();
        var requestForServiceKey = Guid.NewGuid();
        var requestForServiceId = "RFS123Id";
        var requestEvents = new List<UCXEvent>
        {
            new RequestForServiceSubmittedEvent { RequestForServiceKey = duplicateRequestForServiceKey.ToString() }
        };

        var history = new RequestEventHistoryRequest
        {
            History = new List<UCXEvent>()
        };

        var duplicateHistory = new RequestEventHistoryRequest
        {
            RequestForServiceKey = duplicateRequestForServiceKey.ToString(),
            History = requestEvents
        };

        _eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);
        _eventHistoryRepository.Setup(x => x.GetDuplicateRequestEventHistory(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(duplicateHistory);

        var dueDateCalculatedObject = new DueDateCalculatedObject
        {
            DueDateUtc = DateTime.Today.AddDays(14),
            RequestForServiceKey = requestForServiceKey
        };

        var replayResult = new RequestAggregate();
        replayResult.UpdateStatus(StatusEnum.NeedsMdReview);
        replayResult.UpdateDueDateCalculated(dueDateCalculatedObject);
        replayResult.UpdateStartDate(DateTime.Now);

        _replayService.Setup(x => x.ReplayEvents(It.IsAny<List<UCXEvent>>())).ReturnsAsync(replayResult);

        var newEvent = new RequestForServiceSubmittedEvent
        {
            RequestForServiceKey = requestForServiceKey.ToString(),
            RequestForServiceId = requestForServiceId,
            Name = "UCXRequestForServiceSubmitted",
            Version = "1.0.0.0",
            Sent = new DateTime(2023, 06, 23).ToUniversalTime(),
            StartDate = DateTime.MinValue,
            OriginSystem = "IOne",
            MedicalDiscipline = "Sleep"
        };

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert

        _eventHistoryRepository.Verify(x => x.GetDuplicateRequestEventHistory(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);

        _logger.Verify(logger => logger.Log(
                It.Is<LogLevel>(logLevel => logLevel == LogLevel.Information),
                It.Is<EventId>(eventId => eventId.Id == 0),
                It.Is<It.IsAnyType>((@object, type) =>
                    @object.ToString()
                        .Contains(
                            $"Request {requestForServiceKey} removed due to duplication")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Never);
    }


    [Fact]
    public async Task Orchestrator_DoesNotDeleteDuplicateRequest_WhenStartDateIsBeforeRoutingStartDate()
    {
        // arrange
        var duplicateRequestForServiceKey = Guid.NewGuid();
        var requestForServiceKey = Guid.NewGuid();
        var requestForServiceId = "RFS123Id";
        var requestEvents = new List<UCXEvent>
        {
            new RequestForServiceSubmittedEvent { RequestForServiceKey = duplicateRequestForServiceKey.ToString() }
        };

        var history = new RequestEventHistoryRequest
        {
            History = new List<UCXEvent>()
        };

        var duplicateHistory = new RequestEventHistoryRequest
        {
            RequestForServiceKey = duplicateRequestForServiceKey.ToString(),
            History = requestEvents
        };

        _eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);
        _eventHistoryRepository.Setup(x => x.GetDuplicateRequestEventHistory(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(duplicateHistory);

        var dueDateCalculatedObject = new DueDateCalculatedObject
        {
            DueDateUtc = DateTime.Today.AddDays(14),
            RequestForServiceKey = requestForServiceKey
        };

        var replayResult = new RequestAggregate();
        replayResult.UpdateStatus(StatusEnum.NeedsMdReview);
        replayResult.UpdateDueDateCalculated(dueDateCalculatedObject);
        replayResult.UpdateStartDate(DateTime.Now);

        _replayService.Setup(x => x.ReplayEvents(It.IsAny<List<UCXEvent>>())).ReturnsAsync(replayResult);

        var newEvent = new RequestForServiceSubmittedEvent
        {
            RequestForServiceKey = requestForServiceKey.ToString(),
            RequestForServiceId = requestForServiceId,
            Name = "UCXRequestForServiceSubmitted",
            Version = "1.0.0.0",
            Sent = new DateTime(2023, 06, 23).ToUniversalTime(),
            StartDate = new DateTime(2023, 02, 28).ToUniversalTime(),
            OriginSystem = "IOne",
            MedicalDiscipline = "Sleep"
        };

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        _eventHistoryRepository.Verify(x => x.GetDuplicateRequestEventHistory(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);

        _logger.Verify(logger => logger.Log(
                It.Is<LogLevel>(logLevel => logLevel == LogLevel.Information),
                It.Is<EventId>(eventId => eventId.Id == 0),
                It.Is<It.IsAnyType>((@object, type) =>
                    @object.ToString()
                        .Contains(
                            $"Request {requestForServiceKey} removed due to duplication")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task Orchestrator_DoesNotDeleteDuplicateRequest_WhenOriginSystemIsNotIOne()
    {
        // arrange
        var duplicateRequestForServiceKey = Guid.NewGuid();
        var requestForServiceKey = Guid.NewGuid();
        var requestForServiceId = "RFS123Id";
        var requestEvents = new List<UCXEvent>
        {
            new RequestForServiceSubmittedEvent { RequestForServiceKey = duplicateRequestForServiceKey.ToString() }
        };

        var history = new RequestEventHistoryRequest
        {
            History = new List<UCXEvent>()
        };

        var duplicateHistory = new RequestEventHistoryRequest
        {
            RequestForServiceKey = duplicateRequestForServiceKey.ToString(),
            History = requestEvents
        };

        _eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);
        _eventHistoryRepository.Setup(x => x.GetDuplicateRequestEventHistory(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(duplicateHistory);

        var dueDateCalculatedObject = new DueDateCalculatedObject
        {
            DueDateUtc = DateTime.Today.AddDays(14),
            RequestForServiceKey = requestForServiceKey
        };

        var replayResult = new RequestAggregate();
        replayResult.UpdateStatus(StatusEnum.NeedsMdReview);
        replayResult.UpdateDueDateCalculated(dueDateCalculatedObject);
        replayResult.UpdateStartDate(DateTime.Now);

        _replayService.Setup(x => x.ReplayEvents(It.IsAny<List<UCXEvent>>())).ReturnsAsync(replayResult);

        var newEvent = new RequestForServiceSubmittedEvent
        {
            RequestForServiceKey = requestForServiceKey.ToString(),
            RequestForServiceId = requestForServiceId,
            Name = "UCXRequestForServiceSubmitted",
            Version = "1.0.0.0",
            Sent = new DateTime(2023, 06, 23).ToUniversalTime(),
            StartDate = new DateTime(2023, 03, 28).ToUniversalTime(),
            OriginSystem = "eP",
            MedicalDiscipline = "Sleep"
        };

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        _eventHistoryRepository.Verify(x => x.GetDuplicateRequestEventHistory(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);

        _logger.Verify(logger => logger.Log(
                It.Is<LogLevel>(logLevel => logLevel == LogLevel.Information),
                It.Is<EventId>(eventId => eventId.Id == 0),
                It.Is<It.IsAnyType>((@object, type) =>
                    @object.ToString()
                        .Contains(
                            $"Request {requestForServiceKey} removed due to duplication")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task Orchestrator_DoesNotDeleteDuplicateRequest_WhenMedicalDisciplineIsNotRoutable()
    {
        // arrange
        var duplicateRequestForServiceKey = Guid.NewGuid();
        var requestForServiceKey = Guid.NewGuid();
        var requestForServiceId = "RFS123Id";
        var requestEvents = new List<UCXEvent>
        {
            new RequestForServiceSubmittedEvent { RequestForServiceKey = duplicateRequestForServiceKey.ToString() }
        };

        var history = new RequestEventHistoryRequest
        {
            History = new List<UCXEvent>()
        };

        var duplicateHistory = new RequestEventHistoryRequest
        {
            RequestForServiceKey = duplicateRequestForServiceKey.ToString(),
            History = requestEvents
        };

        _eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);
        _eventHistoryRepository.Setup(x => x.GetDuplicateRequestEventHistory(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(duplicateHistory);

        var dueDateCalculatedObject = new DueDateCalculatedObject
        {
            DueDateUtc = DateTime.Today.AddDays(14),
            RequestForServiceKey = requestForServiceKey
        };

        var replayResult = new RequestAggregate();
        replayResult.UpdateStatus(StatusEnum.NeedsMdReview);
        replayResult.UpdateDueDateCalculated(dueDateCalculatedObject);
        replayResult.UpdateStartDate(DateTime.Now);

        _replayService.Setup(x => x.ReplayEvents(It.IsAny<List<UCXEvent>>())).ReturnsAsync(replayResult);

        var newEvent = new RequestForServiceSubmittedEvent
        {
            RequestForServiceKey = requestForServiceKey.ToString(),
            RequestForServiceId = requestForServiceId,
            Name = "UCXRequestForServiceSubmitted",
            Version = "1.0.0.0",
            Sent = new DateTime(2023, 06, 23).ToUniversalTime(),
            StartDate = new DateTime(2023, 03, 28).ToUniversalTime(),
            OriginSystem = "IOne",
            MedicalDiscipline = DisciplineEnum.Cardiology.ToString()
        };

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        _eventHistoryRepository.Verify(x => x.GetDuplicateRequestEventHistory(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);

        _logger.Verify(logger => logger.Log(
                It.Is<LogLevel>(logLevel => logLevel == LogLevel.Information),
                It.Is<EventId>(eventId => eventId.Id == 0),
                It.Is<It.IsAnyType>((@object, type) =>
                    @object.ToString()
                        .Contains(
                            $"Request {requestForServiceKey} removed due to duplication")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task Orchestrator_DoesNotDeleteDuplicateRequest_WhenRequestForServiceIdIsNull()
    {
        // arrange
        var duplicateRequestForServiceKey = Guid.NewGuid();
        var requestForServiceKey = Guid.NewGuid();
        var requestForServiceId = "RFS123Id";
        var requestEvents = new List<UCXEvent>
        {
            new RequestForServiceSubmittedEvent { RequestForServiceKey = duplicateRequestForServiceKey.ToString() }
        };

        var history = new RequestEventHistoryRequest
        {
            History = new List<UCXEvent>()
        };

        var duplicateHistory = new RequestEventHistoryRequest
        {
            RequestForServiceKey = duplicateRequestForServiceKey.ToString(),
            History = requestEvents
        };

        _eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);
        _eventHistoryRepository.Setup(x => x.GetDuplicateRequestEventHistory(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(duplicateHistory);

        var dueDateCalculatedObject = new DueDateCalculatedObject
        {
            DueDateUtc = DateTime.Today.AddDays(14),
            RequestForServiceKey = requestForServiceKey
        };

        var replayResult = new RequestAggregate();
        replayResult.UpdateStatus(StatusEnum.NeedsMdReview);
        replayResult.UpdateDueDateCalculated(dueDateCalculatedObject);
        replayResult.UpdateStartDate(DateTime.Now);

        _replayService.Setup(x => x.ReplayEvents(It.IsAny<List<UCXEvent>>())).ReturnsAsync(replayResult);

        var newEvent = new RequestForServiceSubmittedEvent
        {
            RequestForServiceKey = requestForServiceKey.ToString(),
            RequestForServiceId = null,
            Name = "UCXRequestForServiceSubmitted",
            Version = "1.0.0.0",
            Sent = new DateTime(2023, 06, 23).ToUniversalTime(),
            StartDate = new DateTime(2023, 03, 28).ToUniversalTime(),
            OriginSystem = "IOne",
            MedicalDiscipline = DisciplineEnum.Sleep.ToString()
        };

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        _eventHistoryRepository.Verify(x => x.GetDuplicateRequestEventHistory(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);

        _logger.Verify(logger => logger.Log(
                It.Is<LogLevel>(logLevel => logLevel == LogLevel.Information),
                It.Is<EventId>(eventId => eventId.Id == 0),
                It.Is<It.IsAnyType>((@object, type) =>
                    @object.ToString()
                        .Contains(
                            $"Request {requestForServiceKey} removed due to duplication")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Never);
    }
    [Fact]

    public async Task WhenAppealsrequestDidNotGetAssignmentFromIOne_RequestIsNotRoutable()
    {
        // arrange
        var authorizationKey = Guid.NewGuid();
        var requestForServiceId = $"r{_requestForServiceKey.ToString().Substring(0, 5)}";
        var sentDate = DateTime.UtcNow;
        var requestEvents = new List<UCXEvent>
        {
        new RequestForServiceSubmittedEvent
        {
            RequestForServiceKey = _requestForServiceKey.ToString(),
            RequestForServiceId = requestForServiceId,
            AuthorizationKey = authorizationKey.ToString(),
            MedicalDiscipline = SpecialtyEnum.Sleep.ToString(),
            IsUrgent = false,
            DateOfService = sentDate.AddDays(6),
            Name = "RequestForServiceSubmitted",
            Version = "1.0.0.0",
            Sent = sentDate.AddMinutes(45),
            Key = Guid.NewGuid().ToString()
        },
        new StatusChangeEvent
        {
            AuthorizationKey = authorizationKey,
            Key = Guid.NewGuid().ToString(),
            Name = "UCXStatusChange",
            OriginSystem = "eP",
            RequestForServiceId = requestForServiceId,
            RequestForServiceKey = _requestForServiceKey.ToString(),
            Sent = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(5)),
            Version = "1.3.0",
            Outcome = DecisionOutcomesEnum.Appeal.ToString(),
            Status = DecisionOutcomesEnum.Appeal.ToString()
        }
        };


        var history = new RequestEventHistoryRequest
        {
            History = requestEvents
        };

        _eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);

        var replayResult = new RequestAggregate();
        replayResult.UpdateMedicalDiscipline(DisciplineEnum.Sleep);
        replayResult.UpdateStatus(StatusEnum.Appeal);
       replayResult.UpdateDueDateCalculated(new DueDateCalculatedObject
        { DueDateUtc = sentDate.AddMinutes(5), RequestForServiceKey = _requestForServiceKey });
        replayResult.UpdateStartDate(sentDate);
        replayResult.UpdateSpecialty(SpecialtyEnum.Sleep);
        replayResult.UpdateRequestUrgencyUpdated(new RequestUrgencyUpdatedObject { Priority = PriorityEnum.Standard });
        replayResult.UpdateAssignment("dummy.a@evicore.com", DateTime.UtcNow.AddHours(-2));

        _replayService.Setup(x => x.ReplayEvents(It.IsAny<List<UCXEvent>>())).ReturnsAsync(replayResult);


        var newEvent = new DueDateCalculatedEvent
        {
            Name = "UCXDueDateCalculated",
            Version = "1.0.0.0",
            DueDateByDateTimeUtc = DateTime.Today.AddDays(5),
            Sent = sentDate.AddMinutes(5),
            Key = Guid.NewGuid().ToString()
        };

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        _eventHistoryRepository.Verify(x => x.GetRequestEventHistory(It.IsAny<string>()), Times.Once);
        _eventHistoryRepository.Verify(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()), Times.Once);
        _requestIndexingRepository.Verify(x => x.UpsertWithAssignmentAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
        _requestIndexingRepository.Verify(x => x.DeleteAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]

    public async Task WhenAppealsrequestGetAssignmentFromIOne_RequestIsRoutable()
    {
        // arrange
        var authorizationKey = Guid.NewGuid();
        var requestForServiceKey = Guid.NewGuid();
        var requestForServiceId = $"r{requestForServiceKey.ToString().Substring(0, 5)}";
        var sentDate = DateTime.UtcNow;
        var requestEvents = new List<UCXEvent>
        {
        new RequestForServiceSubmittedEvent
        {
            RequestForServiceKey = requestForServiceKey.ToString(),
            RequestForServiceId = requestForServiceId,
            AuthorizationKey = authorizationKey.ToString(),
            MedicalDiscipline = SpecialtyEnum.Sleep.ToString(),
            IsUrgent = false,
            DateOfService = sentDate.AddDays(6),
            Name = "RequestForServiceSubmitted",
            Version = "1.0.0.0",
            Sent = sentDate.AddMinutes(45),
            Key = Guid.NewGuid().ToString()
        },
        new StatusChangeEvent
        {
            AuthorizationKey = authorizationKey,
            Key = Guid.NewGuid().ToString(),
            Name = "UCXStatusChange",
            OriginSystem = "eP",
            RequestForServiceId = requestForServiceId,
            RequestForServiceKey = requestForServiceKey.ToString(),
            Sent = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(5)),
            Version = "1.3.0",
            Outcome = DecisionOutcomesEnum.Appeal.ToString(),
            Status = DecisionOutcomesEnum.Appeal.ToString()
        },
        new DueDateCalculatedEvent
            {
                Name = "UCXDueDateCalculated",
                Version = "1.0.0.0",
                DueDateByDateTimeUtc = DateTime.Today.AddDays(5),
                Sent = sentDate.AddMinutes(5),
                Key = Guid.NewGuid().ToString()
            }
        };

        var history = new RequestEventHistoryRequest
        {
            History = requestEvents
        };

        _eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);

        var replayResult = new RequestAggregate();
        replayResult.UpdateMedicalDiscipline(DisciplineEnum.Sleep);
        replayResult.UpdateDueDateCalculated(new DueDateCalculatedObject
        { DueDateUtc = sentDate.AddMinutes(5), RequestForServiceKey = requestForServiceKey });
        replayResult.UpdateStartDate(sentDate);
        replayResult.UpdateStatus(StatusEnum.Appeal);
        replayResult.UpdateSpecialty(SpecialtyEnum.Sleep);
        replayResult.UpdateMedicalDiscipline(DisciplineEnum.Sleep);
        replayResult.UpdateRequestUrgencyUpdated(new RequestUrgencyUpdatedObject { Priority = PriorityEnum.Standard });
        DateTime assignmentexpirationutc = DateTime.UtcNow.AddDays(999);
        string AssignedToId = "pawan.vatge@evicore.com";
        replayResult.UpdateAssignment(AssignedToId, assignmentexpirationutc);

        _replayService.Setup(x => x.ReplayEvents(It.IsAny<List<UCXEvent>>())).ReturnsAsync(replayResult);

        var newEvent = new RoutingRequestAssignedEvent
        {
            UnAssigned = false,
            AssignedToId = AssignedToId,
            AssignmentExpirationUtc = assignmentexpirationutc,
            IsPermanentAssignment = true,
            RequestForServiceId = requestForServiceId,
            RequestForServiceKey = requestForServiceKey.ToString(),
            Version = "1.1.0"

        };

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        _eventHistoryRepository.Verify(x => x.GetRequestEventHistory(It.IsAny<string>()), Times.Once);
        _eventHistoryRepository.Verify(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()), Times.Once);
        _requestIndexingRepository.Verify(x => x.UpsertWithAssignmentAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Once);
        _requestIndexingRepository.Verify(x => x.DeleteAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
    }

    public async Task WhenAppealsRecommendedrequestDidNotGetAssignmentFromIOne_RequestIsNotRoutable()
    {
        // arrange
        var authorizationKey = Guid.NewGuid();
        var requestForServiceKey = Guid.NewGuid();
        var requestForServiceId = $"r{requestForServiceKey.ToString().Substring(0, 5)}";
        var sentDate = DateTime.UtcNow;
        var requestEvents = new List<UCXEvent>
        {
        new RequestForServiceSubmittedEvent
        {
            RequestForServiceKey = requestForServiceKey.ToString(),
            RequestForServiceId = requestForServiceId,
            AuthorizationKey = authorizationKey.ToString(),
            MedicalDiscipline = SpecialtyEnum.Sleep.ToString(),
            IsUrgent = false,
            DateOfService = sentDate.AddDays(6),
            Name = "RequestForServiceSubmitted",
            Version = "1.0.0.0",
            Sent = sentDate.AddMinutes(45),
            Key = Guid.NewGuid().ToString()
        },
        new StatusChangeEvent
        {
            AuthorizationKey = authorizationKey,
            Key = Guid.NewGuid().ToString(),
            Name = "UCXStatusChange",
            OriginSystem = "eP",
            RequestForServiceId = requestForServiceId,
            RequestForServiceKey = requestForServiceKey.ToString(),
            Sent = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(5)),
            Version = "1.3.0",
            Outcome = DecisionOutcomesEnum.Appeal.ToString(),
            Status = DecisionOutcomesEnum.Appeal.ToString()
        }
        };


        var history = new RequestEventHistoryRequest
        {
            History = requestEvents
        };

        _eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);

        var replayResult = new RequestAggregate();
        replayResult.UpdateMedicalDiscipline(DisciplineEnum.Sleep);
        replayResult.UpdateStatus(StatusEnum.AppealRecommended);
        replayResult.UpdateDueDateCalculated(new DueDateCalculatedObject
        { DueDateUtc = sentDate.AddMinutes(5), RequestForServiceKey = requestForServiceKey });
        replayResult.UpdateStartDate(sentDate);
        replayResult.UpdateSpecialty(SpecialtyEnum.Sleep);
        replayResult.UpdateRequestUrgencyUpdated(new RequestUrgencyUpdatedObject { Priority = PriorityEnum.Standard });
        replayResult.UpdateAssignment("dummy.a@evicore.com", DateTime.UtcNow.AddHours(-2));

        _replayService.Setup(x => x.ReplayEvents(It.IsAny<List<UCXEvent>>())).ReturnsAsync(replayResult);


        var newEvent = new DueDateCalculatedEvent
        {
            Name = "UCXDueDateCalculated",
            Version = "1.0.0.0",
            DueDateByDateTimeUtc = DateTime.Today.AddDays(5),
            Sent = sentDate.AddMinutes(5),
            Key = Guid.NewGuid().ToString()
        };

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        _eventHistoryRepository.Verify(x => x.GetRequestEventHistory(It.IsAny<string>()), Times.Once);
        _eventHistoryRepository.Verify(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()), Times.Once);
        _requestIndexingRepository.Verify(x => x.UpsertWithAssignmentAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
        _requestIndexingRepository.Verify(x => x.DeleteAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]

    public async Task WhenAppealsRecommendedrequestGetAssignmentFromIOne_RequestIsRoutable()
    {
        // arrange
        var authorizationKey = Guid.NewGuid();
        var requestForServiceKey = Guid.NewGuid();
        var requestForServiceId = $"r{requestForServiceKey.ToString().Substring(0, 5)}";
        var sentDate = DateTime.UtcNow;
        var requestEvents = new List<UCXEvent>
        {
        new RequestForServiceSubmittedEvent
        {
            RequestForServiceKey = requestForServiceKey.ToString(),
            RequestForServiceId = requestForServiceId,
            AuthorizationKey = authorizationKey.ToString(),
            MedicalDiscipline = SpecialtyEnum.Sleep.ToString(),
            IsUrgent = false,
            DateOfService = sentDate.AddDays(6),
            Name = "RequestForServiceSubmitted",
            Version = "1.0.0.0",
            Sent = sentDate.AddMinutes(45),
            Key = Guid.NewGuid().ToString()
        },
        new StatusChangeEvent
        {
            AuthorizationKey = authorizationKey,
            Key = Guid.NewGuid().ToString(),
            Name = "UCXStatusChange",
            OriginSystem = "eP",
            RequestForServiceId = requestForServiceId,
           RequestForServiceKey = requestForServiceKey.ToString(),
            Sent = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(5)),
            Version = "1.3.0",
            Outcome = DecisionOutcomesEnum.Appeal.ToString(),
            Status = DecisionOutcomesEnum.Appeal.ToString()
        },
        new DueDateCalculatedEvent
            {
                Name = "UCXDueDateCalculated",
                Version = "1.0.0.0",
                DueDateByDateTimeUtc = DateTime.Today.AddDays(5),
                Sent = sentDate.AddMinutes(5),
                Key = Guid.NewGuid().ToString()
            }
        };

        var history = new RequestEventHistoryRequest
        {
            History = requestEvents
        };

        _eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);

        var replayResult = new RequestAggregate();
        replayResult.UpdateMedicalDiscipline(DisciplineEnum.Sleep);
        replayResult.UpdateDueDateCalculated(new DueDateCalculatedObject
        { DueDateUtc = sentDate.AddMinutes(5), RequestForServiceKey = requestForServiceKey });
        replayResult.UpdateStartDate(sentDate);
        replayResult.UpdateStatus(StatusEnum.AppealRecommended);
        replayResult.UpdateSpecialty(SpecialtyEnum.Sleep);
        replayResult.UpdateMedicalDiscipline(DisciplineEnum.Sleep);
        replayResult.UpdateRequestUrgencyUpdated(new RequestUrgencyUpdatedObject { Priority = PriorityEnum.Standard });
        DateTime assignmentexpirationutc = DateTime.UtcNow.AddDays(999);
        string AssignedToId = "pawan.vatge@evicore.com";
        replayResult.UpdateAssignment(AssignedToId, assignmentexpirationutc);

        _replayService.Setup(x => x.ReplayEvents(It.IsAny<List<UCXEvent>>())).ReturnsAsync(replayResult);

        var newEvent = new RoutingRequestAssignedEvent
        {
            UnAssigned = false,
            AssignedToId = AssignedToId,
            AssignmentExpirationUtc = assignmentexpirationutc,
            IsPermanentAssignment = true,
            RequestForServiceId = requestForServiceId,
            RequestForServiceKey = requestForServiceKey.ToString(),
            Version = "1.1.0"

        };

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        _eventHistoryRepository.Verify(x => x.GetRequestEventHistory(It.IsAny<string>()), Times.Once);
        _eventHistoryRepository.Verify(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()), Times.Once);
        _requestIndexingRepository.Verify(x => x.UpsertWithAssignmentAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Once);
        _requestIndexingRepository.Verify(x => x.DeleteAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task WhenReconsrequestDidNotGetAssignmentFromIOne_RequestIsNotRoutable()
    {
        // arrange
        var authorizationKey = Guid.NewGuid();
        var requestForServiceId = $"r{_requestForServiceKey.ToString().Substring(0, 5)}";
        var sentDate = DateTime.UtcNow;
        var requestEvents = new List<UCXEvent>
        {
        new RequestForServiceSubmittedEvent
        {
            RequestForServiceKey = _requestForServiceKey.ToString(),
            RequestForServiceId = requestForServiceId,
            AuthorizationKey = authorizationKey.ToString(),
            MedicalDiscipline = SpecialtyEnum.Sleep.ToString(),
            IsUrgent = false,
            DateOfService = sentDate.AddDays(6),
            Name = "RequestForServiceSubmitted",
            Version = "1.0.0.0",
            Sent = sentDate.AddMinutes(45),
            Key = Guid.NewGuid().ToString()
        },
        new StatusChangeEvent
        {
            AuthorizationKey = authorizationKey,
            Key = Guid.NewGuid().ToString(),
            Name = "UCXStatusChange",
            OriginSystem = "eP",
            RequestForServiceId = requestForServiceId,
            RequestForServiceKey = _requestForServiceKey.ToString(),
            Sent = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(5)),
            Version = "1.3.0",
            Outcome = DecisionOutcomesEnum.Appeal.ToString(),
            Status = DecisionOutcomesEnum.Appeal.ToString()
        }
        };


        var history = new RequestEventHistoryRequest
        {
            History = requestEvents
        };

        _eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);

        var replayResult = new RequestAggregate();
        replayResult.UpdateMedicalDiscipline(DisciplineEnum.Sleep);
        replayResult.UpdateStatus(StatusEnum.Reconsideration);
        replayResult.UpdateDueDateCalculated(new DueDateCalculatedObject
        { DueDateUtc = sentDate.AddMinutes(5), RequestForServiceKey = _requestForServiceKey });
        replayResult.UpdateStartDate(sentDate);
        replayResult.UpdateSpecialty(SpecialtyEnum.Sleep);
        replayResult.UpdateRequestUrgencyUpdated(new RequestUrgencyUpdatedObject { Priority = PriorityEnum.Standard });
        replayResult.UpdateAssignment("dummy.a@evicore.com", DateTime.UtcNow.AddHours(-2));

        _replayService.Setup(x => x.ReplayEvents(It.IsAny<List<UCXEvent>>())).ReturnsAsync(replayResult);

        var newEvent = new DueDateCalculatedEvent
        {
            Name = "UCXDueDateCalculated",
            Version = "1.0.0.0",
            DueDateByDateTimeUtc = DateTime.Today.AddDays(5),
            Sent = sentDate.AddMinutes(5),
            Key = Guid.NewGuid().ToString()
        };

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        _eventHistoryRepository.Verify(x => x.GetRequestEventHistory(It.IsAny<string>()), Times.Once);
        _eventHistoryRepository.Verify(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()), Times.Once);
        _requestIndexingRepository.Verify(x => x.UpsertWithAssignmentAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
        _requestIndexingRepository.Verify(x => x.DeleteAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]

    public async Task WhenReconsrequestGetAssignmentFromIOne_RequestIsRoutable()
    {
        // arrange
        var authorizationKey = Guid.NewGuid();
       var requestForServiceKey = Guid.NewGuid();
        var requestForServiceId = $"r{requestForServiceKey.ToString().Substring(0, 5)}";
        var sentDate = DateTime.UtcNow;
        var requestEvents = new List<UCXEvent>
        {
        new RequestForServiceSubmittedEvent
        {
            RequestForServiceKey = requestForServiceKey.ToString(),
            RequestForServiceId = requestForServiceId,
            AuthorizationKey = authorizationKey.ToString(),
            MedicalDiscipline = SpecialtyEnum.Sleep.ToString(),
            IsUrgent = false,
            DateOfService = sentDate.AddDays(6),
            Name = "RequestForServiceSubmitted",
            Version = "1.0.0.0",
            Sent = sentDate.AddMinutes(45),
            Key = Guid.NewGuid().ToString()
        },
        new StatusChangeEvent
        {
            AuthorizationKey = authorizationKey,
            Key = Guid.NewGuid().ToString(),
            Name = "UCXStatusChange",
            OriginSystem = "eP",
            RequestForServiceId = requestForServiceId,
            RequestForServiceKey = requestForServiceKey.ToString(),
            Sent = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(5)),
            Version = "1.3.0",
            Outcome = DecisionOutcomesEnum.Reconsideration.ToString(),
            Status = DecisionOutcomesEnum.Reconsideration.ToString()
        },
        new DueDateCalculatedEvent
            {
                Name = "UCXDueDateCalculated",
                Version = "1.0.0.0",
                DueDateByDateTimeUtc = DateTime.Today.AddDays(5),
                Sent = sentDate.AddMinutes(5),
                Key = Guid.NewGuid().ToString()
            }
        };

        var history = new RequestEventHistoryRequest
        {
            History = requestEvents
        };

        _eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);

        var replayResult = new RequestAggregate();
        replayResult.UpdateMedicalDiscipline(DisciplineEnum.Sleep);
        replayResult.UpdateDueDateCalculated(new DueDateCalculatedObject
        { DueDateUtc = sentDate.AddMinutes(5), RequestForServiceKey = requestForServiceKey });
        replayResult.UpdateStartDate(sentDate);
        replayResult.UpdateStatus(StatusEnum.Reconsideration);
        replayResult.UpdateSpecialty(SpecialtyEnum.Sleep);
        replayResult.UpdateMedicalDiscipline(DisciplineEnum.Sleep);
        replayResult.UpdateRequestUrgencyUpdated(new RequestUrgencyUpdatedObject { Priority = PriorityEnum.Standard });
        DateTime assignmentexpirationutc = DateTime.UtcNow.AddDays(999);
        string AssignedToId = "pawan.vatge@evicore.com";
        replayResult.UpdateAssignment(AssignedToId, assignmentexpirationutc);

        _replayService.Setup(x => x.ReplayEvents(It.IsAny<List<UCXEvent>>())).ReturnsAsync(replayResult);

        var newEvent = new RoutingRequestAssignedEvent
        {
            UnAssigned = false,
            AssignedToId = AssignedToId,
            AssignmentExpirationUtc = assignmentexpirationutc,
            IsPermanentAssignment = true,
            RequestForServiceId = requestForServiceId,
            RequestForServiceKey = requestForServiceKey.ToString(),
            Version = "1.1.0"

        };

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        _eventHistoryRepository.Verify(x => x.GetRequestEventHistory(It.IsAny<string>()), Times.Once);
        _eventHistoryRepository.Verify(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()), Times.Once);
        _requestIndexingRepository.Verify(x => x.UpsertWithAssignmentAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Once);
        _requestIndexingRepository.Verify(x => x.DeleteAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
    }


    [Fact]

    public async Task When_State_AK_LOB_COMMERCIAL_SSL_Request_IsNotRoutable()
    {
        // arrange
        var authorizationKey = Guid.NewGuid();
        var requestForServiceKey = Guid.NewGuid();
        var requestForServiceId = $"r{requestForServiceKey.ToString().Substring(0, 5)}";
        var sentDate = DateTime.UtcNow;
        var requestEvents = new List<UCXEvent>
        {
        new RequestForServiceSubmittedEvent
        {
            RequestForServiceKey = requestForServiceKey.ToString(),
            RequestForServiceId = requestForServiceId,
            AuthorizationKey = authorizationKey.ToString(),
            MedicalDiscipline = SpecialtyEnum.Sleep.ToString(),
            IsUrgent = false,
            DateOfService = sentDate.AddDays(6),
            Name = "RequestForServiceSubmitted",
            Version = "1.0.0.0",
            Sent = sentDate,
            Key = Guid.NewGuid().ToString()
           
        },
        new JurisdictionStateUpdatedEvent{ 
            JurisdictionState ="AK",
            RequestForServiceKey=requestForServiceKey.ToString(),
            RequestForServiceId=requestForServiceId,
            Version ="1.4.0",
            Name ="UcxJurisdictionStateUpdated",
            Key= Guid.NewGuid().ToString(),
            OriginSystem="ep",
            Sent = sentDate.AddMinutes(10)
        },
        new StatusChangeEvent
        {
            AuthorizationKey = authorizationKey,
            Key = Guid.NewGuid().ToString(),
            Name = "UCXStatusChange",
            OriginSystem = "eP",
            RequestForServiceId = requestForServiceId,
            RequestForServiceKey = requestForServiceKey.ToString(),
            Sent = sentDate.AddMinutes(20),
            Version = "1.0.0",
            Outcome = DecisionOutcomesEnum.PendingMDReview.ToString()
        },
        new DueDateCalculatedEvent
            {
                Name = "UCXDueDateCalculated",
                Version = "1.0.0.0",
                DueDateByDateTimeUtc = DateTime.Today.AddDays(5),
                Sent = sentDate.AddMinutes(5),
                Key = Guid.NewGuid().ToString()
            }
        };

        var history = new RequestEventHistoryRequest
        {
            History = requestEvents
        };

        _eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);
       
        _requestSearchRepository.Setup(x => x.SearchByIdAsync(It.IsAny<Guid>())).Returns(CreateRequestSearchResponse());

        var JuridictionState = new StateConfigurationAggregate("AK", new List<LineOfBusinessConfigurationObject>() { new LineOfBusinessConfigurationObject(LineOfBusinessEnum.Commercial, true, new List<RequestRouting.Domain.Entities.InsuranceCarrierEntity>()) });

        var replayResult = new RequestAggregate();
        replayResult.UpdateMedicalDiscipline(DisciplineEnum.Radiology);     
        replayResult.UpdateStartDate(sentDate);
        replayResult.UpdateStatus(StatusEnum.NeedsSslSignoff);
        replayResult.UpdateSpecialty(SpecialtyEnum.VASC);        
        replayResult.UpdateRequestUrgencyUpdated(new RequestUrgencyUpdatedObject { Priority = PriorityEnum.Standard });
        replayResult.UpdateLineOfBusiness(LineOfBusinessEnum.Commercial);
        replayResult.UpdateJurisdictionState(JuridictionState);
        

       _replayService.Setup(x => x.ReplayEvents(It.IsAny<List<UCXEvent>>())).ReturnsAsync(replayResult);

        var newEvent = new StatusChangeEvent
        {
            AuthorizationKey = authorizationKey,
            Key = Guid.NewGuid().ToString(),
            Name = "UCXStatusChange",
            OriginSystem = "eP",
            RequestForServiceId = requestForServiceId,
            RequestForServiceKey = requestForServiceKey.ToString(),
            Sent = sentDate.AddMinutes(20),
            Version = "1.3.0",
            UserType ="MD",
            Outcome = DecisionOutcomesEnum.PendingMDReview.ToString(),
            Status = DecisionOutcomesEnum.PendingMDReview.ToString()
        };

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        _eventHistoryRepository.Verify(x => x.GetRequestEventHistory(It.IsAny<string>()), Times.Once);
        _eventHistoryRepository.Verify(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()), Times.Once);       
        _requestIndexingRepository.Verify(x => x.DeleteAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]

    public async Task When_State_MN_LOB_COMMERCIAL_SSL_PermanentAssignment_True_Request_IsNotRoutable()
    {
        // arrange
        var authorizationKey = Guid.NewGuid();
        var requestForServiceKey = Guid.NewGuid();
        var requestForServiceId = $"r{requestForServiceKey.ToString().Substring(0, 5)}";
        var sentDate = DateTime.UtcNow;
        var requestEvents = new List<UCXEvent>
        {
        new RequestForServiceSubmittedEvent
        {
            RequestForServiceKey = requestForServiceKey.ToString(),
            RequestForServiceId = requestForServiceId,
            AuthorizationKey = authorizationKey.ToString(),
            MedicalDiscipline = SpecialtyEnum.Sleep.ToString(),
            IsUrgent = false,
            DateOfService = sentDate.AddDays(6),
            Name = "RequestForServiceSubmitted",
            Version = "1.0.0.0",
            Sent = sentDate,
            Key = Guid.NewGuid().ToString()

        },
        new JurisdictionStateUpdatedEvent{
            JurisdictionState ="MN",
            RequestForServiceKey=requestForServiceKey.ToString(),
            RequestForServiceId=requestForServiceId,
            Version ="1.4.0",
            Name ="UcxJurisdictionStateUpdated",
            Key= Guid.NewGuid().ToString(),
            OriginSystem="ep",
            Sent = sentDate.AddMinutes(10)
        },
        new StatusChangeEvent
        {
            AuthorizationKey = authorizationKey,
            Key = Guid.NewGuid().ToString(),
            Name = "UCXStatusChange",
            OriginSystem = "eP",
            RequestForServiceId = requestForServiceId,
            RequestForServiceKey = requestForServiceKey.ToString(),
            Sent = sentDate.AddMinutes(20),
            Version = "1.0.0",
           Outcome = DecisionOutcomesEnum.PendingMDReview.ToString()
        },
        new DueDateCalculatedEvent
            {
                Name = "UCXDueDateCalculated",
                Version = "1.0.0.0",
                DueDateByDateTimeUtc = DateTime.Today.AddDays(5),
                Sent = sentDate.AddMinutes(5),
                Key = Guid.NewGuid().ToString()
            }
        };

        var history = new RequestEventHistoryRequest
        {
            History = requestEvents
        };

        _eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);

        _requestSearchRepository.Setup(x => x.SearchByIdAsync(It.IsAny<Guid>())).Returns(CreateRequestSearchResponse());

        var JuridictionState = new StateConfigurationAggregate("MN", new List<LineOfBusinessConfigurationObject>() { new LineOfBusinessConfigurationObject(LineOfBusinessEnum.Commercial, true, new List<RequestRouting.Domain.Entities.InsuranceCarrierEntity>()) });

        var replayResult = new RequestAggregate();
        replayResult.UpdateMedicalDiscipline(DisciplineEnum.Radiology);
        replayResult.UpdateStartDate(sentDate);
        replayResult.UpdateStatus(StatusEnum.NeedsSslSignoff);
        replayResult.UpdateSpecialty(SpecialtyEnum.VASC);
        replayResult.UpdateRequestUrgencyUpdated(new RequestUrgencyUpdatedObject { Priority = PriorityEnum.Standard });
        replayResult.UpdateLineOfBusiness(LineOfBusinessEnum.Commercial);
        replayResult.UpdateJurisdictionState(JuridictionState);
        replayResult.UpdatePermanentAssignment(true);


        _replayService.Setup(x => x.ReplayEvents(It.IsAny<List<UCXEvent>>())).ReturnsAsync(replayResult);

        var newEvent = new StatusChangeEvent
        {
            AuthorizationKey = authorizationKey,
            Key = Guid.NewGuid().ToString(),
            Name = "UCXStatusChange",
            OriginSystem = "eP",
            RequestForServiceId = requestForServiceId,
            RequestForServiceKey = requestForServiceKey.ToString(),
            Sent = sentDate.AddMinutes(20),
            Version = "1.3.0",
            UserType = "MD",
            Outcome = DecisionOutcomesEnum.PendingMDReview.ToString(),
            Status = DecisionOutcomesEnum.PendingMDReview.ToString()
        };

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        _eventHistoryRepository.Verify(x => x.GetRequestEventHistory(It.IsAny<string>()), Times.Once);
        _eventHistoryRepository.Verify(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()), Times.Once);
        _requestIndexingRepository.Verify(x => x.DeleteAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]

    public async Task When_State_MN_LOB_COMMERCIAL_SSL_PermanentAssignment_False_Request_IsNotRoutable()
    {
        // arrange
        var authorizationKey = Guid.NewGuid();
        var requestForServiceKey = Guid.NewGuid();
        var requestForServiceId = $"r{requestForServiceKey.ToString().Substring(0, 5)}";
        var sentDate = DateTime.UtcNow;
        var requestEvents = new List<UCXEvent>
        {
        new RequestForServiceSubmittedEvent
        {
            RequestForServiceKey = requestForServiceKey.ToString(),
            RequestForServiceId = requestForServiceId,
            AuthorizationKey = authorizationKey.ToString(),
            MedicalDiscipline = SpecialtyEnum.Sleep.ToString(),
            IsUrgent = false,
            DateOfService = sentDate.AddDays(6),
            Name = "RequestForServiceSubmitted",
            Version = "1.0.0.0",
            Sent = sentDate,
            Key = Guid.NewGuid().ToString()

        },
        new JurisdictionStateUpdatedEvent{
            JurisdictionState ="MN",
            RequestForServiceKey=requestForServiceKey.ToString(),
            RequestForServiceId=requestForServiceId,
            Version ="1.4.0",
            Name ="UcxJurisdictionStateUpdated",
            Key= Guid.NewGuid().ToString(),
            OriginSystem="ep",
            Sent = sentDate.AddMinutes(10)
        },
        new StatusChangeEvent
        {
            AuthorizationKey = authorizationKey,
            Key = Guid.NewGuid().ToString(),
            Name = "UCXStatusChange",
            OriginSystem = "eP",
            RequestForServiceId = requestForServiceId,
            RequestForServiceKey = requestForServiceKey.ToString(),
            Sent = sentDate.AddMinutes(20),
            Version = "1.0.0",
            Outcome = DecisionOutcomesEnum.PendingMDReview.ToString()
        },
        new DueDateCalculatedEvent
            {
                Name = "UCXDueDateCalculated",
                Version = "1.0.0.0",
                DueDateByDateTimeUtc = DateTime.Today.AddDays(5),
                Sent = sentDate.AddMinutes(5),
                Key = Guid.NewGuid().ToString()
            }
        };

        var history = new RequestEventHistoryRequest
        {
            History = requestEvents
        };

        _eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);

        _requestSearchRepository.Setup(x => x.SearchByIdAsync(It.IsAny<Guid>())).Returns(CreateRequestSearchResponse());

        var JuridictionState = new StateConfigurationAggregate("MN", new List<LineOfBusinessConfigurationObject>() { new LineOfBusinessConfigurationObject(LineOfBusinessEnum.Commercial, true, new List<RequestRouting.Domain.Entities.InsuranceCarrierEntity>()) });

        var replayResult = new RequestAggregate();
        replayResult.UpdateMedicalDiscipline(DisciplineEnum.Radiology);
        replayResult.UpdateStartDate(sentDate);
        replayResult.UpdateStatus(StatusEnum.NeedsSslSignoff);
        replayResult.UpdateSpecialty(SpecialtyEnum.VASC);
        replayResult.UpdateRequestUrgencyUpdated(new RequestUrgencyUpdatedObject { Priority = PriorityEnum.Standard });
        replayResult.UpdateLineOfBusiness(LineOfBusinessEnum.Commercial);
        replayResult.UpdateJurisdictionState(JuridictionState);
        replayResult.UpdatePermanentAssignment(false);


        _replayService.Setup(x => x.ReplayEvents(It.IsAny<List<UCXEvent>>())).ReturnsAsync(replayResult);

        var newEvent = new StatusChangeEvent
       {
            AuthorizationKey = authorizationKey,
            Key = Guid.NewGuid().ToString(),
            Name = "UCXStatusChange",
            OriginSystem = "eP",
            RequestForServiceId = requestForServiceId,
            RequestForServiceKey = requestForServiceKey.ToString(),
            Sent = sentDate.AddMinutes(20),
            Version = "1.3.0",
            UserType = "MD",
            Outcome = DecisionOutcomesEnum.PendingMDReview.ToString(),
            Status = DecisionOutcomesEnum.PendingMDReview.ToString()
        };

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        _eventHistoryRepository.Verify(x => x.GetRequestEventHistory(It.IsAny<string>()), Times.Once);
        _eventHistoryRepository.Verify(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()), Times.Once);
        _requestIndexingRepository.Verify(x => x.DeleteAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Once);
    }
    [Fact]

    public async Task When_State_NotConfigured_As_SpecialState()
    {
        // arrange
        var authorizationKey = Guid.NewGuid();
        var requestForServiceKey = Guid.NewGuid();
        var requestForServiceId = $"r{requestForServiceKey.ToString().Substring(0, 5)}";
        var sentDate = DateTime.UtcNow;
        var requestEvents = new List<UCXEvent>
        {
        new RequestForServiceSubmittedEvent
        {
            RequestForServiceKey = requestForServiceKey.ToString(),
            RequestForServiceId = requestForServiceId,
            AuthorizationKey = authorizationKey.ToString(),
            MedicalDiscipline = SpecialtyEnum.Sleep.ToString(),
            IsUrgent = false,
            DateOfService = sentDate.AddDays(6),
            Name = "RequestForServiceSubmitted",
            Version = "1.0.0.0",
            Sent = sentDate,
            Key = Guid.NewGuid().ToString()

        },
        new JurisdictionStateUpdatedEvent{
            JurisdictionState ="MN",
            RequestForServiceKey=requestForServiceKey.ToString(),
            RequestForServiceId=requestForServiceId,
           Version ="1.4.0",
            Name ="UcxJurisdictionStateUpdated",
            Key= Guid.NewGuid().ToString(),
            OriginSystem="ep",
            Sent = sentDate.AddMinutes(10)
        },
        new StatusChangeEvent
        {
            AuthorizationKey = authorizationKey,
            Key = Guid.NewGuid().ToString(),
            Name = "UCXStatusChange",
            OriginSystem = "eP",
            RequestForServiceId = requestForServiceId,
            RequestForServiceKey = requestForServiceKey.ToString(),
            Sent = sentDate.AddMinutes(20),
            Version = "1.0.0",
            Outcome = DecisionOutcomesEnum.PendingMDReview.ToString()
        },
        new DueDateCalculatedEvent
            {
                Name = "UCXDueDateCalculated",
                Version = "1.0.0.0",
                DueDateByDateTimeUtc = DateTime.Today.AddDays(5),
                Sent = sentDate.AddMinutes(5),
                Key = Guid.NewGuid().ToString()
            }
        };

        var history = new RequestEventHistoryRequest
        {
            History = requestEvents
        };

        _eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);

        _requestSearchRepository.Setup(x => x.SearchByIdAsync(It.IsAny<Guid>())).Returns(CreateRequestSearchResponse());

        var JuridictionState = new StateConfigurationAggregate("Test", new List<LineOfBusinessConfigurationObject>() { new LineOfBusinessConfigurationObject(LineOfBusinessEnum.Commercial, true, new List<RequestRouting.Domain.Entities.InsuranceCarrierEntity>()) });

        var replayResult = new RequestAggregate();
        replayResult.UpdateMedicalDiscipline(DisciplineEnum.Radiology);
        replayResult.UpdateStartDate(sentDate);
        replayResult.UpdateStatus(StatusEnum.NeedsSslSignoff);
        replayResult.UpdateSpecialty(SpecialtyEnum.VASC);
        replayResult.UpdateRequestUrgencyUpdated(new RequestUrgencyUpdatedObject { Priority = PriorityEnum.Standard });
        replayResult.UpdateLineOfBusiness(LineOfBusinessEnum.Commercial);
        replayResult.UpdateJurisdictionState(JuridictionState);


        _replayService.Setup(x => x.ReplayEvents(It.IsAny<List<UCXEvent>>())).ReturnsAsync(replayResult);

        var newEvent = new StatusChangeEvent
        {
            AuthorizationKey = authorizationKey,
            Key = Guid.NewGuid().ToString(),
            Name = "UCXStatusChange",
            OriginSystem = "eP",
            RequestForServiceId = requestForServiceId,
            RequestForServiceKey = requestForServiceKey.ToString(),
            Sent = sentDate.AddMinutes(20),
            Version = "1.3.0",
            UserType = "MD",
            Outcome = DecisionOutcomesEnum.PendingMDReview.ToString(),
            Status = DecisionOutcomesEnum.PendingMDReview.ToString()
        };

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        _eventHistoryRepository.Verify(x => x.GetRequestEventHistory(It.IsAny<string>()), Times.Once);
        _eventHistoryRepository.Verify(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()), Times.Once);
        _requestIndexingRepository.Verify(x => x.DeleteAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
    }


    [Fact]

    public async Task When_Discipline_Not_Radiology()
    {
        // arrange
        var authorizationKey = Guid.NewGuid();
        var requestForServiceKey = Guid.NewGuid();
        var requestForServiceId = $"r{requestForServiceKey.ToString().Substring(0, 5)}";
        var sentDate = DateTime.UtcNow;
        var requestEvents = new List<UCXEvent>
        {
        new RequestForServiceSubmittedEvent
        {
            RequestForServiceKey = requestForServiceKey.ToString(),
            RequestForServiceId = requestForServiceId,
            AuthorizationKey = authorizationKey.ToString(),
            MedicalDiscipline = SpecialtyEnum.Sleep.ToString(),
            IsUrgent = false,
            DateOfService = sentDate.AddDays(6),
            Name = "RequestForServiceSubmitted",
            Version = "1.0.0.0",
            Sent = sentDate,
            Key = Guid.NewGuid().ToString()

        },
        new JurisdictionStateUpdatedEvent{
            JurisdictionState ="MN",
            RequestForServiceKey=requestForServiceKey.ToString(),
            RequestForServiceId=requestForServiceId,
            Version ="1.4.0",
            Name ="UcxJurisdictionStateUpdated",
            Key= Guid.NewGuid().ToString(),
            OriginSystem="ep",
            Sent = sentDate.AddMinutes(10)
        },
        new StatusChangeEvent
        {
            AuthorizationKey = authorizationKey,
            Key = Guid.NewGuid().ToString(),
            Name = "UCXStatusChange",
            OriginSystem = "eP",
            RequestForServiceId = requestForServiceId,
            RequestForServiceKey = requestForServiceKey.ToString(),
            Sent = sentDate.AddMinutes(20),
            Version = "1.0.0",
            Outcome = DecisionOutcomesEnum.PendingMDReview.ToString()
        },
        new DueDateCalculatedEvent
            {
                Name = "UCXDueDateCalculated",
                Version = "1.0.0.0",
                DueDateByDateTimeUtc = DateTime.Today.AddDays(5),
                Sent = sentDate.AddMinutes(5),
                Key = Guid.NewGuid().ToString()
            }
        };

        var history = new RequestEventHistoryRequest
        {
            History = requestEvents
        };

        _eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);

        _requestSearchRepository.Setup(x => x.SearchByIdAsync(It.IsAny<Guid>())).Returns(CreateRequestSearchResponse());

        var JuridictionState = new StateConfigurationAggregate("MN", new List<LineOfBusinessConfigurationObject>() { new LineOfBusinessConfigurationObject(LineOfBusinessEnum.Commercial, true, new List<RequestRouting.Domain.Entities.InsuranceCarrierEntity>()) });

        var replayResult = new RequestAggregate();
        replayResult.UpdateMedicalDiscipline(DisciplineEnum.Cardiology);
        replayResult.UpdateStartDate(sentDate);
        replayResult.UpdateStatus(StatusEnum.NeedsSslSignoff);
        replayResult.UpdateSpecialty(SpecialtyEnum.VASC);
        replayResult.UpdateRequestUrgencyUpdated(new RequestUrgencyUpdatedObject { Priority = PriorityEnum.Standard });
        replayResult.UpdateLineOfBusiness(LineOfBusinessEnum.Commercial);
        replayResult.UpdateJurisdictionState(JuridictionState);


        _replayService.Setup(x => x.ReplayEvents(It.IsAny<List<UCXEvent>>())).ReturnsAsync(replayResult);

        var newEvent = new StatusChangeEvent
        {
            AuthorizationKey = authorizationKey,
            Key = Guid.NewGuid().ToString(),
            Name = "UCXStatusChange",
            OriginSystem = "eP",
            RequestForServiceId = requestForServiceId,
            RequestForServiceKey = requestForServiceKey.ToString(),
            Sent = sentDate.AddMinutes(20),
            Version = "1.3.0",
            UserType = "MD",
            Outcome = DecisionOutcomesEnum.PendingMDReview.ToString(),
            Status = DecisionOutcomesEnum.PendingMDReview.ToString()
        };

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        _eventHistoryRepository.Verify(x => x.GetRequestEventHistory(It.IsAny<string>()), Times.Once);
        _eventHistoryRepository.Verify(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()), Times.Once);
        _requestIndexingRepository.Verify(x => x.DeleteAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
    }
}
using Divergic.Logging.Xunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph.Models;
using Moq;
using Polly;
using Polly.Registry;
using RequestRouting.Application.Commands.Handlers;
using RequestRouting.Application.Configurations;
using RequestRouting.Application.Constants;
using RequestRouting.Application.Interface;
using RequestRouting.Application.Policy;
using RequestRouting.Application.Query.Interfaces;
using RequestRouting.Application.Query.Responses;
using RequestRouting.Application.Replay;
using RequestRouting.Application.Requests;
using RequestRouting.Domain.Aggregates;
using RequestRouting.Domain.Entities;
using RequestRouting.Domain.Enums;
using RequestRouting.Domain.Events;
using RequestRouting.Domain.ValueObjects;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ucx.locking.@base.interfaces;
using ucx.locking.@base.models;
using Xunit;
using Xunit.Abstractions;

namespace RequestRouting.UnitTests.Application.Orchestrator;

public class OrchestratorReplayTests
{
    private readonly Mock<IEventHistoryRepository> _eventHistoryRepository;
    private readonly ICacheLogger _loggerAuthorizationCanceledHandler;
    private readonly ICacheLogger _loggerAuthorizationDismissedHandler;
    private readonly ICacheLogger _loggerDueDateCalculatedHandler;
    private readonly ICacheLogger _loggerDueDateMissedHandler;
    private readonly ICacheLogger _loggerMemberIneligibilitySubmittedHandler;
    private readonly ICacheLogger _loggerMemberInfoUpdatedForRequestHandler;

    private readonly Mock<ILogger<RequestRouting.Application.Orchestrator.Orchestrator>> _loggerOrchestrator;
    private readonly ICacheLogger _loggerPeerToPeerActionSubmittedHandler;
    private readonly ICacheLogger _loggerReplayService;
    private readonly ICacheLogger _loggerRequestForServiceSubmittedHandler;
    private readonly ICacheLogger _loggerRequestUrgencyUpdatedHandler;
    private readonly Mock<IHandlerQueue> _mockHandlerQueue;
    private readonly Mock<IStateConfigurationCacheService> _mockStateConfigurationCacheService;
    private IOptions<AppSettings> _options;
    private readonly PolicyRegistry _policyRegistry;
    private readonly Guid _requestForServiceKey;
    private readonly Mock<IRequestIndexingRepository> _requestIndexingRepository;
    private readonly Mock<IRequestSearchRepository> _requestSearchRepository;
    private RequestRouting.Application.Orchestrator.Orchestrator _orchestrator;
    private readonly Mock<IUserProfileCacheService> _userProfileCacheService;
    private readonly Mock<IHandlerQueue> _handlerQueue;
    private readonly Mock<IConfiguration> _configuration;
    private readonly Mock<IOptions<LockOptions>> _lockOptions;

    private async Task<RequestResponse> CreateRequestResponse()
    {
        var requestResponse = new RequestResponse
        {
            CreateDate = DateTime.UtcNow.AddDays(-1),
        };

        return requestResponse;
    }

    private async Task<RequestSearchResponse> CreateRequestSearchResponse()
    {
        var requestResponse = new RequestResponse
        {
            RequestForServiceKey = _requestForServiceKey.ToString(),
            CreateDate = DateTime.UtcNow.AddDays(-1),
        };

        var response = new RequestSearchResponse();
        response.RequestResponse = requestResponse;

        return response;
    }

    public OrchestratorReplayTests(ITestOutputHelper outputHelper)
    {
        var retryPolicy = Policy
            .Handle<Exception>()
            .OrResult<bool>(x => x == false)
            .WaitAndRetryAsync(2,
                retryCount => TimeSpan.FromSeconds(Math.Pow(2, retryCount)),
                (exception, timeSpan, retryCount, context) =>
                {
                    if (!context.TryGetLogger(out var logger))
                    {
                        return;
                    }

                    context[PolicyConstants.RetryCount] = retryCount;

                    logger.LogInformation("Got exception : {Exception}, retry attempt number {RetryCount}",
                        exception, retryCount);
                });

        _options = Options.Create(new AppSettings { Discipline = "Sleep",ObserverType="Blue" });
        _lockOptions = new Mock<IOptions<LockOptions>>();
        _lockOptions.Setup(x => x.Value).Returns(new LockOptions());

        _loggerDueDateCalculatedHandler = outputHelper.BuildLoggerFor<DueDateCalculatedHandler>();
        _loggerRequestUrgencyUpdatedHandler = outputHelper.BuildLoggerFor<RequestUrgencyUpdatedHandler>();
        _loggerAuthorizationCanceledHandler = outputHelper.BuildLoggerFor<AuthorizationCanceledHandler>();
        _loggerAuthorizationDismissedHandler = outputHelper.BuildLoggerFor<AuthorizationDismissedHandler>();
        _loggerDueDateMissedHandler = outputHelper.BuildLoggerFor<DueDateMissedHandler>();
        _loggerMemberIneligibilitySubmittedHandler = outputHelper.BuildLoggerFor<MemberIneligibilitySubmittedHandler>();
        _loggerMemberInfoUpdatedForRequestHandler = outputHelper.BuildLoggerFor<MemberInfoUpdatedForRequestHandler>();
        _loggerPeerToPeerActionSubmittedHandler = outputHelper.BuildLoggerFor<PeerToPeerActionSubmittedHandler>();
        _loggerRequestForServiceSubmittedHandler = outputHelper.BuildLoggerFor<RequestForServiceSubmittedHandler>();
        _loggerReplayService = outputHelper.BuildLoggerFor<ReplayService>();

        _requestIndexingRepository = new Mock<IRequestIndexingRepository>();
        _requestIndexingRepository.Setup(x => x.GetAsync(It.IsAny<Guid>())).Returns(CreateRequestResponse());

        _requestForServiceKey = Guid.NewGuid();
        _requestSearchRepository = new Mock<IRequestSearchRepository>();
        _requestSearchRepository.Setup(x => x.SearchByIdAsync(It.IsAny<Guid>())).Returns(CreateRequestSearchResponse());

        _loggerOrchestrator = new Mock<ILogger<RequestRouting.Application.Orchestrator.Orchestrator>>();
        _policyRegistry = new PolicyRegistry { { PolicyConstants.RetryPolicy, retryPolicy } };
        _userProfileCacheService = new Mock<IUserProfileCacheService>();

        var _handlers = new Mock<IRequestHandler>();
        _handlerQueue = new Mock<IHandlerQueue>();
        _handlerQueue.Setup(x => x.GetRequestCurrentVersion("UCXRequestForServiceSubmitted", It.IsAny<string>())).Returns(true);
        _handlerQueue.Setup(x => x.GetRequestCurrentVersion("DueDateCalculated", "1.0.0.0")).Returns(true);

        // state cache
        var insuranceCarrierList = new List<InsuranceCarrierEntity> { new("name", Guid.NewGuid()) };
        var lineOfBusinessConfigurations = new List<LineOfBusinessConfigurationObject>
        {
            new(LineOfBusinessEnum.Commercial, true, insuranceCarrierList)
        };
        var stateConfiguration = new StateConfigurationAggregate("Tx", lineOfBusinessConfigurations);

        _mockStateConfigurationCacheService = new Mock<IStateConfigurationCacheService>();
        _mockStateConfigurationCacheService.Setup(x => x.GetStateConfigurationByCode(It.IsAny<string>()))
            .Returns(stateConfiguration);

        _configuration = new Mock<IConfiguration>();
        _configuration.Setup(x => x["Replay"]).Returns("true");

        var requestForServiceSubmittedHandler = new RequestForServiceSubmittedHandler(
            _mockStateConfigurationCacheService.Object,
            (ILogger<RequestForServiceSubmittedHandler>)_loggerRequestForServiceSubmittedHandler);

        var dueDateCalculatedHandler = new DueDateCalculatedHandler((ILogger<DueDateCalculatedHandler>)_loggerDueDateCalculatedHandler);

        _mockHandlerQueue = new Mock<IHandlerQueue>();
        _mockHandlerQueue.Setup(x => x.GetRequestHandler("UCXRequestForServiceSubmitted", "1.2.1.0"))
            .Returns(requestForServiceSubmittedHandler);
        _mockHandlerQueue.Setup(x => x.GetRequestHandler("DueDateCalculated", It.IsAny<string>()))
            .Returns(dueDateCalculatedHandler);

        _requestForServiceKey = Guid.NewGuid();
        var requestEvents = new List<UCXEvent>();
        var existingEvent = CreateRequestAndDueDate(_requestForServiceKey);

        foreach (var item in existingEvent)
        {
            requestEvents.Add(item);
        }

        var history = new RequestEventHistoryRequest
        {
            History = requestEvents,
            RequestForServiceKey = _requestForServiceKey.ToString()
        };

        _eventHistoryRepository = new Mock<IEventHistoryRepository>();
        _eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);
    }

    [Fact]
    public async Task Orchestrates_Replay_Valid()
    {
        // arrange
        var requestForServiceKey = Guid.NewGuid();
        var requestEvents = new List<UCXEvent>();
        var existingEvent = CreateRequestForServiceSubmitted(requestForServiceKey);
        requestEvents.Add(existingEvent);

        var existing1 = CreateDueDateCalculated(requestForServiceKey);
        existing1.Version = "1.0.0.1";
        requestEvents.Add(existing1);

        var history = new RequestEventHistoryRequest
        {
            History = requestEvents,
            RequestForServiceKey = existingEvent.RequestForServiceKey
        };

        var eventHistoryRepository = new Mock<IEventHistoryRepository>();
        eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);

        var requestForServiceSubmittedHandler = new RequestForServiceSubmittedHandler(
            _mockStateConfigurationCacheService.Object,
            (ILogger<RequestForServiceSubmittedHandler>)_loggerRequestForServiceSubmittedHandler);

        var handlerQueue = new Mock<IHandlerQueue>();
        handlerQueue.Setup(x => x.GetRequestHandler("UCXRequestForServiceSubmitted", "1.2.1.0")).Returns(requestForServiceSubmittedHandler);

        var authorizationDismissedHandler = new AuthorizationDismissedHandler((ILogger<AuthorizationDismissedHandler>)_loggerAuthorizationDismissedHandler);

        _mockHandlerQueue.Setup(x => x.GetRequestCurrentVersion("DueDateCalculated", "1.0.0.1")).Returns(false);

        var replayService = new ReplayService(handlerQueue.Object, (ILogger<ReplayService>)_loggerReplayService);

        var newEvent = CreateDueDateCalculated(requestForServiceKey);

        _options = Options.Create(new AppSettings { Discipline = "Sleep", ReplayVersion = false });

        _orchestrator = new RequestRouting.Application.Orchestrator.Orchestrator(
            eventHistoryRepository.Object,
            replayService,
            _loggerOrchestrator.Object,
            _policyRegistry,
            _requestIndexingRepository.Object,
            _options,
            _userProfileCacheService.Object,
            _handlerQueue.Object,
            _requestSearchRepository.Object,
            new Mock<ILockingService>().Object,_lockOptions.Object
        );

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        eventHistoryRepository.Verify(x => x.GetRequestEventHistory(It.IsAny<string>()), Times.AtLeastOnce);
        eventHistoryRepository.Verify(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Orchestrates_VersionReplay_Multiple_DueDates()
    {
        // arrange
        var requestForServiceKey = Guid.NewGuid();
        var requestEvents = new List<UCXEvent>();
        var existingEvent = CreateRequestForServiceSubmitted(requestForServiceKey);
        requestEvents.Add(existingEvent);

        var existing1 = CreateDueDateCalculated(requestForServiceKey);
        existing1.Version = "1.0.0.1";
        requestEvents.Add(existing1);

        var existing2 = CreateDueDateCalculated(requestForServiceKey);
        existing2.Version = "1.0.0.1";
        requestEvents.Add(existing2);

        var history = new RequestEventHistoryRequest
        {
            History = requestEvents,
            RequestForServiceKey = existingEvent.RequestForServiceKey
        };

        var eventHistoryRepository = new Mock<IEventHistoryRepository>();
        eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);

        var requestForServiceSubmittedHandler = new RequestForServiceSubmittedHandler(
            _mockStateConfigurationCacheService.Object,
            (ILogger<RequestForServiceSubmittedHandler>)_loggerRequestForServiceSubmittedHandler);

        var handlerQueue = new Mock<IHandlerQueue>();
        handlerQueue.Setup(x => x.GetRequestHandler("UCXRequestForServiceSubmitted", "1.2.1.0")).Returns(requestForServiceSubmittedHandler);
        handlerQueue.Setup(x => x.GetRequestHandler("DueDateCalculated", It.IsAny<string>())).Returns(requestForServiceSubmittedHandler);

        var authorizationDismissedHandler = new AuthorizationDismissedHandler((ILogger<AuthorizationDismissedHandler>)_loggerAuthorizationDismissedHandler);

        _mockHandlerQueue.Setup(x => x.GetRequestCurrentVersion("DueDateCalculated", "1.0.0.1")).Returns(false);

        var replayService = new ReplayService(handlerQueue.Object, (ILogger<ReplayService>)_loggerReplayService);

        var newEvent = CreateDueDateCalculated(requestForServiceKey);

        _options = Options.Create(new AppSettings { Discipline = "Sleep", ReplayVersion = true });

        _orchestrator = new RequestRouting.Application.Orchestrator.Orchestrator(
            eventHistoryRepository.Object,
            replayService,
            _loggerOrchestrator.Object,
            _policyRegistry,
            _requestIndexingRepository.Object,
            _options,
            _userProfileCacheService.Object,
            _handlerQueue.Object,
            _requestSearchRepository.Object,
            new Mock<ILockingService>().Object, _lockOptions.Object
        );

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        eventHistoryRepository.Verify(x => x.GetRequestEventHistory(It.IsAny<string>()), Times.Once);
        eventHistoryRepository.Verify(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()), Times.Once);
    }

    [Fact]
    public async Task Orchestrates_Initial_RequestForServiceSubmitted()
    {
        // arrange
        var requestEvents = new List<UCXEvent>();

        var history = new RequestEventHistoryRequest
        {
            History = requestEvents
        };
        var eventHistoryRepository = new Mock<IEventHistoryRepository>();
        eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);

        var handler = new RequestForServiceSubmittedHandler(_mockStateConfigurationCacheService.Object,
            (ILogger<RequestForServiceSubmittedHandler>)_loggerRequestForServiceSubmittedHandler);

        var handlerQueue = new Mock<IHandlerQueue>();
        handlerQueue.Setup(x => x.GetRequestHandler(It.IsAny<string>(), It.IsAny<string>())).Returns(handler);

        var replayService = new ReplayService(handlerQueue.Object, (ILogger<ReplayService>)_loggerReplayService);

        var newEvent = CreateRequestForServiceSubmitted(Guid.NewGuid());

        _orchestrator = new RequestRouting.Application.Orchestrator.Orchestrator(
            eventHistoryRepository.Object,
            replayService,
            _loggerOrchestrator.Object,
            _policyRegistry,
            _requestIndexingRepository.Object,
            _options,
            _userProfileCacheService.Object,
            _handlerQueue.Object,
            _requestSearchRepository.Object,
            new Mock<ILockingService>().Object, _lockOptions.Object
        );

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        eventHistoryRepository.Verify(x => x.GetRequestEventHistory(It.IsAny<string>()), Times.Once);
        eventHistoryRepository.Verify(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()), Times.Once);
        _requestIndexingRepository.Verify(x => x.UpsertWithAssignmentAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
        _requestIndexingRepository.Verify(x => x.DeleteAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task Orchestrates_RequestForServiceSubmitted_Appears_First_RegardlessOfDate()
    {
        // arrange
        var requestEvents = new List<UCXEvent>();

        var rfsKey = Guid.NewGuid();
        var dueDateCalculated = CreateDueDateCalculated(rfsKey);
        requestEvents.Add(dueDateCalculated);
        var history = new RequestEventHistoryRequest
        {
            History = requestEvents,
            RequestForServiceKey = dueDateCalculated.RequestForServiceKey
        };
        var eventHistoryRepository = new Mock<IEventHistoryRepository>();
        eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);

        var requestForServiceSubmittedHandler = new RequestForServiceSubmittedHandler(
            _mockStateConfigurationCacheService.Object,
            (ILogger<RequestForServiceSubmittedHandler>)_loggerRequestForServiceSubmittedHandler);

        var dueDateHandler = new RequestForServiceSubmittedHandler(_mockStateConfigurationCacheService.Object,
            (ILogger<RequestForServiceSubmittedHandler>)_loggerRequestForServiceSubmittedHandler);

        var handlerQueue = new Mock<IHandlerQueue>();
        handlerQueue.Setup(x => x.GetRequestHandler("UCXRequestForServiceSubmitted", "1.2.1.0"))
            .Returns(requestForServiceSubmittedHandler);
        handlerQueue.Setup(x => x.GetRequestHandler("DueDateCalculated", "1.0.0.0")).Returns(dueDateHandler);

        var replayService = new ReplayService(handlerQueue.Object, (ILogger<ReplayService>)_loggerReplayService);

        var newEvent = CreateRequestForServiceSubmitted(rfsKey);
        newEvent.Sent = newEvent.Sent.AddDays(1);

        _orchestrator = new RequestRouting.Application.Orchestrator.Orchestrator(
            eventHistoryRepository.Object,
            replayService,
            _loggerOrchestrator.Object,
            _policyRegistry,
            _requestIndexingRepository.Object,
            _options,
            _userProfileCacheService.Object,
            _handlerQueue.Object,
            _requestSearchRepository.Object,
            new Mock<ILockingService>().Object, _lockOptions.Object
        );

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        eventHistoryRepository.Verify(x => x.GetRequestEventHistory(It.IsAny<string>()), Times.Once);
        eventHistoryRepository.Verify(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()), Times.Once);
        _requestIndexingRepository.Verify(x => x.UpsertWithAssignmentAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
        _requestIndexingRepository.Verify(x => x.DeleteAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task Orchestrates_Initial_Minimum_Routing_DueDate()
    {
        // arrange
        var requestForServiceKey = Guid.NewGuid();
        var requestEvents = new List<UCXEvent>();
        var existingEvent = CreateRequestForServiceSubmitted(requestForServiceKey);
        requestEvents.Add(existingEvent);
        var history = new RequestEventHistoryRequest
        {
            History = requestEvents,
            RequestForServiceKey = existingEvent.RequestForServiceKey
        };

        var eventHistoryRepository = new Mock<IEventHistoryRepository>();
        eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);

        var requestForServiceSubmittedHandler = new RequestForServiceSubmittedHandler(
            _mockStateConfigurationCacheService.Object,
            (ILogger<RequestForServiceSubmittedHandler>)_loggerRequestForServiceSubmittedHandler);

        var dueDateCalculatedHandler =
            new DueDateCalculatedHandler((ILogger<DueDateCalculatedHandler>)_loggerDueDateCalculatedHandler);

        var handlerQueue = new Mock<IHandlerQueue>();
        handlerQueue.Setup(x => x.GetRequestHandler("UCXRequestForServiceSubmitted", "1.2.1.0"))
            .Returns(requestForServiceSubmittedHandler);
        handlerQueue.Setup(x => x.GetRequestHandler("DueDateCalculated", "1.0.0.0")).Returns(dueDateCalculatedHandler);

        var replayService = new ReplayService(handlerQueue.Object, (ILogger<ReplayService>)_loggerReplayService);

        var newEvent = CreateDueDateCalculated(requestForServiceKey);

        _orchestrator = new RequestRouting.Application.Orchestrator.Orchestrator(
            eventHistoryRepository.Object,
            replayService,
            _loggerOrchestrator.Object,
            _policyRegistry,
            _requestIndexingRepository.Object,
            _options,
            _userProfileCacheService.Object,
            _handlerQueue.Object,
            _requestSearchRepository.Object,
            new Mock<ILockingService>().Object, _lockOptions.Object
        );

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        eventHistoryRepository.Verify(x => x.GetRequestEventHistory(It.IsAny<string>()), Times.Once);
        eventHistoryRepository.Verify(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()), Times.Once);
        _requestIndexingRepository.Verify(x => x.UpsertWithAssignmentAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Once);
        _requestIndexingRepository.Verify(x => x.DeleteAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task Orchestrates_Plus_AuthorizationDismissed()
    {
        // arrange
        var authorizationDismissedHandler =
            new AuthorizationDismissedHandler(
                (ILogger<AuthorizationDismissedHandler>)_loggerAuthorizationDismissedHandler);

        _mockHandlerQueue.Setup(x => x.GetRequestHandler("UCXAuthorizationDismissed", "1.0.0.0"))
            .Returns(authorizationDismissedHandler);

        var replayService = new ReplayService(_mockHandlerQueue.Object, (ILogger<ReplayService>)_loggerReplayService);

        var newEvent = CreateAuthorizationDismissed(_requestForServiceKey);

        _orchestrator = new RequestRouting.Application.Orchestrator.Orchestrator(
            _eventHistoryRepository.Object,
            replayService,
            _loggerOrchestrator.Object,
            _policyRegistry,
            _requestIndexingRepository.Object,
            _options,
            _userProfileCacheService.Object,
            _handlerQueue.Object,
            _requestSearchRepository.Object,
            new Mock<ILockingService>().Object, _lockOptions.Object
        );

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        _eventHistoryRepository.Verify(x => x.GetRequestEventHistory(It.IsAny<string>()), Times.Once);
        _eventHistoryRepository.Verify(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()), Times.Once);
        _requestIndexingRepository.Verify(x => x.UpsertWithAssignmentAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
        _requestIndexingRepository.Verify(x => x.DeleteAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task Orchestrates_Plus_AuthorizationCanceled()
    {
        // arrange
        var authorizationCanceledHandler =
            new AuthorizationCanceledHandler(
                (ILogger<AuthorizationCanceledHandler>)_loggerAuthorizationCanceledHandler);

        _mockHandlerQueue.Setup(x => x.GetRequestHandler("UCXAuthorizationCanceled", "1.0.0.1"))
            .Returns(authorizationCanceledHandler);

        var replayService = new ReplayService(_mockHandlerQueue.Object, (ILogger<ReplayService>)_loggerReplayService);

        var newEvent = CreateAuthorizationCanceled(_requestForServiceKey);

        _orchestrator = new RequestRouting.Application.Orchestrator.Orchestrator(
            _eventHistoryRepository.Object,
            replayService,
            _loggerOrchestrator.Object,
            _policyRegistry,
            _requestIndexingRepository.Object,
            _options,
            _userProfileCacheService.Object,
            _handlerQueue.Object,
            _requestSearchRepository.Object,
            new Mock<ILockingService>().Object, _lockOptions.Object
        );

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        _eventHistoryRepository.Verify(x => x.GetRequestEventHistory(It.IsAny<string>()), Times.Once);
        _eventHistoryRepository.Verify(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()), Times.Once);
        _requestIndexingRepository.Verify(x => x.UpsertWithAssignmentAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
        _requestIndexingRepository.Verify(x => x.DeleteAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task Orchestrates_Plus_MemberInfoUpdatedForRequest_Different_State()
    {
        // arrange
        var memberInfoUpdatedForRequestHandler = new MemberInfoUpdatedForRequestHandler(
            _mockStateConfigurationCacheService.Object,
            (ILogger<MemberInfoUpdatedForRequestHandler>)_loggerMemberInfoUpdatedForRequestHandler);

        _mockHandlerQueue.Setup(x => x.GetRequestHandler("MemberInfoUpdatedForRequest", "1.0.0.0"))
            .Returns(memberInfoUpdatedForRequestHandler);

        var replayService = new ReplayService(_mockHandlerQueue.Object, (ILogger<ReplayService>)_loggerReplayService);

        var newEvent = CreateMemberInfoUpdatedForRequest(_requestForServiceKey);
        newEvent.JurisdictionState = "Fl";

        _orchestrator = new RequestRouting.Application.Orchestrator.Orchestrator(
            _eventHistoryRepository.Object,
            replayService,
            _loggerOrchestrator.Object,
            _policyRegistry,
            _requestIndexingRepository.Object,
            _options,
            _userProfileCacheService.Object,
            _handlerQueue.Object,
            _requestSearchRepository.Object,
            new Mock<ILockingService>().Object, _lockOptions.Object
        );

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        _eventHistoryRepository.Verify(x => x.GetRequestEventHistory(It.IsAny<string>()), Times.Once);
        _eventHistoryRepository.Verify(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()), Times.Once);
        _requestIndexingRepository.Verify(x => x.UpsertWithAssignmentAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Once);
        _requestIndexingRepository.Verify(x => x.DeleteAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task Orchestrates_Plus_MemberInfoUpdatedForRequest_Same_State()
    {
        // arrange
        var requestEvents = new List<UCXEvent>();

        var requestForServiceSubmitted = CreateRequestForServiceSubmitted(_requestForServiceKey);
        requestForServiceSubmitted.MemberPolicy.StateCode = "Tx";

        requestEvents.Add(requestForServiceSubmitted);
        requestEvents.Add(CreateDueDateCalculated(_requestForServiceKey));
        var history = new RequestEventHistoryRequest
        {
            History = requestEvents,
            RequestForServiceKey = _requestForServiceKey.ToString()
        };
        _eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);

        var requestForServiceSubmittedHandler = new RequestForServiceSubmittedHandler(
            _mockStateConfigurationCacheService.Object,
            (ILogger<RequestForServiceSubmittedHandler>)_loggerRequestForServiceSubmittedHandler);

        var dueDateCalculatedHandler = new DueDateCalculatedHandler((ILogger<DueDateCalculatedHandler>)_loggerDueDateCalculatedHandler);

        var memberInfoUpdatedForRequestHandler = new MemberInfoUpdatedForRequestHandler(
            _mockStateConfigurationCacheService.Object,
            (ILogger<MemberInfoUpdatedForRequestHandler>)_loggerMemberInfoUpdatedForRequestHandler);

        var handlerQueue = new Mock<IHandlerQueue>();
        handlerQueue.Setup(x => x.GetRequestHandler("UCXRequestForServiceSubmitted", "1.2.1.0"))
            .Returns(requestForServiceSubmittedHandler);
        handlerQueue.Setup(x => x.GetRequestHandler("DueDateCalculated", "1.0.0.0")).Returns(dueDateCalculatedHandler);
        handlerQueue.Setup(x => x.GetRequestHandler("MemberInfoUpdatedForRequest", "1.0.0.0"))
            .Returns(memberInfoUpdatedForRequestHandler);

        var replayService = new ReplayService(handlerQueue.Object, (ILogger<ReplayService>)_loggerReplayService);

        var newEvent = CreateMemberInfoUpdatedForRequest(_requestForServiceKey);

        _orchestrator = new RequestRouting.Application.Orchestrator.Orchestrator(
            _eventHistoryRepository.Object,
            replayService,
            _loggerOrchestrator.Object,
            _policyRegistry,
            _requestIndexingRepository.Object,
            _options,
            _userProfileCacheService.Object,
            _handlerQueue.Object,
            _requestSearchRepository.Object,
            new Mock<ILockingService>().Object, _lockOptions.Object
        );

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        _eventHistoryRepository.Verify(x => x.GetRequestEventHistory(It.IsAny<string>()), Times.Once);
        _eventHistoryRepository.Verify(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()), Times.Once);
        _requestIndexingRepository.Verify(x => x.UpsertWithAssignmentAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Once);
        _requestIndexingRepository.Verify(x => x.DeleteAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task Orchestrates_Plus_MemberIneligibilitySubmitted()
    {
        // arrange
        var handler =
            new MemberIneligibilitySubmittedHandler(
                (ILogger<MemberIneligibilitySubmittedHandler>)_loggerMemberIneligibilitySubmittedHandler);

        _mockHandlerQueue.Setup(x => x.GetRequestHandler("UcxMemberIneligibilitySubmitted", "1.0.0.0"))
            .Returns(handler);

        var replayService = new ReplayService(_mockHandlerQueue.Object, (ILogger<ReplayService>)_loggerReplayService);

        var newEvent = CreateMemberIneligibilitySubmitted(_requestForServiceKey);

        _orchestrator = new RequestRouting.Application.Orchestrator.Orchestrator(
            _eventHistoryRepository.Object,
            replayService,
            _loggerOrchestrator.Object,
            _policyRegistry,
            _requestIndexingRepository.Object,
            _options,
            _userProfileCacheService.Object,
            _handlerQueue.Object,
            _requestSearchRepository.Object,
            new Mock<ILockingService>().Object, _lockOptions.Object
        );

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        _eventHistoryRepository.Verify(x => x.GetRequestEventHistory(It.IsAny<string>()), Times.Once);
        _eventHistoryRepository.Verify(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()), Times.Once);
        _requestIndexingRepository.Verify(x => x.UpsertWithAssignmentAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
        _requestIndexingRepository.Verify(x => x.DeleteAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task Orchestrates_Plus_PeerToPeerScheduled()
    {
        // arrange
        var handler =
            new PeerToPeerActionSubmittedHandler(
                (ILogger<PeerToPeerActionSubmittedHandler>)_loggerPeerToPeerActionSubmittedHandler);

        _mockHandlerQueue.Setup(x => x.GetRequestHandler("UcxPeerToPeerActionSubmitted", "1.0.0.0")).Returns(handler);

        var replayService = new ReplayService(_mockHandlerQueue.Object, (ILogger<ReplayService>)_loggerReplayService);

        var newEvent = CreatePeerToPeerActionSubmitted(_requestForServiceKey);

        _orchestrator = new RequestRouting.Application.Orchestrator.Orchestrator(
            _eventHistoryRepository.Object,
            replayService,
            _loggerOrchestrator.Object,
            _policyRegistry,
            _requestIndexingRepository.Object,
            _options,
            _userProfileCacheService.Object,
            _handlerQueue.Object,
            _requestSearchRepository.Object,
            new Mock<ILockingService>().Object, _lockOptions.Object
        );

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        _eventHistoryRepository.Verify(x => x.GetRequestEventHistory(It.IsAny<string>()), Times.Once);
        _eventHistoryRepository.Verify(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()), Times.Once);
        _requestIndexingRepository.Verify(x => x.UpsertWithAssignmentAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
        _requestIndexingRepository.Verify(x => x.DeleteAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task Orchestrates_PeerToPeerWithdrawn()
    {
        // arrange
        var requestEvents = new List<UCXEvent>();
        requestEvents.Add(CreateRequestForServiceSubmitted(_requestForServiceKey));
        requestEvents.Add(CreateDueDateCalculated(_requestForServiceKey));
        requestEvents.Add(CreatePeerToPeerActionSubmitted(_requestForServiceKey));

        var history = new RequestEventHistoryRequest
        {
            History = requestEvents,
            RequestForServiceKey = _requestForServiceKey.ToString()
        };

        _eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);

        var requestForServiceSubmittedHandler = new RequestForServiceSubmittedHandler(
            _mockStateConfigurationCacheService.Object,
            (ILogger<RequestForServiceSubmittedHandler>)_loggerRequestForServiceSubmittedHandler);

        var dueDateCalculatedHandler = new DueDateCalculatedHandler((ILogger<DueDateCalculatedHandler>)_loggerDueDateCalculatedHandler);

        var peerToPeerActionSubmittedHandler = new PeerToPeerActionSubmittedHandler(
                (ILogger<PeerToPeerActionSubmittedHandler>)_loggerPeerToPeerActionSubmittedHandler);

        var handlerQueue = new Mock<IHandlerQueue>();
        handlerQueue.Setup(x => x.GetRequestHandler("UCXRequestForServiceSubmitted", "1.2.1.0"))
            .Returns(requestForServiceSubmittedHandler);
        handlerQueue.Setup(x => x.GetRequestHandler("DueDateCalculated", "1.0.0.0")).Returns(dueDateCalculatedHandler);
        handlerQueue.Setup(x => x.GetRequestHandler("UcxPeerToPeerActionSubmitted", "1.0.0.0"))
            .Returns(peerToPeerActionSubmittedHandler);

        var replayService = new ReplayService(handlerQueue.Object, (ILogger<ReplayService>)_loggerReplayService);

        var newEvent = CreatePeerToPeerActionSubmitted(_requestForServiceKey);
        newEvent.PeerToPeerActionType = PeerToPeerActionTypeEnum.Withdrawn.ToString();

        _orchestrator = new RequestRouting.Application.Orchestrator.Orchestrator(
            _eventHistoryRepository.Object,
            replayService,
            _loggerOrchestrator.Object,
            _policyRegistry,
            _requestIndexingRepository.Object,
            _options,
            _userProfileCacheService.Object,
            _handlerQueue.Object,
            _requestSearchRepository.Object,
            new Mock<ILockingService>().Object, _lockOptions.Object
        );

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        _eventHistoryRepository.Verify(x => x.GetRequestEventHistory(It.IsAny<string>()), Times.Once);
        _eventHistoryRepository.Verify(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()), Times.Once);
        _requestIndexingRepository.Verify(x => x.DeleteAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task Orchestrates_Plus_DueDateMissed()
    {
        // arrange
        var handler = new DueDateMissedHandler((ILogger<DueDateMissedHandler>)_loggerDueDateMissedHandler);

        _mockHandlerQueue.Setup(x => x.GetRequestHandler("UcxDueDateMissed", "1.0.0.0")).Returns(handler);

        var replayService = new ReplayService(_mockHandlerQueue.Object, (ILogger<ReplayService>)_loggerReplayService);

        var newEvent = CreateDueDateMissed(_requestForServiceKey);

        _orchestrator = new RequestRouting.Application.Orchestrator.Orchestrator(
            _eventHistoryRepository.Object,
            replayService,
            _loggerOrchestrator.Object,
            _policyRegistry,
            _requestIndexingRepository.Object,
            _options,
            _userProfileCacheService.Object,
            _handlerQueue.Object,
            _requestSearchRepository.Object,
            new Mock<ILockingService>().Object, _lockOptions.Object
        );

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        _eventHistoryRepository.Verify(x => x.GetRequestEventHistory(It.IsAny<string>()), Times.Once);
        _eventHistoryRepository.Verify(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()), Times.Once);
        _requestIndexingRepository.Verify(x => x.UpsertWithAssignmentAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Once);
        _requestIndexingRepository.Verify(x => x.DeleteAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task Orchestrates_Plus_RequestUrgencyUpdated()
    {
        // arrange
        var handler =
            new RequestUrgencyUpdatedHandler(
                (ILogger<RequestUrgencyUpdatedHandler>)_loggerRequestUrgencyUpdatedHandler);

        _mockHandlerQueue.Setup(x => x.GetRequestHandler("UcxRequestUrgencyUpdated", "1.0.0.0"))
            .Returns(handler);

        var replayService = new ReplayService(_mockHandlerQueue.Object, (ILogger<ReplayService>)_loggerReplayService);

        var newEvent = CreateRequestUrgencyUpdated(_requestForServiceKey);

        _orchestrator = new RequestRouting.Application.Orchestrator.Orchestrator(
            _eventHistoryRepository.Object,
            replayService,
            _loggerOrchestrator.Object,
            _policyRegistry,
            _requestIndexingRepository.Object,
            _options,
            _userProfileCacheService.Object,
            _handlerQueue.Object,
            _requestSearchRepository.Object,
            new Mock<ILockingService>().Object, _lockOptions.Object
        );

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        _eventHistoryRepository.Verify(x => x.GetRequestEventHistory(It.IsAny<string>()), Times.Once);
        _eventHistoryRepository.Verify(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()), Times.Once);
        _requestIndexingRepository.Verify(x => x.UpsertWithAssignmentAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Once);
        _requestIndexingRepository.Verify(x => x.DeleteAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task Orchestrates_VersionDoesntExist()
    {
        // arrange
        var handler =
            new RequestUrgencyUpdatedHandler(
                (ILogger<RequestUrgencyUpdatedHandler>)_loggerRequestUrgencyUpdatedHandler);

        _mockHandlerQueue.Setup(x => x.GetRequestHandler("UcxRequestUrgencyUpdated", "1.0.0.0"))
            .Returns(handler);

        var replayService = new ReplayService(_mockHandlerQueue.Object, (ILogger<ReplayService>)_loggerReplayService);

        var newEvent = CreateRequestUrgencyUpdated(_requestForServiceKey);
        newEvent.Version = "5.0.0.0"; // doesn't exist

        _orchestrator = new RequestRouting.Application.Orchestrator.Orchestrator(
            _eventHistoryRepository.Object,
            replayService,
            _loggerOrchestrator.Object,
            _policyRegistry,
            _requestIndexingRepository.Object,
            _options,
            _userProfileCacheService.Object,
            _handlerQueue.Object,
            _requestSearchRepository.Object,
            new Mock<ILockingService>().Object, _lockOptions.Object
        );

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        _loggerOrchestrator.Verify(logger => logger.Log(
                It.Is<LogLevel>(logLevel => logLevel == LogLevel.Information),
                It.Is<EventId>(eventId => eventId.Id == 0),
                It.Is<It.IsAnyType>((@object, type) => @object.ToString().Contains("does not have version")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.AtLeastOnce());
    }

    [Fact]
    public async Task Orchestrates_RequestBetweenInitialAndDueDate()
    {
        // arrange
        var requestEvents = new List<UCXEvent>();
        var existingEvent = CreateRequestForServiceSubmitted(_requestForServiceKey);
        requestEvents.Add(existingEvent);

        var middleEvent = CreateRequestUrgencyUpdated(_requestForServiceKey);
        requestEvents.Add(middleEvent);

        var history = new RequestEventHistoryRequest
        {
            History = requestEvents,
            RequestForServiceKey = _requestForServiceKey.ToString()
        };
        var eventHistoryRepository = new Mock<IEventHistoryRepository>();
        eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);

        var requestForServiceSubmittedHandler = new RequestForServiceSubmittedHandler(
            _mockStateConfigurationCacheService.Object,
            (ILogger<RequestForServiceSubmittedHandler>)_loggerRequestForServiceSubmittedHandler);

        var requestUrgencyUpdatedHandler = new RequestUrgencyUpdatedHandler(
                (ILogger<RequestUrgencyUpdatedHandler>)_loggerRequestUrgencyUpdatedHandler);

        var dueDateCalculatedHandler = new DueDateCalculatedHandler((ILogger<DueDateCalculatedHandler>)_loggerDueDateCalculatedHandler);

        var handlerQueue = new Mock<IHandlerQueue>();
        handlerQueue.Setup(x => x.GetRequestHandler("UCXRequestForServiceSubmitted", "1.2.1.0"))
            .Returns(requestForServiceSubmittedHandler);
        handlerQueue.Setup(x => x.GetRequestHandler("UcxRequestUrgencyUpdated", "1.0.0.0"))
            .Returns(requestUrgencyUpdatedHandler);
        handlerQueue.Setup(x => x.GetRequestHandler("DueDateCalculated", "1.0.0.0")).Returns(dueDateCalculatedHandler);

        var replayService = new ReplayService(handlerQueue.Object, (ILogger<ReplayService>)_loggerReplayService);
        var newEvent = CreateDueDateCalculated(_requestForServiceKey);

        _orchestrator = new RequestRouting.Application.Orchestrator.Orchestrator(
            eventHistoryRepository.Object,
            replayService,
            _loggerOrchestrator.Object,
            _policyRegistry,
            _requestIndexingRepository.Object,
            _options,
            _userProfileCacheService.Object,
            _handlerQueue.Object,
            _requestSearchRepository.Object,
            new Mock<ILockingService>().Object, _lockOptions.Object
        );

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        eventHistoryRepository.Verify(x => x.GetRequestEventHistory(It.IsAny<string>()), Times.Once);
        eventHistoryRepository.Verify(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()), Times.Once);
        _requestIndexingRepository.Verify(x => x.UpsertWithAssignmentAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Once);
        _requestIndexingRepository.Verify(x => x.DeleteAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task Orchestrates_RequestPriorToFilterDate_NotRoutable()
    {
        // arrange
        var requestEvents = new List<UCXEvent>();
        var existingEvent = CreateRequestForServiceSubmitted(_requestForServiceKey);
        existingEvent.StartDate = new DateTime(2022, 07, 20);
        requestEvents.Add(existingEvent);

        var middleEvent = CreateRequestUrgencyUpdated(_requestForServiceKey);
        requestEvents.Add(middleEvent);
        var history = new RequestEventHistoryRequest
        {
            History = requestEvents,
            RequestForServiceKey = _requestForServiceKey.ToString()
        };
        var eventHistoryRepository = new Mock<IEventHistoryRepository>();
        eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);

        var requestForServiceSubmittedHandler = new RequestForServiceSubmittedHandler(
            _mockStateConfigurationCacheService.Object,
            (ILogger<RequestForServiceSubmittedHandler>)_loggerRequestForServiceSubmittedHandler);

        var requestUrgencyUpdatedHandler =
            new RequestUrgencyUpdatedHandler(
                (ILogger<RequestUrgencyUpdatedHandler>)_loggerRequestUrgencyUpdatedHandler);

        var dueDateCalculatedHandler =
            new DueDateCalculatedHandler((ILogger<DueDateCalculatedHandler>)_loggerDueDateCalculatedHandler);

        var handlerQueue = new Mock<IHandlerQueue>();
        handlerQueue.Setup(x => x.GetRequestHandler("UCXRequestForServiceSubmitted", "1.2.1.0"))
            .Returns(requestForServiceSubmittedHandler);
        handlerQueue.Setup(x => x.GetRequestHandler("UcxRequestUrgencyUpdated", "1.0.0.0"))
            .Returns(requestUrgencyUpdatedHandler);
        handlerQueue.Setup(x => x.GetRequestHandler("DueDateCalculated", "1.0.0.0")).Returns(dueDateCalculatedHandler);

        var replayService = new ReplayService(handlerQueue.Object, (ILogger<ReplayService>)_loggerReplayService);
        var newEvent = CreateDueDateCalculated(_requestForServiceKey);

        _orchestrator = new RequestRouting.Application.Orchestrator.Orchestrator(
            eventHistoryRepository.Object,
            replayService,
            _loggerOrchestrator.Object,
            _policyRegistry,
            _requestIndexingRepository.Object,
            _options,
            _userProfileCacheService.Object,
            _handlerQueue.Object,
            _requestSearchRepository.Object,
            new Mock<ILockingService>().Object, _lockOptions.Object
        );

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        eventHistoryRepository.Verify(x => x.GetRequestEventHistory(It.IsAny<string>()), Times.Once);
        eventHistoryRepository.Verify(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()), Times.Once);
        _requestIndexingRepository.Verify(x => x.UpsertWithAssignmentAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
        _requestIndexingRepository.Verify(x => x.DeleteAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task Orchestrates_AllMessages()
    {
        // arrange
        var requestEvents = new List<UCXEvent>();
        requestEvents.Add(CreateRequestForServiceSubmitted(_requestForServiceKey));
        requestEvents.Add(CreateDueDateCalculated(_requestForServiceKey));
        requestEvents.Add(CreateAuthorizationCanceled(_requestForServiceKey));
        requestEvents.Add(CreateAuthorizationDismissed(_requestForServiceKey));
        requestEvents.Add(CreateDueDateMissed(_requestForServiceKey));
        requestEvents.Add(CreatePeerToPeerActionSubmitted(_requestForServiceKey));
        requestEvents.Add(CreateRequestUrgencyUpdated(_requestForServiceKey));

        var history = new RequestEventHistoryRequest
        {
            History = requestEvents,
            RequestForServiceKey = _requestForServiceKey.ToString()
        };

        var eventHistoryRepository = new Mock<IEventHistoryRepository>();
        eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<string>())).ReturnsAsync(history);

        var requestForServiceSubmittedHandler = new RequestForServiceSubmittedHandler(
            _mockStateConfigurationCacheService.Object,
            (ILogger<RequestForServiceSubmittedHandler>)_loggerRequestForServiceSubmittedHandler);

        var dueDateCalculatedHandler = new DueDateCalculatedHandler((ILogger<DueDateCalculatedHandler>)_loggerDueDateCalculatedHandler);

        var requestUrgencyUpdatedHandler = new RequestUrgencyUpdatedHandler(
            (ILogger<RequestUrgencyUpdatedHandler>)_loggerRequestUrgencyUpdatedHandler);

        var authorizationCanceledHandler = new AuthorizationCanceledHandler(
            (ILogger<AuthorizationCanceledHandler>)_loggerAuthorizationCanceledHandler);

        var authorizationDismissedHandler = new AuthorizationDismissedHandler(
            (ILogger<AuthorizationDismissedHandler>)_loggerAuthorizationDismissedHandler);

        var dueDateMissedHandler = new DueDateMissedHandler((ILogger<DueDateMissedHandler>)_loggerDueDateMissedHandler);

        var memberIneligibilitySubmittedHandler = new MemberIneligibilitySubmittedHandler(
            (ILogger<MemberIneligibilitySubmittedHandler>)_loggerMemberIneligibilitySubmittedHandler);

        var memberInfoUpdatedForRequestHandler = new MemberInfoUpdatedForRequestHandler(
            _mockStateConfigurationCacheService.Object,
            (ILogger<MemberInfoUpdatedForRequestHandler>)_loggerMemberInfoUpdatedForRequestHandler);

        var peerToPeerActionSubmittedHandler = new PeerToPeerActionSubmittedHandler(
            (ILogger<PeerToPeerActionSubmittedHandler>)_loggerPeerToPeerActionSubmittedHandler);

        var mockHandlerQueue = new Mock<IHandlerQueue>();
        mockHandlerQueue.Setup(x => x.GetRequestHandler("UCXRequestForServiceSubmitted", "1.2.1.0"))
            .Returns(requestForServiceSubmittedHandler);
        mockHandlerQueue.Setup(x => x.GetRequestHandler("DueDateCalculated", "1.0.0.0"))
            .Returns(dueDateCalculatedHandler);

        mockHandlerQueue.Setup(x => x.GetRequestHandler("UCXAuthorizationCanceled", "1.0.0.1"))
            .Returns(authorizationCanceledHandler);
        mockHandlerQueue.Setup(x => x.GetRequestHandler("UCXAuthorizationDismissed", "1.0.0.0"))
            .Returns(authorizationDismissedHandler);
        mockHandlerQueue.Setup(x => x.GetRequestHandler("UcxDueDateMissed", "1.0.0.0")).Returns(dueDateMissedHandler);
        mockHandlerQueue.Setup(x => x.GetRequestHandler("UcxMemberIneligibilitySubmitted", "1.0.0.0"))
            .Returns(memberIneligibilitySubmittedHandler);
        mockHandlerQueue.Setup(x => x.GetRequestHandler("MemberInfoUpdatedForRequest", "1.0.0.0"))
            .Returns(memberInfoUpdatedForRequestHandler);
        mockHandlerQueue.Setup(x => x.GetRequestHandler("UcxPeerToPeerActionSubmitted", "1.0.0.0"))
            .Returns(peerToPeerActionSubmittedHandler);
        mockHandlerQueue.Setup(x => x.GetRequestHandler("UcxRequestUrgencyUpdated", "1.0.0.0"))
            .Returns(requestUrgencyUpdatedHandler);

        var replayService = new ReplayService(mockHandlerQueue.Object, (ILogger<ReplayService>)_loggerReplayService);
        var newEvent = CreateMemberIneligibilitySubmitted(_requestForServiceKey);

        _orchestrator = new RequestRouting.Application.Orchestrator.Orchestrator(
            eventHistoryRepository.Object,
            replayService,
            _loggerOrchestrator.Object,
            _policyRegistry,
            _requestIndexingRepository.Object,
            _options,
            _userProfileCacheService.Object,
            _handlerQueue.Object,
            _requestSearchRepository.Object,
            new Mock<ILockingService>().Object, _lockOptions.Object
        );

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        eventHistoryRepository.Verify(x => x.GetRequestEventHistory(It.IsAny<string>()), Times.Once);
        eventHistoryRepository.Verify(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()), Times.Once);
        _requestIndexingRepository.Verify(x => x.UpsertWithAssignmentAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
        _requestIndexingRepository.Verify(x => x.DeleteAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Once);
    }
    
    [Fact]
    public async Task LockTheRequest_WhenOrchestrating_aEvent()
    {
        // arrange
        var authorizationDismissedHandler =
            new AuthorizationDismissedHandler(
                (ILogger<AuthorizationDismissedHandler>)_loggerAuthorizationDismissedHandler);

        _mockHandlerQueue.Setup(x => x.GetRequestHandler("UCXAuthorizationDismissed", "1.0.0.0"))
            .Returns(authorizationDismissedHandler);

        var replayService = new ReplayService(_mockHandlerQueue.Object, (ILogger<ReplayService>)_loggerReplayService);

        var newEvent = CreateAuthorizationDismissed(_requestForServiceKey);

        var lockingService = new Mock<ILockingService>();

        _orchestrator = new RequestRouting.Application.Orchestrator.Orchestrator(
            _eventHistoryRepository.Object,
            replayService,
            _loggerOrchestrator.Object,
            _policyRegistry,
            _requestIndexingRepository.Object,
            _options,
            _userProfileCacheService.Object,
            _handlerQueue.Object,
            _requestSearchRepository.Object,
            lockingService.Object, _lockOptions.Object
        );

        // act
        await _orchestrator.OrchestrateAsync(newEvent, CancellationToken.None);

        // assert
        lockingService.Verify(x => x.LockAsync<string>(It.Is<string>(y => y == "Blue_" + _requestForServiceKey.ToString()), It.IsAny<TimeSpan>(),
            It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
        _eventHistoryRepository.Verify(x => x.GetRequestEventHistory(It.IsAny<string>()), Times.Once);
        _eventHistoryRepository.Verify(x => x.SaveRequestEventHistory(It.IsAny<RequestEventHistoryRequest>()), Times.Once);
        _requestIndexingRepository.Verify(x => x.UpsertWithAssignmentAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
        _requestIndexingRepository.Verify(x => x.DeleteAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Once);
       
    }
    #region private methods

    private AuthorizationDismissedEvent CreateAuthorizationDismissed(Guid requestForServiceKey)
    {
        var newEvent = new AuthorizationDismissedEvent
        {
            RequestForServiceKey = requestForServiceKey.ToString(),
            Name = "UCXAuthorizationDismissed",
            Version = "1.0.0.0",
            Key = Guid.NewGuid().ToString(),
            Sent = DateTime.Now.AddDays(10),
            OriginSystem = "eP",
            RequestForServiceId = "RFS123",
            AuthorizationKey = Guid.NewGuid().ToString(),
            RequestGeneratingAgent = "Agent",
            DismissDate = DateTime.Now,
            Notes = "notes",
            Reason = "reason"
        };

        return newEvent;
    }

    private AuthorizationCanceledEvent CreateAuthorizationCanceled(Guid requestForServiceKey)
    {
        var newEvent = new AuthorizationCanceledEvent
        {
            RequestForServiceKey = requestForServiceKey.ToString(),
            Name = "UCXAuthorizationCanceled",
            Version = "1.0.0.1",
            Key = Guid.NewGuid().ToString(),
            Sent = DateTime.Now.AddDays(10),
            OriginSystem = "eP",
            RequestForServiceId = "RFS123",
            AuthorizationKey = Guid.NewGuid(),
            CancelDate = DateTime.Now.AddDays(11),
            Notes = "notes",
            Reason = "reason"
        };

        return newEvent;
    }

    private UCXEvent CreateDueDateCalculated(Guid requestForServiceKey)
    {
        var dueDateCalculated = new DueDateCalculatedEvent
        {
            RequestForServiceId = "RFS123",
            Name = "DueDateCalculated",
            Version = "1.0.0.0",
            Key = Guid.NewGuid().ToString(),
            Sent = DateTime.Now,
            DueBy = DateTime.Now,
            DueDateByDateTimeUtc = DateTime.Now,
            OriginSystem = "eP",
            RecordedDate = DateTime.Now,
            RequestForServiceKey = requestForServiceKey.ToString()
        };

        return dueDateCalculated;
    }

    private DueDateMissedEvent CreateDueDateMissed(Guid requestForServiceKey)
    {
        var dueDateMissed = new DueDateMissedEvent
        {
            RequestForServiceId = "RFS123",
            Name = "UcxDueDateMissed",
            Version = "1.0.0.0",
            Key = Guid.NewGuid().ToString(),
            Sent = DateTime.Now,
            OriginSystem = "eP",
            RequestForServiceKey = requestForServiceKey.ToString(),
            DueDateByUtc = DateTime.Now
        };

        return dueDateMissed;
    }

    private MemberInfoUpdatedForRequestEvent CreateMemberInfoUpdatedForRequest(Guid requestForServiceKey)
    {
        var newEvent = new MemberInfoUpdatedForRequestEvent
        {
            RequestForServiceKey = requestForServiceKey.ToString(),
            Name = "MemberInfoUpdatedForRequest",
            Version = "1.0.0.0",
            Key = Guid.NewGuid().ToString(),
            Sent = DateTime.Now.AddDays(10),
            OriginSystem = "eP",
            RequestForServiceId = "RFS123",
            JurisdictionState = "Tx"
        };

        return newEvent;
    }

    private MemberIneligibilitySubmittedEvent CreateMemberIneligibilitySubmitted(Guid requestForServiceKey)
    {
        var newEvent = new MemberIneligibilitySubmittedEvent
        {
            RequestForServiceKey = requestForServiceKey.ToString(),
            Name = "UcxMemberIneligibilitySubmitted",
            Version = "1.0.0.0",
            Key = Guid.NewGuid().ToString(),
            Sent = DateTime.Now.AddDays(10),
            OriginSystem = "eP",
            RequestForServiceId = "RFS123",
            Notes = "notes",
            Reason = "reason",
            FirstName = "first",
            LastName = "last",
            MemberPolicyKey = Guid.NewGuid(),
            ProviderOutreachMade = true
        };

        return newEvent;
    }

    private PeerToPeerActionSubmittedEvent CreatePeerToPeerActionSubmitted(Guid requestForServiceKey)
    {
        var newEvent = new PeerToPeerActionSubmittedEvent
        {
            AuthorizationKey = Guid.NewGuid().ToString(),
            PeerToPeerActionType = PeerToPeerActionTypeEnum.Scheduled.ToString(),
            DoctorInformation = new DoctorInformationEvent(),
            Key = Guid.NewGuid().ToString(),
            MedicalDiscipline = DisciplineEnum.Sleep.ToString(),
            Name = "UcxPeerToPeerActionSubmitted",
            OriginSystem = "eP",
            RequestForServiceId = "rew12345",
            RequestForServiceKey = requestForServiceKey.ToString(),
            ReviewerType = "ReviewerType",
            Sent = DateTime.Now,
            Version = "1.0.0.0"
        };

        return newEvent;
    }

    private RequestForServiceSubmittedEvent CreateRequestForServiceSubmitted(Guid requestForServiceKey)
    {
        var memberPolicy = new MemberPolicyEvent
        {
            InsuranceCarrierKey = Guid.NewGuid().ToString(),
            LineofBusiness = "Commercial",
            StateCode = "Fl"
        };

        var requestForServiceSubmitted = new RequestForServiceSubmittedEvent
        {
            RequestForServiceId = "RFS123",
            Name = "UCXRequestForServiceSubmitted",
            AuthorizationKey = "KEY",
            Version = "1.2.1.0",
            Sent = DateTime.Now,
            Key = Guid.NewGuid().ToString(),
            MemberPolicy = memberPolicy,
            RequestForServiceKey = requestForServiceKey.ToString(),
            Status = "InMdReview",
            OriginStatus = "InMdReview",
            MedicalDiscipline = "Sleep",
            RequestSpecialty = "Sleep",
            StartDate = new DateTime(2023, 04, 01)
        };

        return requestForServiceSubmitted;
    }

    private List<UCXEvent> CreateRequestAndDueDate(Guid requestForServiceKey)
    {
        var events = new List<UCXEvent>();

        var requestForServiceSubmitted = CreateRequestForServiceSubmitted(requestForServiceKey);

        var dueDateCalculated = CreateDueDateCalculated(requestForServiceKey);

        events.Add(requestForServiceSubmitted);
        events.Add(dueDateCalculated);

        return events;
    }

    private RequestUrgencyUpdatedEvent CreateRequestUrgencyUpdated(Guid requestForServiceKey)
    {
        var urgentAttestation = new UrgentAttestationEvent
        {
            GeneratingAgent = "agent",
            GeneratingAgentContext = "context",
            IsUrgent = true,
            Reason = "reason",
            UrgentRequestDate = DateTime.Now
        };

        var requestUrgencyUpdated = new RequestUrgencyUpdatedEvent
        {
            RequestForServiceId = "RFS123",
            Name = "UcxRequestUrgencyUpdated",
            Version = "1.0.0.0",
            Sent = DateTime.Now,
            Key = Guid.NewGuid().ToString(),
            MedicalDiscipline = "Sleep",
            UrgentAttestation = urgentAttestation,
            OriginSystem = "eP",
            RequestForServiceKey = requestForServiceKey.ToString()
        };

        return requestUrgencyUpdated;
    }

    #endregion
}

using AutoFixture;
using Confluent.Kafka;
using Divergic.Logging.Xunit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using RequestRouting.Application.Events;
using RequestRouting.Application.Interface;
using RequestRouting.Application.Query.Handlers;
using RequestRouting.Application.Query.Interfaces;
using RequestRouting.Application.Query.Requests;
using RequestRouting.Application.Query.Responses;
using RequestRouting.Application.Requests;
using RequestRouting.Domain.Aggregates;
using RequestRouting.Domain.Entities;
using RequestRouting.Domain.Enums;
using RequestRouting.Domain.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace RequestRouting.UnitTests.Application.Query.Handlers;

public class GetNextRequestHandlerTests
{
    private static readonly string TestUserName = "test@testingtest.com";
    private readonly ICacheLogger _cacheLogger;
    private readonly Mock<IConfiguration> _configuration;
    private readonly Fixture _fixture;
    private readonly GetNextRequestHandler _handler;
    private readonly Mock<IProducer<string, string>> _producer;
    private readonly Mock<IRequestIndexingRepository> _requestIndexingRepository;
    private readonly Mock<IRequestSearchRepository> _searchRepository;
    private readonly UserProfileConfigurationAggregate _userConfiguration;
    private readonly Mock<IUserProfileConfigurationRepository> _userConfigurationRepository;
    private readonly Mock<IEventHistoryRepository> _eventHistoryRepository;

    public GetNextRequestHandlerTests(ITestOutputHelper outputHelper)
    {
        _fixture = new Fixture();
        _configuration = new Mock<IConfiguration>();
        _configuration.Setup(x => x["Discipline"]).Returns(DisciplineEnum.Sleep.ToString());
        _configuration.Setup(x => x["AssignmentExpirationMinutes"]).Returns("20");
        _configuration.Setup(x => x["DefaultRoutableOriginSystems"]).Returns("eP");
        _configuration.Setup(x => x["Environment"]).Returns("dev");
        _configuration.Setup(x => x["MaxAssignmentCount"]).Returns("10");
        _configuration.Setup(x => x["AutomationPrefix"]).Returns("in1_UCXSlpMDPRF");
        _configuration.Setup(x => x["NonDisciplineSslRouting"]).Returns("Sleep");
        _cacheLogger = outputHelper.BuildLoggerFor<GetNextRequestHandler>();
        _requestIndexingRepository = new Mock<IRequestIndexingRepository>();
        _searchRepository = new Mock<IRequestSearchRepository>();
        _userConfigurationRepository = new Mock<IUserProfileConfigurationRepository>();
        _eventHistoryRepository = new Mock<IEventHistoryRepository>();
        _producer = new Mock<IProducer<string, string>>();
        _handler = new GetNextRequestHandler(_searchRepository.Object, _userConfigurationRepository.Object,
            (ILogger<GetNextRequestHandler>)_cacheLogger, _requestIndexingRepository.Object,
            _configuration.Object, _producer.Object, _eventHistoryRepository.Object);

        _userConfiguration = UserProfileConfigurationAggregate.Create
        (
            Guid.NewGuid().ToString(),
            TestUserName, "Sleep MD Licensed In CT state", "first", "last", UserTypeEnum.MD,
            new List<string> { DisciplineEnum.Sleep.ToString() },
            new List<string> { TestUserName },
            new List<string> { "TN", "KY" },
            new Dictionary<SpecialtyEnum, string> { { SpecialtyEnum.Sleep, "TestScoringProfile" } },
            new Dictionary<DisciplineEnum, List<SpecialtyEntity>>()
         );

        var uCXRoutingRequestAssigned = new RoutingRequestAssignedEvent
        {
            AssignedToId = TestUserName,
            RequestForServiceKey = Guid.NewGuid().ToString(),
            Name = "UCXRoutingRequestAssigned"
        };

        var requestEventHistory = new RequestEventHistoryRequest
        {
            History = new List<UCXEvent> { uCXRoutingRequestAssigned }
        };
        _eventHistoryRepository.Setup(x => x.GetRequestEventHistory(It.IsAny<String>())).ReturnsAsync(requestEventHistory);
    }

    [Fact]
    public async Task HandleAsync_LogsUserEmailAndClass_WhenInvoked()
    {
        _userConfigurationRepository.Setup(x => x.GetAsync(It.IsAny<string>())).ReturnsAsync(_userConfiguration);
        var requestSearchResponse = GetTestRequestSearchResponse();
        _searchRepository.Setup(x => x.SearchAsync(It.IsAny<RequestSearchRequest>()))
            .ReturnsAsync(new List<RequestSearchResponse> { requestSearchResponse });

        await _handler.HandleAsync(TestUserName);

        var logEntry = _cacheLogger.Entries.FirstOrDefault(v =>
            v.Message.Contains("HandleAsync started"));
        var scopeData = (Dictionary<string, object>)logEntry.Scopes.FirstOrDefault();
        scopeData["LoggedInUserEmail"].Should().Be(TestUserName);
        scopeData["Class"].Should().Be("GetNextRequestHandler:HandleAsync");
    }

    [Fact]
    public async Task HandleAsync_NocConfigMessage_WhenUserConfigurationIsNotFound()
    {
        _userConfigurationRepository.Setup(x => x.GetAsync(It.IsAny<string>())).ReturnsAsync((UserProfileConfigurationAggregate)null);
        var requestSearchResponse = GetTestRequestSearchResponse();
        _searchRepository.Setup(x => x.SearchAsync(It.IsAny<RequestSearchRequest>()))
            .ReturnsAsync(new List<RequestSearchResponse> { requestSearchResponse });

        var response = await _handler.HandleAsync(TestUserName);
        response.Success.Should().BeFalse();
        response.ErrorMessage.Should().Be($"No configuration found for this user {TestUserName}");
        response.GetNextNoRequestsReason.Should().Be(GetNextNoRequestsReasonEnum.NoConfigAvailable);
    }

    [Fact]
    public async Task HandleAsync_ThrowsNoRequestsAvailableException_WhenSearchResultIsNull()
    {
        _userConfigurationRepository.Setup(x => x.GetAsync(It.IsAny<string>())).ReturnsAsync(_userConfiguration);
        _searchRepository.Setup(x => x.SearchAsync(It.IsAny<RequestSearchRequest>()))
            .ReturnsAsync((List<RequestSearchResponse>)null);

        var response = await _handler.HandleAsync(TestUserName);
        response.Success.Should().BeFalse();
        response.ErrorMessage.Should().Be("No requests available for user");
        response.GetNextNoRequestsReason.Should().Be(GetNextNoRequestsReasonEnum.NoneAvailable);
    }

    [Fact]
    public async Task HandleAsync_NoRequestsAvailable_WhenSearchResultIsEmpty()
    {
        _userConfigurationRepository.Setup(x => x.GetAsync(It.IsAny<string>())).ReturnsAsync(_userConfiguration);
        _searchRepository.Setup(x => x.SearchAsync(It.IsAny<RequestSearchRequest>()))
            .ReturnsAsync(new List<RequestSearchResponse>());

        var response = await _handler.HandleAsync(TestUserName);
        response.Success.Should().BeFalse();
        response.ErrorMessage.Should().Be("No requests available for user");
        response.GetNextNoRequestsReason.Should().Be(GetNextNoRequestsReasonEnum.NoneAvailable);
    }

    [Fact]
    public async Task HandleAsync_NoRequestFoundForUser_WhenAllSearchResultsIsFilteredOut()
    {
        _userConfigurationRepository.Setup(x => x.GetAsync(It.IsAny<string>())).ReturnsAsync(_userConfiguration);
        var requestSearchResponse = GetTestRequestSearchResponse();
        requestSearchResponse.RequestResponse.AssignedToId = "abcd";
        requestSearchResponse.RequestResponse.AssignmentExpirationUtc = DateTime.Now.AddDays(5);
        _searchRepository.Setup(x => x.SearchAsync(It.IsAny<RequestSearchRequest>()))
            .ReturnsAsync(new List<RequestSearchResponse> { requestSearchResponse });

        var response = await _handler.HandleAsync(TestUserName);
        response.Success.Should().BeFalse();
        response.ErrorMessage.Should().Be("No suitable requests found for user");
        response.GetNextNoRequestsReason.Should().Be(GetNextNoRequestsReasonEnum.NoneSuitable);
    }

    [Fact]
    public async Task HandleAsync_FiltersOutRequests_WhenRequestIsAssignedToDifferentUser()
    {
        _userConfigurationRepository.Setup(x => x.GetAsync(It.IsAny<string>())).ReturnsAsync(_userConfiguration);
        var requestSearchResponse1 = GetTestRequestSearchResponse();
        requestSearchResponse1.RequestResponse.AssignedToId = "otheruser";
        requestSearchResponse1.RequestResponse.AssignmentExpirationUtc = DateTime.Now.AddDays(2);

        var requestSearchResponse2 = GetTestRequestSearchResponse();

        _searchRepository.Setup(x => x.SearchAsync(It.IsAny<RequestSearchRequest>()))
            .ReturnsAsync(new List<RequestSearchResponse> { requestSearchResponse1, requestSearchResponse2 });

        var response = await _handler.HandleAsync(TestUserName);

        response.RequestForServiceKey.Should().Be(requestSearchResponse2.RequestResponse.RequestForServiceKey);
        response.RequestForServiceKey.Should().NotBe(requestSearchResponse1.RequestResponse.RequestForServiceKey);
       
    }

    [Fact]
    public async Task HandleAsync_DoesNotFiltersOutRequests_WhenRequestAssignmentIsExpired()
    {
        _userConfigurationRepository.Setup(x => x.GetAsync(It.IsAny<string>())).ReturnsAsync(_userConfiguration);
        var requestSearchResponse1 = GetTestRequestSearchResponse();
        requestSearchResponse1.RequestResponse.AssignedToId = "otheruser";
        requestSearchResponse1.RequestResponse.AssignmentExpirationUtc = DateTime.Now.AddDays(-2);

        
        _searchRepository.Setup(x => x.SearchAsync(It.IsAny<RequestSearchRequest>()))
            .ReturnsAsync(new List<RequestSearchResponse> { requestSearchResponse1 });

        var response = await _handler.HandleAsync(TestUserName);
        response.RequestForServiceKey.Should().Be(requestSearchResponse1.RequestResponse.RequestForServiceKey);
       
    }

    [Fact]
    public async Task HandleAsync_FiltersOutRequests_WhenRequestIsInNeedsSslSignoffAndUserDoesNotHaveStateLicenses()
    {
        _userConfigurationRepository.Setup(x => x.GetAsync(It.IsAny<string>())).ReturnsAsync(_userConfiguration);
        
        var requestSearchResponse1 = GetTestRequestSearchResponse();
        requestSearchResponse1.RequestResponse.JurisdictionState = "AZ";
        requestSearchResponse1.RequestResponse.Status = StatusEnum.NeedsSslSignoff;

        var requestSearchResponse2 = GetTestRequestSearchResponse();
        requestSearchResponse2.RequestResponse.JurisdictionState = "TX";
        requestSearchResponse2.RequestResponse.Status = StatusEnum.NeedsSslSignoff;

        _searchRepository.Setup(x => x.SearchAsync(It.IsAny<RequestSearchRequest>()))
            .ReturnsAsync(new List<RequestSearchResponse> { requestSearchResponse1, requestSearchResponse2 });

        var response = await _handler.HandleAsync(TestUserName);

        response.RequestForServiceKey.Should().Be(null);        
        var logEntry = _cacheLogger.Entries.FirstOrDefault(v =>
            v.Message.Contains(
                $"UserCannotWorkRequestDueToSsl = True, UserName = {TestUserName}, RequestForServiceKey = {requestSearchResponse1.RequestResponse.RequestForServiceKey}, JurisdictionState = {requestSearchResponse1.RequestResponse.JurisdictionState}"));
        var logEntry2 = _cacheLogger.Entries.FirstOrDefault(v =>
           v.Message.Contains(
               $"UserCannotWorkRequestDueToSsl = True, UserName = {TestUserName}, RequestForServiceKey = {requestSearchResponse2.RequestResponse.RequestForServiceKey}, JurisdictionState = {requestSearchResponse2.RequestResponse.JurisdictionState}"));
        logEntry.Should().NotBeNull();
        logEntry2.Should().NotBeNull();
    }

    [Fact]
    public async Task HandleAsync_DoesNotFiltersOutRequests_WhenNotInNeedsSslSignoff()
    {
        _userConfigurationRepository.Setup(x => x.GetAsync(It.IsAny<string>())).ReturnsAsync(_userConfiguration);
        var requestSearchResponse1 = GetTestRequestSearchResponse();
        requestSearchResponse1.RequestResponse.JurisdictionState = "AZ";
        requestSearchResponse1.RequestResponse.Status = StatusEnum.NeedsMdReview;        

        _searchRepository.Setup(x => x.SearchAsync(It.IsAny<RequestSearchRequest>()))
            .ReturnsAsync(new List<RequestSearchResponse> { requestSearchResponse1 });

        var response = await _handler.HandleAsync(TestUserName);

        response.RequestForServiceKey.Should().Be(requestSearchResponse1.RequestResponse.RequestForServiceKey);
    }

    [Fact]
    public async Task HandleAsync_FiltersOutRequests_WhenUserStateLicensesIsNull()
    {
        var userConfiguration = UserProfileConfigurationAggregate.Create(
            Guid.NewGuid().ToString(),
            TestUserName, "Sleep MD Licensed In CT state", "first", "last", UserTypeEnum.MD, new List<string> { DisciplineEnum.Sleep.ToString() },
            new List<string> { TestUserName },
            new List<string>(),
            new Dictionary<SpecialtyEnum, string> { { SpecialtyEnum.Sleep, "TestScoringProfile" } },
            new Dictionary<DisciplineEnum, List<SpecialtyEntity>>()
        );

        var userConfigurationRepository = new Mock<IUserProfileConfigurationRepository>();
        userConfigurationRepository.Setup(x => x.GetAsync(It.IsAny<string>())).ReturnsAsync(userConfiguration);
        var requestSearchResponse1 = GetTestRequestSearchResponse();
        requestSearchResponse1.RequestResponse.JurisdictionState = "AZ";
        requestSearchResponse1.RequestResponse.Status = StatusEnum.NeedsSslSignoff;
        var requestSearchResponse2 = GetTestRequestSearchResponse();
        _searchRepository.Setup(x => x.SearchAsync(It.IsAny<RequestSearchRequest>()))
            .ReturnsAsync(new List<RequestSearchResponse> { requestSearchResponse1, requestSearchResponse2 });
        var handler = new GetNextRequestHandler(_searchRepository.Object, userConfigurationRepository.Object,
            (ILogger<GetNextRequestHandler>)_cacheLogger, _requestIndexingRepository.Object,
            _configuration.Object, _producer.Object, _eventHistoryRepository.Object);

        var response = await handler.HandleAsync(TestUserName);

        response.RequestForServiceKey.Should().Be(requestSearchResponse2.RequestResponse.RequestForServiceKey);
        response.RequestForServiceKey.Should().NotBe(requestSearchResponse1.RequestResponse.RequestForServiceKey);
       
    }

    [Fact]
    public async Task HandleAsync_FiltersOutRequests_WhenMaxAssignment()
    {
        var userEmail = "in1_UCXSlpMDPRF@test.com";

        var userConfiguration = UserProfileConfigurationAggregate.Create
        (
            Guid.NewGuid().ToString(),
            userEmail, "Sleep MD Licensed In CT state", "first", "last", UserTypeEnum.MD, 
            new List<string> { DisciplineEnum.Sleep.ToString() },
            new List<string> { userEmail },
            new List<string> { "TN", "AZ", "KY" },
            new Dictionary<SpecialtyEnum, string> { { SpecialtyEnum.Sleep, "TestScoringProfile" } },
            new Dictionary<DisciplineEnum, List<SpecialtyEntity>>());

        var userConfigurationRepository = new Mock<IUserProfileConfigurationRepository>();
        userConfigurationRepository.Setup(x => x.GetAsync(It.IsAny<string>())).ReturnsAsync(userConfiguration);

        var requestSearchResponse = GetTestRequestSearchResponse();
        requestSearchResponse.RequestResponse.AssignedToId = userEmail;
        _searchRepository.Setup(x => x.SearchAsync(It.IsAny<RequestSearchRequest>()))
            .ReturnsAsync(new List<RequestSearchResponse> { requestSearchResponse });

        var uCXRoutingRequestAssigned = new RoutingRequestAssignedEvent
        {
            AssignedToId = userEmail,
            RequestForServiceKey = Guid.NewGuid().ToString(),
            Name = "UCXRoutingRequestAssigned"
        };

        var authorizationCanceled = new AuthorizationCanceledEvent
        {
            CancelDate = DateTime.UtcNow,
            RequestForServiceKey = Guid.NewGuid().ToString(),
            Name = "AuthorizationCanceled"
        };

        var requestEventHistory = new RequestEventHistoryRequest
        {
            History = new List<UCXEvent> { authorizationCanceled, uCXRoutingRequestAssigned, uCXRoutingRequestAssigned, uCXRoutingRequestAssigned,
                uCXRoutingRequestAssigned, uCXRoutingRequestAssigned, uCXRoutingRequestAssigned, uCXRoutingRequestAssigned,
                uCXRoutingRequestAssigned, uCXRoutingRequestAssigned, uCXRoutingRequestAssigned, uCXRoutingRequestAssigned }
        };

        var eventHistoryRepository2 = new Mock<IEventHistoryRepository>();
        eventHistoryRepository2.Setup(x => x.GetRequestEventHistory(It.IsAny<String>())).ReturnsAsync(requestEventHistory);

        var handler = new GetNextRequestHandler(_searchRepository.Object, userConfigurationRepository.Object,
            (ILogger<GetNextRequestHandler>)_cacheLogger, _requestIndexingRepository.Object,
            _configuration.Object, _producer.Object, eventHistoryRepository2.Object);

        var response = await handler.HandleAsync(userEmail);

        response.RequestForServiceKey.Should().Be(requestSearchResponse.RequestResponse.RequestForServiceKey);
        _requestIndexingRepository.Verify(x => x.UpsertWithAssignmentAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_AssignsRequest_WhenReturningRequestToUser()
    {
        _userConfigurationRepository.Setup(x => x.GetAsync(It.IsAny<string>())).ReturnsAsync(_userConfiguration);
        var requestSearchResponse = GetTestRequestSearchResponse();
        requestSearchResponse.RequestResponse.AssignmentExpirationUtc = null;
        requestSearchResponse.RequestResponse.AssignedToId = null;
        _searchRepository.Setup(x => x.SearchAsync(It.IsAny<RequestSearchRequest>()))
            .ReturnsAsync(new List<RequestSearchResponse> { requestSearchResponse });

        var response = await _handler.HandleAsync(TestUserName);
        response.RequestForServiceKey.Should().Be(requestSearchResponse.RequestResponse.RequestForServiceKey);
        response.AssignedToId.Should().Be(TestUserName);
        var logEntry = _cacheLogger.Entries.FirstOrDefault(v =>
            v.Message.Contains(
                $"Assigning best matching request (RequestForServiceKey = {requestSearchResponse.RequestResponse.RequestForServiceKey} & Score {requestSearchResponse.Score}) to user {TestUserName}, AssignmentExpirationUtc "));
        logEntry.Should().NotBeNull();
    }

    [Fact]
    public async Task HandleAsync_PublishesAssignmentEvent_WhenReturningRequestToUser()
    {
        _userConfigurationRepository.Setup(x => x.GetAsync(It.IsAny<string>())).ReturnsAsync(_userConfiguration);
        var requestSearchResponse = GetTestRequestSearchResponse();
        _searchRepository.Setup(x => x.SearchAsync(It.IsAny<RequestSearchRequest>()))
            .ReturnsAsync(new List<RequestSearchResponse> { requestSearchResponse });

        var response = await _handler.HandleAsync(TestUserName);
        _producer.Verify(
            x => x.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()),
            Times.Once);
        var logEntry = _cacheLogger.Entries.FirstOrDefault(v =>
            v.Message.Contains(
                $"Published request assignment event, RequestForServiceKey = {requestSearchResponse.RequestResponse.RequestForServiceKey}, AssignedToId = {requestSearchResponse.RequestResponse.AssignedToId}"));
        logEntry.Should().NotBeNull();
    }
    [Fact]
    public async Task HandleAsync_DisplayOrReturnRequest_WhenReturningRequestToUserWithPermanentAssignment()
    {
        _userConfigurationRepository.Setup(x => x.GetAsync(It.IsAny<string>())).ReturnsAsync(_userConfiguration);
        var requestSearchResponse = GetTestRequestSearchResponseWithPermanentAssignment();
        _searchRepository.Setup(x => x.SearchAsync(It.IsAny<RequestSearchRequest>()))
            .ReturnsAsync(new List<RequestSearchResponse> { requestSearchResponse });

        var response = await _handler.HandleAsync(TestUserName);
        var logEntry = _cacheLogger.Entries.FirstOrDefault(v =>
            v.Message.Contains(
                $"Request is already assigned to user, UserId = {TestUserName}, RequestForServiceKey = {requestSearchResponse.RequestResponse.RequestForServiceKey}"));
        logEntry.Should().NotBeNull();
    }

    [Fact]
    public async Task HandleAsync_DoesNotReAssignsRequest_WhenTheUserIsAlreadyAssignedToRequest()
    {
        _userConfigurationRepository.Setup(x => x.GetAsync(It.IsAny<string>())).ReturnsAsync(_userConfiguration);
        var requestSearchResponse = GetTestRequestSearchResponse();
        requestSearchResponse.RequestResponse.AssignmentExpirationUtc = DateTime.UtcNow.AddMinutes(20);
        _searchRepository.Setup(x => x.SearchAsync(It.IsAny<RequestSearchRequest>()))
            .ReturnsAsync(new List<RequestSearchResponse> { requestSearchResponse });

        var response = await _handler.HandleAsync(TestUserName);
        response.AssignmentExpirationUtc.Should().Be(requestSearchResponse.RequestResponse.AssignmentExpirationUtc);
        var logEntry = _cacheLogger.Entries.FirstOrDefault(v =>
            v.Message.Contains(
                $"Request is already assigned to user, UserId = {TestUserName}, RequestForServiceKey = {requestSearchResponse.RequestResponse.RequestForServiceKey}"));
        logEntry.Should().NotBeNull();
        _requestIndexingRepository.Verify(x => x.UpsertWithAssignmentAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ReAssignsRequest_WhenTheUserIsAlreadyAssignedToRequest()
    {
        _userConfigurationRepository.Setup(x => x.GetAsync(It.IsAny<string>())).ReturnsAsync(_userConfiguration);
        var requestSearchResponse = GetTestRequestSearchResponse();
        requestSearchResponse.RequestResponse.AssignmentExpirationUtc = DateTime.UtcNow.AddMinutes(-20);
        _searchRepository.Setup(x => x.SearchAsync(It.IsAny<RequestSearchRequest>()))
            .ReturnsAsync(new List<RequestSearchResponse> { requestSearchResponse });

        var response = await _handler.HandleAsync(TestUserName);
        response.AssignmentExpirationUtc.Should().BeAfter(DateTime.UtcNow.AddMinutes(15));
        _requestIndexingRepository.Verify(x => x.UpsertWithAssignmentAsync(It.IsAny<RequestAggregate>(), It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_DoesNotReAssignsRequest_WhenThePostDecisionRequestAssignmentIsExpired()
    {
        _userConfigurationRepository.Setup(x => x.GetAsync(It.IsAny<string>())).ReturnsAsync(_userConfiguration);
        var requestSearchResponse = GetTestRequestSearchResponse();
        requestSearchResponse.RequestResponse.AssignedToId = TestUserName;
        requestSearchResponse.RequestResponse.AssignmentExpirationUtc = DateTime.UtcNow.AddMinutes(-1);
        requestSearchResponse.RequestResponse.Status = StatusEnum.Reconsideration;
        _searchRepository.Setup(x => x.SearchAsync(It.IsAny<RequestSearchRequest>()))
            .ReturnsAsync(new List<RequestSearchResponse> { requestSearchResponse });

        var response = await _handler.HandleAsync(TestUserName);
        response.Success.Should().BeFalse();
        response.ErrorMessage.Should().Be("No suitable requests found for user");
        response.GetNextNoRequestsReason.Should().Be(GetNextNoRequestsReasonEnum.NoneSuitable);
    }
    [Fact]
    public async Task HandleAsync_GetsTheRequest_WhenThePostDecisionRequestAssignmentIsActive()
    {
        _userConfigurationRepository.Setup(x => x.GetAsync(It.IsAny<string>())).ReturnsAsync(_userConfiguration);
        var requestSearchResponse = GetTestRequestSearchResponse();
        string RFSK1 = Guid.NewGuid().ToString();
        requestSearchResponse.RequestResponse.RequestForServiceKey = RFSK1;
        requestSearchResponse.RequestResponse.AssignedToId = TestUserName;
        requestSearchResponse.RequestResponse.AssignmentExpirationUtc = DateTime.UtcNow.AddDays(999);
        requestSearchResponse.RequestResponse.Status = StatusEnum.Reconsideration;
        _searchRepository.Setup(x => x.SearchAsync(It.IsAny<RequestSearchRequest>()))
            .ReturnsAsync(new List<RequestSearchResponse> { requestSearchResponse });

        var response = await _handler.HandleAsync(TestUserName);
        response.RequestForServiceKey.Should().Be(requestSearchResponse.RequestResponse.RequestForServiceKey);
        response.AssignedToId.Should().Be(TestUserName);
        var logEntry = _cacheLogger.Entries.FirstOrDefault(v =>
            v.Message.Contains(
                $"Request is already assigned to user, UserId = {TestUserName}, RequestForServiceKey = {requestSearchResponse.RequestResponse.RequestForServiceKey}"));
        logEntry.Should().NotBeNull();
    }
    [Fact]
    public async Task HandleAsync_GetsTheRequest_WhenThePostDecisionRequestHasNoAssignment()
    {
        _userConfigurationRepository.Setup(x => x.GetAsync(It.IsAny<string>())).ReturnsAsync(_userConfiguration);
        var requestSearchResponse = GetTestRequestSearchResponse();
        string RFSK1 = Guid.NewGuid().ToString();
        requestSearchResponse.RequestResponse.RequestForServiceKey = RFSK1;
        requestSearchResponse.RequestResponse.AssignedToId = null;
        requestSearchResponse.RequestResponse.AssignmentExpirationUtc = null;
        requestSearchResponse.RequestResponse.Status = StatusEnum.Reconsideration;
        _searchRepository.Setup(x => x.SearchAsync(It.IsAny<RequestSearchRequest>()))
            .ReturnsAsync(new List<RequestSearchResponse> { requestSearchResponse });

        var response = await _handler.HandleAsync(TestUserName);
        response.Success.Should().BeFalse();
        response.ErrorMessage.Should().Be("No suitable requests found for user");
        response.GetNextNoRequestsReason.Should().Be(GetNextNoRequestsReasonEnum.NoneSuitable);
    }
   
    private RequestSearchResponse GetTestRequestSearchResponse()
    {
        var requestSearchResponse = _fixture.Create<RequestSearchResponse>();
        requestSearchResponse.RequestResponse.RequestForServiceKey = Guid.NewGuid().ToString();
        requestSearchResponse.RequestResponse.AuthorizationKey = Guid.NewGuid().ToString();
        requestSearchResponse.RequestResponse.InsuranceCarrierKey = Guid.NewGuid().ToString();
        requestSearchResponse.RequestResponse.Specialty = SpecialtyEnum.Sleep;
        requestSearchResponse.RequestResponse.Status = StatusEnum.NeedsMdReview;
        requestSearchResponse.RequestResponse.Priority = PriorityEnum.Urgent;
        requestSearchResponse.RequestResponse.SslRequirementStatus = SslRequirementStatusEnum.SslNotRequired;
        requestSearchResponse.RequestResponse.LineOfBusiness = LineOfBusinessEnum.Commercial;
        requestSearchResponse.RequestResponse.AssignedToId = TestUserName;
        requestSearchResponse.RequestResponse.AssignmentExpirationUtc = DateTime.UtcNow.AddMinutes(-1);
        return requestSearchResponse;
    }
    private RequestSearchResponse GetTestRequestSearchResponseWithPermanentAssignment()
    {
        var requestSearchResponse = _fixture.Create<RequestSearchResponse>();
        requestSearchResponse.RequestResponse.RequestForServiceKey = Guid.NewGuid().ToString();
        requestSearchResponse.RequestResponse.AuthorizationKey = Guid.NewGuid().ToString();
        requestSearchResponse.RequestResponse.InsuranceCarrierKey = Guid.NewGuid().ToString();
        requestSearchResponse.RequestResponse.Specialty = SpecialtyEnum.Sleep;
        requestSearchResponse.RequestResponse.Status = StatusEnum.NeedsMdReview;
        requestSearchResponse.RequestResponse.Priority = PriorityEnum.Urgent;
        requestSearchResponse.RequestResponse.SslRequirementStatus = SslRequirementStatusEnum.SslNotRequired;
        requestSearchResponse.RequestResponse.LineOfBusiness = LineOfBusinessEnum.Commercial;
        requestSearchResponse.RequestResponse.AssignedToId = TestUserName;
        requestSearchResponse.RequestResponse.AssignmentExpirationUtc = DateTime.UtcNow.AddDays(999);
        return requestSearchResponse;
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Divergic.Logging.Xunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json;
using RequestRouting.Application.Commands.Handlers;
using RequestRouting.Application.Configurations;
using RequestRouting.Application.Interface;
using RequestRouting.Application.Replay;
using RequestRouting.Domain.Aggregates;
using RequestRouting.Domain.Enums;
using RequestRouting.Domain.Events;
using RequestRouting.Domain.ValueObjects;
using RequestRouting.Observer.HandlerQueue;
using Xunit;
using Xunit.Abstractions;

namespace RequestRouting.UnitTests.Application.Replay;

public class ReplayServiceTests
{
    private readonly ICacheLogger _logger;
    private readonly IOptions<AppSettings> _options;

    public ReplayServiceTests(ITestOutputHelper outputHelper)
    {
        _logger = outputHelper.BuildLoggerFor<ReplayService>();
        _options = Options.Create(new AppSettings
        {
            EventConfigs = new List<EventConfig>
            {
                new()
                {
                    Name = "UCXRequestForServiceSubmitted", HandlerName = "TestRequestHandler", Version = "1.1.0.0"
                },
                new()
                {
                    Name = "UCXMemberInfoUpdatedForRequest", HandlerName = "MemberInfoUpdatedForRequestHandler",
                    Version = "1.0.0.0"
                },

                new()
                {
                    Name = "UCXDueDateCalculated", HandlerName = "DueDateCalculatedHandler", Version = "1.0.0.0"
                },
                new()
                {
                    Name = "UCXRequestUrgencyUpdated", HandlerName = "RequestUrgencyUpdatedHandler", Version = "1.0.0.0"
                },
                new()
                {
                    Name = "UCXDueDateMissed", HandlerName = "DueDateMissedHandler", Version = "1.0.0.0"
                }
            }
        });
    }

    [Fact]
    public async Task ReplayService_IsRoutableSetToFalse_WhenRequestForServiceSubmittedDoesNotExist()
    {
        // arrange
        var events = new List<UCXEvent>();
        events.Add(JsonConvert.DeserializeObject<MemberInfoUpdatedForRequestEvent>(
            "  {    \"Id\": null,    \"AuthorizationKey\": \"6b08dc3d-c765-4637-9c71-3099fa24e7cd\",    \"MemberKey\": \"0cc423a5-e5d3-4f84-a191-d39d2a7ccf05\",    \"MemberPolicyKey\": \"a2c9871c-28fd-4088-9bf5-0917474d3ac0\",    \"MemberPolicyId\": \"91909589401\",    \"InsuranceCarrierKey\": \"eafa997e-6943-4b96-9d74-001c63f8bbc9\",    \"LineOfBusiness\": \"Commercial\",    \"StateCode\": \"CT\",    \"JurisdictionState\": \"CT\",    \"ZipCode\": \"06011\",    \"CountryCode\": \"\",    \"RequestForServiceKey\": \"e92d2297-3443-4fc4-82a3-1be179f584c4\",    \"RequestForServiceId\": \"0dhkmwqr7w\",    \"Name\": \"UCXMemberInfoUpdatedForRequest\",    \"Version\": \"1.0.0.0\",    \"Key\": \"2292b629-e16c-4dee-936c-a0d4d8137c5b\",    \"OriginSystem\": \"eP\",    \"Sent\": \"2022-11-04T12:26:16.0954974+00:00\"  }"));
        events.Add(JsonConvert.DeserializeObject<MemberInfoUpdatedForRequestEvent>(
            "{    \"Id\": null,    \"AuthorizationKey\": \"6b08dc3d-c765-4637-9c71-3099fa24e7cd\",    \"MemberKey\": \"0cc423a5-e5d3-4f84-a191-d39d2a7ccf05\",    \"MemberPolicyKey\": \"a2c9871c-28fd-4088-9bf5-0917474d3ac0\",    \"MemberPolicyId\": \"91909589401\",    \"InsuranceCarrierKey\": \"eafa997e-6943-4b96-9d74-001c63f8bbc9\",    \"LineOfBusiness\": \"Commercial\",    \"StateCode\": \"CT\",    \"JurisdictionState\": \"CT\",    \"ZipCode\": \"06011\",    \"CountryCode\": \"\",    \"RequestForServiceKey\": \"e92d2297-3443-4fc4-82a3-1be179f584c4\",    \"RequestForServiceId\": \"0dhkmwqr7w\",    \"Name\": \"UCXMemberInfoUpdatedForRequest\",    \"Version\": \"1.0.0.0\",    \"Key\": \"0ef37259-e87e-40ce-81ee-323cb97e08a0\",    \"OriginSystem\": \"eP\",    \"Sent\": \"2022-11-04T12:26:16.3455041+00:00\"  }"));
        events.Add(JsonConvert.DeserializeObject<DueDateMissedEvent>(
            "{    \"DueDateByUtc\": \"2022-12-19T12:26:16.0229609+00:00\",    \"MedicalDiscipline\": \"Sleep\",    \"RequestForServiceKey\": \"e92d2297-3443-4fc4-82a3-1be179f584c4\",    \"RequestForServiceId\": \"0dhkmwqr7w\",    \"Name\": \"UcxDueDateMissed\",    \"Version\": \"1.0.0.0\",    \"Key\": \"b6c2a45a-3dd5-4b5a-843f-d26155b4ba38\",    \"OriginSystem\": \"eP\",    \"Sent\": \"2022-12-19T12:26:28.496+00:00\"  }"));
        events.Add(JsonConvert.DeserializeObject<DueDateMissedEvent>(
            "{      \"DueDateByUtc\": \"2022-12-21T23:59:59+00:00\",      \"MedicalDiscipline\": \"Sleep\",      \"RequestForServiceKey\": \"e92d2297-3443-4fc4-82a3-1be179f584c4\",      \"RequestForServiceId\": \"0dhkmwqr7w\",      \"Name\": \"UcxDueDateMissed\",      \"Version\": \"1.0.0.0\",      \"Key\": \"f6f4c726-1fe0-4bc8-a76f-f04442f24f4f\",      \"OriginSystem\": \"eP\",      \"Sent\": \"2022-12-22T00:01:35.811+00:00\"    }"));
        events.Add(JsonConvert.DeserializeObject<RequestUrgencyUpdatedEvent>(
            "{    \"UrgentAttestation\": {      \"IsUrgent\": true,      \"Reason\": \"test new routing in ucx\",      \"GeneratingAgent\": \"EpPacElig@evicore.com\",      \"GeneratingAgentContext\": \"Phone\",      \"UrgentRequestDate\": \"2023-02-01T21:07:51Z\"    },    \"MedicalDiscipline\": \"Sleep\",    \"RequestForServiceKey\": \"e92d2297-3443-4fc4-82a3-1be179f584c4\",    \"RequestForServiceId\": null,    \"Name\": \"UcxRequestUrgencyUpdated\",    \"Version\": \"1.0.0.0\",    \"Key\": \"8b6131ea-c117-4455-a332-56065967610a\",    \"OriginSystem\": \"eP\",    \"Sent\": \"2023-02-01T21:07:51.832+00:00\"  }"));
        events.Add(JsonConvert.DeserializeObject<DueDateCalculatedEvent>(
            "{    \"DueDateByDateTimeUtc\": \"2023-02-02T00:00:00\",    \"RecordedDate\": \"2023-02-02T15:09:08.344+00:00\",    \"DueBy\": \"2023-02-02T23:59:59+00:00\",    \"MedicalDiscipline\": \"Sleep\",    \"RequestForServiceKey\": \"e92d2297-3443-4fc4-82a3-1be179f584c4\",    \"RequestForServiceId\": null,    \"Name\": \"UCXDueDateCalculated\",    \"Version\": \"1.0.0.0\",    \"Key\": \"b33f211f-4ed5-40ca-bb77-4ef7df6f11cc\",    \"OriginSystem\": \"eP\",    \"Sent\": \"2023-02-02T15:09:08.344+00:00\"  }"));
        events.Add(JsonConvert.DeserializeObject<DueDateMissedEvent>(
            "{    \"DueDateByUtc\": \"2023-02-02T23:59:59+00:00\",    \"MedicalDiscipline\": \"Sleep\",    \"RequestForServiceKey\": \"e92d2297-3443-4fc4-82a3-1be179f584c4\",    \"RequestForServiceId\": \"0dhkmwqr7w\",    \"Name\": \"UcxDueDateMissed\",    \"Version\": \"1.0.0.0\",    \"Key\": \"99ad10bc-61c1-40c7-b247-50aa47f0bcc2\",    \"OriginSystem\": \"eP\",    \"Sent\": \"2023-02-03T00:00:58.079+00:00\"  }"));

        var serviceCollection = new ServiceCollection();

        var memberInfoUpdatedForRequestHandler = new MemberInfoUpdatedForRequestHandler(
            new Mock<IStateConfigurationCacheService>().Object,
            new Mock<ILogger<MemberInfoUpdatedForRequestHandler>>().Object);
        serviceCollection.AddSingleton<IRequestHandler>(memberInfoUpdatedForRequestHandler);

        var dueDateMissedHandler = new DueDateMissedHandler(new Mock<ILogger<DueDateMissedHandler>>().Object);
        serviceCollection.AddSingleton<IRequestHandler>(dueDateMissedHandler);

        var requestUrgencyUpdatedHandler =
            new RequestUrgencyUpdatedHandler(new Mock<ILogger<RequestUrgencyUpdatedHandler>>().Object);
        serviceCollection.AddSingleton<IRequestHandler>(requestUrgencyUpdatedHandler);

        var dueDateCalculatedHandler =
            new DueDateCalculatedHandler(new Mock<ILogger<DueDateCalculatedHandler>>().Object);
        serviceCollection.AddSingleton<IRequestHandler>(dueDateCalculatedHandler);

        var handlerQueue = new HandlerQueue(serviceCollection.BuildServiceProvider(), _options,
            new Mock<ILogger<HandlerQueue>>().Object);
        var replayService = new ReplayService(handlerQueue, (ILogger<ReplayService>)_logger);

        // act
        var request = await replayService.ReplayEvents(events);

        // assert
        request.Should().NotBeNull();
        request.RequestForServiceKey.Should().Be("e92d2297-3443-4fc4-82a3-1be179f584c4");
        request.Priority.Should().Be(PriorityEnum.Urgent);
        request.IsRoutable().Should().BeFalse();
    }

    [Fact]
    public void ReplayService_ReplaysSingleEvent()
    {
        // arrange
        var handlerQueue = SetupMockHandlerQueue();
        var sut = new ReplayService(handlerQueue.Object, (ILogger<ReplayService>)_logger);

        var events = new List<UCXEvent>
        {
            new RequestForServiceSubmittedEvent
            {
                Name = "RequestForServiceSubmitted",
                Version = "1.1.0.0"
            }
        };

        // act
        var result = sut.ReplayEvents(events);

        // assert
        Assert.NotNull(result);
        handlerQueue.Verify(x => x.GetRequestHandler(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void ReplayService_ReplaysMultipleEvents()
    {
        // arrange
        var handlerQueue = SetupMockHandlerQueue();
        var sut = new ReplayService(handlerQueue.Object, (ILogger<ReplayService>)_logger);

        var events = new List<UCXEvent>
        {
            new RequestForServiceSubmittedEvent
            {
                Name = "RequestForServiceSubmitted",
                Version = "1.1.0.0"
            },
            new RequestForServiceSubmittedEvent
            {
                Name = "RequestForServiceSubmitted",
                Version = "1.1.0.0"
            }
        };

        // act
        var result = sut.ReplayEvents(events);

        // assert
        Assert.NotNull(result);
        handlerQueue.Verify(x => x.GetRequestHandler(It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ReplayService_ThrowsExceptionWhenNameMissing()
    {
        // arrange
        var handlerQueue = SetupMockHandlerQueue();
        var sut = new ReplayService(handlerQueue.Object, (ILogger<ReplayService>)_logger);

        var events = new List<UCXEvent>
        {
            new RequestForServiceSubmittedEvent
            {
                Version = "1.1.0.0"
            }
        };

        // act
        // assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.ReplayEvents(events));
    }

    [Fact]
    public async Task ReplayService_ThrowsExceptionWhenVersionMissing()
    {
        // arrange
        var handlerQueue = SetupMockHandlerQueue();
        var sut = new ReplayService(handlerQueue.Object, (ILogger<ReplayService>)_logger);

        var events = new List<UCXEvent>
        {
            new RequestForServiceSubmittedEvent
            {
                Name = "RequestForServiceSubmitted"
            }
        };

        // act
        // assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.ReplayEvents(events));
    }

    [Fact]
    public async Task ReplayService_ThrowsExceptionWhenNameAndVersionMissing()
    {
        // arrange
        var handlerQueue = SetupMockHandlerQueue();
        var sut = new ReplayService(handlerQueue.Object, (ILogger<ReplayService>)_logger);

        var events = new List<UCXEvent>
        {
            new RequestForServiceSubmittedEvent()
        };

        // act
        // assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.ReplayEvents(events));
    }

    [Fact]
    public async Task ReplayService_UpdatesRequestState()
    {
        // arrange
        var handlerQueue = SetupRealHandlerQueue();
        var sut = new ReplayService(handlerQueue, (ILogger<ReplayService>)_logger);

        var events = new List<UCXEvent>
        {
            CreateRequestForServiceSubmittedMessage()
        };

        // act
        var result = await sut.ReplayEvents(events);

        // assert
        Assert.NotNull(result);
        Assert.Equal(events[0].RequestForServiceId, result.RequestForServiceId);
    }

    private Mock<IHandlerQueue> SetupMockHandlerQueue()
    {
        var mockRequestForServiceHandler = new Mock<IRequestHandler>();
        mockRequestForServiceHandler.SetupGet(h => h.EntityTypeName).Returns("RequestForServiceSubmitted");
        mockRequestForServiceHandler.Setup(h => h.HandleAsync(It.Ref<RequestAggregate>.IsAny, It.IsAny<UCXEvent>()));

        var mockDueDateHandler = new Mock<IRequestHandler>();
        mockDueDateHandler.SetupGet(h => h.EntityTypeName).Returns("DueDateCalculated");
        mockDueDateHandler.Setup(h => h.HandleAsync(It.Ref<RequestAggregate>.IsAny, It.IsAny<UCXEvent>()));

        var mock = new Mock<IHandlerQueue>();
        mock.Setup(x => x.GetRequestHandler("RequestForServiceSubmitted", "1.1.0.0"))
            .Returns(mockRequestForServiceHandler.Object);
        mock.Setup(x => x.GetRequestHandler("DueDateCalculated", "1.1.0.0")).Returns(mockDueDateHandler.Object);

        return mock;
    }

    private IHandlerQueue SetupRealHandlerQueue()
    {
        var serviceCollection = new ServiceCollection();
        IRequestHandler testHandler = new TestRequestHandler();
        serviceCollection.AddSingleton(testHandler);

        var handlerQueue = new HandlerQueue(serviceCollection.BuildServiceProvider(), _options,
            new Mock<ILogger<HandlerQueue>>().Object);

        return handlerQueue;
    }

    private RequestForServiceSubmittedEvent CreateRequestForServiceSubmittedMessage()
    {
        var memberPolicy = new MemberPolicyEvent
        {
            MemberPolicyKey = "QWERTY-1234",
            MemberId = "MEMBERID123",
            InsuranceCarrierKey = Guid.NewGuid().ToString(),
            LineofBusiness = "Medicare",
            Gender = "M",
            FirstName = "First",
            MiddleName = "Middle",
            LastName = "Last",
            DateOfBirth = DateTime.Now.AddDays(-10000).ToString(),
            AddressLine1 = "Address 543",
            City = "Nashville",
            StateCode = "TN",
            ZipCode = "34567",
            CountryCode = "USA",
            FamilySequence = "Sequence"
        };

        var provider = new ProviderEvent
        {
            ProviderType = "provider type",
            OrganizationName = "org name",
            FirstName = "firstname",
            LastName = "lastname",
            Address1 = "address 1",
            Address2 = "address 2",
            City = "Nashville",
            StateCode = "TN",
            County = "Davidson",
            Zip = "34567",
            Phone = "1231231234",
            PhoneExtension = "123",
            Fax = "2223334444",
            PrimaryContact = new PrimaryContactEvent(),
            AdditionalContacts = new List<AdditionalContactEvent>()
        };

        var procedureCode = new SubmittedProcedureCodeEvent
        {
            MedicalProcedureKey = "procedure key",
            MedicalProcedureCode = "procedure code",
            MedicalProcedureId = "procedure id",
            Name = "procedure name",
            SimplifiedDescription = "description"
        };

        var requestedServices = new RequestedServicesEvent
        {
            ServicePlace = "place",
            ServicePlaceCode = "code",
            ServiceType = "type",
            ServiceTypeFullName = "fullname"
        };

        return new RequestForServiceSubmittedEvent
        {
            RequestGeneratingAgent = "Jim Halpert",
            AuthorizationKey = Guid.NewGuid().ToString(),
            RequestForServiceKey = Guid.NewGuid().ToString(),
            RequestForServiceId = "ID123",
            MedicalDiscipline = "Sleep",
            DateOfService = DateTime.Now,
            StartDate = DateTime.Now,
            IsUrgent = true,
            MemberPolicy = memberPolicy,
            Providers = new List<ProviderEvent> { provider },
            SubmittedProcedureCodes = new List<SubmittedProcedureCodeEvent> { procedureCode },
            RequestedServices = requestedServices,
            OriginSystem = "eP",
            Name = "UCXRequestForServiceSubmitted",
            Version = "1.1.0.0"
        };
    }
}

internal class TestRequestHandler : IRequestHandler
{
    public string EntityTypeName => "UCXRequestForServiceSubmitted";

    public async Task<RequestAggregate> HandleAsync(RequestAggregate requestState, UCXEvent currentEvent)
    {
        var request = currentEvent as RequestForServiceSubmittedEvent;

        var authKeyParseSuccess = Guid.TryParse(request.AuthorizationKey, out var authKey);
        var insuranceCarrierParseSuccess = Guid.TryParse(request.MemberPolicy.InsuranceCarrierKey, out var carrierKey);

        var requestForServiceSubmitted = new RequestForServiceSubmittedObject
        {
            AuthorizationKey = authKeyParseSuccess ? authKey : Guid.Empty,
            RequestForServiceKey = Guid.Parse(request.RequestForServiceKey),
            RequestForServiceId = request.RequestForServiceId,
            Specialty = Enum.Parse<SpecialtyEnum>(request.MedicalDiscipline),
            InsuranceCarrierKey = insuranceCarrierParseSuccess ? carrierKey : Guid.Empty,
            LineOfBusiness = request.MemberPolicy.LineofBusiness is not null
                ? Enum.Parse<LineOfBusinessEnum>(request.MemberPolicy.LineofBusiness, true)
                : LineOfBusinessEnum.LineOfBusinessUnknown,
            Priority = request.IsUrgent ? PriorityEnum.Urgent : PriorityEnum.Standard,
            Status = StatusEnum.NeedsEligibilityReview
        };

        requestState.PopulateInitialRequestData(requestForServiceSubmitted,
            new StateConfigurationAggregate("TN", new List<LineOfBusinessConfigurationObject>()));

        return requestState;
    }
}

using Divergic.Logging.Xunit;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Moq;
using RequestRouting.Domain.Events;
using RequestRouting.Infrastructure.DatabaseObjects;
using RequestRouting.Infrastructure.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace RequestRouting.UnitTests.Infrastructure.Repositories;

public class EventHistoryRepositoryTests
{
    private readonly Mock<Container> _container;
    private readonly Mock<CosmosClient> _cosmosClient;
    private readonly EventHistoryRepository _eventHistoryRepository;
    private readonly Mock<FeedIterator<RequestEventHistoryDto>> _feedIterator;
    private readonly Mock<FeedResponse<RequestEventHistoryDto>> _feedResponse;
    private readonly ICacheLogger _logger;

    public EventHistoryRepositoryTests(ITestOutputHelper outputHelper)
    {
        _logger = outputHelper.BuildLoggerFor<EventHistoryRepository>();
        _container = new Mock<Container>();
        _cosmosClient = new Mock<CosmosClient>();
        _cosmosClient.Setup(x => x.GetContainer(It.IsAny<string>(), It.IsAny<string>())).Returns(_container.Object);
        _eventHistoryRepository = new EventHistoryRepository(_cosmosClient.Object, "", "",
            (ILogger<EventHistoryRepository>)_logger);
        _feedIterator = new Mock<FeedIterator<RequestEventHistoryDto>>();
        _feedResponse = new Mock<FeedResponse<RequestEventHistoryDto>>();
    }

    [Fact]
    public async Task SaveRequestEventHistory_LogsTwice_WhenCreationIsSuccessful()
    {
        // arrange
        var requestHistory = new RequestRouting.Application.Requests.RequestEventHistoryRequest
        {
            History = new List<UCXEvent>(),
            ETag = "",
            Id = "",
            RequestForServiceKey = ""
        };

        // act
        await _eventHistoryRepository.SaveRequestEventHistory(requestHistory);

        // assert
        _logger.Entries.Count.Should().Be(2);
    }

    [Fact]
    public async Task SaveRequestEventHistory_CallsUpsertItemAsyncOnce_WhenCreationIsSuccessful()
    {
        // arrange
        var requestHistory = new RequestRouting.Application.Requests.RequestEventHistoryRequest
        {
            History = new List<UCXEvent>(),
            ETag = "",
            Id = "",
            RequestForServiceKey = ""
        };

        // act
        await _eventHistoryRepository.SaveRequestEventHistory(requestHistory);

        // assert
        _container.Verify<Task<ItemResponse<RequestEventHistoryDto>>>(
            x => x.UpsertItemAsync(It.IsAny<RequestEventHistoryDto>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveRequestEventHistory_CatchesAndLogsConflict_WhenConflictIsThrown()
    {
        // arrange
        var requestHistory = new RequestRouting.Application.Requests.RequestEventHistoryRequest
        {
            History = new List<UCXEvent>(),
            ETag = "",
            Id = "",
            RequestForServiceKey = ""
        };

        _container.Setup(x => x.UpsertItemAsync(It.IsAny<RequestEventHistoryDto>(), It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("", HttpStatusCode.PreconditionFailed, 412, "", 0.0));

        // act
        var act = () => _eventHistoryRepository.SaveRequestEventHistory(requestHistory);

        // assert
        await act.Should().ThrowAsync<CosmosException>();
        var logEntry = _logger.Entries.FirstOrDefault(x =>
            x.Message.Contains(
                "EventHistoryRepository:SaveRequestEventHistory failed due to precondition check (412)"));
        logEntry.Should().NotBeNull();
    }

    [Fact]
    public async Task SaveRequestEventHistory_RethrowsOtherExceptions_WhenAnyOtherExceptionIsThrown()
    {
        // arrange
        var requestHistory = new RequestRouting.Application.Requests.RequestEventHistoryRequest
        {
            History = new List<UCXEvent>(),
            ETag = "",
            Id = "",
            RequestForServiceKey = ""
        };

        _container.Setup(x => x.UpsertItemAsync(It.IsAny<RequestEventHistoryDto>(), It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("", HttpStatusCode.BadRequest, 400, "", 0.0));

        // act
        var act = () => _eventHistoryRepository.SaveRequestEventHistory(requestHistory);

        // assert
        await act.Should().ThrowAsync<CosmosException>();
        var logEntry = _logger.Entries.FirstOrDefault(x =>
            x.Message.Contains("EventHistoryRepository:SaveRequestEventHistory Cosmos exception while saving"));
        logEntry.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateRequestEventHistory_LogsTwice_WhenCreationIsSuccessful()
    {
        // arrange
        var requestHistory = new RequestRouting.Application.Requests.RequestEventHistoryRequest
        {
            History = new List<UCXEvent>(),
            ETag = "",
            Id = "",
            RequestForServiceKey = ""
        };

        // act
        await _eventHistoryRepository.CreateRequestEventHistory(requestHistory);

        // assert
        _logger.Entries.Count.Should().Be(2);
    }

    [Fact]
    public async Task CreateRequestEventHistory_CallsCreateItemAsyncOnce_WhenCreationIsSuccessful()
    {
        // arrange
        var requestHistory = new RequestRouting.Application.Requests.RequestEventHistoryRequest
        {
            History = new List<UCXEvent>(),
            ETag = "",
            Id = "",
            RequestForServiceKey = ""
        };

        // act
        await _eventHistoryRepository.CreateRequestEventHistory(requestHistory);

        // assert
        _container.Verify<Task<ItemResponse<RequestEventHistoryDto>>>(
            x => x.CreateItemAsync(It.IsAny<RequestEventHistoryDto>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateRequestEventHistory_CatchesAndLogsConflict_WhenConflictIsThrown()
    {
        // arrange
        var requestHistory = new RequestRouting.Application.Requests.RequestEventHistoryRequest
        {
            History = new List<UCXEvent>(),
            ETag = "",
            Id = "",
            RequestForServiceKey = ""
        };

        _container.Setup(x => x.CreateItemAsync(It.IsAny<RequestEventHistoryDto>(), It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("", HttpStatusCode.Conflict, 409, "", 0.0));

        // act
        var act = () => _eventHistoryRepository.CreateRequestEventHistory(requestHistory);

        // assert
        await act.Should().ThrowAsync<CosmosException>();
        var logEntry = _logger.Entries.FirstOrDefault(x =>
            x.Message.Contains("EventHistoryRepository:CreateRequestEventHistory failed due to conflict (409)"));
        logEntry.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateRequestEventHistory_RethrowsOtherExceptions_WhenAnyOtherExceptionIsThrown()
    {
        // arrange
        var requestHistory = new RequestRouting.Application.Requests.RequestEventHistoryRequest
        {
            History = new List<UCXEvent>(),
            ETag = "",
            Id = "",
            RequestForServiceKey = ""
        };

        _container.Setup(x => x.CreateItemAsync(It.IsAny<RequestEventHistoryDto>(), It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("", HttpStatusCode.BadRequest, 400, "", 0.0));

        // act
        var act = () => _eventHistoryRepository.CreateRequestEventHistory(requestHistory);

        // assert
        await act.Should().ThrowAsync<CosmosException>();
        var logEntry = _logger.Entries.FirstOrDefault(x =>
            x.Message.Contains("EventHistoryRepository:CreateRequestEventHistory Cosmos exception while saving"));
        logEntry.Should().NotBeNull();
    }

    [Fact]
    public async Task GetDuplicateRequestEventHistory_ReturnsDocument_WhenDocumentExist()
    {
        // arrange
        var requestEventHistory = new RequestEventHistoryDto { RequestForServiceKey = Guid.NewGuid().ToString() };
        SetupFeedIterator(new List<RequestEventHistoryDto> { requestEventHistory });
        _container.Setup(x => x.GetItemQueryIterator<RequestEventHistoryDto>(It.IsAny<QueryDefinition>(), null, null))
            .Returns(_feedIterator.Object);

        // act
        var response = await _eventHistoryRepository.GetDuplicateRequestEventHistory("", "");

        // assert
        response.Should().NotBeNull();
        response.Should().BeOfType<RequestRouting.Application.Requests.RequestEventHistoryRequest>();
        response.RequestForServiceKey.Should().Be(requestEventHistory.RequestForServiceKey);
    }

    [Fact]
    public async Task GetDuplicateRequestEventHistory_ReturnsEmptyDocument_WhenDocumentDoesNotExist()
    {
        // arrange
        var requestEventHistory = new RequestEventHistoryDto { RequestForServiceKey = Guid.NewGuid().ToString() };
        SetupFeedIterator(new List<RequestEventHistoryDto> { requestEventHistory });
        _container.Setup(x => x.GetItemQueryIterator<RequestEventHistoryDto>(It.IsAny<QueryDefinition>(), null, null))
            .Throws(new CosmosException("", HttpStatusCode.NotFound, 204, null, .0));

        // act
        var response = await _eventHistoryRepository.GetDuplicateRequestEventHistory("", "");

        // assert
        response.Should().NotBeNull();
        response.Should().BeOfType<RequestRouting.Application.Requests.RequestEventHistoryRequest>();
        response.RequestForServiceKey.Should().BeNull();
    }

    [Fact]
    public async Task GetDuplicateRequestEventHistory_RethrowsException_WhenAnExceptionIsThrown()
    {
        // arrange
        var requestEventHistory = new RequestEventHistoryDto { RequestForServiceKey = Guid.NewGuid().ToString() };
        SetupFeedIterator(new List<RequestEventHistoryDto> { requestEventHistory });
        _container.Setup(x => x.GetItemQueryIterator<RequestEventHistoryDto>(It.IsAny<QueryDefinition>(), null, null))
            .Throws(new Exception());

        // act
        Func<Task> act = () => _eventHistoryRepository.GetDuplicateRequestEventHistory("", "");

        // assert
        await act.Should().ThrowAsync<Exception>();
    }

    private void SetupFeedIterator(List<RequestEventHistoryDto> requestEventHistory)
    {
        _feedResponse.Setup(x => x.GetEnumerator()).Returns(requestEventHistory.GetEnumerator());
        _feedResponse.Setup(x => x.Resource).Returns(requestEventHistory);
        _feedIterator.Setup(x => x.HasMoreResults).Returns(true);
        _feedIterator.Setup(x => x.ReadNextAsync(default))
            .ReturnsAsync(_feedResponse.Object)
            .Callback(() => _feedIterator.Setup(f => f.HasMoreResults).Returns(false));
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoFixture;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Divergic.Logging.Xunit;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using RequestRouting.Application.Query.Responses;
using RequestRouting.Domain.Aggregates;
using RequestRouting.Domain.Enums;
using RequestRouting.Domain.ValueObjects;
using RequestRouting.Infrastructure.AzureSearch;
using RequestRouting.Infrastructure.Repositories;
using Xunit;
using Xunit.Abstractions;

namespace RequestRouting.UnitTests.Infrastructure.Repositories;

public class RequestIndexingRepositoryTests
{
    private readonly ICacheLogger _cacheLogger;
    private readonly Fixture _fixture;
    private readonly RequestIndexingRepository _repository;
    private readonly Mock<Response<RequestResponse>> _responseMock;
    private readonly Mock<SearchClient> _searchClient;

    public RequestIndexingRepositoryTests(ITestOutputHelper outputHelper)
    {
        _fixture = new Fixture();
        _cacheLogger = outputHelper.BuildLoggerFor<RequestIndexingRepository>();
        _searchClient = new Mock<SearchClient>();
        _searchClient
            .Setup(x => x.MergeOrUploadDocumentsAsync(It.IsAny<IEnumerable<RequestIndex>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>())).ReturnsAsync((Response<IndexDocumentsResult>)null);
        _searchClient.Setup(x => x.IndexName).Returns(SpecialtyEnum.Sleep.ToString);
        _repository =
            new RequestIndexingRepository((ILogger<RequestIndexingRepository>)_cacheLogger, _searchClient.Object);
        _responseMock = new Mock<Response<RequestResponse>>();
    }

    [Fact]
    public async Task UpsertAsync_UpsertsRquestWithAssignment_WhenAssignmentIsNotNull()
    {
        // arrange
        var request = _fixture.Create<RequestAggregate>();
        request.UpdateSpecialty(SpecialtyEnum.Sleep);
        request.UpdateAssignment("test@test.com", 20);

        // act
        await _repository.UpsertWithAssignmentAsync(request, DateTime.UtcNow);

        // assert
        _searchClient.Verify(
            x => x.MergeOrUploadDocumentsAsync(It.IsAny<IEnumerable<object>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpsertAsync_UpsertsRquestWithoutAssignment_WhenAssignmentIsNull()
    {
        // arrange
        var request = _fixture.Create<RequestAggregate>();
        request.UpdateSpecialty(SpecialtyEnum.Sleep);

        // act
        await _repository.UpsertWithoutAssignmentAsync(request, DateTime.UtcNow);

        _searchClient.Verify(
            x => x.MergeOrUploadDocumentsAsync(It.IsAny<IEnumerable<object>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpsertWithoutAssignmentAsync_LogsTwice_WhenInvoked()
    {
        // arrange
        var request = _fixture.Create<RequestAggregate>();
        request.UpdateSpecialty(SpecialtyEnum.Sleep);

        // act
        await _repository.UpsertWithoutAssignmentAsync(request, DateTime.UtcNow);

        _cacheLogger.Count.Should().Be(2);
    }

    [Fact]
    public async Task UpsertWithoutAssignmentAsync_LogsCustomFields_WhenInvoked()
    {
        // arrange
        var request = _fixture.Create<RequestAggregate>();
        request.UpdateSpecialty(SpecialtyEnum.Sleep);

        // act
        await _repository.UpsertWithoutAssignmentAsync(request, DateTime.UtcNow);

        var logEntry = _cacheLogger.Entries.FirstOrDefault(v =>
            v.Message.Contains("RequestIndexingRepository:UpsertWithoutAssignmentAsync started"));
        var scopeData = (Dictionary<string, object>)logEntry.Scopes.FirstOrDefault();
        scopeData["RequestForServiceKey"].Should().Be(request.RequestForServiceKey);
        scopeData["Specialty"].Should().Be(request.Specialty);
    }

    [Fact]
    public async Task UpsertWithAssignmentAsync_LogsTwice_WhenInvoked()
    {
        // arrange
        var request = _fixture.Create<RequestAggregate>();
        request.UpdateSpecialty(SpecialtyEnum.Sleep);

        // act
        await _repository.UpsertWithAssignmentAsync(request, DateTime.UtcNow);

        _cacheLogger.Count.Should().Be(2);
    }

    [Fact]
    public async Task UpsertWithAssignmentAsync_LogsCustomFields_WhenInvoked()
    {
        // arrange
        var request = _fixture.Create<RequestAggregate>();
        request.UpdateSpecialty(SpecialtyEnum.Sleep);

        // act
        await _repository.UpsertWithAssignmentAsync(request, DateTime.UtcNow);

        var logEntry = _cacheLogger.Entries.FirstOrDefault(v => 
            v.Message.Contains("RequestIndexingRepository:UpsertWithAssignmentAsync started"));
        var scopeData = (Dictionary<string, object>)logEntry.Scopes.FirstOrDefault();
        scopeData["RequestForServiceKey"].Should().Be(request.RequestForServiceKey);
        scopeData["Specialty"].Should().Be(request.Specialty);
    }

    [Fact]
    public async Task DeleteAsync_InvokesSearchClient_WhenCalled()
    {
        // arrange
        var request = _fixture.Create<RequestAggregate>();
        request.UpdateSpecialty(SpecialtyEnum.Sleep);

        // act
        await _repository.DeleteAsync(request, DateTime.UtcNow);

        _searchClient.Verify(
            x => x.DeleteDocumentsAsync(It.IsAny<IEnumerable<RequestIndex>>(), It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_LogsTwice_WhenInvoked()
    {
        // arrange
        var request = _fixture.Create<RequestAggregate>();
        request.UpdateSpecialty(SpecialtyEnum.Sleep);

        // act
        await _repository.DeleteAsync(request, DateTime.UtcNow);

        _cacheLogger.Count.Should().Be(2);
    }

    [Fact]
    public async Task DeleteAsync_LogsCustomFields_WhenInvoked()
    {
        // arrange
        var request = _fixture.Create<RequestAggregate>();
        request.UpdateSpecialty(SpecialtyEnum.Sleep);

        // act
        await _repository.DeleteAsync(request, DateTime.UtcNow);

        var logEntry = _cacheLogger.Entries.FirstOrDefault(v =>
            v.Message.Contains("RequestIndexingRepository:DeleteAsync Deleting Request from index started"));
        var scopeData = (Dictionary<string, object>)logEntry.Scopes.FirstOrDefault();
        scopeData["RequestForServiceKey"].Should().Be(request.RequestForServiceKey);
        scopeData["Specialty"].Should().Be(request.Specialty);
    }

    [Fact]
    public async Task GetAsync_LogsAtLeastTwice_WhenCompletedSuccessfully()
    {
        // arrange
        _responseMock.Setup(x => x.Value).Returns(new RequestResponse());
        _searchClient.Setup(x =>
            x.GetDocumentAsync<RequestResponse>(It.IsAny<string>(), It.IsAny<GetDocumentOptions>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(_responseMock.Object);

        await _repository.GetAsync(Guid.NewGuid());

        _cacheLogger.Entries.Count.Should().Be(2);
    }

    [Fact]
    public async Task GetAsync_CallsSearchClientOnce_WhenCalled()
    {
        // arrange
        _responseMock.Setup(x => x.Value).Returns(new RequestResponse());
        _searchClient.Setup(x =>
            x.GetDocumentAsync<RequestResponse>(It.IsAny<string>(), It.IsAny<GetDocumentOptions>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(_responseMock.Object);

        await _repository.GetAsync(Guid.NewGuid());

        _searchClient.Verify(
            x => x.GetDocumentAsync<RequestResponse>(It.IsAny<string>(), It.IsAny<GetDocumentOptions>(),
                It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenRequestIsNotFoundInAzureSearch()
    {
        // arrange
        var exception = new RequestFailedException(404, "not found");
        _searchClient.Setup(x =>
            x.GetDocumentAsync<RequestResponse>(It.IsAny<string>(), It.IsAny<GetDocumentOptions>(),
                It.IsAny<CancellationToken>())).ThrowsAsync(exception);

        var response = await _repository.GetAsync(Guid.NewGuid());

        response.Should().BeNull();
        var logEntry = _cacheLogger.Entries.FirstOrDefault(v =>
            v.Message.Contains("RequestIndexingRepository:GetAsync Request Not Found In Index"));
        logEntry.Should().NotBeNull();
    }

    [Fact]
    public async Task GetAsync_ThrowsException_WhenAzureSearchCallsFails()
    {
        // arrange
        var exception = new RequestFailedException(500, "not found");
        _searchClient.Setup(x =>
            x.GetDocumentAsync<RequestResponse>(It.IsAny<string>(), It.IsAny<GetDocumentOptions>(),
                It.IsAny<CancellationToken>())).ThrowsAsync(exception);

        Func<Task> act = () => _repository.GetAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<RequestFailedException>();
    }

    [Fact]
    public async Task GetAsync_ReThrowsException_WhenAnyExceptionOccurs()
    {
        // arrange
        _searchClient.Setup(x =>
            x.GetDocumentAsync<RequestResponse>(It.IsAny<string>(), It.IsAny<GetDocumentOptions>(),
                It.IsAny<CancellationToken>())).ThrowsAsync(new Exception());

        Func<Task> act = () => _repository.GetAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task UpsertAsync_UpsertsRquestWithoutAssignment_StateSSL_ConfiguredFalse()
    {
        // arrange
        var request = _fixture.Create<RequestAggregate>();
        request.UpdateSpecialty(SpecialtyEnum.Sleep);

        var lobs = new List<LineOfBusinessConfigurationObject>
        {
            new LineOfBusinessConfigurationObject(LineOfBusinessEnum.Commercial, false, null)
        };
        var stateConfig = new StateConfigurationAggregate("CA", lobs);
        request.UpdateJurisdictionState(stateConfig);

        // act
        await _repository.UpsertWithoutAssignmentAsync(request, DateTime.UtcNow);

        Assert.Equal(SslRequirementStatusEnum.SslNotRequired, request.SslRequirementStatus);
        _searchClient.Verify(
            x => x.MergeOrUploadDocumentsAsync(It.IsAny<IEnumerable<object>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpsertAsync_UpsertsRquestWithoutAssignment_StateSSL_ConfigureTrue()
    {
        // arrange
        var request = _fixture.Create<RequestAggregate>();
        request.UpdateSpecialty(SpecialtyEnum.Sleep);
        request.UpdateLineOfBusiness(LineOfBusinessEnum.Commercial);
        var lobs = new List<LineOfBusinessConfigurationObject>
        {
            new LineOfBusinessConfigurationObject(LineOfBusinessEnum.Commercial, true, null)
        };
        var stateConfig = new StateConfigurationAggregate("CA", lobs);
        request.UpdateJurisdictionState(stateConfig);

        // act
        await _repository.UpsertWithoutAssignmentAsync(request, DateTime.UtcNow);

        Assert.Equal(SslRequirementStatusEnum.SslNotRequired, request.SslRequirementStatus);
        _searchClient.Verify(
            x => x.MergeOrUploadDocumentsAsync(It.IsAny<IEnumerable<object>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpsertAsync_UpsertWithAssignmentAsync_StateSSL_ConfiguredFalse()
    {
        // arrange
        var request = _fixture.Create<RequestAggregate>();
        request.UpdateSpecialty(SpecialtyEnum.Sleep);

        var lobs = new List<LineOfBusinessConfigurationObject>
        {
            new LineOfBusinessConfigurationObject(LineOfBusinessEnum.Commercial, false, null)
        };
        var stateConfig = new StateConfigurationAggregate("CA", lobs);
        request.UpdateJurisdictionState(stateConfig);

        // act
        await _repository.UpsertWithAssignmentAsync(request, DateTime.UtcNow);

        Assert.Equal(SslRequirementStatusEnum.SslNotRequired, request.SslRequirementStatus);
        _searchClient.Verify(
            x => x.MergeOrUploadDocumentsAsync(It.IsAny<IEnumerable<object>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpsertAsync_UpsertWithAssignmentAsync_StateSSL_ConfigureTrue()
    {
        // arrange
        var request = _fixture.Create<RequestAggregate>();
        request.UpdateSpecialty(SpecialtyEnum.Sleep);
        request.UpdateLineOfBusiness(LineOfBusinessEnum.Commercial);
        var lobs = new List<LineOfBusinessConfigurationObject>
        {
            new LineOfBusinessConfigurationObject(LineOfBusinessEnum.Commercial, true, null)
        };
        var stateConfig = new StateConfigurationAggregate("CA", lobs);
        request.UpdateJurisdictionState(stateConfig);

        // act
        await _repository.UpsertWithAssignmentAsync(request, DateTime.UtcNow);

        Assert.Equal(SslRequirementStatusEnum.SslNotRequired, request.SslRequirementStatus);
        _searchClient.Verify(
            x => x.MergeOrUploadDocumentsAsync(It.IsAny<IEnumerable<object>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()), Times.Once);
    }
}

using Confluent.Kafka;
using Divergic.Logging.Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RequestRouting.Application.Configurations;
using RequestRouting.Application.Interface;
using RequestRouting.Domain.Constants;
using RequestRouting.Domain.Events;
using RequestRouting.Infrastructure.Kafka;
using RequestRouting.Observer.Observers;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace RequestRouting.UnitTests.Observer.Observers
{
    public class PhysicianDecisionMadeObserverTests
    {
        private readonly ICacheLogger _cacheLogger;
        private readonly PhysicianDecisionMadeObserver _observer;
        private readonly Mock<IKafkaConsumer> _kafkaConsumer;
        private readonly Mock<IOrchestrator> _orchestrator;
        private readonly Mock<IMessageValidator> _messageValidator;

        public PhysicianDecisionMadeObserverTests(ITestOutputHelper outputHelper)
        {
            var options = Options.Create(new AppSettings
            {
                EventReplayOffset = new Dictionary<string, int>() { { "UcxPhysicianDecisionMade", 1600 } },
                EventConfigs = new List<EventConfig>()
                {
                    new EventConfig{Name = "UcxPhysicianDecisionMade",
                    HandlerName = "UcxPhysicianDecisionMade",
                    Version = "1.0.0" }
                }
            });

            _cacheLogger = outputHelper.BuildLoggerFor<PhysicianDecisionMadeObserver>();
            _kafkaConsumer = new Mock<IKafkaConsumer>();

            _messageValidator = new Mock<IMessageValidator>();

            _orchestrator = new Mock<IOrchestrator>();
            _orchestrator.Setup(x => x.OrchestrateAsync(It.IsAny<PhysicianDecisionMadeEvent>(), It.IsAny<CancellationToken>())).Verifiable();

            _observer = new PhysicianDecisionMadeObserver((ILogger<PhysicianDecisionMadeObserver>)_cacheLogger,
                _kafkaConsumer.Object,
                _messageValidator.Object,
                _orchestrator.Object,
                options,
                TopicNames.PhysicianDecisionMade);
        }

        [Fact]
        public async Task HandleEventAsync_Valid()
        {
            // arrange
            var value = JsonSerializer.Serialize(CreatePhysicianDecisionMade());
            var consumeResult = new ConsumeResult<string, string>();
            var message = new Message<string, string>
            {
                Key = Guid.NewGuid().ToString(),
                Value = value
            };
            consumeResult.Message = message;
            consumeResult.Offset = 1500;
           
            _messageValidator.Setup(x => x.ValidatePlatformAndVersion(It.IsAny<PhysicianDecisionMadeEvent>(), It.IsAny<ILogger>())).Returns(true);
            _messageValidator.Setup(x => x.ValidateRequest(It.IsAny<PhysicianDecisionMadeEvent>(), It.IsAny<ILogger>())).Returns(true);

            // act
            await _observer.HandleEventAsync(consumeResult);

            // assert
            _orchestrator.Verify(x => x.OrchestrateAsync(It.IsAny<PhysicianDecisionMadeEvent>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }

        [Fact]
        public async Task HandleEventAsync_InvalidMessage_NeverCallsOrchestrateAsync()
        {
            // arrange
            var consumeResult = new ConsumeResult<string, string>();
            var message = new Message<string, string>
            {
                Key = Guid.NewGuid().ToString(),
                Value = JsonSerializer.Serialize(new object()),
            };
            consumeResult.Message = message;
            _messageValidator.Setup(x => x.ValidatePlatformAndVersion(It.IsAny<PhysicianDecisionMadeEvent>(), It.IsAny<ILogger>())).Returns(false);
            _messageValidator.Setup(x => x.ValidateRequest(It.IsAny<PhysicianDecisionMadeEvent>(), It.IsAny<ILogger>())).Returns(false);

            // act
            await _observer.HandleEventAsync(consumeResult);

            // assert
            _orchestrator.Verify(x => x.OrchestrateAsync(It.IsAny<PhysicianDecisionMadeEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task HandleEventAsync_NoMessage()
        {
            // arrange
            var consumeResult = new ConsumeResult<string, string>
            {
                Message = null
            };

            // act assert
            await Assert.ThrowsAsync<NullReferenceException>(() => _observer.HandleEventAsync(consumeResult));
        }

        private PhysicianDecisionMadeEvent CreatePhysicianDecisionMade()
        {
            var denial = new DenialEvent
            {
                MemberLanguage = "language",
                ProviderLanguage = "provider",
                ReasonCode = "reason code"
            };

            var user = new UserEvent
            {
                Email = "email@email.com",
                FirstName = "first",
                LastName = "last",
                Role = "role"
            };

            return new PhysicianDecisionMadeEvent
            {
                Key = Guid.NewGuid().ToString(),
                Name = TopicNames.PhysicianDecisionMade,
                OriginSystem = "eP",
                RequestForServiceId = $"r{Guid.NewGuid().ToString().Substring(0, 5)}",
                RequestForServiceKey = Guid.NewGuid().ToString(),
                Sent = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(5)),
                Version = "1.0.0",
                AuthorizationEndDate = DateTime.UtcNow,
                AuthorizationStartDate = DateTime.UtcNow,
                EventSystem = "system",
                Outcome = "PendingMDReview",
                User = user,
                AuthorizationKey = Guid.NewGuid().ToString(),
                Denial = denial
            };
        }
    }
}


using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Moq;
using Serilog.Core;
using Ucx.AclService.Erss.Repo.Service;
using UCX.AclService.Erss.Repo.Interface;
using Xunit;

namespace Ucx.AclService.Erss.Tests.Service
{
    public class CacheRepositoryTest
    {
        [Fact]
        public async Task AddToCacheTest()
        {
            var services = new ServiceCollection();
            services.AddDistributedMemoryCache();
            var provider = services.BuildServiceProvider();
            var cache = provider.GetService<IDistributedCache>();
            var mockRepo = new Mock<ICacheRepository>();
            var logger = Mock.Of<ILogger<CacheRepository>>();
            //Act
            CacheRepository repos = new CacheRepository(cache, logger);
            try
            {
                repos.AddToCache("KEY", "VALUE", "CATEGORY");
                Assert.True(true);
            }
            catch (Exception ex)
            {
                Assert.True(false);
            }

        }

        [Fact]
        public async Task GetFromCache_Returns_Value()
        {
            var services = new ServiceCollection();
            services.AddDistributedMemoryCache();
            var provider = services.BuildServiceProvider();
            var cache = provider.GetService<IDistributedCache>();
            var cacheService = new Mock<IDistributedCache>();
            var mockRepo = new Mock<ICacheRepository>();
            var logger = Mock.Of<ILogger<CacheRepository>>();
            CacheRepository repos = new CacheRepository(cache, logger);
            mockRepo.Setup(x => x.GetFromCache("testkey")).Returns(Task.FromResult("testvalue"));
            //Act
            var result = repos.GetFromCache("testkey");

            Assert.NotNull(result);
        }

        [Fact]
        public async Task GetFromCache_Returns_Value_Null()
        {
            var services = new ServiceCollection();
            services.AddDistributedMemoryCache();
            var provider = services.BuildServiceProvider();
            var cache = provider.GetService<IDistributedCache>();
            var mockRepo = new Mock<ICacheRepository>();
            var cacheService = new Mock<IDistributedCache>();
            var logger = Mock.Of<ILogger<CacheRepository>>();
            CancellationToken token = new CancellationToken();
            CacheRepository repos = new CacheRepository(cache, logger);
            mockRepo.Setup(x => x.GetFromCache(null)).Returns(Task.FromResult<string>(null));
            //Act
            var result = await repos.GetFromCache("");
            //Asert
            Assert.Null(result);
        }
    }
}


using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ucx.AclService.Erss.Dto.Entity;
using Ucx.AclService.Erss.Dto.Event.CaseAssignmentChangedIOne;
using UCX.AclService.Erss.Repo.Interface;
using Ucx.AclService.Erss.Services.Implement.CaseAssignmentChangeIOne;
using Ucx.AclService.Erss.Services.Interface;
using Microsoft.Extensions.Configuration;
using Ucx.AclService.Erss.Dto.Event.Erss;
using Ucx.AclService.Erss.Services.Implement;
using Xunit;
using System.Reactive;
using Newtonsoft.Json;
using evicore.gravity.common.Service;
using Microsoft.Graph;
using File = System.IO.File;
using Microsoft.Extensions.DependencyInjection;
using Ucx.AclService.Erss.Domain.Models;
using evicore.gravity.common.Interface;
using ucx.messages.events;

namespace Ucx.AclService.Erss.Tests.ServiceTests
{
    public class CaseAssignmentChangeIOneServiceTests
    {
        private readonly CaseAssignmentChangedIOneEvent message;
        private readonly Mock<ICacheRepository> cacheRepositoryMock;
        private readonly Mock<IGetUserDetailsService> getUserDetailsServiceMock;
        private readonly Mock<evicore.gravity.common.Interface.ICosmosDbService<ErssRecord>> cosmosDbServiceMock;
        private readonly Mock<ILogger<CaseAssignmentChangeIOneService>> loggerMock;
        private readonly Mock<IProducer<string, string>> producerMock;
        private IConfigurationRoot _configuration;
        private readonly Mock<IConfiguration> _configMock;
        private CaseAssignmentChangeIOneService sut;
        private string _messageText;
        private CaseAssignmentChangedIOneEvent messageObj;
        private Mock<IMessageSenderService<Guid, UCXRoutingRequestAssigned>> messagesendermock;
        Mock<evicore.gravity.common.Interface.ICosmosDbService<ErssRecord>> cosmosDbService;
        Mock<ICacheRepository> cacheRepository;
        Mock<evicore.gravity.common.Interface.ICosmosDbService<CaseAssignmentChangedIOneAuditLog>> auditLogRepo;

        public CaseAssignmentChangeIOneServiceTests()
        {
            // Arrange
            var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EventData", "CaseAssignmentChangedIOne.json");
            _messageText = File.ReadAllText(filePath);
            messageObj = JsonConvert.DeserializeObject<CaseAssignmentChangedIOneEvent>(_messageText);

            cacheRepositoryMock = new Mock<ICacheRepository>();
            getUserDetailsServiceMock = new Mock<IGetUserDetailsService>();
            cosmosDbServiceMock = new Mock<evicore.gravity.common.Interface.ICosmosDbService<ErssRecord>>();
            loggerMock = new Mock<ILogger<CaseAssignmentChangeIOneService>>();
            producerMock = new Mock<IProducer<string, string>>();
            auditLogRepo = new Mock<evicore.gravity.common.Interface.ICosmosDbService<CaseAssignmentChangedIOneAuditLog>>();
            //_configMock = new Mock<IConfiguration>();
            _configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.test.json")
            .Build();
            messagesendermock=new Mock<IMessageSenderService<Guid, UCXRoutingRequestAssigned>>();

            sut = new CaseAssignmentChangeIOneService(loggerMock.Object, getUserDetailsServiceMock.Object, _configuration,
                cosmosDbServiceMock.Object, cacheRepositoryMock.Object, auditLogRepo.Object,
                messagesendermock.Object);
        }

        [Fact]
        public async Task ProcessMessage_WithInvalidEpisodeId_ShouldSkip()
        {
            // Arrange
            CaseAssignmentChangedIOneEvent message = messageObj;
            message.data.EpisodeID = null;
            message.data.QueueOwner = "test";
            // Act
            await sut.ProcessMessage(message);

            // Assert
            //loggerMock.Verify(x => x.LogDebug("CaseAssignmentChangedIOneEvent with invalid EpisodeId or QueueOwner - skipping"), Times.Once);
            loggerMock.Verify(l =>
                    l.Log(
                        LogLevel.Debug,
                        It.IsAny<EventId>(),
                        It.Is<It.IsAnyType>((state, type) => state.ToString().Contains("CaseAssignmentChangedIOneEvent with invalid EpisodeId or QueueOwner - skipping")),
                        It.IsAny<Exception>(),
                        (Func<object, Exception, string>)It.IsAny<object>()
                        ), Times.Once, "Verification didnt pass");
        }

        [Fact]
        public async Task ProcessMessage_WithInvalidQueueOwner_ShouldSkip()
        {
            // Arrange
            CaseAssignmentChangedIOneEvent message = messageObj;
            message.data.EpisodeID = "123";
            message.data.QueueOwner = null;

            // Act
            await sut.ProcessMessage(message);

            // Assert
            //loggerMock.Verify(x => x.LogDebug("CaseAssignmentChangedIOneEvent with invalid EpisodeId or QueueOwner - skipping"), Times.Once);
            loggerMock.Verify(l =>
                    l.Log(
                        LogLevel.Debug,
                        It.IsAny<EventId>(),
                        It.Is<It.IsAnyType>((state, type) => state.ToString().Contains("CaseAssignmentChangedIOneEvent with invalid EpisodeId or QueueOwner - skipping")),
                        It.IsAny<Exception>(),
                        (Func<object, Exception, string>)It.IsAny<object>()
                        ), Times.Once, "Verification didnt pass");
        }

        [Fact]
        public async Task ProcessMessage_WithExcludedQueueOwner_ShouldSkip()
        {
            // Arrange
            CaseAssignmentChangedIOneEvent message = messageObj;
            message.data.EpisodeID = "123";
            message.data.QueueOwner = "HOLD_OUTREACH_QUEUE";

            // Act
            await sut.ProcessMessage(message);

            // Assert
            //loggerMock.Verify(x => x.LogDebug("CaseAssignmentChangedIOneEvent with invalid EpisodeId or QueueOwner - skipping"), Times.Once);
            loggerMock.Verify(l =>
                    l.Log(
                        LogLevel.Debug,
                        It.IsAny<EventId>(),
                        It.Is<It.IsAnyType>((state, type) => state.ToString().Contains("CaseAssignmentChangedIOneEvent with invalid EpisodeId or QueueOwner - skipping")),
                        It.IsAny<Exception>(),
                        (Func<object, Exception, string>)It.IsAny<object>()
                        ), Times.Once, "Verification didnt pass");
        }

        [Fact]
        public async Task ProcessMessage_WithMissingUserEmail_ShouldSkip()
        {
            // Arrange
            CaseAssignmentChangedIOneEvent message = messageObj;
            message.data.EpisodeID = "123";
            message.data.QueueOwner = "test";
            getUserDetailsServiceMock.Setup(x => x.GetUserAsync("test")).ReturnsAsync((User)null);
            sut = new CaseAssignmentChangeIOneService(loggerMock.Object, getUserDetailsServiceMock.Object, _configuration,
            cosmosDbServiceMock.Object, cacheRepositoryMock.Object, auditLogRepo.Object, messagesendermock.Object);

            // Act
            await sut.ProcessMessage(message);

            // Assert
            //loggerMock.Verify(x => x.LogDebug("CaseAssignmentChangedIOneEvent QueueOwner is not present in UCX - skipping"), Times.Once);
            loggerMock.Verify(l =>
                    l.Log(
                        LogLevel.Debug,
                        It.IsAny<EventId>(),
                        It.Is<It.IsAnyType>((state, type) => state.ToString().Contains("CaseAssignmentChangedIOneEvent QueueOwner is not present in UCX - skipping")),
                        It.IsAny<Exception>(),
                        (Func<object, Exception, string>)It.IsAny<object>()
                        ), Times.Once, "Verification didnt pass");
        }

        [Fact]
        public async Task ProcessMessage_WithUnAssignmentQueueOwner_ShouldProduceUnAssignmentMessage()
        {
            // Arrange
            CaseAssignmentChangedIOneEvent message = messageObj;
            message.data.EpisodeID = "123";
            message.data.QueueOwner = "RECONSIDERATION";
            cosmosDbServiceMock.Setup(x => x.ReadItemAsync(message.data.EpisodeID, message.data.EpisodeID, CancellationToken.None))
            .ReturnsAsync(new ErssRecord
            {
                Erss = new Erss<FlattenedServiceRequest>
                {
                    Episode = new Episode<FlattenedServiceRequest>
                    {
                        ServiceRequest = new FlattenedServiceRequest
                        {
                            PrimaryMedicalDisciplineName = "Sleep",
                            RequestForServiceKey = Guid.NewGuid()
                        }
                    }
                }
            }
                    );
            sut = new CaseAssignmentChangeIOneService(loggerMock.Object, getUserDetailsServiceMock.Object, _configuration,
            cosmosDbServiceMock.Object, cacheRepositoryMock.Object, auditLogRepo.Object, messagesendermock.Object);

            // Act
            await sut.ProcessMessage(message);

            // Assert
            messagesendermock.Verify(x => x.SendAsync(It.IsAny<Guid>(), It.Is<ucx.messages.events.UCXRoutingRequestAssigned>(x=>x.UnAssigned==true && x.AssignedToId==null)),
                Times.Once);
        }
        [Fact]
        public async Task ProcessMessage_NoCaseDetailsPresentinUCX_ShouldSkip()
        {
            // Arrange
            CaseAssignmentChangedIOneEvent message = messageObj;
            message.data.EpisodeID = "InValidEpisodeId";
            message.data.QueueOwner = "validId";
            getUserDetailsServiceMock.Setup(x => x.GetUserAsync(message.data.QueueOwner)).ReturnsAsync(new User { UserPrincipalName = "xyz@xyz.com" });
            cosmosDbServiceMock.Setup(x => x.ReadItemAsync(message.data.EpisodeID, message.data.EpisodeID, CancellationToken.None))
            .ReturnsAsync((ErssRecord)null);
            sut = new CaseAssignmentChangeIOneService(loggerMock.Object, getUserDetailsServiceMock.Object, _configuration,
            cosmosDbServiceMock.Object,cacheRepositoryMock.Object, auditLogRepo.Object, messagesendermock.Object);

            // Act
            await sut.ProcessMessage(message);

            // Assert
            //loggerMock.Verify(x => x.LogDebug("CaseAssignmentChangedIOneEvent Case detail is not present in UCX - skipping"), Times.Once);
            loggerMock.Verify(l =>
                    l.Log(
                        LogLevel.Debug,
                        It.IsAny<EventId>(),
                        It.Is<It.IsAnyType>((state, type) => state.ToString().Contains("CaseAssignmentChangedIOneEvent Case detail is not present in UCX - skipping")),
                        It.IsAny<Exception>(),
                        (Func<object, Exception, string>)It.IsAny<object>()
                        ), Times.Once, "Verification didnt pass");
        }
        [Fact]
        public async Task ProcessMessage_WithValidInput_ShouldProduceMessage()
        {
            // Arrange
            CaseAssignmentChangedIOneEvent message = messageObj;
            message.data.EpisodeID = "kz82g5wwv7";// allowed program case
            message.data.QueueOwner = "Alex.Politsmakher@evicore.com";
            getUserDetailsServiceMock.Setup(x => x.GetUserAsync(message.data.QueueOwner)).ReturnsAsync(new User { UserPrincipalName = "Alex.Politsmakher@evicore.com" });
            cosmosDbServiceMock.Setup(x => x.ReadItemAsync(message.data.EpisodeID, message.data.EpisodeID, CancellationToken.None))
            .ReturnsAsync(new ErssRecord
            {
                Erss = new Erss<FlattenedServiceRequest>
                {
                    Episode = new Episode<FlattenedServiceRequest>
                    {
                        ServiceRequest = new FlattenedServiceRequest
                        {
                            PrimaryMedicalDisciplineName = "Sleep",
                            RequestForServiceKey = Guid.NewGuid()
                        }
                    }
                }
            }
                    );

            messagesendermock.Setup(x => x.SendAsync(It.IsAny<Guid>(), It.IsAny<ucx.messages.events.UCXRoutingRequestAssigned>()))
                .Returns(Task.CompletedTask);

            sut = new CaseAssignmentChangeIOneService(loggerMock.Object, getUserDetailsServiceMock.Object, _configuration,
            cosmosDbServiceMock.Object, cacheRepositoryMock.Object, auditLogRepo.Object, messagesendermock.Object);

            // Act
            await sut.ProcessMessage(message);

            // Assert
            messagesendermock.Verify(x => x.SendAsync(It.IsAny<Guid>(), It.Is<ucx.messages.events.UCXRoutingRequestAssigned>(x=>x.Version=="1.1.0")), Times.Once);

        }
        [Fact]
        public async Task ProcessMessage_WithValidDisciplineInput_ShouldProduceMessage()
        {
            // Arrange
            CaseAssignmentChangedIOneEvent message = messageObj;
            message.data.EpisodeID = "kz82g5wwv7";// allowed program case
            message.data.QueueOwner = "Alex.Politsmakher@evicore.com";
            getUserDetailsServiceMock.Setup(x => x.GetUserAsync(message.data.QueueOwner)).ReturnsAsync(new User { UserPrincipalName = "Alex.Politsmakher@evicore.com" });
            cosmosDbServiceMock.Setup(x => x.ReadItemAsync(message.data.EpisodeID, message.data.EpisodeID, CancellationToken.None))
            .ReturnsAsync(new ErssRecord
            {
                Erss = new Erss<FlattenedServiceRequest>
                {
                    Episode = new Episode<FlattenedServiceRequest>
                    {
                        ServiceRequest = new FlattenedServiceRequest
                        {
                            PrimaryMedicalDisciplineName = "MSK",
                            RequestForServiceKey = Guid.NewGuid()
                        }
                    }
                }
            }
                    );

            messagesendermock.Setup(x => x.SendAsync(It.IsAny<Guid>(), It.IsAny<ucx.messages.events.UCXRoutingRequestAssigned>()))
                .Returns(Task.CompletedTask);

            sut = new CaseAssignmentChangeIOneService(loggerMock.Object, getUserDetailsServiceMock.Object, _configuration,
            cosmosDbServiceMock.Object, cacheRepositoryMock.Object, auditLogRepo.Object, messagesendermock.Object);

            // Act
            await sut.ProcessMessage(message);

            // Assert
            messagesendermock.Verify(x => x.SendAsync(It.IsAny<Guid>(), It.Is<ucx.messages.events.UCXRoutingRequestAssigned>(x => x.Version == "1.1.0")), Times.Once);

        }
        [Fact]
        public async Task ProcessMessage_WithGenericQueueOwner_ShouldProduceUnAssignmentMessage()
        {
            // Arrange
            CaseAssignmentChangedIOneEvent message = messageObj;
            var queOwners = new List<string> { "CARECORE CARDIOLOGY",
                    "HP PORTAL VIEW",
                    "NURSE WORK QUEUE",
                    "INTENT TO DENY",
                    "APPEALS",
                    "AwaitingHPDecision",
                    "MD APPEALS",
                    "APPEALS OTHER",
                    "RECON EVALUATION",
                    "APPEALS LEVEL2",
                    "PHYS MED APPEALS",
                    "PAC APPEALS" };
            foreach (var queOwner in queOwners)
            {
                message.data.EpisodeID = "123";
                message.data.QueueOwner = queOwner;
                cosmosDbServiceMock.Setup(x => x.ReadItemAsync(message.data.EpisodeID, message.data.EpisodeID, CancellationToken.None))
                .ReturnsAsync(new ErssRecord
                {
                    Erss = new Erss<FlattenedServiceRequest>
                    {
                        Episode = new Episode<FlattenedServiceRequest>
                        {
                            ServiceRequest = new FlattenedServiceRequest
                            {
                                PrimaryMedicalDisciplineName = "Cardiology",
                                RequestForServiceKey = Guid.NewGuid()
                            }
                        }
                    }
                });
                sut = new CaseAssignmentChangeIOneService(loggerMock.Object, getUserDetailsServiceMock.Object, _configuration,
                cosmosDbServiceMock.Object, cacheRepositoryMock.Object, auditLogRepo.Object, messagesendermock.Object);
                // Act
                await sut.ProcessMessage(message);
                // Assert
                messagesendermock.Verify(x => x.SendAsync(It.IsAny<Guid>(), It.Is<UCXRoutingRequestAssigned>(x => x.UnAssigned == true && x.AssignedToId == null)),
                    Times.Once);
                messagesendermock.Invocations.Clear();
            }
        }
    }
}


using System.Net;
using Confluent.Kafka;
using evicore.eventsource.cosmos.Extensions;
using evicore.eventsource.cosmos.Model;
using evicore.eventsource.library.extensions;
using evicore.gravity.common.Interface;
using Microsoft.AspNetCore.Builder;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using StackExchange.Redis;
using Ucx.AclService.Erss.App.Configuration;
using Ucx.AclService.Erss.Domain.Models;
using Ucx.AclService.Erss.Dto.Event.Erss;
using Ucx.AclService.Erss.Dto.Event.XplErssCaseEnrichedIone;
using UCX.AclService.Erss.Repo.Interface;
using Ucx.AclService.Erss.Services.Configuration;
using Ucx.AclService.Erss.Services.Implement;
using Ucx.AclService.Erss.Services.Interface;
using Ucx.AclService.Erss.Services.models;
using ucx.messages.events;
using Xunit;

namespace Ucx.AclService.Erss.Tests.Configuration;

public class KafkaConfigurationTests
{
    [Fact]
    public void AddConsumersTest()
    {
        var builder = CreateConfiguredBuilder();
        builder.Services.AddAutoMapper(cfg => cfg.AddProfile<MappingProfile>());
        builder.Services.AddEventSource<DiagnosisState>();
        builder.Services.AddEventSource<ErssCaseImageOneState>();
        var cosmosSettings = new CosmosSettings
        {
            Endpoint = "https://test.endpoint/",
            Key = Convert.ToBase64String("Key"u8.ToArray()),
            DatabaseName = "laskjf",
            RetryLockLimit = 1
        };
        var cosmosContainerNames = new CosmosContainerNames
        {
            State = "laksdj",
            EventSourceMessages = "lskdfj"
        };
        builder.Services.AddEventSourceRepositoryCosmos<DiagnosisState>(cosmosSettings, cosmosContainerNames);
        builder.Services.AddEventSourceRepositoryCosmos<ErssCaseImageOneState>(cosmosSettings, cosmosContainerNames);
        builder.Services.AddSingleton(x => new Mock<IImageOneRepository>().Object);
        builder.Services.AddSingleton<ITranslate<XplErssFinalEvent<ServiceRequest>, IEnumerable<XplErssFinalEvent<FlattenedServiceRequest>>>, XplErssFinalTranslation>();
        builder.Services.AddSingleton(_ => new Mock<IOrchestrationService<XplErssFinalEvent<ServiceRequest>>>().Object);
        builder.Services.AddSingleton(() =>
            new Mock<ITranslate<XplErssFinalHistoryEvent<ServiceRequest>, IEnumerable<XplErssFinalHistoryEvent<FlattenedServiceRequest>>>>().Object);
        builder.AddProducers().AddKafkaDeadLetter().AddLocking(null, new Mock<IConnectionMultiplexer>().Object);

        var app = builder.AddConsumers().Build();

        Assert.NotNull(app.Services.GetService<IMessageConsumerService<XplErssCaseEnrichedIone>>());
        Assert.NotNull(app.Services.GetService<IMessageConsumerService<XplAmsFileCreated>>());
        Assert.NotNull(app.Services.GetService<IMessageConsumerService<XplErssFinalEventEventSource<ServiceRequest>>>());
        Assert.NotNull(app.Services.GetService<IMessageConsumerService<DiagnosisMessage>>());
        Assert.NotNull(app.Services.GetService<IMessageConsumerService<UcxProcedureUpdated>>());
        Assert.NotNull(app.Services.GetService<IMessageConsumerService<XplErssFinalHistoryEvent<ServiceRequest>>>());
        Assert.NotNull(app.Services.GetService<IMessageConsumerService<XplErssCaseEnrichedIoneHistory>>());
        Assert.NotNull(app.Services.GetService<IMessageConsumerService<UcxMedicalDisciplineCorrected>>());
    }

    [Fact]
    public void TestStartConsumers()
    {
        var builder = CreateConfiguredBuilder();
        var mockXplErssCaseEnrichedIone = new Mock<IMessageConsumerService<XplErssCaseEnrichedIone>>();
        var mockXplAmsFileCreated = new Mock<IMessageConsumerService<XplAmsFileCreated>>();
        var mockXplErssFinalEventEventSource = new Mock<IMessageConsumerService<XplErssFinalEventEventSource<ServiceRequest>>>();
        var mockXplErssFinalHistoryEvent = new Mock<IMessageConsumerService<XplErssFinalHistoryEvent<ServiceRequest>>>();
        var mockXplErssCaseEnrichedIoneHistory = new Mock<IMessageConsumerService<XplErssCaseEnrichedIoneHistory>>();
        var mockDiagnosisMessage = new Mock<IMessageConsumerService<DiagnosisMessage>>();
        var mockUcxProcedureUpdated = new Mock<IMessageConsumerService<UcxProcedureUpdated>>();
        var mockUcxMedicalDisciplineCorrected = new Mock<IMessageConsumerService<UcxMedicalDisciplineCorrected>>();
        builder.Services.AddSingleton(mockXplErssCaseEnrichedIone.Object);
        builder.Services.AddSingleton(mockXplAmsFileCreated.Object);
        builder.Services.AddSingleton(mockXplErssFinalEventEventSource.Object);
        builder.Services.AddSingleton(mockXplErssFinalHistoryEvent.Object);
        builder.Services.AddSingleton(mockXplErssCaseEnrichedIoneHistory.Object);
        builder.Services.AddSingleton(mockDiagnosisMessage.Object);
        builder.Services.AddSingleton(mockUcxProcedureUpdated.Object);
        builder.Services.AddSingleton(mockUcxMedicalDisciplineCorrected.Object);

        builder.Build().StartConsumers();

        mockXplErssCaseEnrichedIone.Verify(service => service.StartConsumingAllNoMessagePayload(), Times.Once);
        mockXplAmsFileCreated.Verify(service => service.StartConsumingAllNoMessagePayload(), Times.Once);
        mockXplErssFinalEventEventSource.Verify(service => service.StartConsumingAllNoMessagePayload(), Times.Once);
        mockXplErssFinalHistoryEvent.Verify(service => service.StartConsumingAllNoMessagePayload(), Times.Once);
        mockXplErssCaseEnrichedIoneHistory.Verify(service => service.StartConsumingAllNoMessagePayload(), Times.Once);
        mockDiagnosisMessage.Verify(service => service.StartConsumingAllNoMessagePayload(), Times.Once);
        mockUcxProcedureUpdated.Verify(service => service.StartConsuming(It.Is<string>(name => name == nameof(UcxProcedureUpdated)), It.Is<string>(version => version == "1.2.1")),
            Times.Once);
        mockUcxMedicalDisciplineCorrected.Verify(
            service => service.StartConsuming(It.Is<string>(name => name == nameof(UcxMedicalDisciplineCorrected)), It.Is<string>(version => version == "1.0.0")), Times.Once);
    }

    [Fact]
    public void TestAddProducers()
    {
        var app = CreateConfiguredBuilder().AddProducers().Build();

        Assert.NotNull(app.Services.GetRequiredService<IMessageSenderService<Guid, UcxImageOneQualifiersUpdated>>());
        Assert.NotNull(app.Services.GetRequiredService<IMessageSenderService<Guid, UcxWebClinicalUploaded>>());
        Assert.NotNull(app.Services.GetRequiredService<IMessageSenderService<Guid, UcxJurisdictionStateUpdated>>());
        Assert.NotNull(app.Services.GetRequiredService<IMessageSenderService<Guid, UcxExternalSystemStatusChange>>());
        Assert.NotNull(app.Services.GetRequiredService<IMessageSenderService<Guid, UCXRequestForServiceSubmitted>>());
        Assert.NotNull(app.Services.GetRequiredService<IMessageSenderService<Guid, UCXDueDateCalculated>>());
        Assert.NotNull(app.Services.GetRequiredService<IMessageSenderService<Guid, UcxRequestUrgencyUpdated>>());
        Assert.NotNull(app.Services.GetRequiredService<IMessageSenderService<Guid, UcxDueDateMissed>>());
        Assert.NotNull(app.Services.GetRequiredService<IMessageSenderService<Guid, UcxRequestLineOfBusinessUpdated>>());
        Assert.NotNull(app.Services.GetRequiredService<IMessageSenderService<Guid, UcxProcedureUpdated>>());
        Assert.NotNull(app.Services.GetRequiredService<IMessageSenderService<Guid, UCXRequestSpecialtyUpdated>>());
        Assert.NotNull(app.Services.GetRequiredService<IMessageSenderService<Guid, UcxDiagnosesUpdated>>());
        Assert.NotNull(app.Services.GetRequiredService<IMessageSenderService<Guid, UcxHistoricalRequestForServiceSubmitted>>());
        Assert.NotNull(app.Services.GetRequiredService<IMessageSenderService<Guid, UcxHistoricalImageOneQualifiersUpdated>>());
        Assert.NotNull(app.Services.GetRequiredService<IMessageSenderService<Guid, UcxHistoricalProcedureUpdated>>());
        Assert.NotNull(app.Services.GetRequiredService<IMessageSenderService<Guid, UCXRoutingRequestAssigned>>());
        Assert.NotNull(app.Services.GetRequiredService<IMessageSenderService<Guid, UCXMemberInfoUpdatedForRequest>>());
        Assert.NotNull(app.Services.GetRequiredService<IMessageSenderService<Guid, UcxMedicalDisciplineCorrected>>());
    }

    private static WebApplicationBuilder CreateBuilder()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddApplicationInsightsTelemetry(options => { options.EnableAdaptiveSampling = false; });
        return builder;
    }

    private static WebApplicationBuilder CreateConfiguredBuilder()
    {
        var builder = CreateBuilder();
        builder.Configuration["Observers:ErssThrottleMs"] = "1";
        builder.Configuration["Observers:LookupThrottleMs"] = "0";
        builder.Configuration["Observers:ErssCaseEnrichedIoneMs"] = "0";
        builder.Configuration["Observers:ErssQualifiersThrottleMs"] = "0";
        builder.Configuration["Observers:XplAmsFileCreatedThrottleMs"] = "0";
        builder.Configuration["Kafka:BootstrapServers"] = "BootstrapServers";
        builder.Configuration["Kafka:SaslUsername"] = "SaslUsername";
        builder.Configuration["Kafka:SaslPassword"] = "SaslPassword";
        builder.Configuration["Kafka:SslCaLocation"] = "certs/CARoot.crt";

        builder.Configuration["Cosmos:DatabaseName"] = "test";
        builder.Configuration["Cosmos:Endpoint"] = "https://test.com";
        builder.Configuration["Cosmos:Key"] = "test";
        builder.Configuration["Cosmos:DeadLetterContainerName"] = "test";
        return builder;
    }
}


using Xunit;
using System.Text;
using evicore.eventsource.cosmos.Model;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ucx.AclService.Erss.Services.Configuration;

namespace Ucx.AclService.Erss.App.Configuration.Tests;

    public class CosmosConfigurationTests
    {
        [Fact]
        public void RegisterErssDependency_CosmosNotPresent_Test()
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                CosmosConfiguration.RegisterErssDependency(new ConfigurationBuilder().Build(),
                    new ServiceCollection()));
            Assert.Equal("Cosmos not in appsettings", exception.Message);
        }

        [Theory]
        [InlineData("Cosmos:DatabaseName")]
        [InlineData("Cosmos:Endpoint")]
        [InlineData("Cosmos:Key")]
        [InlineData("Cosmos:ErssContainer")]
        public void RegisterDependency_Cosmos_Settings_NotPresent_Test(string Key)
        {
            //Arrange
            var config = new Dictionary<string, string>
            {
                { "Cosmos:DatabaseName", "DatabaseName" },
                { "Cosmos:Endpoint", "https://test.endpoint/" },
                { "Cosmos:Key", Convert.ToBase64String(Encoding.UTF8.GetBytes("Key")) },
                { "Cosmos:ErssContainer", "ContainerName" }
            };

            config.Remove(Key);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(config)
                .Build();
            var services = new ServiceCollection();

            //Act
            var exception = Assert.Throws<InvalidOperationException>(() =>
                CosmosConfiguration.RegisterErssDependency(configuration, services));

            //Assert
            Assert.Equal($"{Key} not in appsettings", exception.Message);
        }

        // [Fact]
        // public async Task SetupLocalContainers()
        // {
        //     var cosmosSettings = new CosmosSettings
        //     {
        //         Endpoint = "https://localhost:8081",
        //         DatabaseName = "ucxaclserviceerssdb",
        //         Key = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==",
        //         RetryLockLimit = 20
        //     };
        //     var cosmosClient = new CosmosClient(cosmosSettings.Endpoint, cosmosSettings.Key, cosmosSettings.CosmosClientOptions);
        //     await cosmosClient.CreateDatabaseIfNotExistsAsync(cosmosSettings.DatabaseName);
        //     var database = cosmosClient.GetDatabase(cosmosSettings.DatabaseName);
        //     await database.CreateContainerIfNotExistsAsync(new ContainerProperties
        //     {
        //         Id = "AuditLog",
        //         PartitionKeyPath = "/lookupKey"
        //     });
        //     await database.CreateContainerIfNotExistsAsync(new ContainerProperties
        //     {
        //         Id = "DeadLetter",
        //         PartitionKeyPath = "/id"
        //     });
        //     await database.CreateContainerIfNotExistsAsync(new ContainerProperties
        //     {
        //         Id = "DiagnosisMessages",
        //         PartitionKeyPath = "/stateIds/id"
        //     });
        //     await database.CreateContainerIfNotExistsAsync(new ContainerProperties
        //     {
        //         Id = "DiagnosisState",
        //         PartitionKeyPath = "/id"
        //     });
        //     await database.CreateContainerIfNotExistsAsync(new ContainerProperties
        //     {
        //         Id = "ErssFinal",
        //         PartitionKeyPath = "/id"
        //     });
        //     await database.CreateContainerIfNotExistsAsync(new ContainerProperties
        //     {
        //         Id = "EventSourceMessages",
        //         PartitionKeyPath = "/stateIds/id "
        //     });
        //     await database.CreateContainerIfNotExistsAsync(new ContainerProperties
        //     {
        //         Id = "EventSourceState",
        //         PartitionKeyPath = "/id"
        //     });
        //     await database.CreateContainerIfNotExistsAsync(new ContainerProperties
        //     {
        //         Id = "Qualifier",
        //         PartitionKeyPath = "/id"
        //     });
        //     await database.CreateContainerIfNotExistsAsync(new ContainerProperties
        //     {
        //         Id = "RequestLookUp",
        //         PartitionKeyPath = "/id"
        //     });
        //     await database.CreateContainerIfNotExistsAsync(new ContainerProperties
        //     {
        //         Id = "ServicingProviderInfo",
        //         PartitionKeyPath = "/id"
        //     });
        //     await database.CreateContainerIfNotExistsAsync(new ContainerProperties
        //     {
        //         Id = "ServicingProviderInfoV2",
        //         PartitionKeyPath = "/id"
        //     });
        // }
    }




Thanks and Regards
Siraj

From: R, Sirajudeen (CTR) <Sirajudeen.R@evicore.com> 
Sent: Wednesday, August 21, 2024 6:18 PM
To: R, Sirajudeen (CTR) <Sirajudeen.R@evicore.com>
Subject: xt



Thanks and Regards
Siraj

