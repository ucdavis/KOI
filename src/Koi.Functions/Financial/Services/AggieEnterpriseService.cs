using System.Text.RegularExpressions;
using AggieEnterpriseApi;
using AggieEnterpriseApi.Extensions;
using AggieEnterpriseApi.Types;
using AggieEnterpriseApi.Validation;
using Koi.Functions.Financial.Configuration;
using Koi.Functions.Financial.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koi.Functions.Financial.Services;

public sealed partial class AggieEnterpriseService : IAggieEnterpriseService
{
    private const string FiscalOfficer = "Fiscal Officer Approver";

    private readonly IAggieEnterpriseClient _apiClient;
    private readonly ILogger<AggieEnterpriseService> _logger;

    public AggieEnterpriseService(
        IOptions<FinancialOptions> financialOptions,
        ILogger<AggieEnterpriseService> logger)
    {
        var options = financialOptions.Value;
        _logger = logger;
        _apiClient = GraphQlClient.Get(
            options.ApiUrl,
            options.TokenEndpoint,
            options.ConsumerKey,
            options.ConsumerSecret,
            $"{options.ScopeApp}-{options.ScopeEnv}");
    }

    public async Task<AeDetails> GetAeDetailsAsync(string segmentString)
    {

        var aeDetails = new AeDetails();
        if (string.IsNullOrWhiteSpace(segmentString))
        {
            aeDetails.Errors.Add("Invalid Chart Type");
            aeDetails.IsValid = false;
            return aeDetails;
        }

        segmentString = segmentString.Trim();

        var isValidPpm = Regex.IsMatch(
            segmentString,
            @"^[0-9A-Z]{10}-[0-9A-Z]{6}-[0-9A-Z]{7}-[0-9A-Z]{6}(-[0-9A-Z]{7}-[0-9A-Za-z]{5,10})?$");

        if (!isValidPpm)
        {
            var upperSegmentString = segmentString.ToUpperInvariant();
            if (!string.Equals(upperSegmentString, segmentString, StringComparison.Ordinal))
            {
                aeDetails.Warnings.Add(
                    "Chart String had lowercase characters. Lowercase characters are not valid in chart string segments.");
            }

            segmentString = upperSegmentString;
        }

        aeDetails.ChartString = segmentString;
        aeDetails.ChartStringType = FinancialChartValidation.GetFinancialChartStringType(segmentString);
        aeDetails.ChartType = aeDetails.ChartStringType.ToString().ToUpperInvariant();

        if (aeDetails.ChartStringType == FinancialChartStringType.Invalid)
        {
            aeDetails.Errors.Add("Invalid Chart Type");
            aeDetails.IsValid = false;
            return aeDetails;
        }

        if (aeDetails.ChartStringType == FinancialChartStringType.Gl)
        {
            return await GetGlDetailsAsync(aeDetails, segmentString);
        }

        if (aeDetails.ChartStringType == FinancialChartStringType.Ppm)
        {
            return await GetPpmDetailsAsync(aeDetails, segmentString);
        }

        aeDetails.Errors.Add("Unknow Error");
        aeDetails.IsValid = false;
        return aeDetails;
    }

    private async Task<AeDetails> GetGlDetailsAsync(AeDetails aeDetails, string segmentString)
    {
        var glSegments = FinancialChartValidation.GetGlSegments(segmentString);
        var result = await _apiClient.DisplayDetailsGl.ExecuteAsync(
            segmentString: segmentString,
            validateCVRs: true,
            project: glSegments.Project,
            entity: glSegments.Entity,
            fund: glSegments.Fund,
            dept: glSegments.Department,
            account: glSegments.Account,
            purpose: glSegments.Purpose,
            program: glSegments.Program,
            activity: glSegments.Activity);

        var data = result.ReadData();
        if (data is null)
        {
            aeDetails.Errors.Add("Unable to get data from Aggie Enterprise");
            aeDetails.IsValid = false;
            return aeDetails;
        }

        SetGlValidationInfo(aeDetails, data);
        SetGlSegmentDetails(aeDetails, glSegments, data);
        SetFundPurpose(aeDetails, data.ErpFund?.FundPurpose);
        SetGlOrgApprovers(aeDetails, data);

        aeDetails.SegmentDetails = aeDetails.SegmentDetails.OrderBy(segment => segment.Order).ToList();
        return aeDetails;
    }

