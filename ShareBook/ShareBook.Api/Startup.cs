﻿using AutoMapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rollbar.NetCore.AspNet;
using ShareBook.Api.AutoMapper;
using ShareBook.Api.Configuration;
using ShareBook.Api.Middleware;
using ShareBook.Api.Services;
using ShareBook.Repository;
using ShareBook.Service;
using ShareBook.Service.Muambator;
using ShareBook.Service.Notification;
using ShareBook.Service.Server;
using ShareBook.Service.Upload;
using Swashbuckle.AspNetCore.Swagger;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ShareBook.Api
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            RegisterHealthChecks(services, Configuration.GetConnectionString("DefaultConnection"));

            services.RegisterRepositoryServices();
            //auto mapper start
            //AutoMapperConfig.RegisterMappings();

            services.AddAutoMapper(typeof(Startup));

            services
                .AddControllers()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.IgnoreNullValues = true;
                    //options.SerializerSettings.NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore;
                });
            //.SetCompatibilityVersion(CompatibilityVersion.Version_2_2);

            services
                .AddHttpContextAccessor()
                .Configure<RollbarOptions>(options => Configuration.GetSection("Rollbar").Bind(options))
                .AddRollbarLogger(loggerOptions => loggerOptions.Filter = (loggerName, loglevel) => loglevel >= LogLevel.Trace);

            services.Configure<ImageSettings>(options => Configuration.GetSection("ImageSettings").Bind(options));

            services.Configure<EmailSettings>(options => Configuration.GetSection("EmailSettings").Bind(options));

            services.Configure<ServerSettings>(options => Configuration.GetSection("ServerSettings").Bind(options));

            services.Configure<NotificationSettings>(options => Configuration.GetSection("NotificationSettings").Bind(options));

            services.AddHttpContextAccessor();

            JWTConfig.RegisterJWT(services, Configuration);

            services.RegisterSwagger();

            //services.AddSwaggerGen(c =>
            //{
            //    c.SwaggerDoc("v1", new Info { Title = "SHAREBOOK API", Version = "v1" });
            //    c.ResolveConflictingActions(x => x.First());
            //    c.AddSecurityDefinition("Bearer", new ApiKeyScheme
            //    {
            //        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
            //        Name = "Authorization",
            //        In = "header",
            //        Type = "apiKey"
            //    });
            //    c.AddSecurityRequirement(new Dictionary<string, IEnumerable<string>> { { "Bearer", Enumerable.Empty<string> () },
            //    });
            //});

            services.AddCors(options =>
            {
                options.AddPolicy("AllowAllHeaders",
                    builder =>
                    {
                        builder.AllowAnyOrigin()
                            .AllowAnyHeader()
                            .AllowAnyMethod();
                    });
            });

            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(Configuration.GetConnectionString("DefaultConnection")));

            RollbarConfigurator
                .Configure(environment: Configuration.GetSection("Rollbar:Environment").Value,
                           isActive: Configuration.GetSection("Rollbar:IsActive").Value,
                           token: Configuration.GetSection("Rollbar:Token").Value);

            MuambatorConfigurator.Configure(Configuration.GetSection("Muambator:Token").Value, Configuration.GetSection("Muambator:IsActive").Value);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        //public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            bool rollbarActive = Convert.ToBoolean(Configuration.GetSection("Rollbar:IsActive").Value.ToLower());
            if (rollbarActive)
            {
                app.UseRollbarMiddleware();
            }

            app.UseHealthChecks("/hc");
            //app.UseCors("AllowAllHeaders");

            app.UseDeveloperExceptionPage();
            app.UseExceptionHandlerMiddleware();

            app.UseStaticFiles(new StaticFileOptions()
            {
                OnPrepareResponse = (context) =>
                {
                    // Enable cors
                    context.Context.Response.Headers["Access-Control-Allow-Origin"] = "*";
                }
            });

            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v2/swagger.json", "SHAREBOOK API V2");
            });

            // IMPORTANT: Make sure UseCors() is called BEFORE this
            //app.UseMvc(routes =>
            //{
            //    routes.MapRoute(
            //        name: "default",
            //        template: "{controller=Book}/{action=Index}/{id?}");

            //    routes.MapSpaFallbackRoute(
            //        name: "spa-fallback",
            //        defaults: new { controller = "ClientSpa", action = "Index" });
            //});
            app.UseRouting();

            app.UseCors("AllowAllHeaders");

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Book}/{action=Index}/{id?}");

                endpoints.MapFallbackToController("Index", "ClientSpa");

                endpoints.MapHealthChecks("/health", new HealthCheckOptions()
                {
                    AllowCachingResponses = false,
                    ResultStatusCodes =
                    {
                        [HealthStatus.Healthy] = StatusCodes.Status200OK,
                        [HealthStatus.Degraded] = StatusCodes.Status200OK,
                        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
                    }
                });
            });

            using (var serviceScope = app.ApplicationServices.GetRequiredService<IServiceScopeFactory>().CreateScope())
            {
                var scopeServiceProvider = serviceScope.ServiceProvider;
                var context = serviceScope.ServiceProvider.GetService<ApplicationDbContext>();
                context.Database.Migrate();
                if (env.IsDevelopment() || env.IsStaging())
                {
                    var sharebookSeeder = new ShareBookSeeder(context);
                    sharebookSeeder.Seed();
                }
            }
        }

        private void RegisterHealthChecks(IServiceCollection services, string connectionString)
        {
            services.AddHealthChecks()
                .AddSqlServer(connectionString);
        }
    }
}