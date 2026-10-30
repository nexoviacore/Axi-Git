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

        /// <summary>
        /// Lists the Directories and their Versions on the Repository's main branch.
        /// </summary>
        /// <remarks>
        /// The GitHub PAT comes from server configuration, never from the request.
        /// Each Directory's versions list "Latest" first (when present), then Version{N} in numeric order.
        /// An empty Repository returns 200 with no Directories and a null commitSha.
        /// </remarks>
        /// <param name="request">The Account, Repository and Manager Type to list.</param>
        /// <param name="cancellationToken">Aborts the GitHub calls when the client disconnects.</param>
        /// <response code="200">The Directories and Versions, with the commit they were read from.</response>
        /// <response code="400">A field is missing or blank, or the Manager Type is unknown.</response>
        /// <response code="404">The Repository or its main branch was not found, or the configured PAT cannot see it.</response>
        /// <response code="500">An unexpected error occurred.</response>
        /// <response code="502">GitHub rejected the configured PAT, failed, or could not be reached.</response>
        [HttpPost("listdirectories")]
        [Consumes("application/json")]
        [ProducesResponseType(typeof(ListDirectoriesResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError, "application/problem+json")]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway, "application/problem+json")]
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
