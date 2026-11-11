using CityFy.Retrieve.MusicBrainz.Services;
using Microsoft.AspNetCore.Mvc;
using ServiceDefault.Errors;

namespace CityFy.Retrieve.MusicBrainz.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RetrieveController : ControllerBase
{
    private readonly RetrieveRunner _runner;

    public RetrieveController(RetrieveRunner runner)
    {
        _runner = runner;
    }

    // POST api/retrieve — download/extract/import the latest dump in the background.
    // Replaces all MusicBrainz data when it completes.
    [HttpPost]
    public IActionResult Retrieve() =>
        _runner.TryStart()
            ? AcceptedAtAction(nameof(GetStatus), null, _runner.Status)
            : this.CityFyProblem(ErrorCodes.MbRetrieveAlreadyRunning, "A retrieve is already queued or running", StatusCodes.Status409Conflict);

    // POST api/retrieve/cancel — stop the running import (admin); the previous catalog stays in place
    [HttpPost("cancel")]
    public IActionResult Cancel() =>
        _runner.Cancel()
            ? Accepted(_runner.Status)
            : this.CityFyProblem(ErrorCodes.ProcessNotCancellable, "No import is running", StatusCodes.Status409Conflict);

    // GET api/retrieve/status
    [HttpGet("status")]
    public IActionResult GetStatus() => Ok(_runner.Status);
}
