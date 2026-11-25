using FaizaCash.Domain;
using FaizaCash.Application;
using FaizaCash.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FaizaCash.Api;

/// <summary>Проверки входных данных кассовых документов и снимок реквизитов для печатной формы.</summary>
internal static class OperationsValidators
{
internal static async Task<IResult?> Validate(OperationInput x, CashDb db, CashOperation? previous=null)
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
    if (await db.Categories.AnyAsync(c => c.Id == x.CategoryId && c.IsSystem)) return Results.Problem("Системные категории межкассового перевода в документах вручную не используются.", statusCode: 400);
    if (!await db.Categories.AnyAsync(c => c.Id == x.CategoryId && (c.Active || c.Id==previousCategory&&c.Type==previousType) && c.Type == x.Type)) return Results.Problem("Укажите активную категорию нужного типа.", statusCode: 400);
    if (x.CounterpartyId.HasValue && !await db.Counterparties.AnyAsync(c => c.Id == x.CounterpartyId && (c.Active||c.Id==previousParty))) return Results.Problem("Контрагент не найден.", statusCode: 400);
    if (x.DocumentTypeId.HasValue && !await db.DocumentTypes.AnyAsync(c => c.Id == x.DocumentTypeId && c.Active)) return Results.Problem("Тип документа не найден.", statusCode: 400);
    return null;
}
internal static async Task CaptureDocumentSnapshot(CashDb db,CashOperation operation,CancellationToken cancellationToken)
{
    var box=await db.Cashboxes.AsNoTracking().SingleAsync(x=>x.Id==operation.CashboxId,cancellationToken);
    var category=await db.Categories.AsNoTracking().SingleAsync(x=>x.Id==operation.CategoryId,cancellationToken);
    var organization=await db.Organizations.AsNoTracking().SingleAsync(cancellationToken);
    operation.OrganizationNameSnapshot=organization.Name;
    operation.CashboxNameSnapshot=box.Name;
    operation.CashboxCodeSnapshot=box.Code;
    operation.CategoryNameSnapshot=category.Name;
    operation.CounterpartyNameSnapshot=operation.CounterpartyId.HasValue
        ?await db.Counterparties.AsNoTracking().Where(x=>x.Id==operation.CounterpartyId).Select(x=>x.Name).SingleAsync(cancellationToken)
        :null;
}
}
