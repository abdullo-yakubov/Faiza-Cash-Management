namespace FaizaGold.Domain;

public sealed class User
{
    public int Id { get; set; }
    public required string Login { get; set; }
    public required string FullName { get; set; }
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<RegisterAccess> RegisterAccess { get; set; } = [];
}

public sealed class Branch
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Доступ кассира к конкретной кассе.</summary>
public sealed class RegisterAccess
{
    public int UserId { get; set; }
    public int CashRegisterId { get; set; }
    public CashRegister? CashRegister { get; set; }
}

public sealed class Category
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public CategoryKind Kind { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class Counterparty
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public CounterpartyKind Kind { get; set; }
    public string? Phone { get; set; }
    public string? Note { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class Transfer
{
    public int Id { get; set; }
    public int FromRegisterId { get; set; }
    public int ToRegisterId { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public int CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AuditEntry
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public int? UserId { get; set; }
    public string? UserLogin { get; set; }
    public required string Action { get; set; }
    public required string Entity { get; set; }
    public string? EntityId { get; set; }
    public string? Details { get; set; }
}
