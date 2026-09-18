using AxiGitAPI.Models;
using AxiGitAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace AxiGitAPI.Controllers
{
    [ApiController]
    [Route("api/git")]
    public class RepositoryListController : ControllerBase
    {
        private readonly RepositoryListService _repositoryListService;

        public RepositoryListController(
            RepositoryListService repositoryListService)
        {
            _repositoryListService = repositoryListService;
        }

        [HttpPost("listrepositories")]
        public async Task<IActionResult> ListRepositories(
            [FromBody] ListRepositoriesRequest request)
        {
            try
            {
                ListRepositoriesResponse response =
                    await _repositoryListService.ListRepositories(request);

                return Ok(response);
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    message = ex.Message
                });
            }
        }
    }
}