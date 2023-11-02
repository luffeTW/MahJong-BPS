using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Serilog.Filters;
using MahJongBPS.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using MahJongBPS.Controllers;
using System.Collections.Generic;
using System.Timers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Newtonsoft.Json;
using System.Net.NetworkInformation;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using MahJongBPS.Models;
using System.Data;
using Serilog;
using Microsoft.EntityFrameworkCore;

namespace MahJongBPS
{


    public class AppSettings
    {
        public string ModBusPort { get; set; }
        //public Dictionary<int, int> TableTimers { get; set; }        
    }
    public class TableTimers
    {
        public Dictionary<int, int>  tableTimers { get; set; } 
    }
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();

        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                })
                .UseSerilog((context, services, configuration) => configuration
                    .ReadFrom.Services(services)
                    .Enrich.FromLogContext()
                    .Filter.ByExcluding(Matching.FromSource("Microsoft")) //log�z�ﱼ�L�n�w�]���T��
                    .WriteTo.Console()
                    .WriteTo.File("logs/log-.log",
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 24 * 30
                    )
                );

    }

    public class Startup
    {
        
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }
        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            //��Ʈw���U
            services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(Configuration.GetConnectionString("MyPostgreSQLConnection")));


            string CashBoxPort = Configuration.GetConnectionString("CashBoxPort");
            string PrinterPort = Configuration.GetConnectionString("PrinterPort");
            string HopperPort  = Configuration.GetConnectionString("HopperPort");
            services.Configure<AppSettings>(Configuration.GetSection("AppSetting"));

            //services.Configure<AppSettings>(Configuration.GetSection("TableTimers"));
            services.Configure<TableTimers>(Configuration.GetSection("TableTimers"));

            //services.AddHostedService<AppShutdownService>();
            services.AddControllers();
            services.AddSingleton<TableController>(); 
            //services.Configure<TableTimersOptions>(Configuration.GetSection("TableTimers"));            
            services.AddSingleton<Dictionary<int, TimerData>>(provider => TableController.tableTimers);

            services.AddSignalR();
            services.AddSingleton<ISerialPortService>(sp =>
                new SerialPortService(CashBoxPort,PrinterPort, HopperPort,
                    sp.GetRequiredService<ILogger<SerialPortService>>(),
                    sp.GetRequiredService<IHubContext<NotificationHub>>(),
                    sp.GetRequiredService<TableController>()
                )
            );
            services.AddControllersWithViews();
            Console.WriteLine("service signed");
            services.Configure<CookieAuthenticationOptions>(options =>
            {
                options.ExpireTimeSpan = TimeSpan.FromHours(24);
                options.SlidingExpiration = true; 

            });

        }
        //private readonly TableController tableController;
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime lifetime, Dictionary<int, TimerData> tableTimers)
        {

            DateTime? previousRequestTime = null;

            lifetime.ApplicationStopping.Register(() =>
            {
                try
                {
                    var appSettingsPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
                    var appSettingsJson = File.ReadAllText(appSettingsPath);
                    var appSettings = JsonConvert.DeserializeObject<Dictionary<string, object>>(appSettingsJson);
                    var allTableTimers = tableTimers;
                    var tableData = new Dictionary<int, int>();
                    foreach (var tableTimer in tableTimers)
                    {
                        try
                        {
                            //Console.WriteLine("Trying to create dictionary");

                            allTableTimers.TryGetValue(tableTimer.Key, out TimerData timerData);
                            int remainingTime = (int)(timerData.Timer.Interval - (DateTime.Now - timerData.StartTime).TotalMilliseconds) / 1000;
                            if (remainingTime > 0)
                            {
                                //Console.WriteLine(remainingTime);
                                tableTimers[tableTimer.Key] = tableTimer.Value;
                                tableData[tableTimer.Key] = remainingTime;
                                Console.WriteLine($"Trying to create dictionary:tableNumber:{tableTimer.Key} remainingTime:{remainingTime} ");
                            }
                            
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Trying to create dictionary error:{ex}");
                        }
                    }
                   
                    appSettings["TableTimers"] = tableData;

                    var updatedAppSettingsJson = JsonConvert.SerializeObject(appSettings, Formatting.Indented);
                    File.WriteAllText(appSettingsPath, updatedAppSettingsJson);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            });



            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.Use(async (context, next) =>
            {
                if (previousRequestTime.HasValue)
                {
                    var timeElapsed = DateTime.Now - previousRequestTime.Value;
                    var logger = context.RequestServices.GetRequiredService<ILogger<Startup>>();
                    logger.LogInformation($"�W�@���ާ@: {timeElapsed.TotalSeconds} ��e");
                }

                previousRequestTime = DateTime.Now;

                await next.Invoke();
            });

            app.UseAuthorization();
           
            app.UseEndpoints(endpoints =>
            {   
                
                endpoints.MapHub<NotificationHub>("/NotificationHub"); // "notificationHub" �OSignalR Hub������
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");

            });
        }

    }
}
