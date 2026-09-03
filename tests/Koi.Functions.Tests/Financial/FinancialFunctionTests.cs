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
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koi.Functions.Tests.Financial;

public sealed class FinancialFunctionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false
    };

    [Fact]
    public void GetFinancialDetailsIsTheOnlyFinancialFunction()
    {
        var method = Assert.Single(
            typeof(FinancialFunction)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            method => method.IsDefined(typeof(FunctionAttribute), inherit: false));
        Assert.Equal(nameof(FinancialFunction.Run), method.Name);

        var function = method.GetCustomAttribute<FunctionAttribute>();
        Assert.Equal(FinancialFunction.FunctionName, function?.Name);

        var requestParameter = method.GetParameters()
            .Single(parameter => parameter.ParameterType == typeof(HttpRequestData));
        var trigger = requestParameter.GetCustomAttribute<HttpTriggerAttribute>();
        Assert.NotNull(trigger);
        Assert.Equal("v1/financial/details/{value}", trigger.Route);
        var httpMethod = Assert.Single(trigger.Methods ?? []);
        Assert.Equal("get", httpMethod, ignoreCase: true);
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
                },
                new SegmentDetails
                {
                    Entity = "Project",
                    Name = "Air Separation Project"
                },
                new SegmentDetails
                {
                    Entity = "Task",
                    Name = "Prototype Development"
                },
                new SegmentDetails
                {
                    Entity = "Expenditure Organization",
                    Name = "Chemical Engineering"
                },
                new SegmentDetails
                {
                    Entity = "Expenditure Type",
                    Name = "Research Supplies"
                }
            ],
            Approvers =
            [
                new Approver
                {
                    FullName = "Unmapped approver",
                    Email = "unmapped@example.test"
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
            "entityName",
            "fundName",
            "departmentName",
            "accountName",
            "purposeName",
            "programName",
            "projectName",
            "activityName",
            "taskName",
            "expenditureOrganizationName",
            "expenditureTypeName",
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
        Assert.Equal(string.Empty, root.GetProperty("entityName").GetString());
        Assert.Equal(string.Empty, root.GetProperty("fundName").GetString());
        Assert.Equal(string.Empty, root.GetProperty("departmentName").GetString());
        Assert.Equal(string.Empty, root.GetProperty("accountName").GetString());
        Assert.Equal(string.Empty, root.GetProperty("purposeName").GetString());
        Assert.Equal(string.Empty, root.GetProperty("programName").GetString());
        Assert.Equal("Air Separation Project", root.GetProperty("projectName").GetString());
        Assert.Equal(string.Empty, root.GetProperty("activityName").GetString());
        Assert.Equal("Prototype Development", root.GetProperty("taskName").GetString());
        Assert.Equal(
            "Chemical Engineering",
            root.GetProperty("expenditureOrganizationName").GetString());
        Assert.Equal(
            "Research Supplies",
            root.GetProperty("expenditureTypeName").GetString());
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
    public async Task RunPassesValueAndCancellationTokenToService()
    {
        var service = new TrackingAggieEnterpriseService();
        var function = new FinancialFunction(service);
        var request = TestHttpRequestData.Create(string.Empty);
        using var cancellationTokenSource = new CancellationTokenSource();

        await function.Run(request, "chart", cancellationTokenSource.Token);

        Assert.Equal("chart", service.SegmentString);
        Assert.Equal(cancellationTokenSource.Token, service.CancellationToken);
    }

    private sealed class TrackingAggieEnterpriseService : IAggieEnterpriseService
    {
        public string SegmentString { get; private set; } = string.Empty;

        public CancellationToken CancellationToken { get; private set; }

        public Task<AeDetails> GetAeDetailsAsync(
            string segmentString,
            CancellationToken cancellationToken)
        {
            SegmentString = segmentString;
            CancellationToken = cancellationToken;
            return Task.FromResult(new AeDetails { ChartString = segmentString });
        }

        public Task<FinancialValidationResult> ValidateAsync(
            string segmentString,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
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

        public override string Method => "GET";

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
