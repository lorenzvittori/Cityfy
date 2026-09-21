using CityFy.Retrieve.MusicBrainz.Services;
using CityFy.Retrieve.MusicBrainz.Models;
using CityFy.Retrieve.MusicBrainz.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace CityFy.Retrieve.MusicBrainz.Controllers
{
    [ApiController]
    [ApiExplorerSettings(GroupName = "Debug-MusicBrainz")]
    [Route("debug/musicbrainz")]
    public class DebugMusicBrainzController : ControllerBase
    {
        private readonly IMusicBrainzRetrieveService _service;
        private readonly IMusicBrainzRepository _repo;

        public DebugMusicBrainzController(IMusicBrainzRetrieveService service, IMusicBrainzRepository repo)
        {
            _service = service;
            _repo = repo;
        }

        // POST debug/musicbrainz/start
        // Body: JSON array of artist MBIDs
        [HttpPost("start")]
        public IActionResult StartRetrieve([FromBody] List<string>? artistMbIds)
        {
            if (artistMbIds == null || artistMbIds.Count == 0)
                return BadRequest("Provide a JSON array of MusicBrainz artist MBIDs in the request body.");

            try
            {
                Task.Run(() => _service.StartRetrieveAsync(artistMbIds));
                return Accepted();
            }
            catch (Exception ex)
            {
                Console.WriteLine("MusicBrainz retrieve failed: " + ex);
                return StatusCode(500);
            }
        }

        // POST debug/musicbrainz/start-related
        // Body: { "tag": "rock", "maxArtists": 50, "top": 20 }
        [HttpPost("start-related")]
        public IActionResult StartRelatedRetrieve([FromBody] RelatedRetrieveRequest? req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Tag))
                return BadRequest("Provide a JSON body with 'tag' string.");

            try
            {
                // Run retrieval + persist logic in background via service
                Task.Run(() => _service.RetrieveRelatedTagsAndPersistAsync(req.Tag!, req.MaxArtists, req.Top));

                return Accepted();
            }
            catch (Exception ex)
            {
                Console.WriteLine("StartRelatedRetrieve failed: " + ex);
                return StatusCode(500);
            }
        }

        // GET debug/musicbrainz/related?tag=rock&maxArtists=50&top=20
        [HttpGet("related")]
        public async Task<IActionResult> GetRelatedTags([FromQuery] string? tag, [FromQuery] int maxArtists = 50, [FromQuery] int top = 20)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return BadRequest("Provide a 'tag' query parameter.");

            try
            {
                var graph = await _service.RetrieveRelatedTagsAndPersistAsync(tag, maxArtists, top);
                var dto = graph.RelatedTags;
                return Ok(dto);
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetRelatedTags failed: " + ex);
                return StatusCode(500);
            }
        }

        // GET debug/musicbrainz/graphs/{id}
        [HttpGet("graphs/{id}")]
        public async Task<IActionResult> GetGraphById([FromRoute] string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return BadRequest("Missing id");
            try
            {
                var graph = await _repo.GetTagGraphByIdAsync(id);
                if (graph == null) return NotFound();
                return Ok(graph);
            }
            catch (FormatException)
            {
                return BadRequest("Invalid id format");
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetGraphById failed: " + ex);
                return StatusCode(500);
            }
        }

        // GET debug/musicbrainz/graphs?page=1&pageSize=20
        [HttpGet("graphs")]
        public async Task<IActionResult> GetGraphsPaged([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            try
            {
                var list = await _repo.GetTagGraphsPagedAsync(page, pageSize);
                return Ok(list);
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetGraphsPaged failed: " + ex);
                return StatusCode(500);
            }
        }
    }
}
