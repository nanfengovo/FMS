using Fms.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fms.EntityFrameworkCore;

public class FmsDbContext : DbContext
{
    public DbSet<VehicleRecord> Vehicles => Set<VehicleRecord>();
    public DbSet<MapNodeRecord> MapNodes => Set<MapNodeRecord>();
    public DbSet<MapPathRecord> MapPaths => Set<MapPathRecord>();
    public DbSet<MutexZoneRecord> MutexZones => Set<MutexZoneRecord>();
    public DbSet<TransportOrderRecord> Orders => Set<TransportOrderRecord>();
    public DbSet<TaskStepRecord> TaskSteps => Set<TaskStepRecord>();
    public DbSet<AuditLogRecord> AuditLogs => Set<AuditLogRecord>();
    public DbSet<UserAccountRecord> Users => Set<UserAccountRecord>();
    public DbSet<WorkflowTemplateRecord> WorkflowTemplates => Set<WorkflowTemplateRecord>();
    public DbSet<WorkflowTemplateHistoryRecord> WorkflowTemplateHistories => Set<WorkflowTemplateHistoryRecord>();
    public DbSet<MapPointCloudRecord> MapPointClouds => Set<MapPointCloudRecord>();
    public DbSet<RefreshTokenRecord> RefreshTokens => Set<RefreshTokenRecord>();
    public DbSet<StorageZoneRecord> StorageZones => Set<StorageZoneRecord>();
    public DbSet<StorageSlotRecord> StorageSlots => Set<StorageSlotRecord>();
    public DbSet<RouteLineRecord> RouteLines => Set<RouteLineRecord>();
    public DbSet<DecisionLogRecord> DecisionLogs => Set<DecisionLogRecord>();
    public DbSet<RoleGrantRecord> RoleGrants => Set<RoleGrantRecord>();
    public DbSet<LogEventRecord> LogEvents => Set<LogEventRecord>();
    public DbSet<LogWikiRecord> LogWiki => Set<LogWikiRecord>();
    public DbSet<AiEndpointRecord> AiEndpoints => Set<AiEndpointRecord>();
    public DbSet<AppStateRecord> AppStates => Set<AppStateRecord>();
    public DbSet<ReplaySceneRecord> ReplayScenes => Set<ReplaySceneRecord>();

    public FmsDbContext(DbContextOptions<FmsDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TransportOrderRecord>()
            .HasMany(o => o.Steps)
            .WithOne()
            .HasForeignKey(s => s.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<VehicleRecord>().HasIndex(v => v.Code);
        modelBuilder.Entity<TransportOrderRecord>().HasIndex(o => o.OrderSerial);
        modelBuilder.Entity<TransportOrderRecord>().HasIndex(o => o.Status);
        modelBuilder.Entity<AuditLogRecord>().HasIndex(a => a.Timestamp);
        modelBuilder.Entity<UserAccountRecord>().HasIndex(u => u.Username).IsUnique();
        modelBuilder.Entity<RoleGrantRecord>().HasKey(g => new { g.RoleName, g.PermissionName });
        modelBuilder.Entity<LogEventRecord>().HasIndex(e => e.Timestamp);
        modelBuilder.Entity<LogEventRecord>().HasIndex(e => e.Category);
    }
}
