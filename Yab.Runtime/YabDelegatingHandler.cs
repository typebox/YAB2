using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Yab.Runtime
{
    public class YabDelegatingHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var testId = YabContext.CurrentTestId;
            if (!string.IsNullOrEmpty(testId))
            {
                request.Headers.TryAddWithoutValidation(YabHeaderConstants.TestContextHeader, testId);
            }

            var traceId = YabContext.CurrentTraceId;
            if (!string.IsNullOrEmpty(traceId))
            {
                request.Headers.TryAddWithoutValidation(YabHeaderConstants.TraceIdHeader, traceId);
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
