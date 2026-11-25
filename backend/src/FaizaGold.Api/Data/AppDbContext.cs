using FaizaGold.Domain;
using Microsoft.EntityFrameworkCore;

namespace FaizaGold.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<CashRegister> CashRegisters => Set<CashRegister>();
    public DbSet<RegisterAccess> RegisterAccess => Set<RegisterAccess>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Counterparty> Counterparties => Set<Counterparty>();
    public DbSet<CashOperation> Operations => Set<CashOperation>();
    public DbSet<Transfer> Transfers => Set<Transfer>();
    public DbSet<AuditEntry> Audit => Set<AuditEntry>();

    public static readonly IReadOnlyDictionary<OperationKind, string> Sequences = new Dictionary<OperationKind, string>
    {
        [OperationKind.Income] = "seq_income",
        [OperationKind.Expense] = "seq_expense",
        [OperationKind.TransferIn] = "seq_transfer",
        [OperationKind.TransferOut] = "seq_transfer",
        [OperationKind.Reversal] = "seq_reversal",
    };

    protected override void OnModelCreating(ModelBuilder b)
    {
        foreach (var seq in Sequences.Values.Distinct())
            b.HasSequence<long>(seq);

        b.Entity<User>(e =>
        {
            e.Property(x => x.Login).HasMaxLength(50);
            e.Property(x => x.FullName).HasMaxLength(150);
            e.HasIndex(x => x.Login).IsUnique();
            e.HasMany(x => x.RegisterAccess).WithOne().HasForeignKey(x => x.UserId);
        });

        b.Entity<Branch>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(150);
            e.Property(x => x.Address).HasMaxLength(250);
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<CashRegister>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.Balance).HasPrecision(18, 2);
            e.HasIndex(x => new { x.BranchId, x.Name }).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("CK_CashRegisters_Balance", "\"Balance\" >= 0"));
        });

        b.Entity<RegisterAccess>(e =>
        {
            e.HasKey(x => new { x.UserId, x.CashRegisterId });
            e.HasOne(x => x.CashRegister).WithMany().HasForeignKey(x => x.CashRegisterId);
        });

        b.Entity<Shift>(e =>
        {
            e.Property(x => x.OpeningBalance).HasPrecision(18, 2);
            e.Property(x => x.ClosingBalance).HasPrecision(18, 2);
            e.Property(x => x.CountedAmount).HasPrecision(18, 2);
            e.Property(x => x.Difference).HasPrecision(18, 2);
            e.HasOne<CashRegister>().WithMany().HasForeignKey(x => x.CashRegisterId);
            e.HasOne(x => x.OpenedBy).WithMany().HasForeignKey(x => x.OpenedById).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ClosedBy).WithMany().HasForeignKey(x => x.ClosedById).OnDelete(DeleteBehavior.Restrict);
            // В одной кассе может быть только одна открытая смена.
            e.HasIndex(x => x.CashRegisterId).IsUnique().HasFilter("\"ClosedAt\" IS NULL")
                .HasDatabaseName("IX_Shifts_OneOpenPerRegister");
            e.Ignore(x => x.IsOpen);
        });

        b.Entity<Category>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.HasIndex(x => new { x.Kind, x.Name }).IsUnique();
        });

        b.Entity<Counterparty>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(150);
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.Note).HasMaxLength(500);
        });

        b.Entity<CashOperation>(e =>
        {
            e.Property(x => x.Number).HasMaxLength(20);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Delta).HasPrecision(18, 2);
            e.Property(x => x.BalanceAfter).HasPrecision(18, 2);
            e.Property(x => x.Description).HasMaxLength(500);
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => new { x.CashRegisterId, x.CreatedAt });
            e.HasIndex(x => x.ReversesId).IsUnique();
            e.HasOne<CashRegister>().WithMany().HasForeignKey(x => x.CashRegisterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Shift>().WithMany().HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Counterparty).WithMany().HasForeignKey(x => x.CounterpartyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Transfer>().WithMany().HasForeignKey(x => x.TransferId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CashOperation>().WithOne().HasForeignKey<CashOperation>(x => x.ReversesId).OnDelete(DeleteBehavior.Restrict);
            e.Ignore(x => x.IsReversed);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Operations_Amount", "\"Amount\" > 0");
                t.HasCheckConstraint("CK_Operations_BalanceAfter", "\"BalanceAfter\" >= 0");
            });
        });

        b.Entity<Transfer>(e =>
        {
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Description).HasMaxLength(500);
            e.HasOne<CashRegister>().WithMany().HasForeignKey(x => x.FromRegisterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CashRegister>().WithMany().HasForeignKey(x => x.ToRegisterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_Transfers_DifferentRegisters", "\"FromRegisterId\" <> \"ToRegisterId\""));
        });

        b.Entity<AuditEntry>(e =>
        {
            e.Property(x => x.Action).HasMaxLength(50);
            e.Property(x => x.Entity).HasMaxLength(50);
            e.Property(x => x.EntityId).HasMaxLength(50);
            e.Property(x => x.UserLogin).HasMaxLength(50);
            e.Property(x => x.Details).HasMaxLength(2000);
            e.HasIndex(x => x.At);
        });
    }

    /// <summary>Блокирует строку кассы до конца транзакции, чтобы две операции не изменили остаток одновременно.</summary>
    public Task<CashRegister?> LockRegisterAsync(int id, CancellationToken ct) =>
        CashRegisters.FromSql($"SELECT * FROM \"CashRegisters\" WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(ct);

    public async Task<string> NextNumberAsync(OperationKind kind, CancellationToken ct)
    {
        var seq = Sequences[kind];
        var value = await Database.SqlQuery<long>($"SELECT nextval({seq}::regclass) AS \"Value\"").SingleAsync(ct);
        return $"{CashOperation.Prefix(kind)}-{value:000000}";
    }
}
