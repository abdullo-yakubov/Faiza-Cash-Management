using FaizaCash.Domain;
using FaizaCash.Application;
using FaizaCash.Infrastructure;
using FaizaCash.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Npgsql;


namespace FaizaCash.Api;
public static class ApiEndpoints {
const string Admin = "admin", Accountant = "accountant", Manager = "manager", Auditor = "auditor";
public static void MapCashApi(this WebApplication app,IConfiguration configuration) {
app.MapReports(); app.MapAudit(); app.MapDirectories();
app.MapGet("/health", async (CashDb db) => await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ok", utc = DateTime.UtcNow }) : Results.Problem("База данных недоступна.", statusCode: 503));

app.MapPost("/api/auth/login", async (LoginDto input, HttpContext ctx, CashDb db, Audit audit) =>
{
    var login = (input.Login ?? "").Trim().ToLowerInvariant();
    var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var count = await db.LoginAttempts.CountAsync(x => x.Login == login && x.Ip == ip && !x.Succeeded && x.CreatedAt > DateTime.UtcNow.AddMinutes(-15));
    if (count >= 5) return Results.Problem("Вход временно ограничен. Повторите попытку позже.", statusCode: 429);
    var user = await db.Users.Include(x => x.UserRoles).ThenInclude(x => x.Role).SingleOrDefaultAsync(x => x.Login == login);
    var valid = user is not null && user.Active && input.Password is { Length: <=256 } && Password.Verify(input.Password, user.PasswordHash);
    db.LoginAttempts.Add(new() { Login = login, Ip = ip, Succeeded = valid }); await db.SaveChangesAsync();
    if (!valid)
    {
        await audit.Add(null, "login_failed", "user", null, new { login, ip });
        await Task.Delay(Math.Min(1000, 150 * (count + 1)), ctx.RequestAborted);
        return Results.Problem("Неверный логин или пароль.", statusCode: 401);
    }
    var result = await Tokens.Issue(user!, ctx, db, configuration);
    await audit.Add(user!.Id, "login", "user", null, new { ip });
    return Results.Ok(result);
}).RequireRateLimiting("login");
app.MapPost("/api/auth/refresh", async (HttpContext ctx, CashDb db) =>
{
    if (!ctx.Request.Cookies.TryGetValue(Tokens.Cookie, out var raw)) return Results.Problem("Сеанс не найден.", statusCode: 401);
    await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
    var old = await db.RefreshTokens.Include(x => x.User).ThenInclude(x => x.UserRoles).ThenInclude(x => x.Role).SingleOrDefaultAsync(x => x.Hash == Tokens.Hash(raw));
    if (old is not null && old.RevokedAt != null) { await db.RefreshTokens.Where(x=>x.UserId==old.UserId && x.RevokedAt==null).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedAt,DateTime.UtcNow)); await tx.CommitAsync(); return Results.Problem("Повторное использование сеанса. Выполните вход.",statusCode:401); }
    if (old is null || !old.User.Active || old.ExpiresAt <= DateTime.UtcNow) return Results.Problem("Сеанс недействителен.", statusCode: 401);
    old.RevokedAt = DateTime.UtcNow;
    var response = await Tokens.Issue(old.User, ctx, db, configuration, old);
    await db.SaveChangesAsync();
    await tx.CommitAsync(); return Results.Ok(response);
});
app.MapPost("/api/auth/logout", [Authorize] async (HttpContext ctx, CashDb db, Audit audit,IConfiguration configuration) =>
{
    if (ctx.Request.Cookies.TryGetValue(Tokens.Cookie, out var raw))
    {
        var token = await db.RefreshTokens.SingleOrDefaultAsync(x => x.Hash == Tokens.Hash(raw) && x.RevokedAt == null);
        if (token is not null) { token.RevokedAt = DateTime.UtcNow; await db.SaveChangesAsync(); }
    }
    ctx.Response.Cookies.Delete(Tokens.Cookie, Tokens.Options(ctx,configuration));
    await audit.Add(ctx.UserId(), "logout", "user", null, new { });
    return Results.NoContent();
});
app.MapGet("/api/auth/me", [Authorize] async (HttpContext ctx, CashDb db) => Results.Ok((await db.Users.Include(x => x.UserRoles).ThenInclude(x => x.Role).SingleAsync(x => x.Id == ctx.UserId())).Public()));
app.MapPost("/api/auth/change-password", [Authorize] async (PasswordDto input, HttpContext ctx, CashDb db, Audit audit) =>
{
    var u = await db.Users.SingleAsync(x => x.Id == ctx.UserId());
    if (!Password.Verify(input.OldPassword ?? "", u.PasswordHash)) return Results.Problem("Неверный текущий пароль.", statusCode: 400);
    if (!Password.Valid(input.NewPassword)||input.NewPassword!.Length>256) return Results.Problem("Пароль должен содержать от 10 до 256 символов, буквы и цифры.", statusCode: 400);
    await db.RefreshTokens.Where(x=>x.UserId==u.Id && x.RevokedAt==null).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedAt,DateTime.UtcNow));
    u.PasswordHash = Password.Hash(input.NewPassword!); u.MustChangePassword = false; await db.SaveChangesAsync();
    await audit.Add(u.Id, "password_changed", "user", null, new { }); return Results.NoContent();
});

app.MapGet("/api/users", [Authorize(Roles = Admin)] async (CashDb db, int page = 1, int pageSize = 25, string? q = null) =>
    await Paged(db.Users.Include(x => x.UserRoles).ThenInclude(x => x.Role).Where(x => q == null || x.Login.Contains(q) || x.FullName.Contains(q)).OrderBy(x => x.Login), page, pageSize, x => x.Public()));
