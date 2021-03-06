﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using CacheManager.Core;
using IdentityServer4.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ocelot.Authentication.Handler.Creator;
using Ocelot.Authentication.Handler.Factory;
using Ocelot.Authorisation;
using Ocelot.Cache;
using Ocelot.Claims;
using Ocelot.Configuration.Authentication;
using Ocelot.Configuration.Creator;
using Ocelot.Configuration.File;
using Ocelot.Configuration.Parser;
using Ocelot.Configuration.Provider;
using Ocelot.Configuration.Repository;
using Ocelot.Configuration.Setter;
using Ocelot.Configuration.Validator;
using Ocelot.DownstreamRouteFinder.Finder;
using Ocelot.DownstreamRouteFinder.UrlMatcher;
using Ocelot.DownstreamUrlCreator;
using Ocelot.DownstreamUrlCreator.UrlTemplateReplacer;
using Ocelot.Headers;
using Ocelot.Infrastructure.Claims.Parser;
using Ocelot.Infrastructure.RequestData;
using Ocelot.LoadBalancer.LoadBalancers;
using Ocelot.Logging;
using Ocelot.Middleware;
using Ocelot.QueryStrings;
using Ocelot.Request.Builder;
using Ocelot.Requester;
using Ocelot.Requester.QoS;
using Ocelot.Responder;
using Ocelot.ServiceDiscovery;
using FileConfigurationProvider = Ocelot.Configuration.Provider.FileConfigurationProvider;
using Ocelot.RateLimit;
using Ocelot.Controllers;
using System.Reflection;

