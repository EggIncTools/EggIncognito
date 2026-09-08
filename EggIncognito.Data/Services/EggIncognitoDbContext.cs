using EggIncognito.Data.Models;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public class EggIncognitoDbContext(DbContextOptions<EggIncognitoDbContext> options)
    : DbContext(options), IDataProtectionKeyContext {
    public DbSet<StoredEndpoint> StoredEndpoints => Set<StoredEndpoint>();
    public DbSet<StoredRoute> StoredRoutes => Set<StoredRoute>();
    public DbSet<RouteOverride> RouteOverrides => Set<RouteOverride>();
    public DbSet<RouteBinaryCatalog> RouteBinaryCatalogs => Set<RouteBinaryCatalog>();
    public DbSet<Doc> Docs => Set<Doc>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<SubjectTag> SubjectTags => Set<SubjectTag>();
    public DbSet<DocImage> DocImages => Set<DocImage>();
    public DbSet<CaptureUserCa> CaptureUserCas => Set<CaptureUserCa>();
    public DbSet<CaptureProxyAddr> CaptureProxyAddrs => Set<CaptureProxyAddr>();
    public DbSet<ProtoVersion> ProtoVersions => Set<ProtoVersion>();
    public DbSet<ProtoProto> ProtoProtos => Set<ProtoProto>();
    public DbSet<ProtoShaOrder> ProtoShaOrders => Set<ProtoShaOrder>();
    public DbSet<ProtoCanonical> ProtoCanonicals => Set<ProtoCanonical>();
    public DbSet<FeedSubscription> FeedSubscriptions => Set<FeedSubscription>();
    public DbSet<FeedDelivery> FeedDeliveries => Set<FeedDelivery>();
    public DbSet<FeedSuppression> FeedSuppressions => Set<FeedSuppression>();
    public DbSet<BackfillJob> BackfillJobs => Set<BackfillJob>();
    public DbSet<KnownVersion> KnownVersions => Set<KnownVersion>();
    public DbSet<ExtractJob> ExtractJobs => Set<ExtractJob>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<DeviceJob> DeviceJobs => Set<DeviceJob>();
    public DbSet<DeviceJobLine> DeviceJobLines => Set<DeviceJobLine>();
    public DbSet<DeviceState> DeviceStates => Set<DeviceState>();
    public DbSet<DeviceAsset> DeviceAssets => Set<DeviceAsset>();
    public DbSet<ProvisionedInstanceRow> ProvisionedInstances => Set<ProvisionedInstanceRow>();
    public DbSet<DeviceIslandRow> DeviceIslands => Set<DeviceIslandRow>();
    public DbSet<StagedProto> StagedProtos => Set<StagedProto>();
    public DbSet<EnvDesign> EnvDesigns => Set<EnvDesign>();
    public DbSet<EnvDesignVersion> EnvDesignVersions => Set<EnvDesignVersion>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<PeriodicalsSnapshot> PeriodicalsSnapshots => Set<PeriodicalsSnapshot>();

    public DbSet<ArtifactConsumeObservation> ArtifactConsumeObservations => Set<ArtifactConsumeObservation>();
    public DbSet<ContributedCapture> ContributedCaptures => Set<ContributedCapture>();
    public DbSet<GameEvent> GameEvents => Set<GameEvent>();
    public DbSet<ContractRelease> ContractReleases => Set<ContractRelease>();
    public DbSet<GameDataDocument> GameDataDocuments => Set<GameDataDocument>();
    public DbSet<StoredBinary> StoredBinaries => Set<StoredBinary>();
    public DbSet<StoredApk> StoredApks => Set<StoredApk>();
    public DbSet<StoredModule> DeviceModules => Set<StoredModule>();
    public DbSet<BuildBlob> BuildBlobs => Set<BuildBlob>();
    public DbSet<ImageBuild> ImageBuilds => Set<ImageBuild>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<SymbolizedBinary> SymbolizedBinaries => Set<SymbolizedBinary>();
    public DbSet<AnalyzedFile> AnalyzedFiles => Set<AnalyzedFile>();
    public DbSet<UserTheme> UserThemes => Set<UserTheme>();
    public DbSet<SiteThemePolicy> SiteThemePolicies => Set<SiteThemePolicy>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        modelBuilder.Entity<StoredEndpoint>(e => {
            e.HasIndex(x => new { x.Path, x.Eid }).IsUnique();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
        });
        modelBuilder.Entity<StoredRoute>(r => {
            r.HasIndex(x => x.Path).IsUnique();

            r.HasIndex(x => x.Source);
            r.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        });
        modelBuilder.Entity<ArtifactConsumeObservation>(o => {
            o.HasKey(x => x.Id);
            o.Property(x => x.Byproducts).HasColumnType("jsonb").HasDefaultValueSql("'[]'");
            o.Property(x => x.OtherRewards).HasColumnType("jsonb").HasDefaultValueSql("'[]'");
            o.Property(x => x.ObservedAt).HasDefaultValueSql("now()");
            o.HasIndex(x => new { x.SpecName, x.SpecLevel, x.SpecRarity, x.Action });
        });
        modelBuilder.Entity<ContributedCapture>(c => {
            c.HasKey(x => x.Id);
            c.Property(x => x.Payload).HasColumnType("jsonb").HasDefaultValueSql("'{}'");
            c.Property(x => x.RecordedAt).HasDefaultValueSql("now()");
            c.HasIndex(x => new { x.ContributorUserId, x.Status });
            c.HasIndex(x => new { x.Status, x.RecordedAt });
            c.HasIndex(x => x.Kind);
            c.HasIndex(x => new { x.ContributorUserId, x.DedupeHash }).IsUnique();
        });
        modelBuilder.Entity<RouteOverride>(r => r.HasKey(x => x.Path));
        modelBuilder.Entity<RouteBinaryCatalog>(r => r.HasKey(x => x.Path));
        modelBuilder.Entity<Doc>(d => {
            d.HasIndex(x => new { x.SubjectKind, x.SubjectKey }).IsUnique();
            d.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            d.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
        });
        modelBuilder.Entity<Tag>(t => t.HasIndex(x => x.Slug).IsUnique());
        modelBuilder.Entity<SubjectTag>(s => s.HasIndex(x => new { x.SubjectKind, x.SubjectKey, x.TagId }).IsUnique());
        modelBuilder.Entity<DocImage>(im =>
            im.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd());
        modelBuilder.Entity<CaptureUserCa>(c =>
            c.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd());
        modelBuilder.Entity<CaptureProxyAddr>(a => {
            a.HasIndex(x => x.Addr).IsUnique();
            a.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        });
        modelBuilder.Entity<ProtoVersion>(e => {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Platform, x.Build }).IsUnique();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        });
        modelBuilder.Entity<ProtoProto>(e => {
            e.HasKey(x => x.ProtoVersionId);
            e.Property(x => x.MessageIndex).HasColumnType("jsonb");
        });
        modelBuilder.Entity<ProtoShaOrder>(e => {
            e.HasKey(x => x.ProtoSha);
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
        });
        modelBuilder.Entity<ProtoCanonical>(e => {
            e.HasKey(x => x.ProtoSha);
            e.Property(x => x.ComputedAt).HasDefaultValueSql("now()");
        });
        modelBuilder.Entity<FeedSubscription>(e => {
            e.HasKey(x => x.Id);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.Property(x => x.Platforms).HasColumnType("text[]");
            e.Property(x => x.Filters).HasColumnType("text[]").HasDefaultValueSql("'{}'");
            e.Property(x => x.EventKind).HasDefaultValue("proto_build");
        });
        modelBuilder.Entity<FeedDelivery>(e => {
            e.HasKey(x => x.Id);
            e.Property(x => x.EventKind).HasDefaultValue("proto_build");
            e.Property(x => x.DedupKey).HasDefaultValue("");
            e.HasIndex(x => new { x.SubscriptionId, x.EventKind, x.DedupKey }).IsUnique();
        });
        modelBuilder.Entity<FeedSuppression>(e => {
            e.HasKey(x => x.Id);
            e.Property(x => x.EventKind).HasDefaultValue("proto_build");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.HasIndex(x => new { x.SubscriptionId, x.CreatedAt });
        });
        modelBuilder.Entity<BackfillJob>(e => {
            e.HasKey(x => x.Id);

            e.HasIndex(x => new { x.Source, x.StartedAt });
        });
        modelBuilder.Entity<KnownVersion>(e => {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Platform, x.AppVersion, x.Source }).IsUnique();
            e.Property(x => x.FirstSeen).HasDefaultValueSql("now()");
        });
        modelBuilder.Entity<ExtractJob>(e => {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Platform, x.AppVersion }).IsUnique();
        });
        modelBuilder.Entity<Device>(e => {
            e.HasKey(x => x.Id);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.Property(x => x.Origin).HasDefaultValue(DeviceOrigins.Runtime);
        });
        modelBuilder.Entity<DeviceJob>(e => {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.DeviceId, x.Id });
            e.HasIndex(x => new { x.DeviceId, x.Kind, x.Id });
            e.HasIndex(x => x.State);
            e.Property(x => x.StartedAt).HasDefaultValueSql("now()");
            e.Property(x => x.Detail).HasColumnType("jsonb");
        });
        modelBuilder.Entity<DeviceJobLine>(e => {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.JobId, x.Id });
            e.Property(x => x.At).HasDefaultValueSql("now()");
            e.HasOne<DeviceJob>()
                .WithMany()
                .HasForeignKey(x => x.JobId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<DeviceState>(e => {
            e.HasKey(x => x.DeviceId);
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
        });
        modelBuilder.Entity<DeviceAsset>(e => {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Platform, x.Kind, x.Name }).IsUnique();
            e.HasIndex(x => x.Sha256);
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
        });
        modelBuilder.Entity<ProvisionedInstanceRow>(e => {
            e.HasKey(x => x.InstanceId);
            e.HasIndex(x => x.State);
            e.HasIndex(x => x.DeviceId);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        });
        modelBuilder.Entity<DeviceIslandRow>(e => {
            e.HasKey(x => new { x.DeviceId, x.UserId });
            e.HasIndex(x => x.DeviceId);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        });
        modelBuilder.Entity<StagedProto>(e => {
            e.HasIndex(x => x.ProtoSha);
            e.HasIndex(x => x.Status);
        });
        modelBuilder.Entity<EnvDesign>(e => {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
        });
        modelBuilder.Entity<EnvDesignVersion>(e => {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.DesignId, x.VersionNo }).IsUnique();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            e.HasOne<EnvDesign>()
                .WithMany()
                .HasForeignKey(x => x.DesignId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ApiKey>(e => {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.KeyHash).IsUnique();
            e.HasIndex(x => x.OwnerUserId);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        });
        modelBuilder.Entity<PeriodicalsSnapshot>(e => {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.CapturedAt);
            e.HasIndex(x => x.Sha).IsUnique();
            e.Property(x => x.CapturedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        });
        modelBuilder.Entity<GameEvent>(e => {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.StartTime);
            e.HasIndex(x => new { x.EventType, x.StartTime });
            e.HasIndex(x => x.EventId);
        });
        modelBuilder.Entity<ContractRelease>(e => {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ContractId);
            e.HasIndex(x => x.StartTime);
            e.HasIndex(x => new { x.ContractId, x.StartTime }).IsUnique();
        });
        modelBuilder.Entity<GameDataDocument>(e => {
            e.HasKey(x => x.Id);
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
        });
        modelBuilder.Entity<StoredBinary>(e => {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Platform, x.AppVersion }).IsUnique();
            e.HasIndex(x => x.Sha256);
            e.Property(x => x.PulledAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        });
        modelBuilder.Entity<StoredApk>(e => {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Platform, x.Package, x.AppVersion, x.Build, x.Split }).IsUnique();
            e.HasIndex(x => new { x.Platform, x.Package, x.Build });
            e.HasIndex(x => x.Sha256);
            e.Property(x => x.CapturedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        });
        modelBuilder.Entity<StoredModule>(e => {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Name).IsUnique();
            e.HasIndex(x => x.Sha256);
            e.Property(x => x.FetchedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        });
        modelBuilder.Entity<BuildBlob>(e => {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Key).IsUnique();
            e.HasIndex(x => x.Sha256);
            e.Property(x => x.FetchedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        });
        modelBuilder.Entity<ImageBuild>(e => {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.State);
            e.HasIndex(x => x.Tag);
            e.Property(x => x.Log).HasDefaultValue("");
            e.Property(x => x.StartedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        });
        modelBuilder.Entity<AppSetting>(e => {
            e.HasKey(x => x.Key);
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            e.ToTable("app_settings", t => t.ExcludeFromMigrations());
        });
        modelBuilder.Entity<SymbolizedBinary>(e => {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Platform, x.AppVersion }).IsUnique();
            e.HasIndex(x => x.Sha256);
            e.Property(x => x.UploadedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        });
        modelBuilder.Entity<AnalyzedFile>(e => {
            e.HasKey(x => x.FileSha);
            e.HasIndex(x => x.FirstSeen);
            e.Property(x => x.FirstSeen).HasDefaultValueSql("now()");
        });
        modelBuilder.Entity<UserTheme>(e => {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.OwnerUserId);
            e.HasIndex(x => new { x.OwnerUserId, x.Slug }).IsUnique();
            e.HasIndex(x => x.OwnerUserId, "ix_user_themes_owner_active").IsUnique().HasFilter("is_active");
            e.Property(x => x.Model).HasColumnType("jsonb");
            e.Property(x => x.Validation).HasColumnType("jsonb");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
        });
        modelBuilder.Entity<SiteThemePolicy>(e => {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
        });
    }
}
