namespace ModsDude.Client.Core.Models;

public record LocalMod(string Id, string Version, string Name, string Description, Func<Stream> GetStream);
