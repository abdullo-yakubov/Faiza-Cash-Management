using FaizaCash.Domain;
namespace FaizaCash.Application;
public static class BalanceRules
{
    public static decimal BalanceAt(decimal opening, IEnumerable<PostingItem> ledger, DateOnly date) =>
        opening + ledger.Where(x=>x.Date<=date).OrderBy(x=>x.Date).ThenBy(x=>x.PostingSeq).Sum(x=>Money.Signed(x.Type,x.Amount));
    public static bool CanPost(decimal opening, IEnumerable<PostingItem> ledger, PostingItem candidate)
    {
        foreach(var row in ledger.Append(candidate).OrderBy(x=>x.Date).ThenBy(x=>x.PostingSeq))
        {
            opening+=Money.Signed(row.Type,row.Amount);
            if(opening<0)return false;
        }
        return true;
    }
}

public static class ShiftRules
{
    public static bool CanPost(decimal currentBalance, decimal? limit, decimal amount, string type)
    {
        if (amount <= 0 || type is not ("income" or "expense")) return false;
        var next = type == "income" ? currentBalance + amount : currentBalance - amount;
        return next >= 0 && (!limit.HasValue || next <= limit.Value);
    }

    public static bool CanClose(string status, decimal expected, decimal actual) =>
        status == "open" && expected >= 0 && actual >= 0;

    public static decimal ClosingDifference(decimal expected, decimal actual) => actual - expected;
}
