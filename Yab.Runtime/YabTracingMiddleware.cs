using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace Yab.Runtime
{
    public class YabTracingMiddleware
    {
        private readonly RequestDelegate _next;
        public YabTracingMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.Request.Headers.TryGetValue("X-Yab-TraceId", out var traceId))
            {
                YabTracker.SetTraceId(traceId!);
            }
            if (context.Request.Headers.TryGetValue("X-Yab-TestId", out var testId))
            {
                YabTracker.SetCurrentTest(testId!);
            }
            
            try { await _next(context); }
            finally
            {
                YabTracker.ClearCurrentTest();
                YabTracker.SetTraceId(null!);
            }
        }
    }
}
