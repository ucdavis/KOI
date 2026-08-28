using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Globalization;
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
    [Theory]
    [InlineData(true, FinancialChartStringType.Gl, "GL", "This is a valid GL chart string.")]
    [InlineData(true, FinancialChartStringType.Ppm, "PPM", "This is a valid PPM chart string.")]
    [InlineData(false, FinancialChartStringType.Gl, "GL", "This is not a valid chart string.")]
    [InlineData(false, FinancialChartStringType.Ppm, "PPM", "This is not a valid chart string.")]
    [InlineData(false, FinancialChartStringType.Invalid, "INVALID", "This is not a valid chart string.")]
    public async Task RunSerializesMessageForKualiBuild(
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

        var response = await function.Run(request, chartString, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(response.Body);
        var root = document.RootElement;
        Assert.Equal(isValid, root.GetProperty("IsValid").GetBoolean());
        Assert.Equal(chartType, root.GetProperty("ChartType").GetString());
        Assert.Equal(expectedMessage, root.GetProperty("Message").GetString());
        Assert.Equal(chartString, root.GetProperty("ChartString").GetString());
        Assert.Equal((int)chartStringType, root.GetProperty("ChartStringType").GetInt32());
        output.WriteLine(root.GetRawText());
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
        var results = await JsonSerializer.DeserializeAsync<AeDetails[]>(response.Body);
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
        var results = await JsonSerializer.DeserializeAsync<FinancialValidationResult[]>(response.Body);
        Assert.NotNull(results);
        Assert.Equal(chartStrings, results.Select(result => result.ChartString));
        Assert.Equal(chartStrings.Length, service.ValidationCallCount);
        Assert.InRange(service.MaxConcurrentValidationCalls, 2, FinancialFunction.MaxConcurrency);
    }

    private sealed class TrackingAggieEnterpriseService(bool delayCalls = false)
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
                return new AeDetails { ChartString = segmentString };
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

                return new FinancialValidationResult { ChartString = segmentString };
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
                    options.Serializer = new JsonObjectSerializer();
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