    private async Task<AeDetails> GetPpmDetailsAsync(AeDetails aeDetails, string segmentString)
    {
        var ppmSegments = FinancialChartValidation.GetPpmSegments(segmentString);
        var result = await _apiClient.DisplayDetailsPpm.ExecuteAsync(
            projectNumber: ppmSegments.Project,
            projectNumberString: ppmSegments.Project,
            segmentString: segmentString,
            taskNumber: ppmSegments.Task,
            expendCode: ppmSegments.ExpenditureType,
            organization: ppmSegments.Organization);

        var data = result.ReadData();
        if (data is null)
        {
            aeDetails.Errors.Add("Unable to get data from Aggie Enterprise");
            aeDetails.IsValid = false;
            return aeDetails;
        }

        SetPpmValidationInfo(aeDetails, data);
        await SetPpmOrgApproversAsync(aeDetails, data);
        SetPoetSegmentDetails(aeDetails, ppmSegments, data);
        await SetExtraPpmSegmentDetailsAsync(aeDetails, data);
        await SetAwardSpecificPpmGlInfoAsync(aeDetails, data);
        await SetPpmPostingSegmentDetailsAsync(aeDetails, data);
        SetPpmDetails(aeDetails, data, ppmSegments);

        aeDetails.SegmentDetails = aeDetails.SegmentDetails.OrderBy(segment => segment.Order).ToList();
        if (aeDetails.PpmDetails is not null)
        {
            aeDetails.PpmDetails.Roles = aeDetails.PpmDetails.Roles.OrderBy(role => role.Order).ToList();
        }

        return aeDetails;
    }

    private static void SetGlValidationInfo(AeDetails aeDetails, IDisplayDetailsGlResult data)
    {
        aeDetails.IsValid = data.GlValidateChartstring.ValidationResponse.Valid;
        if (!aeDetails.IsValid && data.GlValidateChartstring.ValidationResponse.ErrorMessages is not null)
        {
            aeDetails.Errors.AddRange(data.GlValidateChartstring.ValidationResponse.ErrorMessages);
        }

        if (data.GlValidateChartstring.Warnings is null)
        {
            return;
        }

        foreach (var warning in data.GlValidateChartstring.Warnings)
        {
            aeDetails.Warnings.Add($"{warning.SegmentName} - {warning.Warning}");
        }
    }

    private static void SetGlOrgApprovers(AeDetails aeDetails, IDisplayDetailsGlResult data)
    {
        if (data.ErpFinancialDepartment?.Approvers is null)
        {
            return;
        }

        foreach (var approver in data.ErpFinancialDepartment.Approvers
                     .Where(approver => approver.ApproverType == FiscalOfficer))
        {
            aeDetails.Approvers.Add(new Approver
            {
                FirstName = approver.FirstName,
                LastName = approver.LastName,
                Email = approver.EmailAddress
            });
        }
    }

    private static void SetGlSegmentDetails(
        AeDetails aeDetails,
        GlSegments glSegments,
        IDisplayDetailsGlResult data)
    {
        aeDetails.SegmentDetails.Add(new SegmentDetails
        {
            Order = 1,
            Entity = "Entity",
            Code = data.ErpEntity?.Code ?? glSegments.Entity,
            Name = data.ErpEntity?.Name
        });
        aeDetails.SegmentDetails.Add(new SegmentDetails
        {
            Order = 2,
            Entity = "Fund",
            Code = data.ErpFund?.Code ?? glSegments.Fund,
            Name = data.ErpFund?.Name,
            GiftFund = data.ErpFund?.GiftFund ?? false,
            EndowmentGiftFund = data.ErpFund?.EndowmentGiftFund ?? false
        });
        aeDetails.SegmentDetails.Add(new SegmentDetails
        {
            Order = 3,
            Entity = "Department",
            Code = data.ErpFinancialDepartment?.Code ?? glSegments.Department,
            Name = data.ErpFinancialDepartment?.Name
        });
        aeDetails.SegmentDetails.Add(new SegmentDetails
        {
            Order = 4,
            Entity = "Account",
            Code = data.ErpAccount?.Code ?? glSegments.Account,
            Name = data.ErpAccount?.Name
        });
        aeDetails.SegmentDetails.Add(new SegmentDetails
        {
            Order = 5,
            Entity = "Purpose",
            Code = data.ErpPurpose?.Code ?? glSegments.Purpose,
            Name = data.ErpPurpose?.Name
        });
        aeDetails.SegmentDetails.Add(new SegmentDetails
        {
            Order = 6,
            Entity = "Program",
            Code = data.ErpProgram?.Code ?? glSegments.Program,
            Name = data.ErpProgram?.Name
        });
        aeDetails.SegmentDetails.Add(new SegmentDetails
        {
            Order = 7,
            Entity = "Project",
            Code = data.ErpProject?.Code ?? glSegments.Project,
            Name = data.ErpProject?.Name
        });
        aeDetails.SegmentDetails.Add(new SegmentDetails
        {
            Order = 8,
            Entity = "Activity",
            Code = data.ErpActivity?.Code ?? glSegments.Activity,
            Name = data.ErpActivity?.Name
        });
    }

