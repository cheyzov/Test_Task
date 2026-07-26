using Microsoft.EntityFrameworkCore;

namespace Test_Task.Models;

public sealed class TestTaskDbContext(DbContextOptions<TestTaskDbContext> options) : DbContext(options)
{
    public DbSet<PaymentOperation> Operations => Set<PaymentOperation>();

    public DbSet<OperationEvent> OperationEvents => Set<OperationEvent>();

    public DbSet<PaymentDispatch> PaymentDispatches => Set<PaymentDispatch>();

    public DbSet<ProviderReceipt> ProviderReceipts => Set<ProviderReceipt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PaymentOperation>(entity =>
        {
            entity.HasKey(x => x.OperationId);
            entity.Property(x => x.OperationId).HasMaxLength(200);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.ProviderPaymentId).HasMaxLength(200);
            entity.HasIndex(x => x.ProviderPaymentId).IsUnique();
        });

        modelBuilder.Entity<OperationEvent>(entity =>
        {
            entity.HasKey(x => x.EventId);
            entity.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.Type).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Message).HasMaxLength(1000).IsRequired();
            entity.HasIndex(x => new { x.OperationId, x.EventId });
            entity.HasOne(x => x.Operation)
                .WithMany(x => x.Events)
                .HasForeignKey(x => x.OperationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PaymentDispatch>(entity =>
        {
            entity.HasKey(x => x.OperationId);
            entity.HasIndex(x => x.OperationId).IsUnique();
            entity.Property(x => x.RequestBody).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.LastError).HasMaxLength(2000);
            entity.ToTable("DispatchJobs");
            entity.HasOne(x => x.Operation)
                .WithOne(x => x.Dispatch)
                .HasForeignKey<PaymentDispatch>(x => x.OperationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProviderReceipt>(entity =>
        {
            entity.HasKey(x => x.ReceiptId);
            entity.Property(x => x.ProviderPaymentId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.OperationId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Result).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Ignored).IsRequired();
            entity.Property(x => x.Message).HasMaxLength(1000);
            entity.HasIndex(x => new { x.ProviderPaymentId, x.Result }).IsUnique();
            entity.HasOne(x => x.Operation)
                .WithMany(x => x.Receipts)
                .HasForeignKey(x => x.OperationId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
