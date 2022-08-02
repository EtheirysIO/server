using Prometheus;

namespace EtheirysSynchronosServer.Metrics
{
    public class LockedProxyCounter
    {
        private readonly Counter _c;

        public LockedProxyCounter(Counter c)
        {
            _c = c;
        }

        public void Inc(double inc = 1d)
        {
            lock (_c)
            {
                _c.Inc(inc);
            }
        }
    }
}