    private static void SetPpmValidationInfo(AeDetails aeDetails, IDisplayDetailsPpmResult data)
    {
        aeDetails.IsValid = data.PpmSegmentStringValidate.ValidationResponse.Valid;
        if (!aeDetails.IsValid && data.PpmSegmentStringValidate.ValidationResponse.ErrorMessages is not null)
        {
            aeDetails.Errors.AddRange(data.PpmSegmentStringValidate.ValidationResponse.ErrorMessages);
        }

        if (data.PpmSegmentStringValidate.Warnings is null)
        {
            return;
        }

        foreach (var warning in data.PpmSegmentStringValidate.Warnings)
        {
            aeDetails.Warnings.Add($"{warning?.SegmentName} - {warning?.Warning}");
        }
    }

    private async Task SetPpmOrgApproversAsync(AeDetails aeDetails, IDisplayDetailsPpmResult data)
    {
        if (data.PpmProjectByNumber?.ProjectOrganizationName is null)
        {
            return;
        }

        try
        {
            var projectOrgCode = data.PpmProjectByNumber.ProjectOrganizationName.Split('-')[0].Trim();
            var result = await _apiClient.ErpDepartmentApprovers.ExecuteAsync(projectOrgCode);
            var approvers = result.ReadData();

            if (approvers?.ErpFinancialDepartment?.Approvers is null)
            {
                return;
            }

            foreach (var approver in approvers.ErpFinancialDepartment.Approvers
                         .Where(approver => approver.ApproverType == FiscalOfficer))
            {
                aeDetails.Approvers.Add(new Approver
                {
                    FirstName = approver.FirstName,
                    LastName = approver.LastName,
                    Email = approver.EmailAddress
                });
            }
        }
        catch (Exception exception)
        {
            LogUnableToGetFinancialDepartment(
                _logger,
                data.PpmProjectByNumber.ProjectOrganizationName,
                exception);
        }
    }

    private static void SetPoetSegmentDetails(
        AeDetails aeDetails,
        PpmSegments ppmSegments,
        IDisplayDetailsPpmResult data)
    {
        aeDetails.SegmentDetails.Add(new SegmentDetails
        {
            Order = 10,
            Entity = "Project",
            Code = data.PpmProjectByNumber?.ProjectNumber ?? ppmSegments.Project,
            Name = data.PpmProjectByNumber?.Name
        });
        aeDetails.SegmentDetails.Add(new SegmentDetails
        {
            Order = 20,
            Entity = "Task",
            Code = data.PpmTaskByProjectNumberAndTaskNumber?.TaskNumber ?? ppmSegments.Task,
            Name = data.PpmTaskByProjectNumberAndTaskNumber?.Name
        });
        aeDetails.SegmentDetails.Add(new SegmentDetails
        {
            Order = 30,
            Entity = "Expenditure Organization",
            Code = data.PpmOrganization?.Code ?? ppmSegments.Organization,
            Name = data.PpmOrganization?.Name
        });
        aeDetails.SegmentDetails.Add(new SegmentDetails
        {
            Order = 40,
            Entity = "Expenditure Type",
            Code = data.PpmExpenditureTypeByCode?.Code ?? ppmSegments.ExpenditureType,
            Name = data.PpmExpenditureTypeByCode?.Name
        });
    }

