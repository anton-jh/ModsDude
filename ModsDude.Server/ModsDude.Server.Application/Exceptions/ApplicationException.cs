namespace ModsDude.Server.Application.Exceptions;
public class ApplicationException(string? message = null, Exception? innerException = null) : Exception(message, innerException);
