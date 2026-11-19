using System.ComponentModel.DataAnnotations;

namespace AxiGitAPI.Models
{
    // Invalid requests are rejected by [ApiController] model validation
    // with a 400 ValidationProblemDetails naming each offending field.
    public class ListDirectoriesRequest
    {
        [Required]
        public string Account { get; set; } = string.Empty;

        [Required]
        public string Repo { get; set; } = string.Empty;

        [Required]
        [KnownManagerType]
        public string ManagerType { get; set; } = string.Empty;

        // The GitHub PAT configured in AxiCmdLine for this Manager Type.
        // Used only for this request's GitHub calls; never logged or returned.
        [Required]
        public string Token { get; set; } = string.Empty;
    }
}
