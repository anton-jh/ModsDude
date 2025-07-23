using ModsDude.Client.Core.ModsDudeServer.Generated;
using Spectre.Console;

namespace ModsDude.Client.Cli.Commands.Shared.ArgumentCollectors;
internal class UserCollector(
    IAnsiConsole ansiConsole,
    IUsersClient usersClient)
{
    public async Task<UserDto> Collect(string? fromSettings, IEnumerable<string> memberIds, CancellationToken cancellationToken)
    {
        UserDto? user = null;
        var searchTerm = fromSettings;

        while (user is null)
        {
            ansiConsole.Clear();

            if (searchTerm is not null)
            {
                user = (await usersClient.SearchUserV1Async(searchTerm, cancellationToken)).User;
            }

            if (user is null)
            {
                var users = await usersClient.GetUsersV1Async(cancellationToken);

                var idsToExclude = memberIds.ToList();

                users = users.Where(x => !idsToExclude.Contains(x.Id)).ToList();

                if (users.Count != 0)
                {
                    var searchSelection = new UserDto() { Id = "", Username = "Search..." };

                    var prompt = new SelectionPrompt<UserDto>()
                        .Title("Select user")
                        .UseConverter(x => x.Username)
                        .EnableSearch()
                        .WrapAround();

                    prompt.AddChoice(searchSelection);

                    prompt.AddChoiceGroup(new UserDto() { Id = "", Username = "Users in your network" }, users);

                    var selection = await ansiConsole.PromptAsync(prompt, cancellationToken);

                    if (selection != searchSelection)
                    {
                        user = selection;
                        continue;
                    }
                }

                searchTerm = await ansiConsole.AskAsync<string>("Exact username:", cancellationToken);
            }
        }

        return user;
    }
}
