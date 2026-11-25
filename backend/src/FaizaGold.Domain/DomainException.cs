namespace FaizaGold.Domain;

/// <summary>Нарушение бизнес-правила. Текст сообщения показывается пользователю.</summary>
public sealed class DomainException(string message) : Exception(message);