    private async Task SetExtraPpmSegmentDetailsAsync(AeDetails aeDetails, IDisplayDetailsPpmResult data)
    {
        if (!string.IsNullOrWhiteSpace(data.PpmSegmentStringValidate.Segments.Award))
        {
            aeDetails.SegmentDetails.Add(new SegmentDetails
            {
                Order = 50,
                Entity = "Award",
                Code = data.PpmSegmentStringValidate.Segments.Award,
                Name = string.Empty
            });
        }

        if (!string.IsNullOrWhiteSpace(data.PpmSegmentStringValidate.Segments.FundingSource))
        {
            aeDetails.SegmentDetails.Add(new SegmentDetails
            {
                Order = 60,
                Entity = "Funding Source",
                Code = data.PpmSegmentStringValidate.Segments.FundingSource,
                Name = string.Empty
            });
        }

        if (string.IsNullOrWhiteSpace(data.PpmProjectByNumber?.GlPostingEntityCode))
        {
            return;
        }

        var segment = new SegmentDetails
        {
            Order = 70,
            Entity = "GL Entity",
            Code = data.PpmProjectByNumber.GlPostingEntityCode
        };
        segment.Name = await FindEntityNameAsync(segment.Code);
        aeDetails.SegmentDetails.Add(segment);
    }

    private async Task SetAwardSpecificPpmGlInfoAsync(AeDetails aeDetails, IDisplayDetailsPpmResult data)
    {
        var awardDetail = aeDetails.SegmentDetails.SingleOrDefault(segment => segment.Entity == "Award");
        if (awardDetail is not null)
        {
            aeDetails.PpmDetails ??= new PpmDetails();

            var awardResult = await GetAwardAsync(awardDetail.Code);
            if (awardResult is not null)
            {
                awardDetail.Name = awardResult.Name;

                if (!string.IsNullOrWhiteSpace(awardResult.AwardStatus?.ToString()))
                {
                    aeDetails.PpmDetails.AwardStatus = awardResult.AwardStatus.ToString();
                }

                if (!string.IsNullOrWhiteSpace(awardResult.StartDate))
                {
                    aeDetails.PpmDetails.AwardStartDate = awardResult.StartDate;
                }

                if (!string.IsNullOrWhiteSpace(awardResult.EndDate))
                {
                    aeDetails.PpmDetails.AwardEndDate = awardResult.EndDate;
                }

                if (!string.IsNullOrWhiteSpace(awardResult.AwardNumber))
                {
                    aeDetails.PpmDetails.AwardInfo = awardResult.AwardNumber;
                }

                if (!string.IsNullOrWhiteSpace(awardResult.CloseDate))
                {
                    aeDetails.PpmDetails.AwardCloseDate = awardResult.CloseDate;
                }

                if (awardResult.GlFundCode is not null)
                {
                    var segment = new SegmentDetails
                    {
                        Order = 80,
                        Entity = "GL Fund",
                        Code = awardResult.GlFundCode
                    };
                    await SetPpmFundDetailsAsync(aeDetails, segment);
                    aeDetails.SegmentDetails.Add(segment);
                }

                if (awardResult.GlPurposeCode is not null)
                {
                    var segment = new SegmentDetails
                    {
                        Order = 90,
                        Entity = "GL Purpose",
                        Code = awardResult.GlPurposeCode
                    };
                    segment.Name = await FindPurposeNameAsync(segment.Code);
                    aeDetails.SegmentDetails.Add(segment);
                }

                if (awardResult.Personnel.Any())
                {
                    var counter = 200;
                    var awardMembers = awardResult.Personnel
                        .Distinct()
                        .GroupBy(personnel => personnel.RoleName)
                        .OrderBy(group => group.Key);

                    foreach (var awardMember in awardMembers)
                    {
                        var ppmRole = new PpmRoles
                        {
                            RoleName = awardMember.Key,
                            Type = "A",
                            Order = counter++
                        };

                        foreach (var member in awardMember.OrderBy(member => member.Person?.LastName))
                        {
                            ppmRole.Approvers.Add(new Approver
                            {
                                FirstName = member.Person?.FirstName,
                                LastName = member.Person?.LastName,
                                Email = member.Person?.Email
                            });
                        }

                        aeDetails.PpmDetails.Roles.Add(ppmRole);
                    }
                }
            }
        }

        var fundingSourceDetail = aeDetails.SegmentDetails
            .SingleOrDefault(segment => segment.Entity == "Funding Source");
        if (fundingSourceDetail?.Code is not null)
        {
            fundingSourceDetail.Name = await FindFundingSourceNameAsync(fundingSourceDetail.Code);
        }
    }

