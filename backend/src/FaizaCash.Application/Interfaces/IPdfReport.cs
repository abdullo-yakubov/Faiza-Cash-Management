namespace FaizaCash.Application;
public record ReportLine(string Number, DateOnly Date, string Description, string Type, decimal Amount, string? Counterparty=null,decimal? Balance=null,string Status="posted");
public interface IPdfReport
{
    byte[] Generate(string organization,string currency,string title,string subtitle,IReadOnlyList<ReportLine> lines, decimal opening=0, bool balances=true);
    byte[] GenerateVoucher(string organization,string currency,bool income,bool posted,string number,DateOnly date,string cashbox,string cashboxCode,string category,string? counterparty,string description,decimal amount);
    /// <summary>Универсальный табличный отчёт (авансы и др.) с итоговыми строками.</summary>
    byte[] GenerateTable(string organization,string title,string subtitle,IReadOnlyList<string> headers,IReadOnlyList<IReadOnlyList<string>> rows,IReadOnlyList<string> footer);
    /// <summary>Акт инвентаризации наличных денежных средств: пересчёт по номиналам, комиссия, подписи.</summary>
    byte[] GenerateInventoryAct(InventoryActData act);
}
