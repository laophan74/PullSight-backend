using Microsoft.EntityFrameworkCore;
using PullSight.Api.Data.Entities;

namespace PullSight.Api.Data;

public sealed class PullSightDbContext(DbContextOptions<PullSightDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();

    public DbSet<GitHubConnection> GitHubConnections => Set<GitHubConnection>();

    public DbSet<RepositoryRecord> Repositories => Set<RepositoryRecord>();

    public DbSet<PullRequestRecord> PullRequests => Set<PullRequestRecord>();

    public DbSet<ReviewRun> ReviewRuns => Set<ReviewRun>();

    public DbSet<ReviewFinding> ReviewFindings => Set<ReviewFinding>();

    public DbSet<AiProviderLog> AiProviderLogs => Set<AiProviderLog>();

    public DbSet<UsageLimit> UsageLimits => Set<UsageLimit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(user => user.Id);
            entity.HasIndex(user => user.GitHubUserId).IsUnique();
            entity.HasIndex(user => user.Login).IsUnique();
            entity.Property(user => user.Login).HasMaxLength(100);
            entity.Property(user => user.Name).HasMaxLength(200);
            entity.Property(user => user.Email).HasMaxLength(320);
            entity.Property(user => user.AvatarUrl).HasMaxLength(600);
            entity.Property(user => user.ProfileUrl).HasMaxLength(600);
        });

        modelBuilder.Entity<GitHubConnection>(entity =>
        {
            entity.ToTable("github_connections");
            entity.HasKey(connection => connection.Id);
            entity.HasIndex(connection => connection.GitHubUserId).IsUnique();
            entity.Property(connection => connection.Login).HasMaxLength(100);
            entity.Property(connection => connection.Scopes).HasMaxLength(500);
            entity
                .HasOne(connection => connection.User)
                .WithOne(user => user.GitHubConnection)
                .HasForeignKey<GitHubConnection>(connection => connection.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RepositoryRecord>(entity =>
        {
            entity.ToTable("repositories");
            entity.HasKey(repository => repository.Id);
            entity.HasIndex(repository => repository.GitHubRepositoryId).IsUnique();
            entity.HasIndex(repository => new { repository.Owner, repository.Name });
            entity.Property(repository => repository.Owner).HasMaxLength(100);
            entity.Property(repository => repository.Name).HasMaxLength(150);
            entity.Property(repository => repository.FullName).HasMaxLength(260);
            entity.Property(repository => repository.DefaultBranch).HasMaxLength(120);
        });

        modelBuilder.Entity<PullRequestRecord>(entity =>
        {
            entity.ToTable("pull_requests");
            entity.HasKey(pullRequest => pullRequest.Id);
            entity.HasIndex(pullRequest => new { pullRequest.RepositoryId, pullRequest.Number }).IsUnique();
            entity.Property(pullRequest => pullRequest.Title).HasMaxLength(500);
            entity.Property(pullRequest => pullRequest.AuthorLogin).HasMaxLength(100);
            entity.Property(pullRequest => pullRequest.HeadSha).HasMaxLength(80);
            entity.Property(pullRequest => pullRequest.BaseBranch).HasMaxLength(120);
            entity.Property(pullRequest => pullRequest.HeadBranch).HasMaxLength(120);
            entity
                .HasOne(pullRequest => pullRequest.Repository)
                .WithMany(repository => repository.PullRequests)
                .HasForeignKey(pullRequest => pullRequest.RepositoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReviewRun>(entity =>
        {
            entity.ToTable("review_runs");
            entity.HasKey(reviewRun => reviewRun.Id);
            entity.HasIndex(reviewRun => new { reviewRun.RepositoryId, reviewRun.PullRequestNumber, reviewRun.HeadSha });
            entity.Property(reviewRun => reviewRun.HeadSha).HasMaxLength(80);
            entity.Property(reviewRun => reviewRun.Analyzer).HasMaxLength(80);
            entity.Property(reviewRun => reviewRun.Source).HasMaxLength(40);
            entity.Property(reviewRun => reviewRun.Status).HasMaxLength(40);
            entity.Property(reviewRun => reviewRun.Summary).HasMaxLength(4000);
            entity
                .HasOne(reviewRun => reviewRun.User)
                .WithMany(user => user.ReviewRuns)
                .HasForeignKey(reviewRun => reviewRun.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity
                .HasOne(reviewRun => reviewRun.Repository)
                .WithMany(repository => repository.ReviewRuns)
                .HasForeignKey(reviewRun => reviewRun.RepositoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReviewFinding>(entity =>
        {
            entity.ToTable("review_findings");
            entity.HasKey(finding => finding.Id);
            entity.Property(finding => finding.Severity).HasMaxLength(30);
            entity.Property(finding => finding.Title).HasMaxLength(300);
            entity.Property(finding => finding.FilePath).HasMaxLength(600);
            entity.Property(finding => finding.RuleId).HasMaxLength(120);
            entity.Property(finding => finding.Message).HasMaxLength(4000);
            entity.Property(finding => finding.Suggestion).HasMaxLength(4000);
            entity
                .HasOne(finding => finding.ReviewRun)
                .WithMany(reviewRun => reviewRun.Findings)
                .HasForeignKey(finding => finding.ReviewRunId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AiProviderLog>(entity =>
        {
            entity.ToTable("ai_provider_logs");
            entity.HasKey(log => log.Id);
            entity.Property(log => log.Provider).HasMaxLength(80);
            entity.Property(log => log.Model).HasMaxLength(120);
            entity.Property(log => log.Status).HasMaxLength(40);
            entity.Property(log => log.ErrorMessage).HasMaxLength(2000);
            entity
                .HasOne(log => log.ReviewRun)
                .WithMany(reviewRun => reviewRun.AiProviderLogs)
                .HasForeignKey(log => log.ReviewRunId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UsageLimit>(entity =>
        {
            entity.ToTable("usage_limits");
            entity.HasKey(limit => limit.Id);
            entity.HasIndex(limit => new { limit.UserId, limit.UsageDate }).IsUnique();
            entity
                .HasOne(limit => limit.User)
                .WithMany(user => user.UsageLimits)
                .HasForeignKey(limit => limit.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
