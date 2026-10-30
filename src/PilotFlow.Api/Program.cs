using Microsoft.Extensions.FileProviders;
using PilotFlow.Api.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddPilotFlowApi(builder.Configuration, builder.Environment);

var app = builder.Build();

var staticRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(staticRoot),
});
app.UseAuthentication();
app.UseAuthorization();
app.MapPilotFlowApi();

app.Run();

public partial class Program;
