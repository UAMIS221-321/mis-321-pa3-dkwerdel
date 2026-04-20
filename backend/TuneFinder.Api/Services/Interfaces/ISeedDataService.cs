namespace TuneFinder.Api.Services.Interfaces;

public interface ISeedDataService
{
    Task InitializeDatabaseAsync(CancellationToken cancellationToken = default);
}