    private async Task SetPpmPostingSegmentDetailsAsync(AeDetails aeDetails, IDisplayDetailsPpmResult data)
    {
        if (data.PpmTaskByProjectNumberAndTaskNumber?.GlPostingFundCode is not null)
        {
            var segment = new SegmentDetails
            {
                Order = 100,
                Entity = "GL Posting Fund",
                Code = data.PpmTaskByProjectNumberAndTaskNumber.GlPostingFundCode
            };
            await SetPpmFundDetailsAsync(aeDetails, segment);
            aeDetails.SegmentDetails.Add(segment);
        }

        if (data.PpmTaskByProjectNumberAndTaskNumber?.GlPostingPurposeCode is not null)
        {
            var segment = new SegmentDetails
            {
                Order = 110,
                Entity = "GL Posting Purpose",
                Code = data.PpmTaskByProjectNumberAndTaskNumber.GlPostingPurposeCode
            };
            segment.Name = await FindPurposeNameAsync(segment.Code);
            aeDetails.SegmentDetails.Add(segment);
        }

        if (data.PpmTaskByProjectNumberAndTaskNumber?.GlPostingProgramCode is not null)
        {
            var segment = new SegmentDetails
            {
                Order = 120,
                Entity = "GL Posting Program",
                Code = data.PpmTaskByProjectNumberAndTaskNumber.GlPostingProgramCode
            };
            segment.Name = await FindProgramNameAsync(segment.Code);
            aeDetails.SegmentDetails.Add(segment);
        }

        if (data.PpmTaskByProjectNumberAndTaskNumber?.GlPostingActivityCode is not null)
        {
            var segment = new SegmentDetails
            {
                Order = 130,
                Entity = "GL Posting Activity",
                Code = data.PpmTaskByProjectNumberAndTaskNumber.GlPostingActivityCode
            };
            segment.Name = await FindActivityNameAsync(segment.Code);
            aeDetails.SegmentDetails.Add(segment);
        }

        if (data.PpmProjectByNumber?.ProjectOrganizationName is null)
        {
            return;
        }

        var parts = data.PpmProjectByNumber.ProjectOrganizationName.Split('-');
        if (parts.Length >= 2)
        {
            aeDetails.SegmentDetails.Add(new SegmentDetails
            {
                Order = 140,
                Entity = "GL Financial Department",
                Code = parts[0].Trim(),
                Name = parts[1].Trim()
            });
            return;
        }

        aeDetails.Warnings.Add("Unable to get GL Financial Department");
        aeDetails.SegmentDetails.Add(new SegmentDetails
        {
            Order = 140,
            Entity = "GL Financial Department",
            Code = data.PpmProjectByNumber.ProjectOrganizationName,
            Name = string.Empty
        });
    }

