using Bogus;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Models;
public static class ModFakers
{
    static ModFakers()
    {
        // Set deterministic output
        Randomizer.Seed = new Random(1234);
    }

    public static Faker<ModAttributeDto> ModAttributeDtoFaker => new Faker<ModAttributeDto>()
        .RuleFor(a => a.Key, f => f.Commerce.ProductAdjective())
        .RuleFor(a => a.Value, f => f.Random.Bool(0.8f) ? f.Lorem.Sentence() : null);

    public static Faker<ModVersionDto> BaseModVersionDtoFaker => new Faker<ModVersionDto>()
        .RuleFor(v => v.VersionId, f => f.Random.Guid().ToString())
        .RuleFor(v => v.SequenceNumber, f => 1) // Will be overridden later
        .RuleFor(v => v.DisplayName, f => f.Commerce.ProductName())
        .RuleFor(v => v.Description, f => f.Lorem.Paragraph())
        .RuleFor(v => v.Attributes, f => ModAttributeDtoFaker.Generate(f.Random.Int(1, 5)))
        .RuleFor(v => v.Created, f => f.Date.Past());

    public static Faker<ModDto> ModDtoFaker => new Faker<ModDto>()
        .RuleFor(m => m.Id, f => f.Random.Guid().ToString())
        .RuleFor(m => m.Created, f => f.Date.Past(2))
        .RuleFor(m => m.Updated, (f, m) => f.Date.Between(m.Created, DateTime.Now))
        .RuleFor(m => m.Versions, (f, m) =>
        {
            var versionCount = f.Random.Int(1, 2);
            var baseVersion = BaseModVersionDtoFaker.Generate();

            return Enumerable.Range(1, versionCount)
                .Select(i => baseVersion with { SequenceNumber = i })
                .ToList();
        });
}
