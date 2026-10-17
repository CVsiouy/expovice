using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Expovice.Api.Data;

/// <summary>Used only by `dotnet ef migrations add`; never connects to a database.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ExpoviceDbContext>
{
    public ExpoviceDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ExpoviceDbContext>()
            .UseSqlServer("Server=design-time;Database=expovice;Integrated Security=false")
            .Options);
}
