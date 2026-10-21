using System.Security.Claims;
using System.Text.Json;
using Arkanum.Web.Data;
using Arkanum.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Arkanum.Tests;

public class FeedbackIssueTests
{
    static ClaimsPrincipal User(string key = "user") => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, key), new Claim(ClaimTypes.Name, "Tester")], "test"));
    static ArkanumDbContext Db() => new(new DbContextOptionsBuilder<ArkanumDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    sealed class Sender : IEmailSender
    {
        public List<EmailMessage> Messages = [];
        public bool Fail;
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default) { if (Fail) throw new InvalidOperationException("private@example.test"); Messages.Add(message); return Task.CompletedTask; }
    }
    static async Task Configure(ArkanumDbContext db, PortalSettingsService settings) {
        await settings.SaveFeedbackAsync(db, true, "private@example.test", "What should improve?", "admin");
        await settings.SaveMailAsync(db, new MailServerSettings("localhost", "25", false, "sender@example.test", "Test"), "admin");
    }
    [Fact] public async Task DefaultOffPublicProjectionNeverContainsRecipient()
    {
        await using var db = Db(); var settings = TestPortalSettingsServiceFactory.Create();
        Assert.False((await settings.GetFeedbackPublicAsync(db)).Enabled);
        await Configure(db, settings);
        var json = JsonSerializer.Serialize(await settings.GetFeedbackPublicAsync(db));
        Assert.DoesNotContain("private@example.test", json);
        Assert.Contains("What should improve?", json);
    }
    [Fact] public async Task SendsServerRecipientAndBoundedAnswersOnlyAfterValidation()
    {
        await using var db = Db(); var settings = TestPortalSettingsServiceFactory.Create(); await Configure(db, settings);
        var sender = new Sender(); var service = new FeedbackService(settings, sender, new FeedbackRateLimiter());
        var result = await service.SubmitAsync(db, User(), "Useful feedback", ["Search"], [], default);
        Assert.True(result.Success); var message = Assert.Single(sender.Messages);
        Assert.Equal("private@example.test", message.To); Assert.Contains("Search", message.Body);
    }
    [Fact] public async Task DeniesAnonymousDisabledOversizedAnswersAndContent()
    {
        await using var db = Db(); var settings = TestPortalSettingsServiceFactory.Create(); var sender = new Sender();
        var service = new FeedbackService(settings, sender, new FeedbackRateLimiter());
        Assert.False((await service.SubmitAsync(db, User(), "text", [], [], default)).Success);
        await Configure(db, settings);
        Assert.False((await service.SubmitAsync(db, new ClaimsPrincipal(), "text", [], [], default)).Success);
        Assert.False((await service.SubmitAsync(db, User(), new string('x',1001), [], [], default)).Success);
        Assert.False((await service.SubmitAsync(db, User("oversized-answer"), "text", [new string('a',501)], [], default)).Success);
        Assert.False((await service.SubmitAsync(db, User("extra-answers"), "text", ["a", "b"], [], default)).Success);
        Assert.Empty(sender.Messages);
    }
    [Fact] public async Task RejectsSpoofedOrOversizedScreenshotsAndSanitizesNames()
    {
        await using var db = Db(); var settings = TestPortalSettingsServiceFactory.Create(); await Configure(db, settings);
        var sender = new Sender(); var service = new FeedbackService(settings,sender,new FeedbackRateLimiter());
        IFormFile File(byte[] bytes, string name) => new FormFile(new MemoryStream(bytes), 0, bytes.Length, "Screenshots", name) { Headers = new HeaderDictionary(), ContentType = "image/png" };
        Assert.False((await service.SubmitAsync(db, User("a"), "text", [], [File("not an image"u8.ToArray(),"a.png")], default)).Success);
        Assert.False((await service.SubmitAsync(db, User("b"), "text", [], [File(new byte[2*1024*1024+1],"a.png")], default)).Success);
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aZ1sAAAAASUVORK5CYII=");
        Assert.True((await service.SubmitAsync(db, User("c"), "text", [], [File(png,"../../secret\r\n.png")], default)).Success);
        var attachment = Assert.Single(Assert.Single(sender.Messages).Attachments!);
        Assert.Equal("screenshot-1.png", attachment.FileName); Assert.Equal(png,attachment.Content);
    }
    [Fact] public async Task SmtpFailureIsGenericAndAttemptsAreRateLimited()
    {
        await using var db = Db(); var settings = TestPortalSettingsServiceFactory.Create(); await Configure(db, settings);
        var sender = new Sender { Fail = true }; var service = new FeedbackService(settings,sender,new FeedbackRateLimiter());
        var result = await service.SubmitAsync(db,User(),"text",[],[],default);
        Assert.False(result.Success); Assert.DoesNotContain("private",result.Message);
        sender.Fail=false;
        Assert.False((await service.SubmitAsync(db,User(),"text",[],[],default)).Success); Assert.Empty(sender.Messages);
    }
    [Fact] public async Task AbsentSmtpCannotReportSuccessAndSettingsRejectMultipleRecipients()
    {
        await using var db = Db(); var settings=TestPortalSettingsServiceFactory.Create();
        await Assert.ThrowsAsync<ArgumentException>(() => settings.SaveFeedbackAsync(db,true,"a@example.test,b@example.test","","admin"));
        await settings.SaveFeedbackAsync(db,true,"private@example.test","","admin");
        var sender=new Sender(); var service=new FeedbackService(settings,sender,new FeedbackRateLimiter());
        Assert.False((await service.SubmitAsync(db,User(),"text",[],[],default)).Success); Assert.Empty(sender.Messages);
    }
    [Fact] public async Task SettingsRejectOversizedRecipientAndQuestionBounds()
    {
        await using var db=Db(); var settings=TestPortalSettingsServiceFactory.Create();
        await Assert.ThrowsAsync<ArgumentException>(() => settings.SaveFeedbackAsync(db,true,new string('x',250)+"@example.test","","admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => settings.SaveFeedbackAsync(db,true,"a@example.test",new string('q',251),"admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => settings.SaveFeedbackAsync(db,true,"a@example.test",string.Join('\n',Enumerable.Repeat("Question",6)),"admin"));
    }
    [Fact] public async Task NonAdminDirectSettingsHandlerIsForbiddenBeforeAnyWrite()
    {
        await using var db=Db();
        var model=new Arkanum.Web.Pages.Settings.IndexModel(db,null!,TestPortalSettingsServiceFactory.Create(),null!,null!,null!) {
            PageContext=new Microsoft.AspNetCore.Mvc.RazorPages.PageContext { HttpContext=new DefaultHttpContext { User=User() } },
            FeedbackEnabled=true,FeedbackRecipient="forged@example.test"
        };
        Assert.IsType<Microsoft.AspNetCore.Mvc.ForbidResult>(await model.OnPostFeedbackAsync());
        Assert.Empty(await db.PortalSettings.ToListAsync());
    }
    [Fact] public void ModalTransportUsesAntiforgeryAndNoUploadPersistence()
    {
        var component=File.ReadAllText(Path.Combine(Root,"Arkanum.Web/Pages/Shared/Components/Feedback/Default.cshtml"));
        Assert.Contains("<dialog",component); Assert.Contains("aria-labelledby",component);
        Assert.Contains("AntiForgeryToken()",component); Assert.Contains("maxlength=\"1000\"",component);
        Assert.DoesNotContain("Recipient",component); Assert.Contains("must not contain passwords",component);
        var page=File.ReadAllText(Path.Combine(Root,"Arkanum.Web/Pages/Feedback.cshtml.cs"));
        Assert.Contains("[Authorize]",page); Assert.Contains("RequestSizeLimit",page); Assert.Contains("HasJsonContentType",page);
        Assert.DoesNotContain("[IgnoreAntiforgeryToken",page); Assert.DoesNotContain("ReadFormAsync",page);
        var js=File.ReadAllText(Path.Combine(Root,"Arkanum.Web/wwwroot/js/feedback.js"));
        Assert.Contains("X-CSRF-TOKEN",js); Assert.Contains("showModal()",js); Assert.Contains("opener.focus()",js);
    }
    [Fact] public async Task ConfiguredSmtpDeliversRealMimeScreenshotAttachment()
    {
        var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0); listener.Start();
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var port=((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        var captured=new System.Text.StringBuilder();
        async Task Capture()
        {
            using var client=await listener.AcceptTcpClientAsync(deadline.Token);
            await using var stream=client.GetStream();
            using var reader=new StreamReader(stream);
            using var writer=new StreamWriter(stream) { NewLine="\r\n", AutoFlush=true };
            await writer.WriteLineAsync("220 localhost test SMTP");
            var inData=false;
            while (await reader.ReadLineAsync(deadline.Token) is { } line)
            {
                if (inData)
                {
                    if (line==".") { inData=false; await writer.WriteLineAsync("250 accepted"); }
                    else captured.AppendLine(line);
                }
                else if (line.StartsWith("DATA",StringComparison.OrdinalIgnoreCase)) { inData=true; await writer.WriteLineAsync("354 continue"); }
                else if (line.StartsWith("QUIT",StringComparison.OrdinalIgnoreCase)) { await writer.WriteLineAsync("221 goodbye"); break; }
                else await writer.WriteLineAsync("250 localhost");
            }
        }
        var server=Capture();
        try
        {
            var services=new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            var name=Guid.NewGuid().ToString();
            Microsoft.Extensions.DependencyInjection.EntityFrameworkServiceCollectionExtensions.AddDbContext<ArkanumDbContext>(services, o=>o.UseInMemoryDatabase(name));
            Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped<PortalSettingsService>(services,_=>TestPortalSettingsServiceFactory.Create());
            using var provider=Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
            using (var scope=Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(provider))
            {
                var db=Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<ArkanumDbContext>(scope.ServiceProvider);
                var settings=Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<PortalSettingsService>(scope.ServiceProvider);
                await settings.SaveMailAsync(db,new MailServerSettings("127.0.0.1",port.ToString(),false,"sender@example.test","Test"),"admin");
            }
            var sender=new SmtpEmailSender(Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(provider));
            var bytes=new byte[] {137,80,78,71};
            await sender.SendAsync(new EmailMessage("private@example.test","Feedback integration","Probe feedback",[new EmailAttachment("screenshot-1.png","image/png",bytes)],true),deadline.Token);
            await server.WaitAsync(deadline.Token);
            var mime=captured.ToString();
            Assert.Contains("multipart/mixed",mime); Assert.Contains("image/png",mime); Assert.Contains("screenshot-1.png",mime);
            Assert.Contains(Convert.ToBase64String(bytes),mime); Assert.Contains("Probe feedback",mime);
        }
        finally { deadline.Cancel(); listener.Stop(); }
    }
    static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
    [Fact] public void FeedbackHasAuthenticatedModalAndPrivateSettingsContract()
    {
        var layout = File.ReadAllText(Path.Combine(Root, "Arkanum.Web/Pages/Shared/_Layout.cshtml"));
        Assert.Contains("InvokeAsync(\"Feedback\")", layout);
        var settings = File.ReadAllText(Path.Combine(Root, "Arkanum.Web/Pages/Settings/Index.cshtml"));
        Assert.Contains("asp-page-handler=\"Feedback\"", settings);
        Assert.DoesNotContain("while we build the control surface", settings);
        Assert.NotNull(typeof(PortalSettingsService).Assembly.GetType("Arkanum.Web.Services.FeedbackService"));
    }
}
