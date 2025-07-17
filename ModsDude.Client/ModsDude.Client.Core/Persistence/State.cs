using System.Text.Json.Nodes;

namespace ModsDude.Client.Core.Persistence;
internal class State(int version)
{
    public int Version { get; set; } = version;
    public Dictionary<string, JsonNode> Misc { get; init; } = [];
}
