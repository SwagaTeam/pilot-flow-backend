using PilotFlow.Api.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddPilotFlowApi(builder.Configuration, builder.Environment);

var app = builder.Build();

app.MapPilotFlowApi();

app.Run();

public partial class Program;
