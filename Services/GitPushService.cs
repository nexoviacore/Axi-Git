using LibGit2Sharp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.IO.Compression;
using Microsoft.Extensions.Http;
using AxiGitAPI.Models;

namespace AxiGitAPI.Services
{
    public class GitPushService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<GitPushService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        public GitPushService(
            IConfiguration configuration,
            ILogger<GitPushService> logger,
            IHttpClientFactory httpClientFactory)
        {
            _configuration = configuration;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }
        //public string PushFolderToGitHub(
        //                string physicalPath,
        //                string targetRepo,
        //                string packageName,
        //                string packageDescription,
        //                string packageVersion,
        //                string packageAuthor,
        //                string gitBranch)

        public string PushFolderToGitHub(
    string physicalPath,
    string targetRepo,
    string productName,
    string productDescription,
    string productVersion,
    string productAuthor,
    string gitBranch,
    string managerType,
    bool ensureRepository,
    bool compress)
        {
            try
            {
                // _logger.LogInformation("Push process started for package: {PackageName}", packageName);
                _logger.LogInformation(
                    "Push process started for product: {ProductName}",
                    productName);

                if (!Directory.Exists(physicalPath))
                    throw new Exception("Physical path does not exist.");

                //if (string.IsNullOrWhiteSpace(gitBranch))
                //    gitBranch = "aximain";

                if (string.IsNullOrWhiteSpace(gitBranch))
                    gitBranch = "main";

                string username =
                    _configuration["GitHub:Username"];

                string token =
                    _configuration["GitHub:Token"];

                string repoOwner =
                    _configuration["GitHub:RepoOwner"];

                string workingRoot =
                    _configuration["GitHub:WorkingDirectory"];

                Directory.CreateDirectory(workingRoot);

                string repoPath =
                    Path.Combine(
                        workingRoot,
                        targetRepo,
                        gitBranch);

                string repoUrl =
                    $"https://github.com/{repoOwner}/{targetRepo}.git";

                if (!Directory.Exists(
    Path.Combine(repoPath, ".git")))
                {
                    if (ensureRepository)
                    {
                        EnsureRepositoryExists(
                            targetRepo,
                            repoOwner,
                            username,
                            token);
                    }

                    CloneRepository(
                        repoUrl,
                        repoPath,
                        username,
                        token);
                }

                //if (repo.Head.Tip == null)
                //{
                //    InitializeEmptyRepository(repo, username, token, targetRepo);
                //}

                //CheckoutOrCreateBranch(repo, gitBranch);

                //string destinationFolder = Path.Combine(repoPath, packageName);

                //if (Directory.Exists(destinationFolder))
                //    Directory.Delete(destinationFolder, true);

                //CopyDirectory(physicalPath, destinationFolder);

                //Commands.Stage(repo, packageName);

                using var repo = new Repository(repoPath);

                bool isEmptyRepository =
                    repo.Head.Tip == null;

                if (isEmptyRepository)
                {
                    _logger.LogInformation(
                        "Repository is empty. Preparing first commit on branch: {GitBranch}",
                        gitBranch);

                    repo.Refs.UpdateTarget(
                        "HEAD",
                        $"refs/heads/{gitBranch}");
                }
                else
                {
                    CheckoutOrCreateBranch(
                        repo,
                        gitBranch);
                }

                string productFolder =
                    Path.Combine(
                        repoPath,
                        productName);

                string versionNumber =
                        productVersion.StartsWith(
                            "Version",
                            StringComparison.OrdinalIgnoreCase)
                            ? productVersion.Substring(7)
                            : productVersion;

                string versionFolderName =
                    $"Version{versionNumber}";

                string versionFolder =
                    Path.Combine(
                        productFolder,
                        versionFolderName);

                string latestFolder =
                    Path.Combine(
                        productFolder,
                        "Latest");

                if (Directory.Exists(versionFolder))
                {
                    throw new Exception(
                        $"Product version '{versionFolderName}' already exists " +
                        $"for product '{productName}'.");
                }

                if (compress)
                {
                    string versionZipPath =
                        Path.Combine(
                            versionFolder,
                            $"{productName}_version{versionNumber}.zip");

                    string latestZipPath =
                        Path.Combine(
                            latestFolder,
                            $"{productName}_Latest.zip");

                    CreateZipFromDirectoryContents(
                        physicalPath,
                        versionZipPath);

                    UpdateLatestZip(
                        physicalPath,
                        latestZipPath);
                }
                else
                {
                    CopyDirectory(
                        physicalPath,
                        versionFolder);

                    CopyDirectory(
                        physicalPath,
                        latestFolder);
                }

                string versionInfoPath =
                    Path.Combine(
                        latestFolder,
                        "version.info");

                File.WriteAllText(
                    versionInfoPath,
                    versionFolderName);

                Commands.Stage(
                    repo,
                    productName);

                RepositoryStatus status =
                    repo.RetrieveStatus();

                //if (!status.IsDirty)
                //{
                //    _logger.LogInformation("No changes detected for package: {PackageName}", packageName);
                //    return "No changes detected.";
                //}

                if (!status.IsDirty)
                {
                    _logger.LogInformation(
                        "No changes detected for product: {ProductName}",
                        productName);

                    return "No changes detected.";
                }

                var author =
                    new Signature(
                        username,
                        $"{username}@gmail.com",
                        DateTimeOffset.Now);

                //Commit commit = repo.Commit($"Updated folder {packageName}",author,author);

                Commit commit =
                    repo.Commit(
                        $"Updated product {productName} to {versionFolderName}",
                        author,
                        author);

                _logger.LogInformation(
                    "Commit created: {CommitId}",
                    commit.Id);

                // Retry logic for concurrent pushes
                bool pushSuccess = false;
                int retryCount = 0;
                Exception lastException = null;

                while (!pushSuccess && retryCount < 3)
                {
                    try
                    {
                        PushChanges(
                            repo,
                            username,
                            token);

                        pushSuccess = true;
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;

                        if (ex.Message.Contains(
                            "non-fast-forward"))
                        {
                            retryCount++;

                            if (retryCount < 3)
                            {
                                Commands.Fetch(
                                    repo,
                                    "origin",
                                    new string[0],
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

                                continue;
                            }
                        }

                        throw;
                    }
                }

                if (!pushSuccess)
                {
                    throw new Exception(
                        "Push failed after retries",
                        lastException);
                }

                return $"Success. Commit ID: {commit.Id}";
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error occurred while pushing product");

                throw;
            }
        }
        /* public void UpdateJsonInMain(
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
 }  */


        public void UpdateJsonInMain(
    string targetRepo,
    string productName,
    string productDescription,
    string productVersion,
    string productAuthor,
    string gitBranch,
    string managerType)
        {
            string username =
                _configuration["GitHub:Username"];

            string token =
                _configuration["GitHub:Token"];

            string workingRoot =
                _configuration["GitHub:WorkingDirectory"];

            string repoPath =
                Path.Combine(
                    workingRoot,
                    targetRepo,
                    "main");

            if (!Repository.IsValid(repoPath))
            {
                throw new Exception(
                    $"Working Directory not found: {repoPath}");
            }

            using var repo =
                new Repository(repoPath);

            var mainBranch =
                repo.Branches["main"];

            if (mainBranch == null)
            {
                throw new Exception(
                    "Main branch not found.");
            }

            Commands.Checkout(
                repo,
                mainBranch);

            repo.Reset(
                ResetMode.Hard);

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

            //string jsonPath =
            //    Path.Combine(
            //        repoPath,
            //        "axiProduct.json");

            if (string.IsNullOrWhiteSpace(managerType))
            {
                throw new Exception(
                    "ManagerType is required.");
            }

            string normalizedManagerType =
                managerType.Trim().ToLowerInvariant();

            string registryFileName =
                $"axi-{normalizedManagerType}repository.json";

            string jsonPath =
                Path.Combine(
                    repoPath,
                    registryFileName);
            ProductRegistry registry;

            if (File.Exists(jsonPath))
            {
                registry =
                    JsonSerializer.Deserialize<ProductRegistry>(
                        File.ReadAllText(jsonPath))
                    ?? new ProductRegistry();
            }
            else
            {
                registry = new ProductRegistry
                {
                    ManagerType = managerType,
                    Products = new List<ProductDetails>()
                };
            }
            registry.ManagerType = managerType;
            //registry.ManagerType = "product";

            if (registry.Products == null)
            {
                registry.Products =
                    new List<ProductDetails>();
            }

            registry.Products.Add(
                new ProductDetails
                {
                    ProductName =
                        productName,

                    ProductDescription =
                        productDescription,

                    ProductVersion =
                        productVersion,

                    ProductAuthor =
                        productAuthor,

                    ProductCreatedOn =
                        DateTime.UtcNow.ToString(
                            "yyyy-MM-ddTHH:mm:ssZ"),

                    Icon =
                        "shopping_cart",

                    ColorClass =
                        "orange",

                    Tags =
                        productName
                            .Split(
                                '_',
                                '-',
                                ' ')
                            .ToList(),

                    Category =
                        "Enterprise"
                });

            File.WriteAllText(
                jsonPath,
                JsonSerializer.Serialize(
                    registry,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));

            Commands.Stage(
    repo,
    registryFileName);

            //Commands.Stage(
            //    repo,
            //    "axiProduct.json");

            repo.Index.Write();

            var author =
                new Signature(
                    username,
                    $"{username}@gmail.com",
                    DateTimeOffset.Now);

            repo.Commit(
                $"Updated product registry for {productName} Version{productVersion}",
                author,
                author);

            repo.Network.Push(
                repo.Network.Remotes["origin"],
                "refs/heads/main",
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
                "{RegistryFileName} updated successfully for product {ProductName}, version {ProductVersion}",
                registryFileName,
                productName,
                productVersion);
        }

        private void PushChanges(
            Repository repo,
            string username,
            string token)
        {
            var pushOptions =
                new PushOptions
                {
                    CredentialsProvider =
                        (_url, _user, _cred) =>
                            new UsernamePasswordCredentials
                            {
                                Username = username,
                                Password = token
                            }
                };

            var currentBranch =
                repo.Head.FriendlyName;

            repo.Network.Push(
                repo.Network.Remotes["origin"],
                $"refs/heads/{currentBranch}:refs/heads/{currentBranch}",
                pushOptions);
        }

        private void EnsureRepositoryExists(
    string targetRepo,
    string repoOwner,
    string username,
    string token)
        {
            var client =
                _httpClientFactory.CreateClient();

            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "AxiGitAPI");

            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer",
                    token);