app.MapPost("/api/users", [Authorize(Roles = Admin)] async (UserInput input, HttpContext ctx, CashDb db, Audit audit) =>
{
    if (input.Roles is null || string.IsNullOrWhiteSpace(input.Login) || input.Login.Trim().Length>80 || input.Login.Contains('\0') || string.IsNullOrWhiteSpace(input.FullName) || input.FullName.Trim().Length>160 || input.FullName.Contains('\0') || !Password.Valid(input.Password) || input.Password!.Length>256) return Results.Problem("Проверьте логин, имя и пароль.", statusCode: 400);
    var login = input.Login.Trim().ToLowerInvariant();
    if (await db.Users.AnyAsync(x => x.Login == login)) return Results.Problem("Логин уже занят.", statusCode: 409);
    var roles = await db.Roles.Where(x => input.Roles.Contains(x.Code)).ToListAsync();
    if (input.Roles.Count==0 || roles.Count != input.Roles.Count) return Results.Problem("Неизвестная роль.", statusCode: 400);
    var u = new User { Login = login, FullName = input.FullName.Trim(), PasswordHash = Password.Hash(input.Password!), MustChangePassword = input.MustChangePassword };
    u.UserRoles = roles.Select(x => new UserRole { Role = x }).ToList(); db.Users.Add(u); await db.SaveChangesAsync(); await audit.Add(ctx.UserId(), "user_created", "user", null, new { u.Login }); return Results.Created($"/api/users/{u.Id}", u.Public());
});
app.MapPatch("/api/users/{id:guid}", [Authorize(Roles = Admin)] async (Guid id, UserPatch input, CashDb db, HttpContext ctx, Audit audit) =>
{
    if(id==ctx.UserId() && (input.Active==false || input.Roles!=null&&!input.Roles.Contains(Admin))) return Results.Problem("Нельзя отключить собственную административную учётную запись.",statusCode:400);
    await using var tx=await db.Database.BeginTransactionAsync();
    var u = await db.Users.Include(x => x.UserRoles).SingleOrDefaultAsync(x => x.Id == id); if (u is null) return Results.NotFound();
    var beforeName=u.FullName;var beforeActive=u.Active;var beforeRoles=await db.UserRoles.Where(x=>x.UserId==id).Select(x=>x.Role.Code).OrderBy(x=>x).ToArrayAsync();
    if(input.FullName is not null && (string.IsNullOrWhiteSpace(input.FullName)||input.FullName.Trim().Length>160||input.FullName.Contains('\0'))) return Results.Problem("Укажите имя пользователя длиной до 160 символов.",statusCode:400);
    if (input.FullName is not null) u.FullName = input.FullName.Trim(); if (input.Active.HasValue) u.Active = input.Active.Value;
    if (input.Roles is not null) { var rs = await db.Roles.Where(x => input.Roles.Contains(x.Code)).ToListAsync(); if (input.Roles.Count==0 || rs.Count != input.Roles.Count) return Results.Problem("Неизвестная роль.", statusCode: 400); u.UserRoles.RemoveAll(x=>!rs.Any(r=>r.Id==x.RoleId)); foreach(var role in rs) if(!u.UserRoles.Any(x=>x.RoleId==role.Id)) u.UserRoles.Add(new UserRole {UserId=id,RoleId=role.Id}); }
    await db.SaveChangesAsync();
    var changes=new Dictionary<string,object>();
    if(beforeName!=u.FullName)changes["FullName"]=new {before=beforeName,after=u.FullName};
    if(beforeActive!=u.Active)changes["Active"]=new {before=beforeActive,after=u.Active};
    var afterRoles=input.Roles?.OrderBy(x=>x).ToArray()??beforeRoles;
    if(!beforeRoles.SequenceEqual(afterRoles))changes["Roles"]=new {before=beforeRoles,after=afterRoles};
    await audit.Add(ctx.UserId(),"user_updated","user",null,new{id,changes});await tx.CommitAsync();return Results.NoContent();
});
app.MapGet("/api/roles", [Authorize] async (CashDb db) => Results.Ok(await db.Roles.AsNoTracking().OrderBy(x => x.Code).ToListAsync()));

