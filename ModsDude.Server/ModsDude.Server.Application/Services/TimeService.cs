using ModsDude.Server.Domain.Common;

namespace ModsDude.Server.Application.Services;
public class TimeService : ITimeService
{
    public DateTime Now()
    {
        return DateTime.UtcNow;
    }
}
