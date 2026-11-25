using FaizaCash.Application;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;
namespace FaizaCash.Infrastructure;

/// <summary>Печатные формы волны 1: универсальная таблица и акт инвентаризации наличных.</summary>
public sealed partial class PdfReport
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    private static void Setup()
    {
        // Academic diploma use. Reassess QuestPDF license before commercial use.
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.UseEnvironmentFonts = false;
    }

    public byte[] GenerateTable(string organization, string title, string subtitle, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows, IReadOnlyList<string> footer)
    {
        Setup();
        return Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape()); page.Margin(28); page.DefaultTextStyle(s => s.FontSize(9));
            page.Header().Column(c => { Logo(c); c.Item().Text("FAIZA CASH / " + organization).FontColor("#1C5743").Bold().FontSize(12); c.Item().PaddingTop(12).Text(title).FontSize(20).Bold(); c.Item().PaddingTop(6).Text(subtitle).FontColor(Colors.Grey.Darken1); });
            page.Content().PaddingVertical(18).Column(c =>
            {
                c.Item().Table(t =>
                {
                    t.ColumnsDefinition(cols => { foreach (var _ in headers) cols.RelativeColumn(); });
                    t.Header(h => { foreach (var name in headers) h.Cell().Background("#EAF4ED").Padding(5).Text(name).Bold(); });
                    foreach (var row in rows)
                        foreach (var cell in row) t.Cell().BorderBottom(1).BorderColor("#E9EEEB").Padding(6).Text(cell);
                });
                if (rows.Count == 0) c.Item().PaddingTop(10).Text("Нет данных за выбранные условия.").FontColor(Colors.Grey.Darken1);
                foreach (var line in footer) c.Item().PaddingTop(8).Text(line).Bold();
                c.Item().PaddingTop(24).Text("Учебное представление. Не является официальной бухгалтерской формой.").FontColor(Colors.Grey.Darken1).FontSize(8);
            });
            page.Footer().AlignRight().Text(t => { t.Span("Faiza Cash · Страница "); t.CurrentPageNumber(); t.Span(" / "); t.TotalPages(); });
        })).GeneratePdf();
    }

    public byte[] GenerateInventoryAct(InventoryActData act)
    {
        Setup();
        string Money(decimal value) => value.ToString("N2", Ru) + " " + act.Currency;
        return Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4); page.Margin(40); page.DefaultTextStyle(s => s.FontSize(10));
            page.Header().Column(c =>
            {
                Logo(c);
                c.Item().AlignCenter().Text(act.Organization).Bold().FontColor("#1C5743").FontSize(14);
                c.Item().PaddingTop(14).AlignCenter().Text("АКТ ИНВЕНТАРИЗАЦИИ НАЛИЧНЫХ ДЕНЕЖНЫХ СРЕДСТВ").Bold().FontSize(15);
                c.Item().PaddingTop(6).AlignCenter().Text($"№ {act.Number} от {act.CountedAtLocal.ToString("dd.MM.yyyy HH:mm", Ru)}");
            });
            page.Content().PaddingVertical(18).Column(c =>
            {
                c.Item().Text($"Касса: {act.Cashbox} ({act.CashboxCode})");
                c.Item().PaddingTop(4).Text("Комиссия в составе: " + string.Join(", ", act.Commission));
                c.Item().PaddingTop(4).Text("провела пересчёт наличных денежных средств по номиналам. Результаты пересчёта:");
                c.Item().PaddingTop(10).Table(t =>
                {
                    t.ColumnsDefinition(cols => { cols.RelativeColumn(2); cols.RelativeColumn(1.4f); cols.RelativeColumn(1); cols.RelativeColumn(2); });
                    t.Header(h => { foreach (var name in new[] { "Номинал", "Вид", "Количество, шт.", "Сумма" }) h.Cell().Background("#EAF4ED").Padding(5).Text(name).Bold(); });
                    foreach (var line in act.Denominations.Where(x => x.Count > 0).OrderByDescending(x => x.Value))
                    {
                        t.Cell().BorderBottom(1).BorderColor("#E9EEEB").Padding(5).Text(Money(line.Value));
                        t.Cell().BorderBottom(1).BorderColor("#E9EEEB").Padding(5).Text(line.Kind == "coin" ? "монета" : "банкнота");
                        t.Cell().BorderBottom(1).BorderColor("#E9EEEB").Padding(5).Text(line.Count.ToString(Ru));
                        t.Cell().BorderBottom(1).BorderColor("#E9EEEB").Padding(5).Text(Money(line.Total));
                    }
                });
                c.Item().PaddingTop(12).Text("Фактическое наличие: " + Money(act.Actual)).Bold();
                c.Item().PaddingTop(2).Text("Сумма прописью: " + RussianMoneyWords.Format(act.Actual, act.Currency)).FontSize(9);
                c.Item().PaddingTop(6).Text("По данным учёта (расчётный остаток): " + Money(act.Expected));
                var result = act.Difference == 0 ? "Расхождений не выявлено." : act.Difference > 0 ? "Излишек: " + Money(act.Difference) : "Недостача: " + Money(-act.Difference);
                c.Item().PaddingTop(6).Text("Результат инвентаризации: " + result).Bold();
                if (!string.IsNullOrWhiteSpace(act.Note)) c.Item().PaddingTop(6).Text("Примечание: " + act.Note);
                c.Item().PaddingTop(28).Text("Подписи членов комиссии:").Bold();
                foreach (var member in act.Commission)
                    c.Item().PaddingTop(18).Row(r => { r.RelativeItem().Text(member); r.RelativeItem().AlignRight().Text("____________________________"); });
                c.Item().PaddingTop(22).Row(r => { r.RelativeItem().Text("Материально ответственное лицо (кассир)"); r.RelativeItem().AlignRight().Text("____________________________"); });
                c.Item().PaddingTop(28).Text("Учебное представление. Не является утверждённой бухгалтерской формой.").FontColor(Colors.Grey.Darken1).FontSize(8);
            });
            page.Footer().AlignRight().Text(t => { t.Span("Faiza Cash · Страница "); t.CurrentPageNumber(); t.Span(" / "); t.TotalPages(); });
        })).GeneratePdf();
    }
}