    private static void SetPpmDetails(
        AeDetails aeDetails,
        IDisplayDetailsPpmResult data,
        PpmSegments ppmSegments)
    {
        aeDetails.PpmDetails ??= new PpmDetails();

        var entity = data.PpmProjectByNumber?.LegalEntityCode ?? "0000";
        var fund = data.PpmTaskByProjectNumberAndTaskNumber?.GlPostingFundCode ?? "00000";
        var department = data.PpmOrganization?.Code ?? "0000000";
        var account = data.PpmExpenditureTypeByCode?.Code ?? "000000";
        var purpose = data.PpmTaskByProjectNumberAndTaskNumber?.GlPostingPurposeCode ?? "00";
        var program = data.PpmTaskByProjectNumberAndTaskNumber?.GlPostingProgramCode ?? "000";
        var project = data.PpmProjectByNumber?.ProjectNumber ?? "0000000000";
        var activity = data.PpmTaskByProjectNumberAndTaskNumber?.GlPostingActivityCode ?? "000000";

        aeDetails.PpmDetails.PpmGlString =
            $"{entity}-{fund}-{department}-{account}-{purpose}-{program}-{project}-{activity}-0000-000000-000000";

        if (data.PpmProjectByNumber is not null)
        {
            aeDetails.PpmDetails.ProjectStartDate = data.PpmProjectByNumber.ProjectStartDate;
            aeDetails.PpmDetails.ProjectCompletionDate = data.PpmProjectByNumber.ProjectCompletionDate;
            aeDetails.PpmDetails.ProjectStatus = data.PpmProjectByNumber.ProjectStatus;
            aeDetails.PpmDetails.ProjectTypeName = data.PpmProjectByNumber.ProjectTypeName;
        }

        aeDetails.PpmDetails.PoetString =
            $"{ppmSegments.Project}-{ppmSegments.Organization}-{ppmSegments.ExpenditureType}-{ppmSegments.Task}-{data.PpmSegmentStringValidate.Segments.Award ?? "0000000"}-{data.PpmSegmentStringValidate.Segments.FundingSource ?? "00000"}";

        if (aeDetails.PpmDetails.ProjectTypeName?.Equals(
                "Internal",
                StringComparison.OrdinalIgnoreCase) == true)
        {
            aeDetails.PpmDetails.GlRevenueTransferString =
                $"{entity}-{fund}-{department}-775B15-00-{program}-{project}-{activity}-0000-000000-000000";
        }

        if (data.PpmProjectByNumber?.Description is not null
            && data.PpmProjectByNumber.Description != data.PpmProjectByNumber.Name)
        {
            aeDetails.PpmDetails.ProjectDescription = data.PpmProjectByNumber.Description;
        }

        if (data.PpmProjectByNumber?.TeamMembers is not null)
        {
            var counter = 100;
            var teamMembers = data.PpmProjectByNumber.TeamMembers
                .GroupBy(member => member.RoleName)
                .OrderBy(group => group.Key);

            foreach (var teamMember in teamMembers)
            {
                var ppmRole = new PpmRoles
                {
                    RoleName = teamMember.Key,
                    Type = "P",
                    Order = counter++
                };

                foreach (var member in teamMember.OrderBy(member => member.Person?.LastName))
                {
                    ppmRole.Approvers.Add(new Approver
                    {
                        FirstName = member.Person?.FirstName,
                        LastName = member.Person?.LastName,
                        Email = member.Person?.Email
                    });
                }

                aeDetails.PpmDetails.Roles.Add(ppmRole);
            }
        }

        aeDetails.PpmDetails.TaskStartDate = data.PpmTaskByProjectNumberAndTaskNumber?.TaskStartDate;
        aeDetails.PpmDetails.TaskEndDate = data.PpmTaskByProjectNumberAndTaskNumber?.TaskFinishDate;
    }

    private static void SetFundPurpose(AeDetails aeDetails, string? fundPurpose)
    {
        if (!string.IsNullOrWhiteSpace(fundPurpose))
        {
            aeDetails.FundPurpose = fundPurpose;
        }
    }

    private async Task SetPpmFundDetailsAsync(AeDetails aeDetails, SegmentDetails segment)
    {
        if (string.IsNullOrWhiteSpace(segment.Code))
        {
            return;
        }

        var result = await _apiClient.FundDetails.ExecuteAsync(segment.Code);
        var data = result.ReadData();
        if (data?.ErpFund is null)
        {
            return;
        }

        segment.Name = data.ErpFund.Name;
        segment.GiftFund = data.ErpFund.GiftFund;
        segment.EndowmentGiftFund = data.ErpFund.EndowmentGiftFund;
        SetFundPurpose(aeDetails, data.ErpFund.FundPurpose);
    }

    private async Task<string?> FindEntityNameAsync(string code)
    {
        var result = await _apiClient.ErpEntitySearch.ExecuteAsync(
            new ErpEntityFilterInput { Name = new StringFilterInput { Contains = ToFuzzyQuery(code) } },
            ToUpperTrim(code));
        var data = result.ReadData();

        var match = data.ErpEntitySearch.Data
            .FirstOrDefault(entity => entity.EligibleForUse && entity.Code == code);
        if (match is not null)
        {
            return match.Name;
        }

        return data.ErpEntity is { EligibleForUse: true } && data.ErpEntity.Code == code
            ? data.ErpEntity.Name
            : null;
    }

    private async Task<string?> FindPurposeNameAsync(string code)
    {
        var result = await _apiClient.ErpPurposeSearch.ExecuteAsync(
            new ErpPurposeFilterInput { Name = new StringFilterInput { Contains = ToFuzzyQuery(code) } },
            ToUpperTrim(code));
        var data = result.ReadData();

        var match = data.ErpPurposeSearch.Data
            .FirstOrDefault(purpose => purpose.EligibleForUse && purpose.Code == code);
        if (match is not null)
        {
            return match.Name;
        }

        return data.ErpPurpose is { EligibleForUse: true } && data.ErpPurpose.Code == code
            ? data.ErpPurpose.Name
            : null;
    }

