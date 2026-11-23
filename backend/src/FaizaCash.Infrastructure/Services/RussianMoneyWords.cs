using System.Globalization;

namespace FaizaCash.Infrastructure;

/// <summary>Formats a positive or negative money amount in Russian for printable vouchers.</summary>
public static class RussianMoneyWords
{
    private static readonly string[] OnesMale = ["", "один", "два", "три", "четыре", "пять", "шесть", "семь", "восемь", "девять"];
    private static readonly string[] OnesFemale = ["", "одна", "две", "три", "четыре", "пять", "шесть", "семь", "восемь", "девять"];
    private static readonly string[] Teens = ["десять", "одиннадцать", "двенадцать", "тринадцать", "четырнадцать", "пятнадцать", "шестнадцать", "семнадцать", "восемнадцать", "девятнадцать"];
    private static readonly string[] Tens = ["", "", "двадцать", "тридцать", "сорок", "пятьдесят", "шестьдесят", "семьдесят", "восемьдесят", "девяносто"];
    private static readonly string[] Hundreds = ["", "сто", "двести", "триста", "четыреста", "пятьсот", "шестьсот", "семьсот", "восемьсот", "девятьсот"];

    public static string Format(decimal amount, string currency)
    {
        var value = decimal.Round(Math.Abs(amount), 2, MidpointRounding.AwayFromZero);
        if (value > 999_999_999_999_999m) throw new ArgumentOutOfRangeException(nameof(amount));
        var whole = decimal.ToInt64(decimal.Truncate(value));
        var fraction = decimal.ToInt32((value - whole) * 100m);
        var (majorOne, majorFew, majorMany, minorOne, minorFew, minorMany) = CurrencyWords(currency);
        var words = whole == 0 ? "ноль" : Spell(whole);
        var result = (amount < 0 ? "минус " : "") + words + " " + Noun(whole, majorOne, majorFew, majorMany);
        return result + " " + fraction.ToString("D2", CultureInfo.InvariantCulture) + " " + Noun(fraction, minorOne, minorFew, minorMany);
    }

    private static (string, string, string, string, string, string) CurrencyWords(string currency) => currency.ToUpperInvariant() switch
    {
        "TJS" => ("сомони", "сомони", "сомони", "дирам", "дирама", "дирамов"),
        "RUB" => ("рубль", "рубля", "рублей", "копейка", "копейки", "копеек"),
        _ => ("единица валюты", "единицы валюты", "единиц валюты", "сотая", "сотых", "сотых")
    };

    private static string Spell(long value)
    {
        var groups = new[]
        {
            ("", "", "", false),
            ("тысяча", "тысячи", "тысяч", true),
            ("миллион", "миллиона", "миллионов", false),
            ("миллиард", "миллиарда", "миллиардов", false),
            ("триллион", "триллиона", "триллионов", false)
        };
        var parts = new List<string>();
        for (var index = 0; value > 0; index++, value /= 1000)
        {
            var group = (int)(value % 1000);
            if (group == 0) continue;
            var words = SpellThree(group, index == 1);
            var forms = groups[index];
            parts.Insert(0, index == 0 ? words : words + " " + Noun(group, forms.Item1, forms.Item2, forms.Item3));
        }
        return string.Join(" ", parts);
    }

    private static string SpellThree(int value, bool feminine)
    {
        var parts = new List<string>();
        if (value / 100 > 0) parts.Add(Hundreds[value / 100]);
        var rest = value % 100;
        if (rest >= 10 && rest < 20) parts.Add(Teens[rest - 10]);
        else
        {
            if (rest / 10 > 0) parts.Add(Tens[rest / 10]);
            if (rest % 10 > 0) parts.Add((feminine ? OnesFemale : OnesMale)[rest % 10]);
        }
        return string.Join(" ", parts);
    }

    private static string Noun(long value, string one, string few, string many)
    {
        var lastTwo = value % 100;
        if (lastTwo is >= 11 and <= 14) return many;
        return (value % 10) switch { 1 => one, 2 or 3 or 4 => few, _ => many };
    }
}
