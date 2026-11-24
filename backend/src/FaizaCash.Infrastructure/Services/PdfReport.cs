using FaizaCash.Application;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;
namespace FaizaCash.Infrastructure;
public sealed class PdfReport : IPdfReport
{
    public byte[] GenerateVoucher(string organization,string currency,bool income,bool posted,string number,DateOnly date,string cashbox,string cashboxCode,string category,string? counterparty,string description,decimal amount)
    {
        QuestPDF.Settings.License=LicenseType.Community;
        QuestPDF.Settings.UseEnvironmentFonts=false;
        string Money(decimal value)=>value.ToString("N2",CultureInfo.GetCultureInfo("ru-RU"))+" "+currency;
        var title=income?"ПРИХОДНЫЙ КАССОВЫЙ ОРДЕР":"РАСХОДНЫЙ КАССОВЫЙ ОРДЕР";
        return Document.Create(document=>document.Page(page=>
        {
            page.Size(PageSizes.A4);page.Margin(42);page.DefaultTextStyle(style=>style.FontSize(11));
            page.Header().Column(column=>
            {
                column.Item().AlignCenter().Text(organization).Bold().FontColor("#1C5743").FontSize(15);
                column.Item().PaddingTop(20).AlignCenter().Text(title).Bold().FontSize(19);
                column.Item().PaddingTop(8).AlignCenter().Text(posted?"ПРОВЕДЁН":"ЧЕРНОВИК").FontColor(posted?"#1C5743":"#8A5A00").Bold();
            });
            page.Content().PaddingVertical(24).Column(column=>
            {
                column.Item().Row(row=>
                {
                    row.RelativeItem().Text("Документ № "+number).Bold();
                    row.ConstantItem(150).AlignRight().Text(date.ToString("dd.MM.yyyy",CultureInfo.GetCultureInfo("ru-RU")));
                });
                column.Item().PaddingTop(16).Border(1).BorderColor("#C9D8D0").Padding(16).Column(details=>
                {
                    details.Item().Text("Касса: "+cashbox+" ("+cashboxCode+")");
                    details.Item().PaddingTop(10).Text("Контрагент: "+(string.IsNullOrWhiteSpace(counterparty)?"—":counterparty));
                    details.Item().PaddingTop(10).Text("Категория: "+category);
                    details.Item().PaddingTop(10).Text("Основание: "+description);
                });
                column.Item().PaddingTop(22).Text("Сумма: "+Money(amount)).Bold().FontSize(16);
                column.Item().PaddingTop(8).Text("Сумма прописью: "+RussianMoneyWords.Format(amount,currency)).FontSize(11);
                column.Item().PaddingTop(54).Row(row=>
                {
                    row.RelativeItem().Column(signature=>{signature.Item().Text(income?"Принято от":"Выдано");signature.Item().PaddingTop(26).Text("____________________________");});
                    row.RelativeItem().Column(signature=>{signature.Item().Text("Ответственный сотрудник");signature.Item().PaddingTop(26).Text("____________________________");});
                });
                column.Item().PaddingTop(30).Text("Учебное представление. Не является утверждённой бухгалтерской формой.").FontColor(Colors.Grey.Darken1).FontSize(8);
            });
            page.Footer().AlignRight().Text(t=>{t.Span("Faiza Cash · Страница ");t.CurrentPageNumber();t.Span(" / ");t.TotalPages();});
        })).GeneratePdf();
    }