    private async Task<string?> FindProgramNameAsync(string code)
    {
        var result = await _apiClient.ErpProgramSearch.ExecuteAsync(
            new ErpProgramFilterInput { Name = new StringFilterInput { Contains = ToFuzzyQuery(code) } },
            ToUpperTrim(code));
        var data = result.ReadData();

        var match = data.ErpProgramSearch.Data
            .FirstOrDefault(program => program.EligibleForUse && program.Code == code);
        if (match is not null)
        {
            return match.Name;
        }

        return data.ErpProgram is { EligibleForUse: true } && data.ErpProgram.Code == code
            ? data.ErpProgram.Name
            : null;
    }

    private async Task<string?> FindActivityNameAsync(string code)
    {
        var result = await _apiClient.ErpActivitySearch.ExecuteAsync(
            new ErpActivityFilterInput { Name = new StringFilterInput { Contains = ToFuzzyQuery(code) } },
            ToUpperTrim(code));
        var data = result.ReadData();

        var match = data.ErpActivitySearch.Data
            .FirstOrDefault(activity => activity.EligibleForUse && activity.Code == code);
        if (match is not null)
        {
            return match.Name;
        }

        return data.ErpActivity is { EligibleForUse: true } && data.ErpActivity.Code == code
            ? data.ErpActivity.Name
            : null;
    }

    private async Task<string?> FindFundingSourceNameAsync(string code)
    {
        var result = await _apiClient.PpmFundingSourceSearch.ExecuteAsync(
            new PpmFundingSourceFilterInput
            {
                Name = new StringFilterInput { Contains = ToFuzzyQuery(code) }
            },
            ToUpperTrim(code));
        var data = result.ReadData();

        var match = data.PpmFundingSourceSearch.Data
            .FirstOrDefault(source => source.EligibleForUse && source.FundingSourceNumber == code);
        if (match is not null)
        {
            return match.Name;
        }

        return data.PpmFundingSourceByNumber is { EligibleForUse: true }
               && data.PpmFundingSourceByNumber.FundingSourceNumber == code
            ? data.PpmFundingSourceByNumber.Name
            : null;
    }

    private async Task<IPpmAward_PpmAwardByPpmAwardNumber?> GetAwardAsync(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        try
        {
            var result = await _apiClient.PpmAward.ExecuteAsync(ToUpperTrim(query));
            var awards = result.ReadData().PpmAwardByPpmAwardNumber;
            return awards.Count == 0 ? null : awards[0];
        }
        catch (Exception exception)
        {
            LogUnableToGetAward(_logger, query, exception);
            return new LocalPpmAwardDetails
            {
                PpmAwardNumber = query,
                Name = "Error fetching award details"
            };
        }
    }

    private static string ToFuzzyQuery(string value)
    {
        return string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Trim().Replace(" ", "%", StringComparison.Ordinal);
    }

    private static string ToUpperTrim(string value)
    {
        return string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Trim().ToUpperInvariant();
    }

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Error,
        Message = "Unable to get GL Financial Department for {ProjectOrganizationName}")]
    private static partial void LogUnableToGetFinancialDepartment(
        ILogger logger,
        string projectOrganizationName,
        Exception exception);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Error,
        Message = "Error fetching award {AwardNumber}")]
    private static partial void LogUnableToGetAward(
        ILogger logger,
        string awardNumber,
        Exception exception);

    private sealed class LocalPpmAwardDetails : IPpmAward_PpmAwardByPpmAwardNumber
    {
        public bool EligibleForUse { get; set; }

        public string PpmAwardNumber { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string? StartDate { get; set; }

        public string? EndDate { get; set; }

        public string? CloseDate { get; set; }

        public string? GlFundCode { get; set; }

        public string? GlPurposeCode { get; set; }

        public long Id => 1;

        public string AwardNumber => PpmAwardNumber;

        public IReadOnlyList<IPpmAward_PpmAwardByPpmAwardNumber_Personnel> Personnel => [];

        PpmAwardStatus? IPpmAward_PpmAwardByPpmAwardNumber.AwardStatus => new();
    }
}
