using FaizaCash.Application;
using FaizaCash.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;
namespace FaizaCash.Infrastructure;
public sealed class LedgerService(CashDb db, Audit audit, ICashboxAccess access) : ILedgerService
{
    public async Task<object> Post(long id,int expectedVersion,Guid userId,bool privileged,CancellationToken cancellationToken=default)
    {
        try
        {
            await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
            var operation=await db.Operations.FromSqlInterpolated($"SELECT * FROM \"Operations\" WHERE \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync(cancellationToken) ?? throw new BusinessException("Операция не найдена",404);
            if(operation.Version!=expectedVersion)throw new BusinessException("Черновик изменён после подтверждения. Обновите документ и подтвердите его снова.",409);
            if(operation.Status!="draft")throw new BusinessException("Операция уже проведена");
            var box=await db.Cashboxes.FromSqlInterpolated($"SELECT * FROM \"Cashboxes\" WHERE \"Id\"={operation.CashboxId} FOR UPDATE").SingleAsync(cancellationToken);
            await db.Entry(operation).ReloadAsync(cancellationToken);
            await access.Require(userId,operation.CashboxId,cancellationToken);
            if(operation.CashboxId!=box.Id)throw new BusinessException("Касса черновика изменилась. Повторите запрос.");
            if(operation.Status!="draft")throw new BusinessException("Операция уже проведена");
            if(operation.CreatedBy!=userId&&!privileged)throw new BusinessException("Можно проводить только собственный черновик",403);
            if(!box.Active)throw new BusinessException("Касса неактивна");
            if(!Money.IsValid(operation.Amount))throw new BusinessException("Недопустимая сумма",400);
            var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow,"Asia/Dushanbe"));
            if(operation.Date>today)throw new BusinessException("Будущая дата недопустима",400);
            var opening=await db.Openings.SingleOrDefaultAsync(x=>x.CashboxId==box.Id,cancellationToken);
            if(opening!=null&&operation.Date<opening.Date)throw new BusinessException("Дата раньше начала учёта",400);
            var ledger=await db.Operations.Where(x=>x.CashboxId==box.Id&&x.Status=="posted").Select(x=>new PostingItem(x.Date,x.PostingSeq,x.Type,x.Amount)).ToListAsync(cancellationToken);
            var seq=(ledger.Max(x=>x.PostingSeq)??0)+1;
            var shift=await db.Shifts.FromSqlInterpolated($"SELECT * FROM \"Shifts\" WHERE \"CashboxId\"={box.Id} AND \"Date\"={operation.Date} FOR UPDATE").SingleOrDefaultAsync(cancellationToken);
            if(shift is null)throw new BusinessException("Сначала откройте кассовую смену.",409);
            if(shift.Status!="open")throw new BusinessException("Cash shift is closed",409);
            var currentBalance=BalanceRules.BalanceAt(opening?.Amount??0,ledger,operation.Date);
            if(!ShiftRules.CanPost(currentBalance,box.BalanceLimit,operation.Amount,operation.Type))throw new BusinessException("Operation exceeds cashbox limit or creates a negative balance",409);
            if(!BalanceRules.CanPost(opening?.Amount??0,ledger,new(operation.Date,seq,operation.Type,operation.Amount)))throw new BusinessException("Расход создаёт отрицательный остаток");
            var counter=await db.Sequences.SingleOrDefaultAsync(x=>x.CashboxId==box.Id&&x.Type==operation.Type&&x.Year==operation.Date.Year,cancellationToken);
            if(counter==null){counter=new(){CashboxId=box.Id,Type=operation.Type,Year=(short)operation.Date.Year};db.Sequences.Add(counter);}
            counter.LastNumber++;operation.ShiftId=shift.Id;operation.PostingSeq=seq;operation.DocumentNumber=$"{(operation.Type=="income"?"ПКО":"РКО")}-{box.Code}-{operation.Date.Year}-{counter.LastNumber:D6}";
            operation.Status="posted";operation.PostedBy=userId;operation.PostedAt=DateTime.UtcNow;operation.Version++;
            if(operation.IsReversal)
            {
                var original=await db.Operations.FromSqlInterpolated($"SELECT * FROM \"Operations\" WHERE \"Id\"={operation.ReversalOfId} FOR UPDATE").SingleOrDefaultAsync(cancellationToken);
                if(original is null || !ReversalRules.MatchesOriginal(operation,original,today))throw new BusinessException("Сторно не соответствует исходному документу.");
                if(original.ReversalId.HasValue || await db.Operations.AnyAsync(x=>x.ReversalOfId==original.Id&&x.Id!=operation.Id,cancellationToken))throw new BusinessException("Документ уже сторнирован");
                original.ReversalId=operation.Id;original.Version++;
            }
            await db.SaveChangesAsync(cancellationToken);
            await audit.Add(userId,"operation_posted","operation",operation.Id,new {operation.DocumentNumber,operation.ReversalReason});
            await tx.CommitAsync(cancellationToken);
            return operation.View();
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            throw new BusinessException("Проведение документа конфликтует с другой транзакцией. Повторите попытку.", 409);
        }
        catch (InvalidOperationException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            throw new BusinessException("Проведение документа конфликтует с другой транзакцией. Повторите попытку.", 409);
        }
    }
}
