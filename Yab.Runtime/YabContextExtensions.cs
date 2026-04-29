using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Yab.Runtime
{
    public static class YabContextExtensions
    {
        public static IApplicationBuilder UseYabContext(this IApplicationBuilder app)
        {
            return app.UseMiddleware<YabTracingMiddleware>();
        }

        public static IHttpClientBuilder AddYabContextPropagation(this IHttpClientBuilder builder)
        {
            return builder.AddHttpMessageHandler<YabDelegatingHandler>();
        }
        
        public static IServiceCollection AddYabContext(this IServiceCollection services)
        {
            services.AddTransient<YabDelegatingHandler>();
            return services;
        }
    }
}
