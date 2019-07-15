using System;
using CMS.Activities.Loggers;
using CMS.ContactManagement;
using CMS.ContactManagement.Internal;
using CMS.Core;
using Umbrella.Kentico.Utilities.ContactManagement;
using Umbrella.Kentico.Utilities.ContactManagement.Abstractions;
using Umbrella.Kentico.Utilities.ContactManagement.Options;
using Umbrella.Kentico.Utilities.Middleware;
using Umbrella.Kentico.Utilities.Middleware.Options;
using Umbrella.Kentico.Utilities.Users;
using Umbrella.Kentico.Utilities.Users.Abstractions;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class IServiceCollectionExtensions
    {
        public static IServiceCollection AddUmbrellaKenticoUtilities(this IServiceCollection services)
        {
            // Contact Management
            services.AddScoped(ctx => Service.Resolve<IContactProcessingChecker>());
            services.AddScoped(ctx => Service.Resolve<IContactCreator>());
            services.AddScoped(ctx => Service.Resolve<IContactRelationAssigner>());
            services.AddScoped(ctx => Service.Resolve<IContactPersistentStorage>());
            services.AddScoped(ctx => Service.Resolve<IContactMergeService>());
            services.AddSingleton<IMembershipActivityLogger, MembershipActivityLogger>();

            services.AddScoped<IKenticoContactManager, KenticoContactManager>();
            services.AddSingleton<IKenticoUserNameNormalizer, KenticoUserNameNormalizer>();

            // Default Options - These can be replaced by calls to the Configure* methods below.
            services.AddSingleton<KenticoContactManagerOptions>();

            // Middleware
            services.AddScoped<MergeMarketingContactMiddleware>();

            return services;
        }

        public static IServiceCollection ConfigureKenticoContactManagerOptions(this IServiceCollection services, Action<IServiceProvider, KenticoContactManagerOptions> optionsBuilder)
            => services.ConfigureUmbrellaOptions(optionsBuilder);

        public static IServiceCollection ConfigureMergeMarketingContactMiddlewareOptions(this IServiceCollection services, Action<IServiceProvider, MergeMarketingContactMiddlewareOptions> optionsBuilder)
            => services.ConfigureUmbrellaOptions(optionsBuilder);
    }
}