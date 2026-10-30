using AxiGitAPI.Models;
using AxiGitAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace AxiGitAPI.Controllers
{
    [ApiController]
    [Route("api/git")]
    public class GitPushController : ControllerBase
    {
        private readonly GitPushService
            _gitHubService;

        private readonly ILogger<GitPushController>
            _logger;

        public GitPushController(
            GitPushService gitHubService,
            ILogger<GitPushController> logger)
        {
            _gitHubService = gitHubService;

            _logger = logger;
        }

        /* [HttpPost("push")]
         public IActionResult Push(PushFolderRequest request)
         {
             try
             {
                 _logger.LogInformation(
     "Push request received for package: {PackageName}",
     request.PackageName);
                 var commitId = _gitHubService.PushFolderToGitHub(
                     request.PhysicalPath,
                     request.TargetRepo,
                     request.PackageName,
                     request.PackageDescription,
                     request.PackageVersion,
                     request.PackageAuthor,
                     request.GitBranch);

                 _logger.LogInformation(
     "Package pushed successfully: {PackageName}",
     request.PackageName);

                 _gitHubService.UpdateJsonInMain(
                     request.TargetRepo,
                     request.PackageName,
                     request.PackageDescription,
                     request.PackageVersion,
                     request.PackageAuthor,
                     request.GitBranch);

                 _logger.LogInformation(
     "Package Details updated successfully in axiPackages.json for {PackageName}",
     request.PackageName); 
                 return Ok(new
                 {
                     success = true,
                     commit = commitId
                 });
             }
             catch (Exception ex)
             {
                 _logger.LogError(
     ex,
     "Error occurred while pushing package: {PackageName}",
     request.PackageName);
                 return BadRequest(new
                 {
                     success = false,
                     message = ex.Message
                 });
             }
         } */
        [HttpPost("push")]
        public IActionResult Push(PushFolderRequest request)
        {
            try
            {
                _logger.LogInformation(
                    "Push request received for product: {ProductName}",
                    request.ProductName);

                var commitId =
                    _gitHubService.PushFolderToGitHub(
                        request.PhysicalPath,
                        request.TargetRepo,
                        request.ProductName,
                        request.ProductDescription,
                        request.ProductVersion,
                        request.ProductAuthor,
                        request.GitBranch,
                        request.ManagerType,
                        request.EnsureRepository,
                        request.Compress);

                _logger.LogInformation(
                    "Product pushed successfully: {ProductName}",
                    request.ProductName);

                _gitHubService.UpdateJsonInMain(
                    request.TargetRepo,
                    request.ProductName,
                    request.ProductDescription,
                    request.ProductVersion,
                    request.ProductAuthor,
                    request.GitBranch,
                    request.ManagerType);

                _logger.LogInformation(
                    "Product details updated successfully for {ProductName}",
                    request.ProductName);

                return Ok(new
                {
                    success = true,
                    commit = commitId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error occurred while pushing product: {ProductName}",
                    request.ProductName);

                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }
    }
}