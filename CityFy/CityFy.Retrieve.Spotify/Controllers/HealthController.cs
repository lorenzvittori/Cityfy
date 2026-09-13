using Microsoft.AspNetCore.Mvc;

namespace CityFy.RtrieveSpotify.Controllers
{
    [ApiController]
    [Route("/health")]
    public class HealthController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get() => Ok(new { status = "Healthy", timestamp = System.DateTime.UtcNow });
    }
}
