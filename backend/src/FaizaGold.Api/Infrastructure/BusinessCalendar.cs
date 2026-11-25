namespace FaizaGold.Api.Infrastructure;

/// <summary>Границы рабочего дня в часовом поясе компании (по умолчанию Душанбе, UTC+5).</summary>
public sealed class BusinessCalendar
{
    public TimeZoneInfo Zone { get; }

    public BusinessCalendar(IConfiguration config)
    {
        var id = config["Company:TimeZone"] ?? "Asia/Dushanbe";
        try { Zone = TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { Zone = TimeZoneInfo.CreateCustomTimeZone(id, TimeSpan.FromHours(5), id, id); }
    }

    public DateTimeOffset StartOfDay(DateOnly day)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, Zone.GetUtcOffset(local)).ToUniversalTime();
    }

    public DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Zone).DateTime);

    public DateTimeOffset ToLocal(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, Zone);
}
