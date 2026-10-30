var builder = WebApplication.CreateBuilder(args);
string connection;
try
{
    connection = ApiHostPipeline.ResolveConnectionString(builder);
}
catch (InvalidOperationException error)
{
    // 缺少設定屬於維運層面的問題，不是傾印堆疊追蹤的理由。
    await Console.Error.WriteLineAsync(error.Message + " 請透過安全的外部設定提供，勿將連線密碼寫入原始碼。");
    return 1;
}
ApiHostPipeline.ConfigureServices(builder, connection);

var app = builder.Build();
ApiHostPipeline.ConfigurePipeline(app);
await app.RunAsync();
return 0;

public partial class Program { protected Program() { } }
