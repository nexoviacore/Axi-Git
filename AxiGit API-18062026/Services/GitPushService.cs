using LibGit2Sharp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using AxiGitAPI.Models;

namespace AxiGitAPI.Services
{
    public class GitPushService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<GitPushService> _logger;

        public GitPushService(IConfiguration configuration, ILogger<GitPushService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public string PushFolderToGitHub(
                        string physicalPath,
                        string targetRepo,
                        string packageName,
                        string packageDescription,
                        string packageVersion,
                        string packageAuthor,
                        string gitBranch)
        {
            try
            {
                _logger.LogInformation("Push process started for package: {PackageName}", packageName);

                if (!Directory.Exists(physicalPath))
                    throw new Exception("Physical path does not exist.");

                if (string.IsNullOrWhiteSpace(gitBranch))
                    gitBranch = "aximain";

                string username = _configuration["GitHub:Username"];
                string token = _configuration["GitHub:Token"];
                string repoOwner = _configuration["GitHub:RepoOwner"];
                string workingRoot = _configuration["GitHub:WorkingDirectory"];

                Directory.CreateDirectory(workingRoot);

                string repoPath = Path.Combine(workingRoot, targetRepo, gitBranch);
                string repoUrl = $"https://github.com/{repoOwner}/{targetRepo}.git";

                if (!Directory.Exists(Path.Combine(repoPath, ".git")))
                {
                    CloneRepository(repoUrl, repoPath, username, token);
                }

                using var repo = new Repository(repoPath);
                if (repo.Head.Tip == null)
                {
                    InitializeEmptyRepository(repo,username,token, targetRepo);
                }

                CheckoutOrCreateBranch(repo, gitBranch);

                string destinationFolder = Path.Combine(repoPath, packageName);

                if (Directory.Exists(destinationFolder))
                    Directory.Delete(destinationFolder, true);

                CopyDirectory(physicalPath, destinationFolder);

                Commands.Stage(repo, packageName);

                RepositoryStatus status = repo.RetrieveStatus();

                if (!status.IsDirty)
                {
                    _logger.LogInformation("No changes detected for package: {PackageName}", packageName);
                    return "No changes detected.";
                }

                var author = new Signature(username, $"{username}@gmail.com", DateTimeOffset.Now);

                Commit commit = repo.Commit($"Updated folder {packageName}",author,author);

                _logger.LogInformation("Commit created: {CommitId}", commit.Id);

                // Retry logic for concurrent pushes
                bool pushSuccess = false;
                int retryCount = 0;
                Exception lastException = null;
                while (!pushSuccess && retryCount < 3)
                {
                    try
                    {
                        PushChanges(repo, username, token);
                        pushSuccess = true;
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;

                        if (ex.Message.Contains("non-fast-forward"))
                        {
                            retryCount++;

                            if (retryCount < 3)
                            {
                                Commands.Fetch(repo, "origin", new string[0], new FetchOptions
                                {
                                    CredentialsProvider = (_url, _user, _cred) =>
                                        new UsernamePasswordCredentials
                                        {
                                            Username = username,
                                            Password = token
                                        }
                                }, 
                                null);
                                continue;
                            }
                        }
                        throw;
                    }
                }
                if (!pushSuccess)
                    throw new Exception("Push failed after retries", lastException);
                return $"Success. Commit ID: {commit.Id}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while pushing package");
                throw;
            }
        }
        public void UpdateJsonInMain(
                    string targetRepo,
                    string packageName,
                    string packageDescription,
                    string packageVersion,
                    string packageAuthor,
                    string gitBranch)
        {
            if (string.IsNullOrWhiteSpace(gitBranch))
                gitBranch = "aximain";
            string username = _configuration["GitHub:Username"];
            string token = _configuration["GitHub:Token"];
            string repoOwner = _configuration["GitHub:RepoOwner"];
            string workingRoot = _configuration["GitHub:WorkingDirectory"];
            string repoPath = Path.Combine(workingRoot,targetRepo,"aximain");
            string repoUrl = $"https://github.com/{repoOwner}/{targetRepo}.git";

            if (!Repository.IsValid(repoPath))
            {
                throw new Exception(
                    $"Working Directory not found: {repoPath}");
            }

            using var repo = new Repository(repoPath);
            var mainBranch = repo.Branches["aximain"];
            if (mainBranch == null)
                throw new Exception("AxiMain branch not found.");

            Commands.Checkout(repo, mainBranch);

            repo.Reset(ResetMode.Hard);

            Commands.Fetch(repo,"origin",Array.Empty<string>(),
                new FetchOptions
                {
                    CredentialsProvider = (_url, _user, _cred) =>
                        new UsernamePasswordCredentials
                        {
                            Username = username,
                            Password = token
                        }
                },null);

            string jsonPath =Path.Combine(repoPath, "axiPackages.json");

            PackageRegistry registry;

            if (File.Exists(jsonPath))
            {
                registry =JsonSerializer.Deserialize<PackageRegistry>(File.ReadAllText(jsonPath))?? new PackageRegistry();
            }
            else
            {
                registry = new PackageRegistry
                {
                    Packages = new List<PackageDetails>(),
                    GitBranches = new List<string>()
                };
            }

            if (registry.Packages == null)
                registry.Packages = new List<PackageDetails>();

            if (registry.GitBranches == null)
                registry.GitBranches = new List<string>();

            registry.Packages.Add(new PackageDetails
            {
                PackageName = packageName,
                Branch = gitBranch,
                PackageDescription = packageDescription,
                PackageVersion = packageVersion,
                PackageAuthor = packageAuthor,
                PackageCreatedOn = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                Icon = "shopping_cart",
                ColorClass = "orange",
                Category = "Enterprise",
                Tags = packageName.Split('_', '-', ' ').ToList()
            });

            if (!registry.GitBranches.Contains(gitBranch))
                registry.GitBranches.Add(gitBranch);

            File.WriteAllText(jsonPath,JsonSerializer.Serialize(registry,new JsonSerializerOptions{WriteIndented = true}));

            Commands.Stage(repo, "axiPackages.json");

            repo.Index.Write();

            var author = new Signature(username,$"{username}@gmail.com",DateTimeOffset.Now);

            repo.Commit($"Updated package registry from {gitBranch}",author,author);

            repo.Network.Push(repo.Network.Remotes["origin"],"refs/heads/aximain",
                new PushOptions
                {
                    CredentialsProvider = (_url, _user, _cred) =>
                        new UsernamePasswordCredentials
                        {
                            Username = username,
                            Password = token
                        }
                });

            _logger.LogInformation("axiPackages.json updated successfully for package {PackageName}",packageName);
        }

