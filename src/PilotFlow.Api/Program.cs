using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.StaticFiles;
using PilotFlow.Api.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddPilotFlowApi(builder.Configuration, builder.Environment);

var app = builder.Build();

app.UseExceptionHandler(errorApp => errorApp.Run(ApiExceptionHandler.WriteAsync));

var staticRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
var staticContentTypes = new FileExtensionContentTypeProvider();
staticContentTypes.Mappings[".yaml"] = "application/yaml";
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(staticRoot),
    ContentTypeProvider = staticContentTypes,
});
app.UseAuthentication();
app.UseAuthorization();
app.MapPilotFlowApi();

app.Run();

public partial class Program;