app.MapGet("/api/currencies", [Authorize] async (CashDb db) => Results.Ok(await db.Currencies.AsNoTracking().OrderBy(x => x.Code).ToListAsync()));
app.MapGet("/api/branches", [Authorize] async (CashDb db, bool? active = null) => Results.Ok(await db.Branches.AsNoTracking().Where(x=>!active.HasValue||x.Active==active).OrderBy(x=>x.Name).ToListAsync()));
app.MapPost("/api/branches", [Authorize(Roles = Admin)] async (BranchInput x, CashDb db, Audit audit, HttpContext ctx) => { var code=x.Code?.Trim().ToUpperInvariant(); var name=x.Name?.Trim(); var city=x.City?.Trim(); if(string.IsNullOrWhiteSpace(code)||code.Length>24||string.IsNullOrWhiteSpace(name)||name.Length>160||string.IsNullOrWhiteSpace(city)||city.Length>100||code.Contains('\0')||name.Contains('\0')||city.Contains('\0')) return Results.BadRequest(); if(await db.Branches.AnyAsync(b=>b.Code==code)) return Results.Conflict(); var branch=new Branch{Code=code,Name=name,City=city}; db.Branches.Add(branch); await db.SaveChangesAsync(); await audit.Add(ctx.UserId(),"branch_created","branch",branch.Id,new{branch.Code,branch.City}); return Results.Created($"/api/branches/{branch.Id}",branch); });
app.MapPatch("/api/branches/{id:long}", [Authorize(Roles = Admin)] async (long id, BranchPatch x, CashDb db, Audit audit, HttpContext ctx) => { var branch=await db.Branches.FindAsync(id); if(branch is null)return Results.NotFound(); if(x.Name is not null){if(string.IsNullOrWhiteSpace(x.Name)||x.Name.Trim().Length>160)return Results.BadRequest();branch.Name=x.Name.Trim();} if(x.City is not null){if(string.IsNullOrWhiteSpace(x.City)||x.City.Trim().Length>100)return Results.BadRequest();branch.City=x.City.Trim();} if(x.Active.HasValue)branch.Active=x.Active.Value; await db.SaveChangesAsync(); await audit.Add(ctx.UserId(),"branch_updated","branch",id,new{x.Name,x.City,x.Active}); return Results.NoContent(); });
app.MapGet("/api/cashboxes", [Authorize] async (HttpContext ctx,CashDb db,ICashboxAccess access,bool? active = null, long? branchId = null) => {var allowed=await access.AllowedCashboxes(ctx.UserId(),ctx.RequestAborted);return Results.Ok(await db.Cashboxes.Include(x => x.Currency).Include(x=>x.Branch).Where(x => allowed.Contains(x.Id)&&(!active.HasValue || x.Active == active)&&(!branchId.HasValue||x.BranchId==branchId)).Select(x => new { x.Id, x.Code, x.Name, branchId=x.BranchId, branch=x.Branch.Name, city=x.Branch.City, currency = x.Currency.Code, x.BalanceLimit, x.Active }).ToListAsync());});
app.MapPost("/api/cashboxes", [Authorize(Roles = Admin)] async (CashboxInput x, HttpContext ctx, CashDb db, Audit audit) =>
{
    if (string.IsNullOrWhiteSpace(x.Code) || x.Code.Trim().Length>24 || !System.Text.RegularExpressions.Regex.IsMatch(x.Code.Trim(),"^[A-Za-z0-9-]+$") || string.IsNullOrWhiteSpace(x.Name) || x.Name.Trim().Length>160 || x.Name.Contains('\0') || x.BalanceLimit < 0 || !await db.Currencies.AnyAsync(c => c.Id == x.CurrencyId) || !await db.Branches.AnyAsync(b=>b.Id==x.BranchId&&b.Active)) return Results.Problem("Некорректные реквизиты кассы или филиала.", statusCode: 400);
    if(!await db.Currencies.AnyAsync(c=>c.Id==x.CurrencyId && c.Code==db.Organizations.Select(o=>o.Currency).First())) return Results.Problem("Касса должна использовать валюту организации.",statusCode:400);
    var code = x.Code.Trim().ToUpperInvariant(); if (await db.Cashboxes.AnyAsync(c => c.Code == code)) return Results.Problem("Код кассы уже существует.", statusCode: 409);
    var c = new Cashbox { Code = code, Name = x.Name.Trim(), BranchId=x.BranchId, CurrencyId = x.CurrencyId, BalanceLimit = x.BalanceLimit }; db.Cashboxes.Add(c); await db.SaveChangesAsync(); await audit.Add(ctx.UserId(), "cashbox_created", "cashbox", c.Id, new { c.Code, c.BranchId }); return Results.Created($"/api/cashboxes/{c.Id}", new {c.Id,c.Code,c.Name,c.BranchId,c.Active});
});
app.MapPatch("/api/cashboxes/{id:long}", [Authorize(Roles = Admin)] async (long id, CashboxPatch x, CashDb db) => { var c = await db.Cashboxes.FindAsync(id); if (c is null) return Results.NotFound(); if(x.Name is not null && (string.IsNullOrWhiteSpace(x.Name)||x.Name.Trim().Length>160||x.Name.Contains('\0')))return Results.Problem("Укажите название кассы длиной до 160 символов.",statusCode:400); if(x.BalanceLimit<0)return Results.Problem("Лимит не может быть отрицательным.",statusCode:400); if (x.Name is not null) c.Name = x.Name.Trim(); if (x.Active.HasValue) c.Active = x.Active.Value; if (x.BalanceLimit.HasValue) c.BalanceLimit = x.BalanceLimit; await db.SaveChangesAsync(); return Results.NoContent(); });
app.MapGet("/api/cashboxes/{id:long}/balance", [Authorize] async (long id, DateOnly? date,HttpContext ctx,CashDb db,ICashboxAccess access) => {await access.Require(ctx.UserId(),id,ctx.RequestAborted);return Results.Ok(await Balance(db, id, date ?? Local.Today));});
app.MapGet("/api/shifts", [Authorize] async (long cashboxId, DateOnly? date, HttpContext ctx, CashDb db, ICashboxAccess access) =>
{
    await access.Require(ctx.UserId(), cashboxId, ctx.RequestAborted);
    var query=db.Shifts.AsNoTracking().Where(x=>x.CashboxId==cashboxId);
    if(date.HasValue) query=query.Where(x=>x.Date==date.Value);
    return Results.Ok(await query.OrderByDescending(x=>x.Date).Take(30).ToListAsync(ctx.RequestAborted));
});
app.MapPost("/api/shifts/open", [Authorize(Roles = Admin + "," + Accountant + ",cashier")] async (ShiftOpenInput x, HttpContext ctx, CashDb db, ICashboxAccess access) =>
{
    await access.Require(ctx.UserId(),x.CashboxId,ctx.RequestAborted);
    if(x.OpeningBalance<0 || x.Date>DateOnly.FromDateTime(DateTime.UtcNow)) return Results.BadRequest();
    await using var tx=await db.Database.BeginTransactionAsync(ctx.RequestAborted);
    var box=await db.Cashboxes.FromSqlInterpolated($"SELECT * FROM \"Cashboxes\" WHERE \"Id\"={x.CashboxId} FOR UPDATE").SingleOrDefaultAsync(ctx.RequestAborted);
    if(box is null || !box.Active) return Results.NotFound();
    if(await db.Shifts.AnyAsync(s=>s.CashboxId==x.CashboxId&&s.Status=="open",ctx.RequestAborted)) return Results.Conflict();
    if(box.BalanceLimit.HasValue && x.OpeningBalance>box.BalanceLimit.Value) return Results.BadRequest();
    var shift=new CashShift{CashboxId=x.CashboxId,Date=x.Date,OpeningBalance=x.OpeningBalance,OpenedBy=ctx.UserId()};
    db.Shifts.Add(shift); await db.SaveChangesAsync(ctx.RequestAborted); await tx.CommitAsync(ctx.RequestAborted); return Results.Created($"/api/shifts/{shift.Id}",shift);
});
app.MapPost("/api/shifts/{id:long}/close", [Authorize(Roles = Admin + "," + Accountant + ",cashier")] async (long id, ShiftCloseInput x, HttpContext ctx, CashDb db, ICashboxAccess access) =>
{
    await using var tx=await db.Database.BeginTransactionAsync(ctx.RequestAborted);
    var shift=await db.Shifts.FromSqlInterpolated($"SELECT * FROM \"CashShifts\" WHERE \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync(ctx.RequestAborted);
    if(shift is null) return Results.NotFound();
    await access.Require(ctx.UserId(),shift.CashboxId,ctx.RequestAborted);
    if(!ShiftRules.CanClose(shift.Status,shift.OpeningBalance,x.ClosingBalance)) return Results.Conflict();
    var expected=await Balance(db,shift.CashboxId,shift.Date);
    shift.ClosingBalance=x.ClosingBalance; shift.ClosingDifference=ShiftRules.ClosingDifference(expected.Balance,x.ClosingBalance); shift.Status="closed"; shift.ClosedBy=ctx.UserId(); shift.ClosedAt=DateTime.UtcNow;
    await db.SaveChangesAsync(ctx.RequestAborted); await tx.CommitAsync(ctx.RequestAborted); return Results.Ok(shift);
});

app.MapPost("/api/transfers", [Authorize(Roles = Admin + "," + Accountant + ",cashier")] async (CashTransferInput x, HttpContext ctx, CashDb db, ICashboxAccess access, Audit audit) =>
{
    if (x.SourceCashboxId == x.TargetCashboxId || x.Amount <= 0 || x.Amount != decimal.Round(x.Amount, 2) || x.Date > Local.Today || x.RequestKey == Guid.Empty || string.IsNullOrWhiteSpace(x.Description) || x.Description.Trim().Length > 500)
        return Results.BadRequest();
    await access.Require(ctx.UserId(), x.SourceCashboxId, ctx.RequestAborted); await access.Require(ctx.UserId(), x.TargetCashboxId, ctx.RequestAborted);
    await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ctx.RequestAborted);
    var existing = await db.Transfers.AsNoTracking().SingleOrDefaultAsync(t=>t.RequestKey==x.RequestKey,ctx.RequestAborted);
    if(existing is not null) return Results.Ok(existing);
    var ids = new[]{x.SourceCashboxId,x.TargetCashboxId}.OrderBy(x=>x).ToArray();
    var boxes = await db.Cashboxes.FromSqlInterpolated($"SELECT * FROM \"Cashboxes\" WHERE \"Id\" IN ({ids[0]},{ids[1]}) ORDER BY \"Id\" FOR UPDATE").ToListAsync(ctx.RequestAborted);
    var source=boxes.SingleOrDefault(b=>b.Id==x.SourceCashboxId); var target=boxes.SingleOrDefault(b=>b.Id==x.TargetCashboxId);
    if(source is null||target is null||!source.Active||!target.Active)return Results.NotFound();
    var sourceBalance=await Balance(db,source.Id,x.Date); if(sourceBalance.Balance<x.Amount)return Results.Conflict();
    if(target.BalanceLimit.HasValue && (await Balance(db,target.Id,x.Date)).Balance+x.Amount>target.BalanceLimit.Value)return Results.Conflict();
    var expenseCategory=await db.Categories.Where(c=>c.Active&&c.Type=="expense").OrderBy(c=>c.Id).FirstOrDefaultAsync(ctx.RequestAborted);
    var incomeCategory=await db.Categories.Where(c=>c.Active&&c.Type=="income").OrderBy(c=>c.Id).FirstOrDefaultAsync(ctx.RequestAborted);
    if(expenseCategory is null||incomeCategory is null)return Results.Conflict();
    var transfer=new CashTransfer{SourceCashboxId=source.Id,TargetCashboxId=target.Id,Date=x.Date,Amount=x.Amount,Description=x.Description.Trim(),RequestKey=x.RequestKey,CreatedBy=ctx.UserId()}; db.Transfers.Add(transfer); await db.SaveChangesAsync(ctx.RequestAborted);
    var sourceSeq=(await db.Operations.Where(o=>o.CashboxId==source.Id&&o.Status=="posted").MaxAsync(o=>(long?)o.PostingSeq,ctx.RequestAborted)??0)+1;
    var targetSeq=(await db.Operations.Where(o=>o.CashboxId==target.Id&&o.Status=="posted").MaxAsync(o=>(long?)o.PostingSeq,ctx.RequestAborted)??0)+1;
    var year=(short)x.Date.Year; var sourceCounter=await db.Sequences.SingleOrDefaultAsync(s=>s.CashboxId==source.Id&&s.Type=="expense"&&s.Year==year,ctx.RequestAborted); var targetCounter=await db.Sequences.SingleOrDefaultAsync(s=>s.CashboxId==target.Id&&s.Type=="income"&&s.Year==year,ctx.RequestAborted); if(sourceCounter is null){sourceCounter=new DocumentSequence{CashboxId=source.Id,Type="expense",Year=year};db.Sequences.Add(sourceCounter);} if(targetCounter is null){targetCounter=new DocumentSequence{CashboxId=target.Id,Type="income",Year=year};db.Sequences.Add(targetCounter);} sourceCounter.LastNumber++; targetCounter.LastNumber++;
    var sourceOp=new CashOperation{CashboxId=source.Id,Date=x.Date,Type="expense",Status="posted",Amount=x.Amount,Description="Межкассовый перевод: "+transfer.Description,CategoryId=expenseCategory.Id,DocumentNumber=$"ПЕРЕВОД-РКО-{source.Code}-{year}-{sourceCounter.LastNumber:D6}",PostingSeq=sourceSeq,TransferId=transfer.Id,RequestKey=Guid.NewGuid(),CreatedBy=ctx.UserId(),PostedBy=ctx.UserId(),PostedAt=DateTime.UtcNow};
    var targetOp=new CashOperation{CashboxId=target.Id,Date=x.Date,Type="income",Status="posted",Amount=x.Amount,Description="Межкассовый перевод: "+transfer.Description,CategoryId=incomeCategory.Id,DocumentNumber=$"ПЕРЕВОД-ПКО-{target.Code}-{year}-{targetCounter.LastNumber:D6}",PostingSeq=targetSeq,TransferId=transfer.Id,RequestKey=Guid.NewGuid(),CreatedBy=ctx.UserId(),PostedBy=ctx.UserId(),PostedAt=DateTime.UtcNow};
    db.Operations.AddRange(sourceOp,targetOp); await db.SaveChangesAsync(ctx.RequestAborted); transfer.SourceOperationId=sourceOp.Id; transfer.TargetOperationId=targetOp.Id; await db.SaveChangesAsync(ctx.RequestAborted); await audit.Add(ctx.UserId(),"cash_transfer_posted","cash_transfer",transfer.Id,new{transfer.SourceCashboxId,transfer.TargetCashboxId,transfer.Amount}); await tx.CommitAsync(ctx.RequestAborted); return Results.Created($"/api/transfers/{transfer.Id}",transfer);
});