        private void PushChanges(
                    Repository repo,
                    string username,
                    string token)
        {
            var pushOptions = new PushOptions
            {
                CredentialsProvider =(_url, _user, _cred) =>
                        new UsernamePasswordCredentials
                        {
                            Username = username,
                            Password = token
                        }
            };

            var currentBranch = repo.Head.FriendlyName;

            repo.Network.Push(repo.Network.Remotes["origin"],$"refs/heads/{currentBranch}:refs/heads/{currentBranch}",pushOptions);
        }

        private void CloneRepository(
                        string repoUrl,
                        string repoPath,
                        string username,
                        string token)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(repoPath)!);

            var cloneOptions = new CloneOptions
            { FetchOptions =
                  { CredentialsProvider = (_url, _user, _cred) =>
                       new UsernamePasswordCredentials
                       {
                         Username = username,
                         Password = token
                       }
                    }
            };
            Repository.Clone(repoUrl,repoPath,cloneOptions);
        }

        private void CheckoutOrCreateBranch(
                        Repository repo,
                        string branchName)
        {
            Commands.Fetch(repo,"origin",Array.Empty<string>(),new FetchOptions(),null);
            var remoteBranch = repo.Branches[$"origin/{branchName}"];
            Branch branch;
            if (remoteBranch != null)
            {
                branch = repo.Branches[branchName]?? repo.CreateBranch(branchName,remoteBranch.Tip);
            }
            else
            {
                var main =repo.Branches["main"];
                if (main == null)
                {
                    Commands.Fetch(repo,"origin",Array.Empty<string>(),new FetchOptions(),null);
                    main =repo.Branches["origin/main"];
                }
                branch =repo.Branches[branchName]?? repo.CreateBranch(branchName,main.Tip);
            }
            Commands.Checkout(repo, branch);
        }

        private void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);
            }

            foreach (var folder in Directory.GetDirectories(sourceDir))
            {
                CopyDirectory(folder, Path.Combine(destDir, Path.GetFileName(folder)));
            }
        }

        private void InitializeEmptyRepository(
                            Repository repo,
                            string username,
                            string token,
                            string repoName)
        {
            if (repo.Head?.Tip != null)
                return;

            _logger.LogInformation("Empty repository detected. Creating initial commit.");

            string readmePath =Path.Combine(repo.Info.WorkingDirectory,"README.md");

            if (!File.Exists(readmePath))
            {
                File.WriteAllText(readmePath,$"# {repoName}");
            }

            Commands.Stage(repo, "README.md");

            var author =new Signature(username,$"{username}@gmail.com",DateTimeOffset.Now);

            repo.Commit("Initial repository commit",author,author);

            repo.Network.Push(repo.Network.Remotes["origin"],"refs/heads/master:refs/heads/main",
                new PushOptions
                {
                    CredentialsProvider =
                        (_url, _user, _cred) =>
                            new UsernamePasswordCredentials
                            {
                                Username = username,
                                Password = token
                            }
                });

            _logger.LogInformation(
                "Initial commit pushed successfully.");
        }
    }
}