            client.DefaultRequestHeaders.Accept.ParseAdd(
                "application/vnd.github+json");

            string checkUrl =
                $"https://api.github.com/repos/{repoOwner}/{targetRepo}";

            var checkResponse =
                client.GetAsync(checkUrl)
                      .GetAwaiter()
                      .GetResult();

            if (checkResponse.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "GitHub repository already exists: {TargetRepo}",
                    targetRepo);

                return;
            }

            if (checkResponse.StatusCode !=
                System.Net.HttpStatusCode.NotFound)
            {
                string error =
                    checkResponse.Content
                        .ReadAsStringAsync()
                        .GetAwaiter()
                        .GetResult();

                throw new Exception(
                    $"Unable to check GitHub repository '{targetRepo}'. " +
                    $"Status: {checkResponse.StatusCode}. " +
                    $"Response: {error}");
            }

            _logger.LogInformation(
                "GitHub repository does not exist. Creating repository: {TargetRepo}",
                targetRepo);

            var requestBody =
                new
                {
                    name = targetRepo,
                    @private = false
                };

            string json =
                JsonSerializer.Serialize(requestBody);

            using var content =
                new StringContent(
                    json,
                    System.Text.Encoding.UTF8,
                    "application/json");

            string createUrl;

            if (string.Equals(
                repoOwner,
                username,
                StringComparison.OrdinalIgnoreCase))
            {
                createUrl =
                    "https://api.github.com/user/repos";
            }
            else
            {
                createUrl =
                    $"https://api.github.com/orgs/{repoOwner}/repos";
            }

            var createResponse =
                client.PostAsync(
                        createUrl,
                        content)
                    .GetAwaiter()
                    .GetResult();

            if (!createResponse.IsSuccessStatusCode)
            {
                string error =
                    createResponse.Content
                        .ReadAsStringAsync()
                        .GetAwaiter()
                        .GetResult();

                throw new Exception(
                    $"Failed to create GitHub repository '{targetRepo}'. " +
                    $"Status: {createResponse.StatusCode}. " +
                    $"Response: {error}");
            }

            _logger.LogInformation(
                "GitHub repository created successfully: {TargetRepo}",
                targetRepo);
        }

        private void CloneRepository(
            string repoUrl,
            string repoPath,
            string username,
            string token)
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(repoPath)!);

            var cloneOptions =
                new CloneOptions
                {
                    FetchOptions =
                    {
                        CredentialsProvider =
                            (_url, _user, _cred) =>
                                new UsernamePasswordCredentials
                                {
                                    Username = username,
                                    Password = token
                                }
                    }
                };

            Repository.Clone(
                repoUrl,
                repoPath,
                cloneOptions);
        }

        private void CheckoutOrCreateBranch(
            Repository repo,
            string branchName)
        {
            Commands.Fetch(
                repo,
                "origin",
                Array.Empty<string>(),
                new FetchOptions(),
                null);

            var remoteBranch =
                repo.Branches[$"origin/{branchName}"];

            Branch branch;

            if (remoteBranch != null)
            {
                branch =
                    repo.Branches[branchName]
                    ?? repo.CreateBranch(
                        branchName,
                        remoteBranch.Tip);
            }
            else
            {
                var main =
                    repo.Branches["main"];

                if (main == null)
                {
                    Commands.Fetch(
                        repo,
                        "origin",
                        Array.Empty<string>(),
                        new FetchOptions(),
                        null);

                    main =
                        repo.Branches["origin/main"];
                }

                if (main == null)
                {
                    throw new Exception(
                        "Main branch not found.");
                }

                branch =
                    repo.Branches[branchName]
                    ?? repo.CreateBranch(
                        branchName,
                        main.Tip);
            }

            Commands.Checkout(
                repo,
                branch);
        }

        private void CreateZipFromDirectoryContents(
            string sourceDir,
            string zipFilePath)
        {
            string? parentDirectory =
                Path.GetDirectoryName(zipFilePath);

            if (!string.IsNullOrWhiteSpace(parentDirectory))
            {
                Directory.CreateDirectory(parentDirectory);
            }

            if (File.Exists(zipFilePath))
            {
                File.Delete(zipFilePath);
            }

            ZipFile.CreateFromDirectory(
                sourceDir,
                zipFilePath,
                CompressionLevel.Optimal,
                false);
        }

        private void UpdateLatestZip(
            string physicalPath,
            string latestZipPath)
        {
            string latestTempFolder =
                Path.Combine(
                    Path.GetTempPath(),
                    Guid.NewGuid().ToString());

            try
            {
                Directory.CreateDirectory(
                    latestTempFolder);

                if (File.Exists(latestZipPath))
                {
                    ZipFile.ExtractToDirectory(
                        latestZipPath,
                        latestTempFolder);
                }

                // Copy the newly exported files over the extracted latest
                // contents. Existing files are overwritten and files that
                // are not present in the new export are retained.
                CopyDirectory(
                    physicalPath,
                    latestTempFolder);

                CreateZipFromDirectoryContents(
                    latestTempFolder,
                    latestZipPath);
            }
            finally
            {
                if (Directory.Exists(latestTempFolder))
                {
                    Directory.Delete(
                        latestTempFolder,
                        true);
                }
            }
        }

        private void CopyDirectory(
            string sourceDir,
            string destDir)
        {
            Directory.CreateDirectory(
                destDir);

            foreach (var file in
                     Directory.GetFiles(sourceDir))
            {
                File.Copy(
                    file,
                    Path.Combine(
                        destDir,
                        Path.GetFileName(file)),
                    true);
            }

            foreach (var folder in
                     Directory.GetDirectories(sourceDir))
            {
                CopyDirectory(
                    folder,
                    Path.Combine(
                        destDir,
                        Path.GetFileName(folder)));
            }
        }
        /* 
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
         */
    }
}