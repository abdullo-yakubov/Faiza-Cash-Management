using System.Security.Claims;
using FaizaGold.Api.Data;
using FaizaGold.Domain;
using Microsoft.EntityFrameworkCore;

namespace FaizaGold.Api.Infrastructure;

/// <summary>Текущий пользователь запроса и его права на кассы.</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor, AppDbContext db)
{
    private ClaimsPrincipal Principal => accessor.HttpContext?.User ?? new ClaimsPrincipal();

    public int Id => int.Parse(Principal.FindFirstValue(ClaimTypes.NameIdentifier)
                               ?? throw new UnauthorizedAccessException());

    public string Login => Principal.FindFirstValue(ClaimTypes.Name) ?? "";

    public UserRole Role => Enum.Parse<UserRole>(Principal.FindFirstValue(ClaimTypes.Role)
                                                 ?? throw new UnauthorizedAccessException());

    /// <summary>Видит все кассы (все роли, кроме кассира).</summary>
    public bool SeesAllRegisters => Role != UserRole.Cashier;

    public bool CanOperate => Role is UserRole.Admin or UserRole.Accountant or UserRole.Cashier;

    public async Task<bool> CanAccessRegisterAsync(int registerId, CancellationToken ct) =>
        SeesAllRegisters || await db.RegisterAccess.AnyAsync(a => a.UserId == Id && a.CashRegisterId == registerId, ct);

    public async Task<HashSet<int>?> AllowedRegisterIdsAsync(CancellationToken ct) =>
        SeesAllRegisters
            ? null
            : (await db.RegisterAccess.Where(a => a.UserId == Id).Select(a => a.CashRegisterId).ToListAsync(ct)).ToHashSet();

    /// <summary>Бросает исключение, если пользователь не может проводить операции по кассе.</summary>
    public async Task EnsureCanOperateAsync(int registerId, CancellationToken ct)
    {
        if (!CanOperate || !await CanAccessRegisterAsync(registerId, ct))
            throw new ForbiddenException("Нет доступа к этой кассе.");
    }
}

public sealed class ForbiddenException(string message) : Exception(message);

public sealed class NotFoundException(string message) : Exception(message);
