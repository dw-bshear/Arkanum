using System.Text.Json;
using Arkanum.Web.Data;
using Arkanum.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Arkanum.Web.Pages;

[Authorize]
[RequestSizeLimit(6 * 1024 * 1024)]
public sealed class FeedbackModel(ArkanumDbContext db, FeedbackService feedback) : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Index");
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true) return Challenge();
        if (!Request.HasJsonContentType()) return BadRequest(new { success = false, message = "Invalid feedback request." });
        var streams = new List<MemoryStream>();
        try
        {
            var input = await Request.ReadFromJsonAsync<FeedbackInput>(cancellationToken);
            if (input is null || input.Screenshots is null || input.Answers is null || input.Screenshots.Count > 2 || input.Answers.Count > 5)
                return BadRequest(new { success = false, message = "Invalid feedback request." });
            var files = new List<IFormFile>();
            foreach (var image in input.Screenshots)
            {
                if (image is null || image.Data is null || image.Name is null || image.Name.Length > 255 || image.Data.Length > 2796204)
                    return BadRequest(new { success = false, message = "Invalid screenshot." });
                var bytes = Convert.FromBase64String(image.Data);
                var stream = new MemoryStream(bytes, writable: false); streams.Add(stream);
                files.Add(new FormFile(stream, 0, bytes.Length, "Screenshots", image.Name));
            }
            var result = await feedback.SubmitAsync(db, User, input.Text, input.Answers, files, cancellationToken);
            return new JsonResult(new { success = result.Success, message = result.Message }) { StatusCode = result.Success ? 200 : 400 };
        }
        catch (Exception ex) when (ex is JsonException or FormatException or BadHttpRequestException)
        {
            return BadRequest(new { success = false, message = "Invalid feedback request." });
        }
        finally { foreach (var stream in streams) stream.Dispose(); }
    }
    public sealed record FeedbackInput(string? Text, List<string> Answers, List<ScreenshotInput> Screenshots);
    public sealed record ScreenshotInput(string Name, string Data);
}