    public byte[] Generate(string organization,string currency,string title,string subtitle,IReadOnlyList<ReportLine> lines,decimal opening=0,bool balances=true)
    {
        // Academic diploma use. Reassess QuestPDF license before commercial use.
        QuestPDF.Settings.License=LicenseType.Community;
        QuestPDF.Settings.UseEnvironmentFonts=false;
        string Money(decimal v)=>v.ToString("N2",CultureInfo.GetCultureInfo("ru-RU"))+" "+currency;
        return Document.Create(document=>document.Page(page=>
        {
            page.Size(balances?PageSizes.A4.Landscape():PageSizes.A4);page.Margin(28);page.DefaultTextStyle(s=>s.FontSize(9));
            page.Header().Column(c=>{c.Item().Text("FAIZA CASH / "+organization).FontColor("#1C5743").Bold().FontSize(12);c.Item().PaddingTop(12).Text(title).FontSize(20).Bold();c.Item().PaddingTop(6).Text(subtitle).FontColor(Colors.Grey.Darken1);});
            page.Content().PaddingVertical(18).Column(c=>
            {
                if(balances)c.Item().Text("Остаток на начало: "+Money(opening));
                c.Item().PaddingTop(12).Table(t=>
                {
                    t.ColumnsDefinition(cols=>{cols.ConstantColumn(62);cols.RelativeColumn(1.3f);cols.RelativeColumn(2);cols.RelativeColumn(1.2f);cols.ConstantColumn(74);cols.ConstantColumn(74);if(balances)cols.ConstantColumn(78);});
                    t.Header(h=>{foreach(var name in (balances?new[]{"Дата","Документ","Основание","Контрагент","Приход","Расход","Остаток"}:new[]{"Дата","Документ","Основание","Контрагент","Приход","Расход"}))h.Cell().Background("#EAF4ED").Padding(5).Text(name).Bold();});
                    foreach(var row in lines)
                    {
                        t.Cell().BorderBottom(1).BorderColor("#E9EEEB").Padding(6).Text(row.Date.ToString("dd.MM.yyyy"));
                        t.Cell().BorderBottom(1).BorderColor("#E9EEEB").Padding(6).Text(row.Number+(row.Status=="draft"?" · Черновик":""));
                        t.Cell().BorderBottom(1).BorderColor("#E9EEEB").Padding(6).Text(row.Description);
                        t.Cell().BorderBottom(1).BorderColor("#E9EEEB").Padding(6).Text(row.Counterparty??"—");
                        t.Cell().BorderBottom(1).BorderColor("#E9EEEB").Padding(6).Text(row.Type=="income"?Money(row.Amount):"—");
                        t.Cell().BorderBottom(1).BorderColor("#E9EEEB").Padding(6).Text(row.Type=="expense"?Money(row.Amount):"—");
                        if(balances)t.Cell().BorderBottom(1).BorderColor("#E9EEEB").Padding(6).Text(row.Balance.HasValue?Money(row.Balance.Value):"—");
                    }
                });
                // "opening" lines are cashbox initial balances activated inside the period: part of the balance, not of income.
                var income=lines.Where(x=>x.Type=="income").Sum(x=>x.Amount);var expense=lines.Where(x=>x.Type=="expense").Sum(x=>x.Amount);var activations=lines.Where(x=>x.Type=="opening").Sum(x=>x.Amount);
                c.Item().PaddingTop(16).Text("Приход: "+Money(income)+"   Расход: "+Money(expense)+(activations!=0?"   Начальные остатки касс в периоде: "+Money(activations):"")).Bold();
                if(balances)c.Item().PaddingTop(8).Text("Остаток на конец: "+Money(opening+activations+income-expense)).Bold().FontSize(12);
                if(balances)c.Item().PaddingTop(40).Row(signatures=>
                {
                    signatures.RelativeItem().Column(signature=>{signature.Item().Text("Кассир");signature.Item().PaddingTop(28).Text("____________________________");});
                    signatures.RelativeItem().Column(signature=>{signature.Item().Text("Бухгалтер");signature.Item().PaddingTop(28).Text("____________________________");});
                });
                c.Item().PaddingTop(24).Text("Учебное представление. Не является официальной бухгалтерской формой.").FontColor(Colors.Grey.Darken1).FontSize(8);
            });
            page.Footer().AlignRight().Text(t=>{t.Span("Faiza Cash · Страница ");t.CurrentPageNumber();t.Span(" / ");t.TotalPages();});
        })).GeneratePdf();
    }
}
