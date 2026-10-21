using Arkanum.Web.Data;
using Arkanum.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Arkanum.Web.ViewComponents;

public sealed class FeedbackViewComponent(ArkanumDbContext db, PortalSettingsService settings) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (User.Identity?.IsAuthenticated != true) return Content("");
        var config = await settings.GetFeedbackPublicAsync(db);
        return config.Enabled ? View(config) : Content("");
    }
}
