namespace ModsDude.Server.Domain.Exceptions;
public class DomainValidationException(string developerMessage)
    : Exception(developerMessage);
