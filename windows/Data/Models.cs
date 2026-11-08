using System;
using System.Threading;
using System.Threading.Tasks;

namespace UsageOrbit.Data
{
    public enum ReadState { Ok, NeedsAuth, ConnectionError, RateLimited, ServiceError, NoData }

    public sealed class UsageWindow
    {
        public double? UsedPercent { get; set; }
        public int? WindowMinutes { get; set; }
        public DateTimeOffset? ResetsAt { get; set; }
    }

    public sealed class UsageReading
    {
        public UsageWindow Session { get; set; }
        public UsageWindow Weekly { get; set; }
        public ReadState State { get; set; }
        public DateTimeOffset? LastSuccessAt { get; set; }
        public DateTimeOffset? RetryAfter { get; set; }
        public string Message { get; set; }

        public static UsageReading Failure(ReadState state, string message)
        {
            return new UsageReading { State = state, Message = message };
        }

        public static UsageReading Success(UsageWindow session, UsageWindow weekly)
        {
            return new UsageReading {
                Session = session, Weekly = weekly,
                State = (session == null || !session.UsedPercent.HasValue) &&
                    (weekly == null || !weekly.UsedPercent.HasValue) ? ReadState.NoData : ReadState.Ok,
                LastSuccessAt = DateTimeOffset.UtcNow, Message = ""
            };
        }
    }

    public interface IUsageSource
    {
        Task<UsageReading> ReadAsync(CancellationToken cancellation);
    }
}
