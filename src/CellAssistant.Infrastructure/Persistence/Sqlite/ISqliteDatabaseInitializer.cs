namespace CellAssistant.Infrastructure.Persistence.Sqlite;

using System.Threading;
using System.Threading.Tasks;

public interface ISqliteDatabaseInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    void Initialize();
}
