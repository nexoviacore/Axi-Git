using AxiGitAPI.Models;
using AxiGitAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace AxiGitAPI.Controllers
{
    [ApiController]
    [Route("api/git")]
    public class GitPullController : ControllerBase
    {
        private readonly GitPullService
            _gitPullService;

        private readonly ILogger<GitPullController>
            _logger;

        public GitPullController(GitPullService gitPullService, ILogger<GitPullController> logger)
        {
            _gitPullService =
                gitPullService;

            _logger = logger;
        }

        [HttpPost("pull")]
        public IActionResult PullPackage([FromBody] PullPackageRequest request)
        {
            try
            {
                _logger.LogInformation(
                    "Pull request received for package: {PackageName}",request.PackageName);

                string result =
                    _gitPullService.PullPackage(
                        request.PhysicalPath,
                        request.RepoUrl,
                        request.PackageName,
                        request.GitBranch);

                _logger.LogInformation(
                    "Package pulled successfully: {PackageName}",
                    request.PackageName);

                return Ok(new
                {
                    success = true,
                    message = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error occurred while pulling package: {PackageName}",
                    request.PackageName);

                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }
    }
}