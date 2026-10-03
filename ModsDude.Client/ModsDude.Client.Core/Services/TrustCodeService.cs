using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Services;

public class TrustCodeService(ITrustCodesClient trustCodesClient) : ITrustCodeService
{
    public async Task<CurrentUserDto> Redeem(string code, CancellationToken cancellationToken)
    {
        var request = new RedeemTrustCodeRequest() { Code = code };

        try
        {
            return await trustCodesClient.RedeemTrustCodeV1Async(request, cancellationToken);
        }
        catch (ApiException<CustomProblemDetails> ex) when (ex.Result.Type == ProblemType.TrustCodeNotFound)
        {
            throw new UserFriendlyException("No such trust code", ex.Result.Detail, ex);
        }
        catch (ApiException<CustomProblemDetails> ex) when (ex.Result.Type == ProblemType.TrustCodeNotUsable)
        {
            throw new UserFriendlyException("That trust code no longer works", ex.Result.Detail, ex);
        }
        catch (ApiException<CustomProblemDetails> ex) when (ex.Result.Type == ProblemType.TrustCodeRedemptionConflict)
        {
            throw new UserFriendlyException("Try that again", ex.Result.Detail, ex);
        }
    }
}
