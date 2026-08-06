using Microsoft.Extensions.Hosting;

// 占位入口，后续任务替换为双模式宿主
var builder = Host.CreateApplicationBuilder(args);
builder.Build().Run();