app.MapGet("/api/categories", [Authorize] async (CashDb db, string? type, string? q) =>
{
    if(type is not null&&type is not ("income" or "expense"))return Results.Problem("Недопустимое направление категории.",statusCode:400);
    if(q?.Length>160||q?.Contains('\0')==true)return Results.Problem("Некорректный поисковый запрос.",statusCode:400);
    return Results.Ok(await db.Categories.Where(x=>(type==null||x.Type==type)&&(q==null||x.Name.Contains(q))).OrderBy(x=>x.Name).ToListAsync());
});
app.MapGet("/api/counterparties", [Authorize] async (CashDb db, string? q) =>
{
    if(q?.Length>160||q?.Contains('\0')==true)return Results.Problem("Некорректный поисковый запрос.",statusCode:400);
    return Results.Ok(await db.Counterparties.Where(x=>q==null||x.Name.Contains(q)).OrderBy(x=>x.Name).ToListAsync());
});
app.MapGet("/api/settlements", [Authorize] async (CashDb db, long? counterpartyId, string? status) => Results.Ok(await db.Settlements.AsNoTracking().Include(x=>x.Counterparty).Where(x=>(!counterpartyId.HasValue||x.CounterpartyId==counterpartyId)&& (status==null||x.Status==status)).OrderByDescending(x=>x.CreatedAt).Select(x=>new{x.Id,x.CounterpartyId,counterparty=x.Counterparty.Name,x.Direction,x.BasisType,x.BasisReference,x.TotalAmount,x.PaidAmount,remaining=x.TotalAmount-x.PaidAmount,x.Status,x.DueDate}).ToListAsync()));
app.MapPost("/api/settlements", [Authorize(Roles = Admin + "," + Accountant + ",manager")] async (SettlementInput x, CashDb db, Audit audit, HttpContext ctx) =>
{
    var direction=x.Direction?.Trim().ToLowerInvariant(); var basis=x.BasisType?.Trim().ToLowerInvariant(); var reference=x.BasisReference?.Trim();
    if(!await db.Counterparties.AnyAsync(c=>c.Id==x.CounterpartyId&&c.Active)||direction is not ("receivable" or "payable")||basis is not ("order" or "delivery" or "return" or "manual")||string.IsNullOrWhiteSpace(reference)||reference.Length>120||x.TotalAmount<=0||x.TotalAmount!=decimal.Round(x.TotalAmount,2)||x.DueDate<DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-20)))return Results.BadRequest();
    var settlement=new Settlement{CounterpartyId=x.CounterpartyId,Direction=direction,BasisType=basis,BasisReference=reference,TotalAmount=x.TotalAmount,DueDate=x.DueDate}; db.Settlements.Add(settlement); await db.SaveChangesAsync(); await audit.Add(ctx.UserId(),"settlement_created","settlement",settlement.Id,new{settlement.CounterpartyId,settlement.Direction,settlement.BasisType,settlement.BasisReference,settlement.TotalAmount}); return Results.Created($"/api/settlements/{settlement.Id}",settlement);
});
app.MapGet("/api/settlements/{id:long}/history", [Authorize] async (long id,CashDb db, HttpContext ctx, ICashboxAccess access) => { var allowed=await access.AllowedCashboxes(ctx.UserId(),ctx.RequestAborted); return Results.Ok(await db.PaymentAllocations.AsNoTracking().Where(x=>x.SettlementId==id&&allowed.Contains(x.Operation.CashboxId)).Include(x=>x.Operation).OrderBy(x=>x.CreatedAt).Select(x=>new{x.Id,x.SettlementId,x.OperationId,x.Amount,x.CreatedAt,operationType=x.Operation.Type,cashboxId=x.Operation.CashboxId,documentNumber=x.Operation.DocumentNumber}).ToListAsync()); });
app.MapGet("/api/representative-receipts", [Authorize] async (CashDb db, Guid? representativeId, string? status, HttpContext ctx, ICashboxAccess access) => { var allowed=await access.AllowedCashboxes(ctx.UserId(),ctx.RequestAborted); return Results.Ok(await db.RepresentativeReceipts.AsNoTracking().Include(x=>x.Counterparty).Where(x=>allowed.Contains(x.CashboxId)&&(!representativeId.HasValue||x.RepresentativeId==representativeId)&&(status==null||x.Status==status)).OrderByDescending(x=>x.CreatedAt).Select(x=>new{x.Id,x.RepresentativeId,x.CounterpartyId,counterparty=x.Counterparty.Name,x.BasisType,x.BasisReference,x.Amount,x.CashboxId,x.Status,x.RequestKey,x.CreatedAt,x.HandedOverAt,x.AcceptedBy,x.AcceptedAt}).ToListAsync()); });
app.MapPost("/api/representative-receipts", [Authorize(Roles = Admin + "," + Accountant + ",manager")] async (RepresentativeReceiptInput x, HttpContext ctx, CashDb db, ICashboxAccess access, Audit audit) => { if(x.Amount<=0||string.IsNullOrWhiteSpace(x.BasisReference)||x.BasisType is not ("order" or "delivery" or "return" or "manual")) return Results.BadRequest(); await access.Require(ctx.UserId(),x.CashboxId,ctx.RequestAborted); var existing=await db.RepresentativeReceipts.SingleOrDefaultAsync(r=>r.RequestKey==x.RequestKey); if(existing is not null)return Results.Ok(existing); if(!await db.Users.AnyAsync(u=>u.Id==x.RepresentativeId&&u.Active))return Results.BadRequest(); if(!await db.Counterparties.AnyAsync(c=>c.Id==x.CounterpartyId&&c.Active))return Results.BadRequest(); var r=new RepresentativeReceipt{RepresentativeId=x.RepresentativeId,CounterpartyId=x.CounterpartyId,BasisType=x.BasisType!,BasisReference=x.BasisReference.Trim(),Amount=x.Amount,CashboxId=x.CashboxId,RequestKey=x.RequestKey,CreatedBy=ctx.UserId()}; db.RepresentativeReceipts.Add(r); await db.SaveChangesAsync(); await audit.Add(ctx.UserId(),"representative_receipt_created","representative_receipt",r.Id,new{x.RepresentativeId,x.CounterpartyId,x.Amount}); return Results.Created($"/api/representative-receipts/{r.Id}",r); });
app.MapPost("/api/representative-receipts/{id:long}/handover", [Authorize(Roles = Admin + "," + Accountant + ",manager")] async (long id, HttpContext ctx, CashDb db, ICashboxAccess access, Audit audit) => { var r=await db.RepresentativeReceipts.FindAsync(id); if(r is null)return Results.NotFound(); await access.Require(ctx.UserId(),r.CashboxId,ctx.RequestAborted); if(r.Status!="collected")return Results.Conflict(); r.Status="handed_over";r.HandedOverAt=DateTime.UtcNow;await db.SaveChangesAsync();await audit.Add(ctx.UserId(),"representative_receipt_handed_over","representative_receipt",id,new{});return Results.Ok(r); });
app.MapPost("/api/representative-receipts/{id:long}/accept", [Authorize(Roles = Admin + "," + Accountant + ",cashier")] async (long id, HttpContext ctx, CashDb db, ICashboxAccess access, Audit audit) => { var r=await db.RepresentativeReceipts.FindAsync(id); if(r is null)return Results.NotFound(); await access.Require(ctx.UserId(),r.CashboxId,ctx.RequestAborted); if(r.Status!="handed_over")return Results.Conflict(); r.Status="accepted";r.AcceptedAt=DateTime.UtcNow;r.AcceptedBy=ctx.UserId();await db.SaveChangesAsync();await audit.Add(ctx.UserId(),"representative_receipt_accepted","representative_receipt",id,new{});return Results.Ok(r); });
app.MapPost("/api/settlements/{id:long}/allocate", [Authorize(Roles = Admin + "," + Accountant + ",manager")] async (long id, PaymentAllocationInput x, HttpContext ctx, CashDb db, Audit audit) =>
{
    if(x.Amount<=0||x.Amount!=decimal.Round(x.Amount,2))return Results.BadRequest(); await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
    var settlement=await db.Settlements.FromSqlInterpolated($"SELECT * FROM \"Settlements\" WHERE \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync(); if(settlement is null)return Results.NotFound();
    var operation=await db.Operations.SingleOrDefaultAsync(o=>o.Id==x.OperationId); if(operation is null||operation.Status!="posted"||operation.CounterpartyId!=settlement.CounterpartyId)return Results.Problem("Платёж должен быть проведён и принадлежать тому же контрагенту.",statusCode:400); await ctx.RequestServices.GetRequiredService<ICashboxAccess>().Require(ctx.UserId(),operation.CashboxId,ctx.RequestAborted);
    var expectedType=settlement.Direction=="receivable"?"income":"expense"; if(operation.Type!=expectedType)return Results.Problem("Направление платежа не соответствует задолженности.",statusCode:400);
    if(await db.PaymentAllocations.AnyAsync(a=>a.SettlementId==id&&a.OperationId==x.OperationId))return Results.Conflict(); if(settlement.PaidAmount+x.Amount>settlement.TotalAmount)return Results.Problem("Сумма погашения превышает остаток долга.",statusCode:409);
    var allocation=new PaymentAllocation{SettlementId=id,OperationId=x.OperationId,Amount=x.Amount,CreatedBy=ctx.UserId()}; settlement.PaidAmount+=x.Amount; settlement.Status=settlement.PaidAmount==settlement.TotalAmount?"paid":"partially_paid"; db.PaymentAllocations.Add(allocation); await db.SaveChangesAsync(); await audit.Add(ctx.UserId(),"settlement_payment_allocated","settlement",id,new{allocation.OperationId,allocation.Amount,settlement.PaidAmount,settlement.Status}); await tx.CommitAsync(); return Results.Ok(new{settlement.Id,settlement.TotalAmount,settlement.PaidAmount,remaining=settlement.TotalAmount-settlement.PaidAmount,settlement.Status});
});
app.MapGet("/api/document-types", [Authorize] async (CashDb db) => Results.Ok(await db.DocumentTypes.OrderBy(x => x.Name).ToListAsync()));
app.MapPost("/api/document-types", [Authorize(Roles = Admin + "," + Accountant)] async (DocumentTypeInput x, CashDb db) =>
{
    var code=x.Code?.Trim().ToUpperInvariant();var name=x.Name?.Trim();
    if(string.IsNullOrEmpty(code)||code.Length>24||code.Contains('\0')||string.IsNullOrEmpty(name)||name.Length>160||name.Contains('\0'))
        return Results.Problem("Укажите код длиной до 24 символов и название длиной до 160 символов.",statusCode:400);
    db.DocumentTypes.Add(new(){Code=code,Name=name});
    try { await db.SaveChangesAsync(); }
    catch(DbUpdateException error) when(error.InnerException is PostgresException { SqlState:PostgresErrorCodes.UniqueViolation,ConstraintName:"IX_DocumentTypes_NormalizedCode" })
    {
        return Results.Problem("Код вида документа уже существует.",statusCode:409);
    }
    return Results.NoContent();
});

app.MapGet("/api/operations", [Authorize] async (HttpContext ctx,CashDb db,ICashboxAccess access,long? cashboxId, DateOnly? from, DateOnly? to, string? type, string? status, long? categoryId, long? counterpartyId, string? q, string? sort, int page = 1, int pageSize = 25) =>
{
    if(type is not (null or "income" or "expense")||status is not (null or "all" or "draft" or "posted")||sort is not (null or "amount_asc" or "amount_desc" or "date_asc" or "date_desc")||q?.Length>1000||q?.Contains('\0')==true)
        throw new BusinessException("Некорректный фильтр операций.",400);
    var allowed=await access.AllowedCashboxes(ctx.UserId(),ctx.RequestAborted);
    if(!ctx.User.IsInRole(Admin)&&!cashboxId.HasValue)throw new BusinessException("Выберите доступную кассу.",400);
    if(cashboxId.HasValue)await access.Require(ctx.UserId(),cashboxId.Value,ctx.RequestAborted);
    var rows = db.Operations.Include(x => x.Category).Include(x => x.Counterparty).Include(x => x.Author).Where(x => allowed.Contains(x.CashboxId) &&
        (!cashboxId.HasValue || x.CashboxId == cashboxId) && (!from.HasValue || x.Date >= from) && (!to.HasValue || x.Date <= to) &&
        (type == null || x.Type == type) && (status == null || x.Status == status) && (!categoryId.HasValue || x.CategoryId == categoryId) &&
        (!counterpartyId.HasValue || x.CounterpartyId == counterpartyId) && (q == null || x.Description.Contains(q) || (x.DocumentNumber ?? "").Contains(q)));
    var ordered=sort switch { "amount_asc"=>rows.OrderBy(x=>x.Amount).ThenBy(x=>x.Id), "amount_desc"=>rows.OrderByDescending(x=>x.Amount).ThenBy(x=>x.Id), "date_asc"=>rows.OrderBy(x=>x.Date).ThenBy(x=>x.Id), _=>rows.OrderByDescending(x=>x.Date).ThenByDescending(x=>x.Id) };
    return await Paged(ordered, page, pageSize, x => x.View());
});
app.MapGet("/api/operations/{id:long}", [Authorize] async (long id,HttpContext ctx,CashDb db,ICashboxAccess access) => { var x = await db.Operations.Include(x => x.Category).Include(x => x.Counterparty).Include(x => x.Author).SingleOrDefaultAsync(x => x.Id == id);if(x is null)return Results.NotFound();await access.Require(ctx.UserId(),x.CashboxId,ctx.RequestAborted);return Results.Ok(x.View()); });
app.MapPost("/api/operations", [Authorize(Roles = Admin + "," + Accountant + ",cashier")] async (OperationInput x, HttpContext ctx, CashDb db, Audit audit, ICashboxAccess access) =>
{
    await using var tx = await db.Database.BeginTransactionAsync();
    await access.Require(ctx.UserId(),x.CashboxId,ctx.RequestAborted);
    var bad = await Validate(x, db); if (bad is not null) return bad;
    if (x.RequestKey == Guid.Empty) return Results.Problem("Требуется ключ запроса.",statusCode:400);
    var existing = await db.Operations.FirstOrDefaultAsync(o=>o.CreatedBy==ctx.UserId() && o.RequestKey==x.RequestKey); if(existing != null) { if(existing.CashboxId!=x.CashboxId || existing.Amount!=x.Amount || existing.Type!=x.Type || existing.Date!=x.Date || existing.Description!=x.Description?.Trim() || existing.CategoryId!=x.CategoryId || existing.CounterpartyId!=x.CounterpartyId || existing.DocumentTypeId!=x.DocumentTypeId) return Results.Problem("Ключ уже использован для другого запроса.",statusCode:409); return Results.Ok(existing.View()); }
    var o = new CashOperation { RequestKey = x.RequestKey, CashboxId = x.CashboxId, Date = x.Date, Type = x.Type!, Amount = x.Amount, Description = x.Description!.Trim(), CategoryId = x.CategoryId, CounterpartyId = x.CounterpartyId, DocumentTypeId = x.DocumentTypeId, CreatedBy = ctx.UserId() };
    db.Operations.Add(o); await db.SaveChangesAsync(); await audit.Add(ctx.UserId(), "operation_created", "operation", o.Id, new { }); await tx.CommitAsync(); return Results.Created($"/api/operations/{o.Id}", o.View());
});
app.MapPatch("/api/operations/{id:long}", [Authorize(Roles = Admin + "," + Accountant + ",cashier")] async (long id, OperationPatch x, HttpContext ctx, CashDb db, Audit audit, ICashboxAccess access) =>
{
    await using var tx=await db.Database.BeginTransactionAsync();
    var o = await db.Operations.FromSqlInterpolated($"SELECT * FROM \"Operations\" WHERE \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync(); if (o is null) return Results.NotFound();
    await access.Require(ctx.UserId(),o.CashboxId,ctx.RequestAborted);
    if ((o.CreatedBy != ctx.UserId() && !ctx.User.IsInRole(Admin) && !ctx.User.IsInRole(Accountant)) || o.Status != "draft") return Results.Problem("Можно изменить только собственный черновик.", statusCode: 403);
    if (o.Version != x.Version) return Results.Problem("Черновик изменён другим пользователем.", statusCode: 409);
    var input = new OperationInput(x.CashboxId ?? o.CashboxId, x.Date ?? o.Date, x.Type ?? o.Type, x.Amount ?? o.Amount, x.Description ?? o.Description, x.CategoryId ?? o.CategoryId, (x.ClearCounterparty?null:x.CounterpartyId ?? o.CounterpartyId), x.DocumentTypeId ?? o.DocumentTypeId, o.RequestKey);
    await access.Require(ctx.UserId(),input.CashboxId,ctx.RequestAborted);
    var bad = await Validate(input, db, o); if (bad is not null) return bad;
    o.CashboxId = input.CashboxId; o.Date = input.Date; o.Type = input.Type!; o.Amount = input.Amount; o.Description = input.Description!.Trim(); o.CategoryId = input.CategoryId; o.CounterpartyId = input.CounterpartyId; o.DocumentTypeId = input.DocumentTypeId; o.Version++; o.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync(); await audit.Add(ctx.UserId(), "operation_updated", "operation", id, new { }); await tx.CommitAsync(); return Results.Ok(o.View());
});
app.MapDelete("/api/operations/{id:long}", [Authorize(Roles = Admin + "," + Accountant + ",cashier")] async (long id, int version, HttpContext ctx, CashDb db, Audit audit, ICashboxAccess access) =>
{
    await using var tx=await db.Database.BeginTransactionAsync();
    var o = await db.Operations.FromSqlInterpolated($"SELECT * FROM \"Operations\" WHERE \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync(); if (o is null) return Results.NotFound();
    await access.Require(ctx.UserId(),o.CashboxId,ctx.RequestAborted);
    if ((o.CreatedBy != ctx.UserId() && !ctx.User.IsInRole(Admin) && !ctx.User.IsInRole(Accountant)) || o.Status != "draft") return Results.Problem("Можно удалить только собственный черновик.", statusCode: 403);
    if (o.Version != version) return Results.Problem("Черновик изменён другим пользователем.", statusCode: 409);
    db.Operations.Remove(o); await db.SaveChangesAsync(); await audit.Add(ctx.UserId(), "operation_deleted", "operation", id, new { }); await tx.CommitAsync(); return Results.NoContent();
});
app.MapPost("/api/operations/{id:long}/post", [Authorize(Roles = Admin + "," + Accountant + ",cashier")] async (long id,PostingRequest request,HttpContext ctx,ILedgerService ledger)=>Results.Ok(await ledger.Post(id,request.ExpectedVersion,ctx.UserId(),ctx.User.IsInRole(Admin)||ctx.User.IsInRole(Accountant),ctx.RequestAborted)));
app.MapPost("/api/operations/{id:long}/reversals", [Authorize(Roles = Admin + "," + Accountant)] async (long id, ReversalInput x, HttpContext ctx, CashDb db, Audit audit, ICashboxAccess access) =>
{
    await using var tx = await db.Database.BeginTransactionAsync();
    var original = await db.Operations.FromSqlInterpolated($"SELECT * FROM \"Operations\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(); if (original is null) return Results.NotFound();
    await access.Require(ctx.UserId(),original.CashboxId,ctx.RequestAborted);
    if (original.Status != "posted" || original.IsReversal || original.ReversalId.HasValue || await db.Operations.AnyAsync(o=>o.ReversalOfId==id)) return Results.Problem("Эту операцию нельзя сторнировать.", statusCode: 409);
    var date = x.Date ?? Local.Today; if (string.IsNullOrWhiteSpace(x.Reason) || date < original.Date || date > Local.Today) return Results.Problem("Укажите причину и корректную дату.", statusCode: 400);
    var reversal = new CashOperation { CashboxId = original.CashboxId, Date = date, Type = original.Type == "income" ? "expense" : "income", Amount = original.Amount, Description = "СТОРНО: " + original.Description, CategoryId = original.CategoryId, CounterpartyId = original.CounterpartyId, DocumentTypeId = original.DocumentTypeId, CreatedBy = ctx.UserId(), IsReversal = true, ReversalOfId = original.Id, ReversalReason=x.Reason!.Trim() };
    db.Operations.Add(reversal); await db.SaveChangesAsync(); await audit.Add(ctx.UserId(), "reversal_created", "operation", original.Id, new { reversal.Id, x.Reason }); await tx.CommitAsync(); return Results.Created($"/api/operations/{reversal.Id}", reversal.View());
});

app.MapGet("/api/summary", [Authorize] async (HttpContext ctx,CashDb db,ICashboxAccess access,long cashboxId, DateOnly? from, DateOnly? to) =>
{
    await access.Require(ctx.UserId(),cashboxId,ctx.RequestAborted);
    var end = to ?? Local.Today; var start = from ?? (end.DayNumber>=30?end.AddDays(-30):DateOnly.MinValue);
    if(start==DateOnly.MinValue||start>end||end.DayNumber-start.DayNumber>3660)throw new BusinessException("Укажите корректный период отчёта.",400);
    await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ctx.RequestAborted);
    var opening=await Balance(db,cashboxId,start.AddDays(-1));
    var totals=await db.Operations.AsNoTracking().Where(x=>x.CashboxId==cashboxId&&x.Status=="posted"&&x.Date>=start&&x.Date<=end)
        .GroupBy(_=>1).Select(group=>new
        {
            Income=group.Where(x=>x.Type=="income").Sum(x=>x.Amount),
            Expense=group.Where(x=>x.Type=="expense").Sum(x=>x.Amount)
        }).SingleOrDefaultAsync(ctx.RequestAborted);
    var income=totals?.Income??0m;var expense=totals?.Expense??0m;
    await tx.CommitAsync(ctx.RequestAborted);
    return Results.Ok(new{from=start,to=end,opening=opening.Balance,income,expense,closing=opening.Balance+income-expense});
});
app.MapGet("/api/settings", [Authorize] async (CashDb db)=>Results.Ok(await db.Organizations.SingleAsync()));
app.MapGet("/api/dashboard", [Authorize] async (HttpContext ctx,CashDb db,ICashboxAccess access,DateOnly? from,DateOnly? to,long? branchId) =>
{
    var allowed=await access.AllowedCashboxes(ctx.UserId(),ctx.RequestAborted); var end=to??Local.Today; var start=from??end.AddDays(-30);
    if(start>end||end.DayNumber-start.DayNumber>3660)return Results.BadRequest();
    var boxes=await db.Cashboxes.AsNoTracking().Include(x=>x.Branch).Where(x=>allowed.Contains(x.Id)&&(!branchId.HasValue||x.BranchId==branchId)).ToListAsync(ctx.RequestAborted);
    var balances=await Task.WhenAll(boxes.Select(async b=>new{b.Id,b.Code,b.Name,branchId=b.BranchId,branch=b.Branch.Name,balance=(await Balance(db,b.Id,end)).Balance}));
    var totals=await db.Operations.AsNoTracking().Where(x=>allowed.Contains(x.CashboxId)&&x.Status=="posted"&&x.Date>=start&&x.Date<=end).GroupBy(_=>1).Select(g=>new{income=g.Where(x=>x.Type=="income").Sum(x=>x.Amount),expense=g.Where(x=>x.Type=="expense").Sum(x=>x.Amount)}).SingleOrDefaultAsync(ctx.RequestAborted);
    var debt=await db.Settlements.AsNoTracking().Where(x=>x.Status!="paid").GroupBy(x=>x.Direction).Select(g=>new{direction=g.Key,total=g.Sum(x=>x.TotalAmount-x.PaidAmount)}).ToListAsync(ctx.RequestAborted);
    var latest=await db.Operations.AsNoTracking().Where(x=>allowed.Contains(x.CashboxId)).OrderByDescending(x=>x.CreatedAt).Take(10).Select(x=>new{x.Id,x.CashboxId,x.Date,x.Type,x.Status,x.Amount,x.Description,x.DocumentNumber}).ToListAsync(ctx.RequestAborted);
    var openShifts=await db.Shifts.AsNoTracking().Where(x=>allowed.Contains(x.CashboxId)&&x.Status=="open").Select(x=>new{x.Id,x.CashboxId,x.Date,x.OpeningBalance}).ToListAsync(ctx.RequestAborted);
    var warnings=openShifts.Where(x=>x.OpeningBalance<0).Select(x=>new{code="negative_shift_opening",shiftId=x.Id}).ToList();
    var flow=await db.Operations.AsNoTracking().Where(x=>boxes.Select(b=>b.Id).Contains(x.CashboxId)&&x.Status=="posted"&&x.Date>=start&&x.Date<=end).GroupBy(x=>new{x.Date,x.Type}).Select(g=>new{date=g.Key.Date,type=g.Key.Type,amount=g.Sum(x=>x.Amount)}).OrderBy(x=>x.date).ToListAsync(ctx.RequestAborted);
    return Results.Ok(new{period=new{from=start,to=end},totalBalance=balances.Sum(x=>x.balance),income=totals?.income??0m,expense=totals?.expense??0m,balances,debts=debt,cashflow=flow,latestOperations=latest,openShifts,warnings});
});
app.MapGet("/api/operations/{id:long}/document.pdf", [Authorize] async (long id,HttpContext ctx,CashDb db,ICashboxAccess access,IPdfReport pdf)=>{
    var row=await db.Operations.Include(x=>x.Counterparty).SingleOrDefaultAsync(x=>x.Id==id);if(row==null)return Results.NotFound();var org=await db.Organizations.SingleAsync();
    await access.Require(ctx.UserId(),row.CashboxId,ctx.RequestAborted);
    var bytes=pdf.Generate(org.Name,org.Currency,row.Type=="income"?"Приходный кассовый документ":"Расходный кассовый документ",row.Status=="posted"?"Проведено":"Черновик",[new(row.DocumentNumber??$"Черновик №{row.Id}",row.Date,row.Description,row.Type,row.Amount,row.Counterparty?.Name)],balances:false);
    return Results.File(bytes,"application/pdf",$"Faiza-{row.Id}.pdf");
});
app.MapPut("/api/settings", [Authorize(Roles=Admin)] async (OrganizationInput x,CashDb db)=> { if(string.IsNullOrWhiteSpace(x.Name)||x.Name.Trim().Length>160||x.Name.Contains('\0')||string.IsNullOrWhiteSpace(x.Currency)||x.Currency.Length!=3||!x.Currency.All(char.IsAsciiLetter)||x.Currency.Contains('\0'))return Results.BadRequest();var org=await db.Organizations.SingleAsync();if(org.Currency!=x.Currency&&await db.Operations.AnyAsync())return Results.Problem("Валюта не меняется после начала учёта.",statusCode:409);org.Name=x.Name.Trim();org.Currency=x.Currency.ToUpperInvariant();var currency=await db.Currencies.FirstAsync();currency.Code=org.Currency;await db.SaveChangesAsync();return Results.NoContent(); });
app.MapGet("/api/access", [Authorize(Roles=Admin)] async(CashDb db)=>Results.Ok(await db.CashAccess.ToListAsync()));
app.MapPost("/api/access", [Authorize(Roles=Admin)] async(AccessInput x,HttpContext ctx,CashDb db,Audit audit)=>
{
    if(!await db.Users.AnyAsync(u=>u.Id==x.UserId)||!await db.Cashboxes.AnyAsync(c=>c.Id==x.CashboxId))return Results.Problem("Пользователь или касса не найдены.",statusCode:400);
    await using var tx=await db.Database.BeginTransactionAsync();
    if(!await db.CashAccess.AnyAsync(a=>a.UserId==x.UserId&&a.CashboxId==x.CashboxId))
    {
        db.CashAccess.Add(new(){UserId=x.UserId,CashboxId=x.CashboxId});
        await db.SaveChangesAsync();
        await audit.Add(ctx.UserId(),"cash_access_granted","CashAccess",x.CashboxId,new{ x.UserId,x.CashboxId });
    }
    await tx.CommitAsync();
    return Results.NoContent();
});
app.MapDelete("/api/access/{userId:guid}/{cashboxId:long}", [Authorize(Roles=Admin)] async(Guid userId,long cashboxId,HttpContext ctx,CashDb db,Audit audit)=>
{
    await using var tx=await db.Database.BeginTransactionAsync();
    var access=await db.CashAccess.SingleOrDefaultAsync(a=>a.UserId==userId&&a.CashboxId==cashboxId);
    if(access is not null)
    {
        db.CashAccess.Remove(access);
        await db.SaveChangesAsync();
        await audit.Add(ctx.UserId(),"cash_access_revoked","CashAccess",cashboxId,new{UserId=userId,CashboxId=cashboxId});
    }
    await tx.CommitAsync();
    return Results.NoContent();
});
app.MapGet("/api/category-summary",[Authorize] async(HttpContext ctx,CashDb db,ICashboxAccess access,long cashboxId,DateOnly from,DateOnly to)=>{await access.Require(ctx.UserId(),cashboxId,ctx.RequestAborted);return Results.Ok(await db.Operations.Where(x=>x.CashboxId==cashboxId&&x.Status=="posted"&&x.Date>=from&&x.Date<=to).GroupBy(x=>new {x.Category.Name,x.Type}).Select(g=>new {g.Key.Name,g.Key.Type,amount=g.Sum(x=>x.Amount)}).OrderByDescending(x=>x.amount).ToListAsync());});
app.MapGet("/api/analytics",[Authorize] async(HttpContext ctx,CashDb db,ICashboxAccess access,long cashboxId,DateOnly from,DateOnly to)=>{await access.Require(ctx.UserId(),cashboxId,ctx.RequestAborted);return Results.Ok(await db.Operations.Where(x=>x.CashboxId==cashboxId && x.Status=="posted" && x.Date>=from && x.Date<=to).GroupBy(x=>new {x.Date,x.Type}).Select(g=>new {g.Key.Date,g.Key.Type,amount=g.Sum(x=>x.Amount)}).OrderBy(x=>x.Date).ToListAsync());});

}


static async Task<IResult?> Validate(OperationInput x, CashDb db, CashOperation? previous=null)
{
    if (x.Date==DateOnly.MinValue || x.Description?.Length>1000 || x.Description?.Contains('\0')==true || x.Type is not ("income" or "expense") || x.Amount <= 0 || x.Amount > 9999999999999999.99m || decimal.Round(x.Amount,2) != x.Amount || string.IsNullOrWhiteSpace(x.Description) || x.Date > Local.Today) return Results.Problem("Проверьте тип, дату, сумму и содержание.", statusCode: 400);
    var box = await db.Cashboxes.Include(c => c.Opening).SingleOrDefaultAsync(c => c.Id == x.CashboxId);
    if (box is null || !box.Active || (box.Opening is not null && x.Date < box.Opening.Date)) return Results.Problem("Касса недоступна для этой даты.", statusCode: 400);
    if(previous?.IsReversal==true)
    {
        if(x.CashboxId!=previous.CashboxId || x.Date!=previous.Date || x.Type!=previous.Type || x.Amount!=previous.Amount || x.CategoryId!=previous.CategoryId || x.CounterpartyId!=previous.CounterpartyId || x.DocumentTypeId!=previous.DocumentTypeId)
            return Results.Problem("Финансовые реквизиты сторно изменять нельзя.",statusCode:409);
        var original=await db.Operations.AsNoTracking().SingleOrDefaultAsync(o=>o.Id==previous.ReversalOfId);
        if(original is null || original.ReversalId.HasValue || !ReversalRules.MatchesOriginal(previous,original,Local.Today))
            return Results.Problem("Сторно не соответствует исходному документу.",statusCode:409);
        return null; // Reversal retains the original category even though its direction is opposite.
    }
    var previousCategory=previous?.CategoryId;var previousType=previous?.Type;var previousParty=previous?.CounterpartyId;
    if (!await db.Categories.AnyAsync(c => c.Id == x.CategoryId && (c.Active || c.Id==previousCategory&&c.Type==previousType) && c.Type == x.Type)) return Results.Problem("Укажите активную категорию нужного типа.", statusCode: 400);
    if (x.CounterpartyId.HasValue && !await db.Counterparties.AnyAsync(c => c.Id == x.CounterpartyId && (c.Active||c.Id==previousParty))) return Results.Problem("Контрагент не найден.", statusCode: 400);
    if (x.DocumentTypeId.HasValue && !await db.DocumentTypes.AnyAsync(c => c.Id == x.DocumentTypeId && c.Active)) return Results.Problem("Тип документа не найден.", statusCode: 400);
    return null;
}
static async Task<BalanceDto> Balance(CashDb db, long boxId, DateOnly date)
{
    var box = await db.Cashboxes.Include(x => x.Opening).SingleOrDefaultAsync(x => x.Id == boxId) ?? throw new KeyNotFoundException("Касса не найдена.");
    var opening = box.Opening is not null && box.Opening.Date <= date ? box.Opening.Amount : 0m;
    var delta = await db.Operations.Where(x => x.CashboxId == boxId && x.Status == "posted" && x.Date <= date).SumAsync(x => x.Type == "income" ? x.Amount : -x.Amount);
    return new(boxId, date, opening + delta);
}
static async Task<IResult> Paged<T, R>(IQueryable<T> q, int page, int size, Func<T, R> map) { page = Math.Max(1, page); size = Math.Clamp(size, 1, 100); var count = await q.CountAsync(); var rows = await q.Skip((page - 1) * size).Take(size).ToListAsync(); return Results.Ok(new { page, pageSize = size, total = count, items = rows.Select(map) }); }
public static async Task Seed(CashDb db, IConfiguration cfg, ILogger log)
{
    if(!await db.Organizations.AnyAsync()) { db.Organizations.Add(new()); await db.SaveChangesAsync(); }
    if (!await db.Roles.AnyAsync()) { db.Roles.AddRange(new Role { Code = Admin, Name = "Администратор" }, new Role { Code = Accountant, Name = "Бухгалтер" }, new Role { Code = Manager, Name = "Руководитель" }, new Role { Code = Auditor, Name = "Аудитор" }, new Role {Code="cashier",Name="Кассир"}); db.Currencies.Add(new Currency { Code = "TJS", Name = "Таджикский сомони" }); await db.SaveChangesAsync(); }
    if (!await db.Users.AnyAsync())
    {
        var pass = cfg["Seed:AdminPassword"];
        if (Password.Valid(pass)) { var u = new User { Login = cfg["Seed:AdminLogin"] ?? "admin", FullName = "Системный администратор", PasswordHash = Password.Hash(pass!), MustChangePassword = true }; u.UserRoles.Add(new UserRole { Role = await db.Roles.SingleAsync(x => x.Code == Admin) }); db.Users.Add(u); await db.SaveChangesAsync(); }
        else log.LogWarning("Первый администратор не создан. Задайте Seed__AdminPassword.");
    }
}


}