namespace Ocelot.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddOcelotOutputCaching(this IServiceCollection services, Action<ConfigurationBuilderCachePart> settings)
        {
            var cacheManagerOutputCache = CacheFactory.Build<HttpResponseMessage>("OcelotOutputCache", settings);
            var ocelotCacheManager = new OcelotCacheManagerCache<HttpResponseMessage>(cacheManagerOutputCache);
            services.AddSingleton<ICacheManager<HttpResponseMessage>>(cacheManagerOutputCache);
            services.AddSingleton<IOcelotCache<HttpResponseMessage>>(ocelotCacheManager);

            return services;
        }

        public static IServiceCollection AddOcelot(this IServiceCollection services, IConfigurationRoot configurationRoot)
        {
            services.Configure<FileConfiguration>(configurationRoot);
            services.AddSingleton<IOcelotConfigurationCreator, FileOcelotConfigurationCreator>();
            services.AddSingleton<IOcelotConfigurationRepository, InMemoryOcelotConfigurationRepository>();
            services.AddSingleton<IConfigurationValidator, FileConfigurationValidator>();
            services.AddSingleton<IBaseUrlFinder, BaseUrlFinder>();
            services.AddSingleton<IClaimsToThingCreator, ClaimsToThingCreator>();
            services.AddSingleton<IAuthenticationOptionsCreator, AuthenticationOptionsCreator>();
            services.AddSingleton<IUpstreamTemplatePatternCreator, UpstreamTemplatePatternCreator>();
            services.AddSingleton<IRequestIdKeyCreator, RequestIdKeyCreator>();
            services.AddSingleton<IServiceProviderConfigurationCreator,ServiceProviderConfigurationCreator>();
            services.AddSingleton<IQoSOptionsCreator, QoSOptionsCreator>();
            services.AddSingleton<IReRouteOptionsCreator, ReRouteOptionsCreator>();
            services.AddSingleton<IRateLimitOptionsCreator, RateLimitOptionsCreator>();

            var identityServerConfiguration = IdentityServerConfigurationCreator.GetIdentityServerConfiguration();
            
            if(identityServerConfiguration != null)
            {
                services.AddSingleton<IIdentityServerConfiguration>(identityServerConfiguration);
                services.AddSingleton<IHashMatcher, HashMatcher>();
                services.AddIdentityServer()
                    .AddTemporarySigningCredential()
                    .AddInMemoryApiResources(new List<ApiResource>
                    {
                        new ApiResource
                        {
                            Name = identityServerConfiguration.ApiName,
                            Description = identityServerConfiguration.Description,
                            Enabled = identityServerConfiguration.Enabled,
                            DisplayName = identityServerConfiguration.ApiName,
                            Scopes = identityServerConfiguration.AllowedScopes.Select(x => new Scope(x)).ToList(),
                            ApiSecrets = new List<Secret>
                            {
                                new Secret
                                {
                                    Value = identityServerConfiguration.ApiSecret.Sha256()
                                }
                            }
                        }
                    })
                    .AddInMemoryClients(new List<Client>
                    {
                        new Client
                        {
                            ClientId = identityServerConfiguration.ApiName,
                            AllowedGrantTypes = GrantTypes.ResourceOwnerPassword,
                            ClientSecrets = new List<Secret> {new Secret(identityServerConfiguration.ApiSecret.Sha256())},
                            AllowedScopes = identityServerConfiguration.AllowedScopes,
                            AccessTokenType = identityServerConfiguration.AccessTokenType,
                            Enabled = identityServerConfiguration.Enabled,
                            RequireClientSecret = identityServerConfiguration.RequireClientSecret
                        }
                    }).AddResourceOwnerValidator<OcelotResourceOwnerPasswordValidator>();
            }

            var assembly = typeof(FileConfigurationController).GetTypeInfo().Assembly;

            services.AddMvcCore()
                .AddApplicationPart(assembly)
                .AddControllersAsServices()
                .AddAuthorization()
                .AddJsonFormatters();

            services.AddLogging();
            services.AddSingleton<IFileConfigurationRepository, FileConfigurationRepository>();
            services.AddSingleton<IFileConfigurationSetter, FileConfigurationSetter>();
            services.AddSingleton<IFileConfigurationProvider, FileConfigurationProvider>();
            services.AddSingleton<IQosProviderHouse, QosProviderHouse>();
            services.AddSingleton<IQoSProviderFactory, QoSProviderFactory>();
            services.AddSingleton<IServiceDiscoveryProviderFactory, ServiceDiscoveryProviderFactory>();
            services.AddSingleton<ILoadBalancerFactory, LoadBalancerFactory>();
            services.AddSingleton<ILoadBalancerHouse, LoadBalancerHouse>();
            services.AddSingleton<IOcelotLoggerFactory, AspDotNetLoggerFactory>();
            services.AddSingleton<IUrlBuilder, UrlBuilder>();
            services.AddSingleton<IRemoveOutputHeaders, RemoveOutputHeaders>();
            services.AddSingleton<IOcelotConfigurationProvider, OcelotConfigurationProvider>();
            services.AddSingleton<IClaimToThingConfigurationParser, ClaimToThingConfigurationParser>();
            services.AddSingleton<IAuthoriser, ClaimsAuthoriser>();
            services.AddSingleton<IAddClaimsToRequest, AddClaimsToRequest>();
            services.AddSingleton<IAddHeadersToRequest, AddHeadersToRequest>();
            services.AddSingleton<IAddQueriesToRequest, AddQueriesToRequest>();
            services.AddSingleton<IClaimsParser, ClaimsParser>();
            services.AddSingleton<IUrlPathToUrlTemplateMatcher, RegExUrlMatcher>();
            services.AddSingleton<IUrlPathPlaceholderNameAndValueFinder, UrlPathPlaceholderNameAndValueFinder>();
            services.AddSingleton<IDownstreamPathPlaceholderReplacer, DownstreamTemplatePathPlaceholderReplacer>();
            services.AddSingleton<IDownstreamRouteFinder, DownstreamRouteFinder.Finder.DownstreamRouteFinder>();
            services.AddSingleton<IHttpRequester, HttpClientHttpRequester>();
            services.AddSingleton<IHttpResponder, HttpContextResponder>();
            services.AddSingleton<IRequestCreator, HttpRequestCreator>();
            services.AddSingleton<IErrorsToHttpStatusCodeMapper, ErrorsToHttpStatusCodeMapper>();
            services.AddSingleton<IAuthenticationHandlerFactory, AuthenticationHandlerFactory>();
            services.AddSingleton<IAuthenticationHandlerCreator, AuthenticationHandlerCreator>();
            services.AddSingleton<IRateLimitCounterHandler, MemoryCacheRateLimitCounterHandler>();
            services.AddSingleton<IHttpClientCache, MemoryHttpClientCache>();

            // see this for why we register this as singleton http://stackoverflow.com/questions/37371264/invalidoperationexception-unable-to-resolve-service-for-type-microsoft-aspnetc
            // could maybe use a scoped data repository
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.AddScoped<IRequestScopedDataRepository, HttpDataRepository>();
            services.AddMemoryCache();
            return services;
        }
    }
}
