using Notifications.Application.Database;
using Notifications.Application.Notifications;
using Notifications.Host.Database;
using Notifications.Host.Infrastructure;
using SharedKernel.Infrastructure.Hosting;
using Teck.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddTeckService(typeof(Program).Assembly, builder.Configuration);
builder.AddNotificationPersistence();
builder.Services.Configure<NotificationOptions>(builder.Configuration.GetSection(NotificationOptions.SectionName));
builder.Services.AddScoped<IStubEmailAcceptanceStore, StubEmailAcceptanceDbContextStore>();
builder.Services.AddScoped<IEmailSender, StubEmailSender>();
builder.AddTeckMessaging(typeof(NotificationDbContext).Assembly, "NotificationWrite");
var app = builder.Build();
app.UseTeckService();
app.MapDefaultEndpoints();
return await app.RunTeckServiceAsync<NotificationDbContext>(args);
