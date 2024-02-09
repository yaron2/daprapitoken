// ------------------------------------------------------------------------
// Copyright 2021 The Dapr Authors
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//     http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// ------------------------------------------------------------------------

namespace RoutingSample
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Dapr;
    using Dapr.AspNetCore;
    using Dapr.Client;
    using Google.Protobuf.WellKnownTypes;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Startup class.
    /// </summary>
    public class Startup
    {
        /// <summary>
        /// State store name.
        /// </summary>
        public const string StoreName = "statestore";

        /// <summary>
        /// Initializes a new instance of the <see cref="Startup"/> class.
        /// </summary>
        /// <param name="configuration">Configuration.</param>
        public Startup(IConfiguration configuration)
        {
            this.Configuration = configuration;
        }

        /// <summary>
        /// Gets the configuration.
        /// </summary>
        public IConfiguration Configuration { get; }

        /// <summary>
        /// Configures Services.
        /// </summary>
        /// <param name="services">Service Collection.</param>
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDaprClient();

            services.AddSingleton(new JsonSerializerOptions()
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
            });
        }

        /// <summary>
        /// Configures Application Builder and WebHost environment.
        /// </summary>
        /// <param name="app">Application builder.</param>
        /// <param name="env">Webhost environment.</param>
        /// <param name="serializerOptions">Options for JSON serialization.</param>
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, JsonSerializerOptions serializerOptions,
            ILogger<Startup> logger)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseCloudEvents();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapSubscribeHandler();
                endpoints.MapPost("deposit", Deposit);
            });

            async Task Deposit(HttpContext context)
            {
                // We try to get the token sent by the caller. If Dapr sent it, it will be populated
                context.Request.Headers.TryGetValue("dapr-api-token", out var token);

                // Get the token that we will authorize against from an environment variable. You'll want to put this in your program startup.
                // On Kubernetes, you will mount the token as an environment variable from a secret.
                var apiToken = System.Environment.GetEnvironmentVariable("MY_APP_TOKEN");

                // Validate the token we got from Dapr against the one we gave our app
                if (token != apiToken)
                {
                    logger.LogInformation("Unauthorized call rejected");
                    // Return unauthorized
                     context.Response.StatusCode = 401;
                    return;
                }

                logger.LogInformation("Enter Deposit");

                var client = context.RequestServices.GetRequiredService<DaprClient>();
                var transaction = await JsonSerializer.DeserializeAsync<Transaction>(context.Request.Body, serializerOptions);

                logger.LogInformation("Id is {0}, Amount is {1}", transaction.Id, transaction.Amount);

                var account = await client.GetStateAsync<Account>(StoreName, transaction.Id);
                if (account == null)
                {
                    account = new Account() { Id = transaction.Id, };
                }

                if (transaction.Amount < 0m)
                {
                    logger.LogInformation("Invalid amount");
                    context.Response.StatusCode = 400;
                    return;
                }

                account.Balance += transaction.Amount;
                await client.SaveStateAsync(StoreName, transaction.Id, account);
                logger.LogInformation("Balance is {0}", account.Balance);

                context.Response.ContentType = "application/json";
                await JsonSerializer.SerializeAsync(context.Response.Body, account, serializerOptions);
            }
        }
    }
}