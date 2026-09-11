using LYBox.Layout.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LYBox.Layout.Core.Data;

public sealed class DesignTimeAppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // 与 ServiceCollectionExtensions 保持一致：宿主共享 db 落在主体 Data 根目录下。
        var hostDataRoot = PluginDataDirectoryProvider.ResolveHostDataRoot();
        Directory.CreateDirectory(hostDataRoot);
        var dbPath = Path.Combine(hostDataRoot, "appdata.db");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        return new AppDbContext(options);
    }
}
