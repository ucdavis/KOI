using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Globalization;
using Azure.Core.Serialization;
using Koi.Functions.Financial;
using Koi.Functions.Financial.Models;
using Koi.Functions.Financial.Services;
using Koi.Functions.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koi.Functions.Tests.Financial;

public sealed class FinancialFunctionTests
{
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

        var response = await function.RunBulk(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        response.Body.Position = 0;
        var results = await JsonSerializer.DeserializeAsync<AeDetails[]>(response.Body);
        Assert.NotNull(results);
        Assert.Equal(chartStrings, results.Select(result => result.ChartString));
        Assert.NotEqual(chartStrings, service.CompletionOrder);
        Assert.Equal(FinancialFunction.MaxBatchSize, service.CallCount);
        Assert.InRange(service.MaxConcurrentCalls, 2, FinancialFunction.MaxConcurrency);
    }

    private sealed class TrackingAggieEnterpriseService(bool delayCalls = false)
        : IAggieEnterpriseService
    {
        private int _activeCalls;
        private int _callCount;
        private int _maxConcurrentCalls;
        private readonly ConcurrentQueue<string> _completionOrder = [];

        public int CallCount => _callCount;

        public int MaxConcurrentCalls => _maxConcurrentCalls;

        public string[] CompletionOrder => _completionOrder.ToArray();

        public async Task<AeDetails> GetAeDetailsAsync(string segmentString)
        {
            Interlocked.Increment(ref _callCount);
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
                    await Task.Delay(delayMultiplier * 5);
                }

                _completionOrder.Enqueue(segmentString);
                return new AeDetails { ChartString = segmentString };
            }
            finally
            {
                Interlocked.Decrement(ref _activeCalls);
            }
        }

        private void UpdateMaxConcurrentCalls(int activeCalls)
        {
            var currentMaximum = _maxConcurrentCalls;
            while (activeCalls > currentMaximum)
            {
                var observedMaximum = Interlocked.CompareExchange(
                    ref _maxConcurrentCalls,
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

        public override Uri Url { get; } = new("https://example.test/api/v1/financial");

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
