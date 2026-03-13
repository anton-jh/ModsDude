using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.WinForms.Authentication;
using System.ComponentModel;

namespace ModsDude.Client.WinForms;

public partial class MainWindow : Form
{
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly AuthenticationService _authenticationService;
    private readonly IReposClient _reposClient;
    private readonly BindingList<RepoMembershipWrapper> _repos = [];


    public MainWindow(
        AuthenticationService authenticationService,
        IReposClient reposClient)
    {
        InitializeComponent();
        FormClosing += (_, __) => _lifetimeCts.Cancel();
        _authenticationService = authenticationService;
        _reposClient = reposClient;
    }


    private async void MainWindow_Load(object sender, EventArgs e)
    {
        await _authenticationService.Get(_lifetimeCts.Token);

        repoSelector.DataSource = _repos;
        repoSelector.DisplayMember = nameof(RepoMembershipWrapper.RepoName);

        await RefreshRepos();
    }


    private async Task RefreshRepos()
    {
        var repos = await _reposClient.GetMyReposV1Async(_lifetimeCts.Token);

        if (InvokeRequired)
        {
            Invoke(new Action(() =>
            {
                _repos.Clear();
                foreach (var repo in repos)
                {
                    _repos.Add(new(repo));
                }
            }));
        }
        else
        {
            _repos.Clear();
            foreach (var repo in repos)
            {
                _repos.Add(new(repo));
            }
        }
    }
}

public class RepoMembershipWrapper(RepoMembershipDto dto)
{
    public RepoMembershipDto Dto { get; } = dto;
    public string RepoName { get; } = dto.Repo.Name;
}
