using Koi.Functions.Financial.Models;

namespace Koi.Functions.Financial.Services;

public interface IAggieEnterpriseService
{
    Task<AeDetails> GetAeDetailsAsync(string segmentString);
}
