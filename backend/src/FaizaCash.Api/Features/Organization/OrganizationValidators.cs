using FaizaCash.Application;
using FaizaCash.Domain;

namespace FaizaCash.Api;

/// <summary>Проверки входных данных настроек организации.</summary>
internal static class OrganizationValidators
{
    /// <summary>Название и код валюты — прежние правила (400 без текста), порог согласования — сумма с точностью до дирама или null.</summary>
    public static IResult? Settings(OrganizationInput x)
    {
        if (string.IsNullOrWhiteSpace(x.Name) || x.Name.Trim().Length > 160 || x.Name.Contains('\0') || string.IsNullOrWhiteSpace(x.Currency) || x.Currency.Length != 3 || !x.Currency.All(char.IsAsciiLetter) || x.Currency.Contains('\0'))
            return Results.BadRequest();
        if (x.ExpenseApprovalThreshold.HasValue && !Money.IsValid(x.ExpenseApprovalThreshold.Value))
            return Results.Problem("Порог согласования должен быть положительной суммой с точностью до дирама или пустым (согласование выключено).", statusCode: 400);
        return null;
    }
}
