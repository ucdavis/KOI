using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AggieEnterpriseApi.Validation;
using Azure.Core.Serialization;
using Koi.Functions.Financial;
using Koi.Functions.Financial.Models;
using Koi.Functions.Financial.Services;
using Koi.Functions.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace Koi.Functions.Tests.Financial;

public sealed class FinancialFunctionTests(ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false
    };

    [Theory]
    [InlineData(
        nameof(FinancialFunction.Run),
        FinancialFunction.FunctionName,
        "get",
        "v1/financial/details/{value}")]
    [InlineData(
        nameof(FinancialFunction.RunBulk),
        FinancialFunction.BulkFunctionName,
        "post",
        "v1/financial/details")]
    [InlineData(
        nameof(FinancialFunction.RunFullDetails),
        FinancialFunction.FullDetailsFunctionName,
        "get",
        "v1/financial/full-details/{value}")]
    [InlineData(
        nameof(FinancialFunction.RunBulkFullDetails),
        FinancialFunction.BulkFullDetailsFunctionName,
        "post",
        "v1/financial/full-details")]
    [InlineData(
        nameof(FinancialFunction.RunValidation),
        FinancialFunction.ValidationFunctionName,
        "get",
        "v1/financial/validate/{value}")]
    [InlineData(
        nameof(FinancialFunction.RunBulkValidation),
        FinancialFunction.BulkValidationFunctionName,
        "post",
        "v1/financial/validate")]
    public void FinancialRoutesAreStable(
        string methodName,
        string expectedFunctionName,
        string expectedHttpMethod,
        string expectedRoute)
    {
        var method = typeof(FinancialFunction).GetMethod(methodName);
        Assert.NotNull(method);

        var function = method.GetCustomAttribute<FunctionAttribute>();
        Assert.Equal(expectedFunctionName, function?.Name);

        var requestParameter = method.GetParameters()
            .Single(parameter => parameter.ParameterType == typeof(HttpRequestData));
        var trigger = requestParameter.GetCustomAttribute<HttpTriggerAttribute>();
        Assert.NotNull(trigger);
        Assert.Equal(expectedRoute, trigger.Route);
        var methods = trigger.Methods ?? [];
        Assert.Contains(
            methods,
            method => string.Equals(method, expectedHttpMethod, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(true, FinancialChartStringType.Gl, "GL", "This is a valid GL chart string.")]
    [InlineData(true, FinancialChartStringType.Ppm, "PPM", "This is a valid PPM chart string.")]
    [InlineData(false, FinancialChartStringType.Gl, "GL", "This is not a valid chart string.")]
    [InlineData(false, FinancialChartStringType.Ppm, "PPM", "This is not a valid chart string.")]
    [InlineData(false, FinancialChartStringType.Invalid, "INVALID", "This is not a valid chart string.")]
    public async Task RunFullDetailsSerializesMessageForKualiBuild(
        bool isValid,
        FinancialChartStringType chartStringType,
        string chartType,
        string expectedMessage)
    {
        const string chartString = "example-chart-string";
        var service = new StubAggieEnterpriseService(new AeDetails
        {
            IsValid = isValid,
            ChartType = chartType,
            ChartString = chartString,
            ChartStringType = chartStringType
        });
        var function = new FinancialFunction(service);
        var request = TestHttpRequestData.Create(string.Empty);

        var response = await function.RunFullDetails(
            request,
            chartString,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(response.Body);
        var root = document.RootElement;
        Assert.Equal(isValid, root.GetProperty("isValid").GetBoolean());
        Assert.Equal(chartType, root.GetProperty("chartType").GetString());
        Assert.Equal(expectedMessage, root.GetProperty("message").GetString());
        Assert.Equal(chartString, root.GetProperty("chartString").GetString());
        Assert.Equal((int)chartStringType, root.GetProperty("chartStringType").GetInt32());
        output.WriteLine(root.GetRawText());
    }

    [Fact]
    public async Task RunReturnsOnlyFlatMappedFinancialDetails()
    {
        const string chartString = "0000000000-000000-0000000-000000";
        var service = new StubAggieEnterpriseService(new AeDetails
        {
            IsValid = false,
            ChartType = "PPM",
            ChartString = chartString,
            ChartStringType = FinancialChartStringType.Ppm,
            Errors = ["First error.", "Second error."],
            Warnings = ["First warning.", "Second warning."],
            FundPurpose = "Research",
            SegmentDetails =
            [
                new SegmentDetails
                {
                    Entity = "GL Financial Department",
                    Name = "Biological Sciences"
                },
                new SegmentDetails
                {
                    Entity = "Award",
                    Code = "A232815",
                    Name = "A232815 SP0A232815 PO2610129 Electrochemically Mediated Air Separation Modules (EM-ASM)"
                }
            ],
            Approvers =
            [
                new Approver
                {
                    FullName = "Full-details approver",
                    Email = "full-details@example.test"
                }
            ],
            PpmDetails = new PpmDetails
            {
                ProjectStartDate = "2025-01-01",
                ProjectCompletionDate = "2026-12-31",
                AwardStatus = "Active",
                AwardStartDate = "2025-02-01",
                AwardEndDate = "2026-11-30",
                AwardInfo = "legacy award number",
                ProjectTypeName = "Sponsored",
                Roles =
                [
                    new PpmRoles
                    {
                        RoleName = "Principal Investigator",
                        Type = "A",
                        Approvers =
                        [
                            new Approver
                            {
                                FullName = "Award Principal Investigator",
                                Email = "award-pi@example.test"
                            }
                        ]
                    },
                    new PpmRoles
                    {
                        RoleName = "Principal Investigator",
                        Type = "P",
                        Approvers =
                        [
                            new Approver
                            {
                                FirstName = "Ada",
                                LastName = "Lovelace",
                                Email = "ada@example.test"
                            },
                            new Approver
                            {
                                FullName = "Second Project Principal Investigator",
                                Email = "second-pi@example.test"
                            }
                        ]
                    },
                    new PpmRoles
                    {
                        RoleName = "Project Manager",
                        Type = "P",
                        Approvers =
                        [
                            new Approver
                            {
                                FullName = "Grace Hopper",
                                Email = "grace@example.test"
                            }
                        ]
                    }
                ]
            }
        });
        var function = new FinancialFunction(service);
        var request = TestHttpRequestData.Create(string.Empty);

        var response = await function.Run(request, chartString, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(response.Body);
        var root = document.RootElement;

        var expectedProperties = new[]
        {
            "isValid",
            "validationStatus",
            "chartType",
            "chartString",
            "error",
            "warning",
            "glFinancialDepartmentName",
            "projectStartDate",
            "projectCompletionDate",
            "awardStatus",
            "awardStartDate",
            "awardEndDate",
            "awardInfo",
            "projectTypeName",
            "principalInvestigatorName",
            "principalInvestigatorEmail",
            "projectManagerName",
            "projectManagerEmail",
            "fundPurpose"
        };
        var actualProperties = root.EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
        Assert.Equal(
            expectedProperties.Order(StringComparer.Ordinal),
            actualProperties.Order(StringComparer.Ordinal));
        Assert.Equal(["isValid", "validationStatus"], actualProperties.Take(2));

        Assert.False(root.GetProperty("isValid").GetBoolean());
        Assert.Equal(
            "This is not a valid chart string.",
            root.GetProperty("validationStatus").GetString());
        Assert.Equal("PPM", root.GetProperty("chartType").GetString());
        Assert.Equal(chartString, root.GetProperty("chartString").GetString());
        Assert.Equal("First error. Second error.", root.GetProperty("error").GetString());
        Assert.Equal("First warning. Second warning.", root.GetProperty("warning").GetString());
        Assert.Equal(
            "Biological Sciences",
            root.GetProperty("glFinancialDepartmentName").GetString());
        Assert.Equal("2025-01-01", root.GetProperty("projectStartDate").GetString());
        Assert.Equal("2026-12-31", root.GetProperty("projectCompletionDate").GetString());
        Assert.Equal("Active", root.GetProperty("awardStatus").GetString());
        Assert.Equal("2025-02-01", root.GetProperty("awardStartDate").GetString());
        Assert.Equal("2026-11-30", root.GetProperty("awardEndDate").GetString());
        Assert.Equal(
            "A232815 SP0A232815 PO2610129 Electrochemically Mediated Air Separation Modules (EM-ASM)",
            root.GetProperty("awardInfo").GetString());
        Assert.Equal("Sponsored", root.GetProperty("projectTypeName").GetString());
        Assert.Equal(
            "Lovelace, Ada",
            root.GetProperty("principalInvestigatorName").GetString());
        Assert.Equal(
            "ada@example.test",
            root.GetProperty("principalInvestigatorEmail").GetString());
        Assert.Equal("Grace Hopper", root.GetProperty("projectManagerName").GetString());
        Assert.Equal(
            "grace@example.test",
            root.GetProperty("projectManagerEmail").GetString());
        Assert.Equal("Research", root.GetProperty("fundPurpose").GetString());

        string[] fullDetailsOnlyProperties =
        [
            "message",
            "chartStringType",
            "errors",
            "warnings",
            "segmentDetails",
            "approvers",
            "ppmDetails",
            "hasWarnings"
        ];
        Assert.All(
            fullDetailsOnlyProperties,
            propertyName => Assert.False(root.TryGetProperty(propertyName, out _)));
    }

    [Fact]
    public async Task RunPassesCancellationTokenToService()
    {
        var service = new TrackingAggieEnterpriseService();
        var function = new FinancialFunction(service);
        var request = TestHttpRequestData.Create(string.Empty);
        using var cancellationTokenSource = new CancellationTokenSource();

        await function.Run(request, "chart", cancellationTokenSource.Token);

        Assert.Equal([cancellationTokenSource.Token], service.CancellationTokens);
    }

    [Fact]
    public async Task RunValidationPassesValueAndCancellationTokenToService()
    {
        var service = new TrackingAggieEnterpriseService();
        var function = new FinancialFunction(service);
        var request = TestHttpRequestData.Create(string.Empty);
        using var cancellationTokenSource = new CancellationTokenSource();

        var response = await function.RunValidation(
            request,
            "chart",
            cancellationTokenSource.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["chart"], service.ValidatedChartStrings);
        Assert.Equal([cancellationTokenSource.Token], service.ValidationCancellationTokens);
    }

    [Theory]
    [InlineData(
        true,
        "0000-00000-0000000-000000-00-000-0000000000-000000-0000-000000-000000",
        "GL",
        "This is a valid GL chart string.")]
    [InlineData(
        true,
        "0000000000-000000-0000000-000000",
        "PPM",
        "This is a valid PPM chart string.")]
    [InlineData(false, "invalid", "INVALID", "This is not a valid chart string.")]
    public async Task RunValidationSerializesMessageForKualiBuild(
        bool isValid,
        string chartString,
        string expectedChartType,
        string expectedMessage)
    {
        var service = new TrackingAggieEnterpriseService(
            validationResultFactory: value => new FinancialValidationResult
            {
                ChartString = value,
                IsValid = isValid
            });
        var function = new FinancialFunction(service);
        var request = TestHttpRequestData.Create(string.Empty);

        var response = await function.RunValidation(
            request,
            chartString,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(response.Body);
        var root = document.RootElement;
        Assert.Equal(chartString, root.GetProperty("chartString").GetString());
        Assert.Equal(expectedChartType, root.GetProperty("chartType").GetString());
        Assert.Equal(expectedMessage, root.GetProperty("message").GetString());
    }

    [Fact]
    public async Task RunBulkReturnsBadRequestWhenBatchExceedsMaximum()
    {
        var service = new TrackingAggieEnterpriseService();
        var function = new FinancialFunction(service);
        var chartStrings = Enumerable.Range(0, FinancialFunction.MaxBatchSize + 1)
            .Select(index => $"chart-{index}")
            .ToArray();
        var request = TestHttpRequestData.Create(JsonSerializer.Serialize(chartStrings));

        var response = await function.RunBulk(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, service.CallCount);

        response.Body.Position = 0;
        var error = await JsonSerializer.DeserializeAsync<ErrorResponse>(
            response.Body,
            JsonOptions,
            cancellationToken: CancellationToken.None);
        Assert.Equal(
            $"request body must contain no more than {FinancialFunction.MaxBatchSize} chart strings",
            error?.Error);
    }

    [Fact]
    public async Task RunBulkProcessesMaximumBatchInInputOrderWithBoundedConcurrency()
    {
        var service = new TrackingAggieEnterpriseService(delayCalls: true);
        var function = new FinancialFunction(service);
        var chartStrings = Enumerable.Range(0, FinancialFunction.MaxBatchSize)
            .Select(index => $"chart-{index}")
            .ToArray();
        var request = TestHttpRequestData.Create(JsonSerializer.Serialize(chartStrings));
        using var cancellationTokenSource = new CancellationTokenSource();

        var response = await function.RunBulk(request, cancellationTokenSource.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        response.Body.Position = 0;
        var results = await JsonSerializer.DeserializeAsync<FinancialDetails[]>(
            response.Body,
            JsonOptions);
        Assert.NotNull(results);
        Assert.Equal(chartStrings, results.Select(result => result.ChartString));
        Assert.NotEqual(chartStrings, service.CompletionOrder);
        Assert.Equal(FinancialFunction.MaxBatchSize, service.CallCount);
        Assert.InRange(service.MaxConcurrentCalls, 2, FinancialFunction.MaxConcurrency);
        Assert.All(
            service.CancellationTokens,
            cancellationToken => Assert.True(cancellationToken.CanBeCanceled));
    }

    [Fact]
    public async Task RunBulkFullDetailsPreservesOriginalResponseGraph()
    {
        var chartStrings = new[] { "chart-one", "chart-two" };
        var service = new TrackingAggieEnterpriseService(
            detailsFactory: chartString => new AeDetails
            {
                ChartString = chartString,
                ChartStringType = FinancialChartStringType.Ppm,
                SegmentDetails =
                [
                    new SegmentDetails
                    {
                        Entity = "GL Financial Department",
                        Name = $"Department for {chartString}"
                    }
                ],
                PpmDetails = new PpmDetails
                {
                    ProjectTypeName = $"Project type for {chartString}"
                }
            });
        var function = new FinancialFunction(service);
        var request = TestHttpRequestData.Create(JsonSerializer.Serialize(chartStrings));

        var response = await function.RunBulkFullDetails(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        response.Body.Position = 0;
        var results = await JsonSerializer.DeserializeAsync<AeDetails[]>(
            response.Body,
            JsonOptions);
        Assert.NotNull(results);
        Assert.Collection(
            results,
            result =>
            {
                Assert.Equal("chart-one", result.ChartString);
                Assert.Equal(FinancialChartStringType.Ppm, result.ChartStringType);
                Assert.Equal("Department for chart-one", Assert.Single(result.SegmentDetails).Name);
                Assert.Equal("Project type for chart-one", result.PpmDetails?.ProjectTypeName);
            },
            result =>
            {
                Assert.Equal("chart-two", result.ChartString);
                Assert.Equal(FinancialChartStringType.Ppm, result.ChartStringType);
                Assert.Equal("Department for chart-two", Assert.Single(result.SegmentDetails).Name);
                Assert.Equal("Project type for chart-two", result.PpmDetails?.ProjectTypeName);
            });
    }

    [Fact]
    public async Task RunBulkValidationProcessesInputInOrder()
    {
        var service = new TrackingAggieEnterpriseService(delayCalls: true);
        var function = new FinancialFunction(service);
        var chartStrings = Enumerable.Range(0, 10)
            .Select(index => $"chart-{index}")
            .ToArray();
        var request = TestHttpRequestData.Create(JsonSerializer.Serialize(chartStrings));

        var response = await function.RunBulkValidation(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        response.Body.Position = 0;
        var results = await JsonSerializer.DeserializeAsync<FinancialValidationResult[]>(
            response.Body,
            JsonOptions);
        Assert.NotNull(results);
        Assert.Equal(chartStrings, results.Select(result => result.ChartString));
        Assert.Equal(chartStrings.Length, service.ValidationCallCount);
        Assert.InRange(service.MaxConcurrentValidationCalls, 2, FinancialFunction.MaxConcurrency);
    }

    [Fact]
    public async Task RunBulkValidationSerializesMessagesForKualiBuild()
    {
        const string glChartString =
            "0000-00000-0000000-000000-00-000-0000000000-000000-0000-000000-000000";
        const string ppmChartString = "0000000000-000000-0000000-000000";
        const string invalidChartString = "invalid";
        var chartStrings = new[] { glChartString, ppmChartString, invalidChartString };
        var service = new TrackingAggieEnterpriseService(
            validationResultFactory: value => new FinancialValidationResult
            {
                ChartString = value,
                IsValid = value != invalidChartString
            });
        var function = new FinancialFunction(service);
        var request = TestHttpRequestData.Create(JsonSerializer.Serialize(chartStrings));

        var response = await function.RunBulkValidation(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(response.Body);
        var results = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(chartStrings, results.Select(result =>
            result.GetProperty("chartString").GetString()));
        Assert.Collection(
            results,
            result =>
            {
                Assert.Equal("GL", result.GetProperty("chartType").GetString());
                Assert.Equal(
                    "This is a valid GL chart string.",
                    result.GetProperty("message").GetString());
            },
            result =>
            {
                Assert.Equal("PPM", result.GetProperty("chartType").GetString());
                Assert.Equal(
                    "This is a valid PPM chart string.",
                    result.GetProperty("message").GetString());
            },
            result =>
            {
                Assert.Equal("INVALID", result.GetProperty("chartType").GetString());
                Assert.Equal(
                    "This is not a valid chart string.",
                    result.GetProperty("message").GetString());
            });
    }

    private sealed class TrackingAggieEnterpriseService(
        bool delayCalls = false,
        Func<string, AeDetails>? detailsFactory = null,
        Func<string, FinancialValidationResult>? validationResultFactory = null)
        : IAggieEnterpriseService
    {
        private int _activeCalls;
        private int _callCount;
        private int _maxConcurrentCalls;
        private int _activeValidationCalls;
        private int _validationCallCount;
        private int _maxConcurrentValidationCalls;
        private readonly ConcurrentQueue<CancellationToken> _cancellationTokens = [];
        private readonly ConcurrentQueue<CancellationToken> _validationCancellationTokens = [];
        private readonly ConcurrentQueue<string> _validatedChartStrings = [];
        private readonly ConcurrentQueue<string> _completionOrder = [];

        public int CallCount => _callCount;

        public int MaxConcurrentCalls => _maxConcurrentCalls;

        public int ValidationCallCount => _validationCallCount;

        public int MaxConcurrentValidationCalls => _maxConcurrentValidationCalls;

        public CancellationToken[] CancellationTokens => _cancellationTokens.ToArray();

        public CancellationToken[] ValidationCancellationTokens =>
            _validationCancellationTokens.ToArray();

        public string[] ValidatedChartStrings => _validatedChartStrings.ToArray();

        public string[] CompletionOrder => _completionOrder.ToArray();

        public async Task<AeDetails> GetAeDetailsAsync(
            string segmentString,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            _cancellationTokens.Enqueue(cancellationToken);
            var activeCalls = Interlocked.Increment(ref _activeCalls);
            UpdateMaxConcurrentCalls(activeCalls);

            try
            {
                if (delayCalls)
                {
                    var index = int.Parse(
                        segmentString.AsSpan("chart-".Length),
                        CultureInfo.InvariantCulture);
                    var delayMultiplier = FinancialFunction.MaxConcurrency -
                        (index % FinancialFunction.MaxConcurrency);
                    await Task.Delay(delayMultiplier * 5, cancellationToken);
                }

                _completionOrder.Enqueue(segmentString);
                return detailsFactory?.Invoke(segmentString)
                    ?? new AeDetails { ChartString = segmentString };
            }
            finally
            {
                Interlocked.Decrement(ref _activeCalls);
            }
        }

        public async Task<FinancialValidationResult> ValidateAsync(
            string segmentString,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _validationCallCount);
            _validationCancellationTokens.Enqueue(cancellationToken);
            _validatedChartStrings.Enqueue(segmentString);
            var activeCalls = Interlocked.Increment(ref _activeValidationCalls);
            UpdateMaximum(ref _maxConcurrentValidationCalls, activeCalls);

            try
            {
                if (delayCalls)
                {
                    var index = int.Parse(
                        segmentString.AsSpan("chart-".Length),
                        CultureInfo.InvariantCulture);
                    var delayMultiplier = FinancialFunction.MaxConcurrency -
                        (index % FinancialFunction.MaxConcurrency);
                    await Task.Delay(delayMultiplier * 5, cancellationToken);
                }

                return validationResultFactory?.Invoke(segmentString)
                    ?? new FinancialValidationResult { ChartString = segmentString };
            }
            finally
            {
                Interlocked.Decrement(ref _activeValidationCalls);
            }
        }

        private void UpdateMaxConcurrentCalls(int activeCalls)
        {
            UpdateMaximum(ref _maxConcurrentCalls, activeCalls);
        }

        private static void UpdateMaximum(ref int maximum, int activeCalls)
        {
            var currentMaximum = maximum;
            while (activeCalls > currentMaximum)
            {
                var observedMaximum = Interlocked.CompareExchange(
                    ref maximum,
                    activeCalls,
                    currentMaximum);
                if (observedMaximum == currentMaximum)
                {
                    return;
                }

                currentMaximum = observedMaximum;
            }
        }
    }

    private sealed class StubAggieEnterpriseService(AeDetails details)
        : IAggieEnterpriseService
    {
        public Task<AeDetails> GetAeDetailsAsync(
            string segmentString,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(details);
        }

        public Task<FinancialValidationResult> ValidateAsync(
            string segmentString,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class TestHttpRequestData : HttpRequestData
    {
        private readonly TestFunctionContext _context;

        private TestHttpRequestData(TestFunctionContext context, Stream body)
            : base(context)
        {
            _context = context;
            Body = body;
        }

        public override Stream Body { get; }

        public override HttpHeadersCollection Headers { get; } = [];

        public override IReadOnlyCollection<IHttpCookie> Cookies { get; } = [];

        public override Uri Url { get; } = new("https://example.test/api/v1/financial/details");

        public override IEnumerable<ClaimsIdentity> Identities { get; } = [];

        public override string Method => "POST";

        public static TestHttpRequestData Create(string body)
        {
            var services = new ServiceCollection()
                .AddOptions()
                .Configure<WorkerOptions>(options =>
                {
                    options.Serializer = new JsonObjectSerializer(JsonOptions);
                })
                .BuildServiceProvider();

            return new TestHttpRequestData(
                new TestFunctionContext(services),
                new MemoryStream(Encoding.UTF8.GetBytes(body)));
        }

        public override HttpResponseData CreateResponse()
        {
            return new TestHttpResponseData(_context);
        }
    }

    private sealed class TestHttpResponseData(FunctionContext functionContext)
        : HttpResponseData(functionContext)
    {
        public override HttpStatusCode StatusCode { get; set; }

        public override HttpHeadersCollection Headers { get; set; } = [];

        public override Stream Body { get; set; } = new MemoryStream();

        public override HttpCookies Cookies => throw new NotSupportedException();
    }

    private sealed class TestFunctionContext(IServiceProvider services) : FunctionContext
    {
        public override string InvocationId => "test-invocation";

        public override string FunctionId => "test-function";

        public override TraceContext TraceContext => throw new NotSupportedException();

        public override BindingContext BindingContext => throw new NotSupportedException();

        public override RetryContext RetryContext => throw new NotSupportedException();

        public override IServiceProvider InstanceServices { get; set; } = services;

        public override FunctionDefinition FunctionDefinition => throw new NotSupportedException();

        public override IDictionary<object, object> Items { get; set; } =
            new Dictionary<object, object>();

        public override IInvocationFeatures Features => throw new NotSupportedException();
    }
}
