using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SmartX.Client.Pages;

// All data access happens client-side via wwwroot/js/sensors.js calling the SmartX.Api
// gateway directly - this PageModel only needs to serve the shell markup.
public class SensorsModel : PageModel
{
    public void OnGet() { }
}
