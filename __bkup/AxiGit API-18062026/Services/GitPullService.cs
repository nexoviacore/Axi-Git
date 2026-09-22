using LibGit2Sharp;
using Microsoft.Extensions.Configuration;

namespace AxiGitAPI.Services
{
    public class GitPullService
    {
        private readonly IConfiguration _configuration;

        private readonly ILogger<GitPullService>
            _logger;

        public GitPullService(
            IConfiguration configuration,
            ILogger<GitPullService> logger)
        {
            _configuration = configuration;

            _logger = logger;
        }

        public string PullPackage(
                string physicalPath,
                string repoUrl,
                string packageName,
                string gitBranch)
        {
            try
            {
                _logger.LogInformation(
                    "Pull process started for package: {PackageName}",
                    packageName);

                // Validate destination path
                if (string.IsNullOrWhiteSpace(physicalPath))
                {
                    throw new Exception(
                        "Physical path is required.");
                }

                _logger.LogInformation(
                    "Physical path validated: {PhysicalPath}",
                    physicalPath);

                //Validate Package Name
                if (string.IsNullOrWhiteSpace(packageName))
                {
                    throw new Exception(
                        "Package name is required.");
                }

                // Read repository URL
                if (string.IsNullOrWhiteSpace(repoUrl))
                {
                    repoUrl = _configuration["GitHub:RepoUrl"];
                }

                if (string.IsNullOrWhiteSpace(repoUrl))
                {
                    throw new Exception(
                        "Repository URL is required.");
                }

                //Validate Git Branch,Default = 'aximain'
                if (string.IsNullOrWhiteSpace(gitBranch))
                {
                    gitBranch = "aximain";
                }
                _logger.LogInformation(
                    "Target branch resolved: {GitBranch}",
                    gitBranch);

                // GitHub credentials
                string username =
                    _configuration["GitHub:Username"];

                string token =
                    _configuration["GitHub:Token"];

                // Working directory
                string workingRoot =
                    _configuration["GitHub:WorkingDirectory"];

                Directory.CreateDirectory(
                    workingRoot);

                _logger.LogInformation(
                    "Working directory ensured: {WorkingRoot}",
                    workingRoot);

                // Extract repo name
                string repoName =
                    GetRepositoryName(repoUrl);

                _logger.LogInformation(
                    "Repository name resolved: {RepoName}",
                    repoName);

                // Local repo path
                string repoPath =
    Path.Combine(
        workingRoot,
        repoName,
        gitBranch);

                _logger.LogInformation(
                    "Repository local path resolved: {RepoPath}",
                    repoPath);

                // Clone if repo not exists locally
                if (!Directory.Exists(
                    Path.Combine(repoPath, ".git")))
                {
                    _logger.LogInformation(
                        "Repository not found locally. Cloning started.");

                    CloneRepository(
                        repoUrl,
                        repoPath,
                        username,
                        token);

                    _logger.LogInformation(
                        "Repository cloned successfully.");
                }

                using var repo = new Repository(repoPath);

                Commands.Fetch(
                        repo,
                        "origin",
                        Array.Empty<string>(),
                        new FetchOptions
                        {
                            CredentialsProvider =
                                (_url, _user, _cred) =>
                                    new UsernamePasswordCredentials
                                    {
                                        Username = username,
                                        Password = token
                                    }
                        },
                        null);

                Branch remoteBranch =
    repo.Branches[$"origin/{gitBranch}"];

                if (remoteBranch == null)
                {
                    throw new Exception(
                        $"Branch '{gitBranch}' not found in repository.");
                }

                Branch branch =
                    repo.Branches[gitBranch];

                if (branch == null)
                {
                    branch =
                        repo.CreateBranch(
                            gitBranch,
                            remoteBranch.Tip);

                    repo.Branches.Update(
                        branch,
                        b => b.Remote = "origin",
                        b => b.UpstreamBranch = remoteBranch.CanonicalName);
                }
                Commands.Checkout(
                                repo,
                                branch);

                _logger.LogInformation(
                    "Checked out branch successfully: {GitBranch}",
                    gitBranch);

                // Package path inside repo
                string sourcePackagePath =
                    Path.Combine(
                        repoPath,
                        packageName);

                if (!Directory.Exists(
                    sourcePackagePath))
                {
                    throw new Exception(
                        $"Package '{packageName}' not found in repository.");
                }

                _logger.LogInformation(
                    "Package found in repository: {PackagePath}",
                    sourcePackagePath);

                // Final output path
                string destinationPath =
                    Path.Combine(
                        physicalPath,
                        packageName);

                _logger.LogInformation(
                    "Destination path resolved: {DestinationPath}",
                    destinationPath);

                // Copy package
                CopyDirectory(
                    sourcePackagePath,
                    destinationPath);

                _logger.LogInformation(
                    "Package copied successfully.");

                return
                    $"Package pulled successfully to: {destinationPath}";
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error occurred while pulling package: {PackageName}",
                    packageName);

                throw;
            }
        }

        private void CloneRepository(
            string repoUrl,
            string repoPath,
            string username,
            string token)
        {
            try
            {
                _logger.LogInformation(
                    "Cloning repository from URL: {RepoUrl}",
                    repoUrl);

                var cloneOptions =
                    new CloneOptions();

                cloneOptions.FetchOptions
                    .CredentialsProvider =
                        (_url, _user, _cred) =>
                            new UsernamePasswordCredentials
                            {
                                Username = username,
                                Password = token
                            };

                Repository.Clone(
                    repoUrl,
                    repoPath,
                    cloneOptions);

                _logger.LogInformation(
                    "Repository cloned successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error occurred while cloning repository.");

                throw;
            }
        }

        private string GetRepositoryName(
            string repoUrl)
        {
            try
            {
                string repoName =
                    repoUrl
                        .Split('/')
                        .Last();

                if (repoName.EndsWith(".git"))
                {
                    repoName =
                        repoName.Replace(
                            ".git",
                            "");
                }

                return repoName;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error occurred while extracting repository name.");

                throw;
            }
        }

        private void CopyDirectory(
            string sourceDir,
            string destDir)
        {
            try
            {
                Directory.CreateDirectory(
                    destDir);

                // Copy files
                foreach (string file in
                    Directory.GetFiles(sourceDir))
                {
                    string fileName =
                        Path.GetFileName(file);

                    string destFile =
                        Path.Combine(
                            destDir,
                            fileName);

                    File.Copy(
                        file,
                        destFile,
                        true);
                }

                // Copy subfolders
                foreach (string folder in
                    Directory.GetDirectories(sourceDir))
                {
                    string folderName =
                        Path.GetFileName(folder);

                    string destFolder =
                        Path.Combine(
                            destDir,
                            folderName);

                    CopyDirectory(
                        folder,
                        destFolder);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error occurred while copying directory from {SourceDir} to {DestDir}",
                    sourceDir,
                    destDir);

                throw;
            }
        }
    }
}