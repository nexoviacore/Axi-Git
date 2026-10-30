using AxiGitAPI.Models;
using AxiGitAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace AxiGitAPI.Controllers
{
    [ApiController]
    [Route("api/git")]
    public class DirectoryListController : ControllerBase
    {
        private readonly DirectoryListService _directoryListService;

        public DirectoryListController(
            DirectoryListService directoryListService)
        {
            _directoryListService = directoryListService;
        }

        // Lists the Directories and their Versions on the Repository's main branch.
        // The GitHub PAT comes from server configuration, never from the request.
        [HttpPost("listdirectories")]
        [ProducesResponseType(typeof(ListDirectoriesResponse), StatusCodes.Status200OK)]
        public async Task<ActionResult<ListDirectoriesResponse>> ListDirectories(
            [FromBody] ListDirectoriesRequest request,
            CancellationToken cancellationToken)
        {
            ListDirectoriesResponse response =
                await _directoryListService.ListDirectories(
                    request,
                    cancellationToken);

            return Ok(response);
        }
    }
}
